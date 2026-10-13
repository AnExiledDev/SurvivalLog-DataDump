using System;
using System.Collections.Generic;
using System.Linq;

namespace DataDump.Core;

/// <summary>The last lines of a log, from the end of the file only, since the game's logs can be large.</summary>
public static class LogTail
{
    public const int LineCount = 300;

    /// <param name="text">The end of a log file.</param>
    /// <param name="isCutFromLongerFile">True when <paramref name="text"/> starts part way into the file, so its first line is partial and dropped.</param>
    public static string LastLines(string text, int count, bool isCutFromLongerFile)
    {
        var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));

        if (isCutFromLongerFile) lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return "";

        return string.Join("\n", lines.Skip(Math.Max(0, lines.Count - count))) + "\n";
    }
}
