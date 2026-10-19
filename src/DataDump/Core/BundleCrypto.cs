using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DataDump.Core;

/// <summary>
/// The game's asset bundle encryption (BundleDecryption, rva 0x60a070): every byte is XORed with a 32-byte key made
/// from the bundle's logical name, at the byte's offset in the file mod 32. The key is MD5(salt + name) followed by
/// MD5(name + salt). The files are named by GUID; the logical names are in the YooAsset manifest, and the name whose
/// key turns a file's first bytes into "UnityFS" is that file's name.
/// </summary>
public static class BundleCrypto
{
    public const int KeyLength = 32;

    private const string Salt = "SL_BundleCrypto_v1_9f3d7a1c";
    private static readonly byte[] UnityFsHead = Encoding.ASCII.GetBytes("UnityFS\0");
    private static readonly Regex ManifestName = new("[a-z0-9_./-]+\\.bundle", RegexOptions.CultureInvariant);

    /// <summary>How many of a file's first bytes <see cref="Opens"/> needs.</summary>
    public static int HeadLength => UnityFsHead.Length;

    public static byte[] Key(string logicalName)
    {
        var key = new byte[KeyLength];

        MD5.HashData(Encoding.UTF8.GetBytes(Salt + logicalName)).CopyTo(key, 0);
        MD5.HashData(Encoding.UTF8.GetBytes(logicalName + Salt)).CopyTo(key, KeyLength / 2);

        return key;
    }

    /// <summary>XORs <paramref name="bytes"/> in place; <paramref name="fileOffset"/> is where they start in the file.</summary>
    public static void Apply(Span<byte> bytes, long fileOffset, byte[] key)
    {
        for (var i = 0; i < bytes.Length; i++) bytes[i] ^= key[(int)((fileOffset + i) % KeyLength)];
    }

    /// <summary>True when the file's first bytes decrypt to a plain Unity bundle with this key.</summary>
    public static bool Opens(ReadOnlySpan<byte> head, byte[] key)
    {
        if (head.Length < UnityFsHead.Length) return false;

        for (var i = 0; i < UnityFsHead.Length; i++)
        {
            if ((head[i] ^ key[i]) != UnityFsHead[i]) return false;
        }

        return true;
    }

    /// <summary>Every logical bundle name in a YooAsset manifest that is safe as a file name, each once, in manifest order.</summary>
    public static IReadOnlyList<string> NamesIn(byte[] manifest)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in ManifestName.Matches(Encoding.Latin1.GetString(manifest)))
        {
            if (IsSafeFileName(match.Value) && seen.Add(match.Value)) names.Add(match.Value);
        }

        return names;
    }

    /// <summary>A logical name becomes the output file name, so it must not name a folder or climb out of one.</summary>
    public static bool IsSafeFileName(string name) =>
        name.Length > 0 && name[0] != '.' && name.IndexOf('/') < 0 && name.IndexOf('\\') < 0 && !name.Contains("..", StringComparison.Ordinal);

    /// <summary>Whether a bundle is wanted: the filter is a comma-separated list of parts of the name; empty means every bundle.</summary>
    public static bool IsWanted(string name, string filter)
    {
        var parts = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0) return true;

        foreach (var part in parts)
        {
            if (name.Contains(part, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
