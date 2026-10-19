using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DataDump.Core;

namespace DataDump.Adapters;

/// <summary>
/// Writes the game's asset bundles decrypted to bundles/&lt;logical name&gt;, for tools such as AssetRipper or UnityPy
/// (scheme: <see cref="BundleCrypto"/>). Off unless the player turns it on. Plain file work with no game reads, so it
/// runs on a thread of its own and the game does not wait for it. A bundle already written at its full size is skipped,
/// so turning it on again only writes what is missing.
/// </summary>
internal static class BundleExport
{
    public const string OutputFolder = "bundles";

    private const string BundleFolder = "StreamingAssets/PackageManifest/MainPackage";
    private const string ManifestPattern = "RuntimeAssets_MainPackage_*.bytes";
    private const int ChunkBytes = 1 << 20;

    public static void Start(string dataPath, string dumpFolder, string filter, Action<string> log)
    {
        var thread = new Thread(() => Run(Path.Combine(dataPath, BundleFolder), Path.Combine(dumpFolder, OutputFolder), filter, log))
        {
            IsBackground = true,
            Name = "Data Dump bundle export",
            Priority = ThreadPriority.BelowNormal,
        };

        thread.Start();
    }

    private static void Run(string bundleFolder, string outputFolder, string filter, Action<string> log)
    {
        try
        {
            var names = ManifestNames(bundleFolder).Where(name => BundleCrypto.IsWanted(name, filter)).ToList();
            var keys = names.Select(name => (Name: name, Key: BundleCrypto.Key(name))).ToList();
            var written = 0;
            var skipped = 0;
            var unknown = 0;

            Directory.CreateDirectory(outputFolder);
            log($"bundle export: {names.Count} bundles wanted (filter \"{filter}\"), writing to {outputFolder}");

            foreach (var file in Directory.EnumerateFiles(bundleFolder, "*.bundle"))
            {
                switch (ExportOne(file, keys, outputFolder))
                {
                    case Outcome.Written: written++; break;
                    case Outcome.AlreadyThere: skipped++; break;
                    case Outcome.NotWanted: unknown++; break;
                }
            }

            log($"bundle export done: {written} written, {skipped} already there, {unknown} not wanted or not named in the manifest");
        }
        catch (Exception ex)
        {
            log("bundle export stopped: " + ex);
        }
    }

    private enum Outcome { Written, AlreadyThere, NotWanted }

    private static IReadOnlyList<string> ManifestNames(string bundleFolder)
    {
        var manifest = Directory.EnumerateFiles(bundleFolder, ManifestPattern).OrderBy(path => path, StringComparer.Ordinal).LastOrDefault()
                       ?? throw new FileNotFoundException("no bundle manifest", Path.Combine(bundleFolder, ManifestPattern));

        return BundleCrypto.NamesIn(File.ReadAllBytes(manifest));
    }

    private static Outcome ExportOne(string file, List<(string Name, byte[] Key)> keys, string outputFolder)
    {
        using var source = File.OpenRead(file);
        var head = new byte[BundleCrypto.HeadLength];

        if (source.Read(head, 0, head.Length) != head.Length) return Outcome.NotWanted;

        var match = keys.FirstOrDefault(entry => BundleCrypto.Opens(head, entry.Key));

        if (match.Name is null) return Outcome.NotWanted;

        var target = Path.Combine(outputFolder, match.Name);

        if (File.Exists(target) && new FileInfo(target).Length == source.Length) return Outcome.AlreadyThere;

        source.Position = 0;
        Decrypt(source, target, match.Key);

        return Outcome.Written;
    }

    /// <summary>Written beside the final name and moved there, so a half-written bundle never looks whole.</summary>
    private static void Decrypt(Stream source, string target, byte[] key)
    {
        var partPath = target + ".part";
        var buffer = new byte[ChunkBytes];
        long offset = 0;

        using (var output = File.Create(partPath))
        {
            int read;

            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                BundleCrypto.Apply(buffer.AsSpan(0, read), offset, key);
                output.Write(buffer, 0, read);
                offset += read;
            }
        }

        File.Move(partPath, target, overwrite: true);
    }
}
