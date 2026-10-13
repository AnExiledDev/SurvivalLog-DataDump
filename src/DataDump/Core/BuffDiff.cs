using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DataDump.Core;

/// <summary>Buffs gained and lost between two moments, compared by buff config id and written as <c>Name(ID)</c>.</summary>
public sealed record BuffDiff(IReadOnlyList<string> Gained, IReadOnlyList<string> Lost)
{
    /// <param name="start">Buff config id to name at the start.</param>
    /// <param name="end">Buff config id to name at the end.</param>
    public static BuffDiff Between(IReadOnlyDictionary<int, string> start, IReadOnlyDictionary<int, string> end) =>
        new(Missing(end, start), Missing(start, end));

    public static string Label(int id, string name) => name + "(" + id.ToString(CultureInfo.InvariantCulture) + ")";

    private static IReadOnlyList<string> Missing(IReadOnlyDictionary<int, string> from, IReadOnlyDictionary<int, string> other) =>
        from.Where(buff => !other.ContainsKey(buff.Key)).OrderBy(buff => buff.Key).Select(buff => Label(buff.Key, buff.Value)).ToList();
}
