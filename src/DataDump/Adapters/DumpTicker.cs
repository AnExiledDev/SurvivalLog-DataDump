using System;
using System.Collections.Generic;
using UnityEngine;

namespace DataDump.Adapters;

/// <summary>
/// The Unity object that hands frames to the plugin's checks. Each check runs on its own, so one that fails cannot stop
/// the others. Each distinct failure is logged once, never rethrown into the game; a failure that repeats every frame
/// would otherwise flood the log.
/// </summary>
public sealed class DumpTicker : MonoBehaviour
{
    private const int MaxReportedFailures = 20;

    internal static readonly List<Action<float>> Checks = new();
    internal static Action<string> Log = _ => { };
    private static readonly HashSet<string> ReportedFailures = new();

    public DumpTicker(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        var now = Time.realtimeSinceStartup;

        foreach (var check in Checks)
        {
            try
            {
                check(now);
            }
            catch (Exception ex)
            {
                ReportFailure(ex);
            }
        }
    }

    private static void ReportFailure(Exception ex)
    {
        if (ReportedFailures.Count >= MaxReportedFailures || !ReportedFailures.Add(ex.GetType().FullName + ": " + ex.Message)) return;

        Log("update failed (each distinct failure reported once): " + ex);

        if (ReportedFailures.Count == MaxReportedFailures) Log($"{MaxReportedFailures} distinct update failures reported; further ones are not logged");
    }
}
