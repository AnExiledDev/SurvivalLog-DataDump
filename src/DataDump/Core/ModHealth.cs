using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DataDump.Core;

public sealed record PluginEntry(string Guid, string Name, string Version, string Dll, bool HasInstance);

public enum PatchKind
{
    Prefix,
    Postfix,
    Transpiler,
    Finalizer,
}

/// <param name="Method">The patch method, as Type.Method.</param>
public sealed record PatchEntry(string Owner, PatchKind Kind, string Method, int Priority);

/// <param name="Target">The patched game method, as Type.Method(parameters).</param>
public sealed record PatchedMethod(string Target, IReadOnlyList<PatchEntry> Patches);

public sealed record HealthInput(
    DateTimeOffset At,
    string Trigger,
    IReadOnlyList<PluginEntry> Plugins,
    IReadOnlyList<string> LoadErrors,
    IReadOnlyList<PatchedMethod> Methods,
    IReadOnlyList<string> Problems);

/// <summary>
/// mod-health.json and mod-health.txt: the loaded plugins, the ones that failed, every Harmony-patched method with
/// each owner's patches, and the methods more than one owner patches, which is where two mods can clash.
/// </summary>
public static class ModHealthReport
{
    /// <summary>Harmony's Priority.Normal; only a patch that asks for another order shows its priority.</summary>
    public const int NormalPriority = 400;

    public sealed record OwnerPatches(IReadOnlyList<string> Prefixes, IReadOnlyList<string> Postfixes, IReadOnlyList<string> Transpilers, IReadOnlyList<string> Finalizers);

    public sealed record MethodReport(string Target, SortedDictionary<string, OwnerPatches> Owners);

    public sealed record SharedMethod(string Target, IReadOnlyList<string> Owners);

    public sealed record Document(
        string At,
        string Trigger,
        IReadOnlyList<PluginEntry> Plugins,
        IReadOnlyList<string> LoadErrors,
        IReadOnlyList<SharedMethod> Shared,
        IReadOnlyList<MethodReport> PatchedMethods,
        IReadOnlyList<string> Problems);

    public static Document Build(HealthInput input)
    {
        var methods = input.Methods
            .OrderBy(method => method.Target, StringComparer.Ordinal)
            .Select(method => new MethodReport(method.Target, ByOwner(method.Patches)))
            .ToList();

        return new Document(
            input.At.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            input.Trigger,
            input.Plugins.OrderBy(plugin => plugin.Guid, StringComparer.Ordinal).ToList(),
            input.LoadErrors,
            methods.Where(method => method.Owners.Count > 1).Select(method => new SharedMethod(method.Target, method.Owners.Keys.ToList())).ToList(),
            methods,
            input.Problems);
    }

    public static string ToJson(Document document) => new ValueWriter(new PlainValueReader(), 8, isIndented: true).Write(document);

    public static string ToText(Document document)
    {
        var text = new StringBuilder();

        text.Append("Mod health at ").Append(document.At).Append(" (").Append(document.Trigger).Append(")\n\n");
        AppendPlugins(text, document.Plugins);
        AppendList(text, "Failed to load", document.LoadErrors);
        AppendShared(text, document.Shared);
        AppendMethods(text, document.PatchedMethods);
        AppendList(text, "Could not read", document.Problems);

        return text.ToString();
    }

    private static SortedDictionary<string, OwnerPatches> ByOwner(IReadOnlyList<PatchEntry> patches)
    {
        var owners = new SortedDictionary<string, OwnerPatches>(StringComparer.Ordinal);

        foreach (var owner in patches.Select(patch => patch.Owner).Distinct())
        {
            var mine = patches.Where(patch => patch.Owner == owner).ToList();

            owners[owner] = new OwnerPatches(Names(mine, PatchKind.Prefix), Names(mine, PatchKind.Postfix), Names(mine, PatchKind.Transpiler), Names(mine, PatchKind.Finalizer));
        }

        return owners;
    }

    private static IReadOnlyList<string> Names(IEnumerable<PatchEntry> patches, PatchKind kind) =>
        patches.Where(patch => patch.Kind == kind).Select(Describe).ToList();

    private static string Describe(PatchEntry patch) =>
        patch.Priority == NormalPriority ? patch.Method : patch.Method + " [priority " + patch.Priority.ToString(CultureInfo.InvariantCulture) + "]";

    private static void AppendPlugins(StringBuilder text, IReadOnlyList<PluginEntry> plugins)
    {
        text.Append("Plugins (").Append(plugins.Count).Append("):\n");

        foreach (var plugin in plugins)
        {
            text.Append("  ").Append(plugin.Guid).Append("  ").Append(plugin.Name).Append(' ').Append(plugin.Version).Append("  ").Append(plugin.Dll);

            if (!plugin.HasInstance) text.Append("  (no instance)");

            text.Append('\n');
        }

        text.Append('\n');
    }

    private static void AppendShared(StringBuilder text, IReadOnlyList<SharedMethod> shared)
    {
        text.Append("Patched by more than one owner (").Append(shared.Count).Append("):\n");

        foreach (var method in shared) text.Append("  ").Append(method.Target).Append(": ").Append(string.Join(", ", method.Owners)).Append('\n');

        text.Append('\n');
    }

    private static void AppendMethods(StringBuilder text, IReadOnlyList<MethodReport> methods)
    {
        text.Append("Patched methods (").Append(methods.Count).Append("):\n");

        foreach (var method in methods)
        {
            text.Append("  ").Append(method.Target).Append('\n');

            foreach (var owner in method.Owners)
            {
                text.Append("    ").Append(owner.Key).Append(':');
                AppendKind(text, "prefix", owner.Value.Prefixes);
                AppendKind(text, "postfix", owner.Value.Postfixes);
                AppendKind(text, "transpiler", owner.Value.Transpilers);
                AppendKind(text, "finalizer", owner.Value.Finalizers);
                text.Append('\n');
            }
        }

        text.Append('\n');
    }

    private static void AppendKind(StringBuilder text, string kind, IReadOnlyList<string> names)
    {
        foreach (var name in names) text.Append(' ').Append(kind).Append(' ').Append(name).Append(';');
    }

    private static void AppendList(StringBuilder text, string title, IReadOnlyList<string> lines)
    {
        text.Append(title).Append(" (").Append(lines.Count).Append("):\n");

        foreach (var line in lines) text.Append("  ").Append(line).Append('\n');

        text.Append('\n');
    }
}
