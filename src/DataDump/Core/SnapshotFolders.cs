using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DataDump.Core;

/// <summary>
/// Names snapshot folders by the local time they were taken (yyyyMMdd-HHmmss, with -2, -3 for more in one second)
/// and picks the oldest to delete past the cap. Only folders with that name shape are ever picked, so nothing else
/// someone put in the folder is touched.
/// </summary>
public static class SnapshotFolders
{
    public const int Cap = 50;

    private static readonly Regex NameShape = new(@"^([0-9]{8}-[0-9]{6})(?:-([0-9]+))?$", RegexOptions.CultureInvariant);

    public static string NameFor(DateTime localTime, Func<string, bool> isTaken)
    {
        var name = localTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        if (!isTaken(name)) return name;

        for (var suffix = 2; ; suffix++)
        {
            var numbered = name + "-" + suffix.ToString(CultureInfo.InvariantCulture);

            if (!isTaken(numbered)) return numbered;
        }
    }

    /// <returns>The snapshot folders to delete, oldest first, so that <paramref name="keep"/> remain.</returns>
    public static IReadOnlyList<string> ToDelete(IEnumerable<string> folderNames, int keep = Cap)
    {
        var snapshots = folderNames
            .Select(name => (Name: name, Match: NameShape.Match(name)))
            .Where(folder => folder.Match.Success)
            .OrderBy(folder => folder.Match.Groups[1].Value, StringComparer.Ordinal)
            .ThenBy(folder => Suffix(folder.Match))
            .Select(folder => folder.Name)
            .ToList();

        return snapshots.Take(Math.Max(0, snapshots.Count - keep)).ToList();
    }

    /// <summary>The unnumbered folder of a second came first; a number too long to read sorts last.</summary>
    private static int Suffix(Match name)
    {
        if (!name.Groups[2].Success) return 1;

        return int.TryParse(name.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) ? suffix : int.MaxValue;
    }
}
