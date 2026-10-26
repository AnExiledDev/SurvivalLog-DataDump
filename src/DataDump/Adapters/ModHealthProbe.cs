using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using DataDump.Core;
using HarmonyLib;

namespace DataDump.Adapters;

/// <summary>
/// Writes mod-health.json and mod-health.txt: what BepInEx loaded, what it refused to load, and every method Harmony has
/// patched, with each owner's patches. Reads BepInEx and Harmony only, never the game.
/// </summary>
internal sealed class ModHealthProbe
{
    public const string JsonFile = "mod-health.json";
    public const string TextFile = "mod-health.txt";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Action<string> _log;
    private readonly SerialWorker _worker;

    public ModHealthProbe(Action<string> log, SerialWorker worker)
    {
        _log = log;
        _worker = worker;
    }

    /// <summary>Reads and writes on the worker: every patched method of every mod is a lot of reflection for one frame.</summary>
    public void Post(string trigger, params string[] folders) => _worker.Post(() => WriteTo(trigger, folders));

    private void WriteTo(string trigger, string[] folders)
    {
        var document = ModHealthReport.Build(Read(trigger));
        var json = ModHealthReport.ToJson(document) + "\n";
        var text = ModHealthReport.ToText(document);

        foreach (var folder in folders)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, JsonFile), json, Utf8);
                File.WriteAllText(Path.Combine(folder, TextFile), text, Utf8);
            }
            catch (Exception ex)
            {
                _log($"mod health: could not write to {folder}: {ProblemList.Reason(ex)}");
            }
        }

        _log($"mod health ({trigger}): {document.Plugins.Count} plugins, {document.LoadErrors.Count} load errors, " +
             $"{document.PatchedMethods.Count} patched methods, {document.Shared.Count} patched by more than one owner");
    }

    private static HealthInput Read(string trigger)
    {
        var problems = new ProblemList();
        var plugins = problems.Object("the loaded plugins", () => Plugins()) ?? new List<PluginEntry>();
        var errors = problems.Object("the load errors", () => LoadErrors()) ?? new List<string>();
        var methods = problems.Object("the patched methods", () => PatchedMethods(problems)) ?? new List<PatchedMethod>();

        return new HealthInput(DateTimeOffset.Now, trigger, plugins, errors, methods, problems.Lines);
    }

    private static List<PluginEntry> Plugins() =>
        IL2CPPChainloader.Instance.Plugins.Values
            .Select(info => new PluginEntry(info.Metadata.GUID, info.Metadata.Name, VersionOf(info.Metadata), Path.GetFileName(info.Location ?? ""), info.Instance is not null))
            .ToList();

    /// <summary>Read by name: the version's type lives in a library this plugin does not otherwise need.</summary>
    private static string VersionOf(BepInPlugin metadata) =>
        metadata.GetType().GetProperty("Version", BindingFlags.Public | BindingFlags.Instance)?.GetValue(metadata)?.ToString() ?? "";

    /// <summary>BepInEx's messages for every plugin it did not load: a missing or too old dependency, an incompatibility.</summary>
    private static List<string> LoadErrors() => IL2CPPChainloader.Instance.DependencyErrors.ToList();

    private static List<PatchedMethod> PatchedMethods(ProblemList problems)
    {
        var methods = new List<PatchedMethod>();

        foreach (var method in Harmony.GetAllPatchedMethods().ToList())
        {
            var target = Describe(method);
            var info = problems.Object(target, () => Harmony.GetPatchInfo(method));

            if (info is null) continue;

            var patches = new List<PatchEntry>();

            AddPatches(patches, info.Prefixes, PatchKind.Prefix);
            AddPatches(patches, info.Postfixes, PatchKind.Postfix);
            AddPatches(patches, info.Transpilers, PatchKind.Transpiler);
            AddPatches(patches, info.Finalizers, PatchKind.Finalizer);
            methods.Add(new PatchedMethod(target, patches));
        }

        return methods;
    }

    private static void AddPatches(List<PatchEntry> entries, IEnumerable<Patch>? patches, PatchKind kind)
    {
        if (patches is null) return;

        foreach (var patch in patches) entries.Add(new PatchEntry(patch.owner, kind, PatchName(patch.PatchMethod), patch.priority));
    }

    /// <summary>The patched method as Type.Method(parameter types), so overloads stay apart.</summary>
    private static string Describe(MethodBase method)
    {
        var parameters = string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name));

        return $"{method.DeclaringType?.FullName ?? "?"}.{method.Name}({parameters})";
    }

    private static string PatchName(MethodInfo? method) => method is null ? "?" : $"{method.DeclaringType?.Name ?? "?"}.{method.Name}";
}
