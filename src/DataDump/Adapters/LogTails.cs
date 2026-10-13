using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using DataDump.Core;
using UnityEngine;

namespace DataDump.Adapters;

/// <summary>
/// Copies the end of every log a bug report needs into a snapshot: each BepInEx/*.log (LogOutput.log and every mod's
/// own log) and the game's Player.log. The game and the mods are still writing them, so each is opened shared and only
/// its last megabyte is read.
/// </summary>
internal static class LogTails
{
    private const int TailBytes = 1024 * 1024;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void CopyTo(string folder, Action<string> log)
    {
        Directory.CreateDirectory(folder);

        foreach (var path in Sources(log))
        {
            var name = FileNames.Safe(Path.GetFileName(path));

            try
            {
                var (text, isCut) = ReadTail(path);

                File.WriteAllText(Path.Combine(folder, name), LogTail.LastLines(text, LogTail.LineCount, isCut), Utf8);
            }
            catch (Exception ex)
            {
                log($"snapshot: could not copy the end of {path}: {ProblemList.Reason(ex)}");
            }
        }
    }

    private static IEnumerable<string> Sources(Action<string> log)
    {
        var paths = new List<string>();

        try
        {
            paths.AddRange(Directory.GetFiles(Paths.BepInExRootPath, "*.log").OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            log("snapshot: could not list the BepInEx logs: " + ProblemList.Reason(ex));
        }

        try
        {
            var playerLog = Application.consoleLogPath;

            if (!string.IsNullOrEmpty(playerLog) && File.Exists(playerLog)) paths.Add(playerLog);
        }
        catch (Exception ex)
        {
            log("snapshot: could not find the game's Player.log: " + ProblemList.Reason(ex));
        }

        return paths;
    }

    /// <returns>The last <see cref="TailBytes"/> of the file, and whether that starts part way into it.</returns>
    private static (string Text, bool IsCut) ReadTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - TailBytes);

        stream.Seek(start, SeekOrigin.Begin);

        using var reader = new StreamReader(stream, Utf8);

        return (reader.ReadToEnd(), start > 0);
    }
}
