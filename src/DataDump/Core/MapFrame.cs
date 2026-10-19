using System;
using System.Collections.Generic;
using System.Linq;

namespace DataDump.Core;

/// <summary>A point in the game's world, in meters. Y is up.</summary>
public readonly record struct WorldPoint(float X, float Y, float Z);

/// <summary>One piece of a picture, rendered on its own: its pixels (counted from the bottom left) and its centre in the frame.</summary>
public readonly record struct MapTile(int PixelX, int PixelY, int Width, int Height, float CenterU, float CenterV);

/// <summary>
/// How one top-down picture is taken: an orthographic camera straight above, looking down, turned <see cref="Yaw"/>
/// degrees about the vertical so a building standing at that angle lies square in the picture. Picture right is the
/// frame's U axis and picture up its V axis: <c>U = X cos(Yaw) - Z sin(Yaw)</c>, <c>V = X sin(Yaw) + Z cos(Yaw)</c>.
/// A node lands on the picture at <c>x = (U - MinU) * PixelsPerMeter</c> from the left and
/// <c>y = (MaxV - V) * PixelsPerMeter</c> from the top.
/// </summary>
public sealed record MapFrame(float Yaw, float MinU, float MaxU, float MinV, float MaxV, float FloorY, int Width, int Height, float PixelsPerMeter)
{
    /// <summary>Meters added all round; a building's walls often stand past its camera area.</summary>
    public const float Margin = 3;

    public float CenterU => (MinU + MaxU) / 2;

    public float CenterV => (MinV + MaxV) / 2;

    /// <summary>Half the picture's height in meters, which is what an orthographic camera's size means.</summary>
    public float HalfHeight => (MaxV - MinV) / 2;

    public (float U, float V) FrameOf(float x, float z) => FrameOf(x, z, Yaw);

    public (float X, float Z) WorldOf(float u, float v)
    {
        var (cos, sin) = Turn(Yaw);

        return (u * cos + v * sin, -u * sin + v * cos);
    }

    /// <summary>
    /// The frame holding every point, turned <paramref name="yaw"/> degrees, with a margin all round. The picture gets
    /// <paramref name="pixelsPerMeter"/> unless its longer side would pass <paramref name="maxPixels"/>; then every
    /// meter gets fewer, so the whole area still fits. Null when the points have no size or are not numbers.
    /// </summary>
    public static MapFrame? For(IReadOnlyList<WorldPoint> points, float yaw, float floorY, float pixelsPerMeter, int maxPixels)
    {
        if (points.Count == 0 || pixelsPerMeter <= 0 || maxPixels <= 0) return null;

        var turned = points.Select(point => FrameOf(point.X, point.Z, yaw)).ToList();
        var minU = turned.Min(point => point.U) - Margin;
        var maxU = turned.Max(point => point.U) + Margin;
        var minV = turned.Min(point => point.V) - Margin;
        var maxV = turned.Max(point => point.V) + Margin;
        var width = maxU - minU;
        var depth = maxV - minV;
        var isUsable = width > 0 && depth > 0 && float.IsFinite(width) && float.IsFinite(depth);

        if (!isUsable) return null;

        var scale = Math.Min(pixelsPerMeter, maxPixels / Math.Max(width, depth));
        var pixelsWide = Math.Clamp((int)Math.Round(width * scale), 1, maxPixels);
        var pixelsHigh = Math.Clamp((int)Math.Round(depth * scale), 1, maxPixels);

        // The far edges follow the whole pixels, so a position lands on the same pixel counted from either side.
        return new MapFrame(yaw, minU, minU + pixelsWide / scale, minV, minV + pixelsHigh / scale, floorY, pixelsWide, pixelsHigh, scale);
    }

    /// <summary>The picture cut into pieces of at most <paramref name="size"/> pixels a side, row by row from the bottom left.</summary>
    public IReadOnlyList<MapTile> Tiles(int size)
    {
        var tiles = new List<MapTile>();

        for (var y = 0; y < Height; y += size)
        {
            for (var x = 0; x < Width; x += size)
            {
                var width = Math.Min(size, Width - x);
                var height = Math.Min(size, Height - y);

                tiles.Add(new MapTile(x, y, width, height, MinU + (x + width / 2f) / PixelsPerMeter, MinV + (y + height / 2f) / PixelsPerMeter));
            }
        }

        return tiles;
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

    private static (float U, float V) FrameOf(float x, float z, float yaw)
    {
        var (cos, sin) = Turn(yaw);

        return (x * cos - z * sin, x * sin + z * cos);
    }

    private static (float Cos, float Sin) Turn(float yaw)
    {
        var radians = yaw * MathF.PI / 180;

        return (MathF.Cos(radians), MathF.Sin(radians));
    }

    private static bool IsBetween(float value, float a, float b) => value >= Math.Min(a, b) && value <= Math.Max(a, b);
}
