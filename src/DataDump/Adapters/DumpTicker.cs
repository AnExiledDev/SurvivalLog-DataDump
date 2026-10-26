using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    /// <summary>Run when the game quits, before Unity tears down: waits a moment for the files still being written.</summary>
    internal static Action OnQuit = () => { };

    private static readonly HashSet<string> ReportedFailures = new();

    // Time.realtimeSinceStartup through interop boxes its answer on every call; a Stopwatch reads the same clock for free.
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public DumpTicker(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        var now = (float)Clock.Elapsed.TotalSeconds;

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

    private void OnApplicationQuit()
    {
        try
        {
            OnQuit();
        }
        catch (Exception)
        {
            // Quitting goes on whatever happens here.
        }
    }

    private static void ReportFailure(Exception ex)
    {
        if (ReportedFailures.Count >= MaxReportedFailures || !ReportedFailures.Add(ex.GetType().FullName + ": " + ex.Message)) return;

        Log("update failed (each distinct failure reported once): " + ex);

        if (ReportedFailures.Count == MaxReportedFailures) Log($"{MaxReportedFailures} distinct update failures reported; further ones are not logged");
    }
}
