using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DataDump.Adapters;

/// <summary>Hooking and failure logging shared by the hooks. A hook that cannot be placed costs its feature, never the game.</summary>
internal static class PatchKit
{
    private static readonly HashSet<string> ReportedFailures = new();

    public static Action<string> Log { get; set; } = _ => { };

    /// <summary>Hooks one game method; throws when the method is not there, so the caller can give up on its feature.</summary>
    public static void Patch(Harmony harmony, Type hooks, Type type, string name, Type[]? arguments, string? prefix = null, string? postfix = null)
    {
        var target = (arguments == null ? AccessTools.Method(type, name) : AccessTools.Method(type, name, arguments))
                     ?? throw new MissingMethodException(type.Name + "." + name);

        harmony.Patch(
            target,
            prefix: prefix == null ? null : new HarmonyMethod(hooks, prefix),
            postfix: postfix == null ? null : new HarmonyMethod(hooks, postfix));
    }

    /// <summary>
    /// Runs from hooks the game calls again and again, so each <paramref name="doing"/> is written once: a failure that
    /// repeats cannot flood the log. Give every hook its own <paramref name="doing"/>, or the first hook to fail hides
    /// the others' failures.
    /// </summary>
    public static void Fail(string doing, Exception ex)
    {
        lock (ReportedFailures)
        {
            if (!ReportedFailures.Add(doing)) return;
        }

        try
        {
            Log($"failed {doing} (reported once): {ex}");
        }
        catch (Exception)
        {
            // Fail runs inside the game's own calls; a logger that throws must not carry the exception into them.
        }
    }
}
