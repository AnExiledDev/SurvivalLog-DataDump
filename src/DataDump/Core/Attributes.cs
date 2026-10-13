using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DataDump.Core;

/// <summary>One attribute of one character at one moment, as the game stores it (Logic.Attr).</summary>
/// <param name="Scale">For an attribute the game shows as a fraction (Attr.isSingle), GameKey.Attr_ScalingRatio; else null.</param>
public sealed record AttrSample(int Base, int Strengthening, int Condition, int Min, int Max, float? Scale)
{
    /// <summary>
    /// The value the game uses: base plus strengthening, held to the attribute's bounds, min first
    /// (AttributeComponent.GetTotalValue_Int, rva 0x2de0da0; GetTotalValue, rva 0x2de0ef0). Condition is not part of it.
    /// </summary>
    public int Total
    {
        get
        {
            var sum = unchecked(Base + Strengthening);

            if (sum < Min) return Min;
            if (sum > Max) return Max;

            return sum;
        }
    }

    /// <summary>What the game shows: the total over the scaling ratio for a fraction attribute (GetTotalValue_Float, rva 0x2de0c80).</summary>
    public double Shown => Scale is { } scale ? (float)Total / scale : Total;
}

/// <summary>How one attribute changed. <see cref="ShownChange"/> is set for fraction attributes only.</summary>
public sealed record AttrChange(string Name, int RawChange, double? ShownChange)
{
    public override string ToString()
    {
        var raw = Signed(RawChange.ToString(CultureInfo.InvariantCulture), RawChange);

        if (ShownChange is not { } shown) return Name + " " + raw;

        return Name + " " + raw + " (" + Signed(shown.ToString("R", CultureInfo.InvariantCulture), shown) + ")";
    }

    private static string Signed(string text, double value) => value > 0 ? "+" + text : text;
}

public static class AttrDelta
{
    /// <summary>
    /// Every attribute whose total changed between two samples, by name. An attribute missing from either side is
    /// left out: it has no change to report. The raw change is exact; the shown change is derived from it.
    /// </summary>
    public static IReadOnlyList<AttrChange> Between(IReadOnlyDictionary<string, AttrSample> start, IReadOnlyDictionary<string, AttrSample> end)
    {
        var changes = new List<AttrChange>();

        foreach (var name in end.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!start.TryGetValue(name, out var before)) continue;

            var after = end[name];
            var raw = unchecked(after.Total - before.Total);

            if (raw == 0) continue;

            changes.Add(new AttrChange(name, raw, after.Scale is { } scale ? (double)((float)raw / scale) : null));
        }

        return changes;
    }
}
