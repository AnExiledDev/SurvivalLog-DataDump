using System;
using System.IO;
using BepInEx.Logging;

namespace DataDump.Adapters;

/// <summary>
/// Writes to the BepInEx log and to an append-only file that survives restarts
/// (LogOutput.log and Player.log are both overwritten on the next launch).
/// Never throws: it is called from inside Load and the game's hooks, where an escaping exception would reach the game.
/// </summary>
internal sealed class DumpLog
{
    private const long TrimAboveBytes = 1024 * 1024;
    private const int KeepBytes = 200 * 1024;

    private readonly ManualLogSource _bepinex;
    private readonly string _path;
    private readonly object _gate = new();

    public DumpLog(ManualLogSource bepinex, string path)
    {
        _bepinex = bepinex;
        _path = path;
    }

    public void Write(string message)
    {
        try
        {
            _bepinex.LogInfo(message);
        }
        catch (Exception)
        {
            // A failing log listener costs the BepInEx copy, never the plugin; the file copy below still gets the line.
        }

        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // A locked or read-only file costs the file copy, never the plugin.
            }
        }
    }

    /// <summary>Past 1 MB only the newest ~200 KB of whole lines is kept.</summary>
    public void TrimIfLarge()
    {
        lock (_gate)
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
}
