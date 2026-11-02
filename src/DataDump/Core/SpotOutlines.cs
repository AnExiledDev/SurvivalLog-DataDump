using System;
using System.Collections.Generic;
using System.Linq;

namespace DataDump.Core;

/// <summary>A point on the floor plan, in world meters: X and Z, with height dropped.</summary>
public readonly record struct FloorPoint(float X, float Z);

/// <summary>
/// One furniture or building spot as the game's layout view draws it: the box the game shows for it, seen from above.
/// Pivot is the spot's own position (the one the map data lists), Center the middle of its box; the two differ for
/// spots whose box sits beside their pivot.
/// </summary>
public sealed record SpotOutline(string Name, int Type, WorldPoint Pivot, WorldPoint Center, float MinY, float MaxY, IReadOnlyList<FloorPoint> Outline);

public static class SpotOutlines
{
    /// <summary>Positions are written to the millimeter, so a file read twice from the same game is the same file.</summary>
    private const int Digits = 3;

    /// <summary>A spot from its pivot and the eight world corners of its box.</summary>
    public static SpotOutline Of(string name, int type, WorldPoint pivot, IReadOnlyList<WorldPoint> corners)
    {
        if (corners.Count == 0) throw new ArgumentException("a box has corners", nameof(corners));

        var center = new WorldPoint(corners.Average(c => c.X), corners.Average(c => c.Y), corners.Average(c => c.Z));

        return new SpotOutline(name, type, Round(pivot), Round(center), Round(corners.Min(c => c.Y)), Round(corners.Max(c => c.Y)), Hull(corners.Select(c => new FloorPoint(c.X, c.Z))));
    }

    /// <summary>
    /// The outline of points seen from above, counter-clockwise from the lowest X (then lowest Z), with points on a
    /// straight edge left out. A box turned only about the vertical gives its four floor corners.
    /// </summary>
    public static IReadOnlyList<FloorPoint> Hull(IEnumerable<FloorPoint> points)
    {
        var sorted = points.Select(Round).Distinct().OrderBy(p => p.X).ThenBy(p => p.Z).ToList();

        if (sorted.Count < 3) return sorted;

        var hull = new List<FloorPoint>();

        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            var start = hull.Count;

            foreach (var point in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);

                hull.Add(point);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    /// <summary>Above zero when a, b, c turn counter-clockwise.</summary>
    private static double Cross(FloorPoint a, FloorPoint b, FloorPoint c) =>
        ((double)b.X - a.X) * ((double)c.Z - a.Z) - ((double)b.Z - a.Z) * ((double)c.X - a.X);

    private static float Round(float value) => MathF.Round(value, Digits);

    private static FloorPoint Round(FloorPoint point) => new(Round(point.X), Round(point.Z));

    private static WorldPoint Round(WorldPoint point) => new(Round(point.X), Round(point.Y), Round(point.Z));
}
