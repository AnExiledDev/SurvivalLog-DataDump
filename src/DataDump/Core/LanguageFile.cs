using System.Collections.Generic;
using System.IO;

namespace DataDump.Core;

/// <summary>
/// A language map as one JSON object sorted by key, so two dumps diff line by line. The entries are read from the game
/// first; the sort and the text are plain .NET work, done off the game's frame.
/// </summary>
public static class LanguageFile
{
    /// <returns>How many entries were written.</returns>
    public static int Write(TextWriter writer, List<KeyValuePair<string, string?>> entries)
    {
        var shape = FileShape.JsonObject;
        var count = 0;

        entries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        writer.Write(shape.Start);

        foreach (var entry in entries)
        {
            writer.Write(shape.Before(count));
            writer.Write(FileShape.Entry(entry.Key, entry.Value));
            count++;
        }

        writer.Write(shape.Finish(count));

        return count;
    }
}
