using System;
using System.Collections.Generic;
using System.Linq;

namespace DataDump.Core;

/// <summary>A point in the game's world, in meters. Y is up.</summary>
public readonly record struct WorldPoint(float X, float Y, float Z);

/// <summary>
/// How one top-down picture of a map area is taken: an orthographic camera straight above the area's centre, looking
/// down, with the picture's right along world X and its top along world Z. A node lands on the picture at
/// <c>x = (X - MinX) * PixelsPerMeter</c> from the left and <c>y = (MaxZ - Z) * PixelsPerMeter</c> from the top.
/// </summary>
public sealed record MapFrame(float MinX, float MaxX, float MinZ, float MaxZ, float FloorY, int Width, int Height, float PixelsPerMeter)
{
    public const float Margin = 1;

    public float CenterX => (MinX + MaxX) / 2;

    public float CenterZ => (MinZ + MaxZ) / 2;

    /// <summary>Half the picture's height in meters, which is what an orthographic camera's size means.</summary>
    public float HalfHeight => (MaxZ - MinZ) / 2;

    /// <summary>
    /// The frame for a box, with a margin all round. The picture gets <paramref name="pixelsPerMeter"/> unless its
    /// longer side would pass <paramref name="maxPixels"/>; then every meter gets fewer, so the whole area still fits.
    /// Null when the box has no size or is not a number.
    /// </summary>
    public static MapFrame? For(WorldPoint min, WorldPoint max, float floorY, float pixelsPerMeter, int maxPixels)
    {
        var minX = Math.Min(min.X, max.X) - Margin;
        var maxX = Math.Max(min.X, max.X) + Margin;
        var minZ = Math.Min(min.Z, max.Z) - Margin;
        var maxZ = Math.Max(min.Z, max.Z) + Margin;
        var width = maxX - minX;
        var depth = maxZ - minZ;
        var isUsable = width > 0 && depth > 0 && !float.IsInfinity(width) && !float.IsInfinity(depth) && pixelsPerMeter > 0 && maxPixels > 0;

        if (!isUsable) return null;

        var scale = Math.Min(pixelsPerMeter, maxPixels / Math.Max(width, depth));
        var pixelsWide = Math.Clamp((int)Math.Round(width * scale), 1, maxPixels);
        var pixelsHigh = Math.Clamp((int)Math.Round(depth * scale), 1, maxPixels);

        return new MapFrame(minX, maxX, minZ, maxZ, floorY, pixelsWide, pixelsHigh, scale);
    }

    /// <summary>
    /// The floor of an area: the median height of the game's nodes inside it (furniture spots stand on the floor), or
    /// <paramref name="fallback"/> when none is inside. The median keeps one node on a shelf or a roof from moving it.
    /// </summary>
    public static float FloorOf(IEnumerable<WorldPoint> nodes, WorldPoint min, WorldPoint max, float fallback)
    {
        var heights = nodes
            .Where(node => IsBetween(node.X, min.X, max.X) && IsBetween(node.Z, min.Z, max.Z))
            .Select(node => node.Y)
            .OrderBy(height => height)
            .ToList();

        return heights.Count == 0 ? fallback : heights[heights.Count / 2];
    }

    /// <summary>The box round every node, for a map that names no areas of its own; null when there are no nodes.</summary>
    public static (WorldPoint Min, WorldPoint Max)? BoundsOf(IEnumerable<WorldPoint> nodes)
    {
        var list = nodes.ToList();

        if (list.Count == 0) return null;

        var min = new WorldPoint(list.Min(node => node.X), list.Min(node => node.Y), list.Min(node => node.Z));
        var max = new WorldPoint(list.Max(node => node.X), list.Max(node => node.Y), list.Max(node => node.Z));

        return (min, max);
    }

    private static bool IsBetween(float value, float a, float b) => value >= Math.Min(a, b) && value <= Math.Max(a, b);
}
