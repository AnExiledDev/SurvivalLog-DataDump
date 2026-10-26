using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using BepInEx.Logging;
using DataDump.Core;

namespace DataDump.Adapters;

/// <summary>
/// Writes to the BepInEx log and to an append-only file that survives restarts
/// (LogOutput.log and Player.log are both overwritten on the next launch).
/// Never throws: it is called from inside Load and the game's hooks, where an escaping exception would reach the game.
/// The file lines are written by <see cref="Worker"/>, in order, so the game's frame never waits on the disk; it is
/// also the worker the plugin's other files are written by. A line from another thread waits for the main thread's
/// next <see cref="Drain"/>, so only the main thread ever calls BepInEx's log listeners.
/// </summary>
internal sealed class DumpLog
{
    private const long TrimAboveBytes = 1024 * 1024;
    private const int KeepBytes = 200 * 1024;

    private readonly ManualLogSource _bepinex;
    private readonly string _path;
    private readonly ConcurrentQueue<(DateTime At, string Message)> _fromOtherThreads = new();
    private int _mainThread = Environment.CurrentManagedThreadId;

    public DumpLog(ManualLogSource bepinex, string path)
    {
        _bepinex = bepinex;
        _path = path;
        Worker = new SerialWorker(ex => Write("a background file write failed: " + ex));
    }

    public SerialWorker Worker { get; }

    /// <summary>Any thread.</summary>
    public void Write(string message)
    {
        var at = DateTime.Now;

        if (Environment.CurrentManagedThreadId != Volatile.Read(ref _mainThread))
        {
            _fromOtherThreads.Enqueue((at, message));

            return;
        }

        WriteNow(at, message);
    }

    /// <summary>The ticker's check: writes the lines other threads left. Costs nothing while there are none.</summary>
    public void Drain(float now)
    {
        Volatile.Write(ref _mainThread, Environment.CurrentManagedThreadId);

        if (_fromOtherThreads.IsEmpty) return;

        while (_fromOtherThreads.TryDequeue(out var line)) WriteNow(line.At, line.Message);
    }

    /// <summary>Past 1 MB only the newest ~200 KB of whole lines is kept. Runs on the worker, ahead of every line written after it.</summary>
    public void TrimIfLarge() => Worker.Post(TrimNow);

    private void WriteNow(DateTime at, string message)
    {
        try
        {
            if (HookFailure.Is(message)) _bepinex.LogError(message);
            else _bepinex.LogInfo(message);
        }
        catch (Exception)
        {
            // A failing log listener costs the BepInEx copy, never the plugin; the file copy below still gets the line.
        }

        Worker.Post(() => AppendQuietly(at, message));
    }

    private void AppendQuietly(DateTime at, string message)
    {
        try
        {
            File.AppendAllText(_path, $"{at:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // A locked or read-only file costs the file copy, never the plugin.
        }
    }

    private void TrimNow()
    {
        try
        {
            var file = new FileInfo(_path);

            if (!file.Exists || file.Length <= TrimAboveBytes) return;

            var bytes = File.ReadAllBytes(_path);
            var cut = bytes.Length - KeepBytes;
            var nextLine = Array.IndexOf(bytes, (byte)'\n', cut) + 1;

            File.WriteAllBytes(_path, bytes[(nextLine > 0 ? nextLine : cut)..]);
        }
        catch (Exception)
        {
            // An oversized log is harmless; failing to trim it must not stop the plugin loading.
        }
    }
}
