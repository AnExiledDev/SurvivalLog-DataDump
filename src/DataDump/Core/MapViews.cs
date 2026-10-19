using System;
using System.Collections.Generic;

namespace DataDump.Core;

/// <summary>
/// What the game is showing right now: the camera area it keeps the view in and the floor it shows. The game only
/// draws the area the player is at and only the floor being viewed, so a picture is only worth taking of this.
/// </summary>
public sealed record MapView(string Location, MapArea Area, int FloorId, float FloorY)
{
    /// <summary>The picture's file name without its mode: the area, and the floor when the location has floors.</summary>
    public string Stem => FloorId > 0 ? FileNames.Safe(Area.Name) + ".floor" + FloorId : FileNames.Safe(Area.Name);

    /// <summary>One key per picture place, so each is taken once and a retake replaces only its own files.</summary>
    public string Key => Location + "/" + Stem;
}

public static class MapViews
{
    /// <summary>How far, in meters, the game camera's box may be from a camera area of the map and still be it.</summary>
    public const float MatchTolerance = 1;

    /// <summary>How far outside an area a node may stand and still be drawn in its picture; some shelves stand just past it.</summary>
    public const float NearReach = 5;

    public const string UnknownArea = "view";

    /// <summary>The map's camera area the game camera's box is, or null when none is within <see cref="MatchTolerance"/>.</summary>
    public static MapArea? AreaOf(IReadOnlyList<MapArea> areas, WorldPoint min, WorldPoint max)
    {
        MapArea? best = null;
        var bestDistance = float.MaxValue;

        foreach (var area in areas)
        {
            var distance = BoxDistance(area, min, max);

            if (distance > MatchTolerance || distance >= bestDistance) continue;

            best = area;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// The box a picture of an area covers: the area grown to hold the nodes standing near it. A map with a single
    /// area (an expedition) is grown to hold every node, as its one area is often far smaller than the place.
    /// </summary>
    public static MapArea Cover(MapArea area, int areaCount, IReadOnlyList<WorldPoint> nodes)
    {
        var reach = areaCount <= 1 ? float.PositiveInfinity : NearReach;
        float minX = Math.Min(area.Min.X, area.Max.X), maxX = Math.Max(area.Min.X, area.Max.X);
        float minZ = Math.Min(area.Min.Z, area.Max.Z), maxZ = Math.Max(area.Min.Z, area.Max.Z);
        float grownMinX = minX, grownMaxX = maxX, grownMinZ = minZ, grownMaxZ = maxZ;

        foreach (var node in nodes)
        {
            var outside = Math.Max(Math.Max(minX - node.X, node.X - maxX), Math.Max(minZ - node.Z, node.Z - maxZ));

            if (outside > reach) continue;

            grownMinX = Math.Min(grownMinX, node.X);
            grownMaxX = Math.Max(grownMaxX, node.X);
            grownMinZ = Math.Min(grownMinZ, node.Z);
            grownMaxZ = Math.Max(grownMaxZ, node.Z);
        }

        return area with { Min = new WorldPoint(grownMinX, area.Min.Y, grownMinZ), Max = new WorldPoint(grownMaxX, area.Max.Y, grownMaxZ) };
    }

    /// <summary>
    /// True when a picture came out empty: every sampled pixel is the black the camera clears to. That is what the
    /// game gives for a place it is not drawing, and such a picture must not replace a good one.
    /// </summary>
    public static bool IsBlank(IEnumerable<(byte R, byte G, byte B)> samples)
    {
        const int darkest = 2;

        foreach (var (r, g, b) in samples)
        {
            if (r > darkest || g > darkest || b > darkest) return false;
        }

        return true;
    }

    private static float BoxDistance(MapArea area, WorldPoint min, WorldPoint max)
    {
        var corners = new[]
        {
            Math.Abs(Math.Min(area.Min.X, area.Max.X) - Math.Min(min.X, max.X)),
            Math.Abs(Math.Max(area.Min.X, area.Max.X) - Math.Max(min.X, max.X)),
            Math.Abs(Math.Min(area.Min.Z, area.Max.Z) - Math.Min(min.Z, max.Z)),
            Math.Abs(Math.Max(area.Min.Z, area.Max.Z) - Math.Max(min.Z, max.Z)),
        };

        return Math.Max(Math.Max(corners[0], corners[1]), Math.Max(corners[2], corners[3]));
    }
}

/// <summary>
/// When to take the pictures of what the game shows on its own: once the view has stayed on one place for a few
/// seconds, once a session. An empty picture is tried again a little later, a few times, as the place may still be
/// loading or the game paused on a black screen.
/// </summary>
public sealed class AutoViews
{
    public const float SettleSeconds = 5;
    public const float RetrySeconds = 10;
    public const int MaxTries = 3;

    private readonly Dictionary<string, (int Tries, float NextAt)> _tried = new(StringComparer.Ordinal);
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);
    private string? _current;
    private float _since;

    /// <summary>True when the pictures of <paramref name="key"/>, the place on view now, are due.</summary>
    public bool IsDue(string? key, float now)
    {
        if (key != _current)
        {
            _current = key;
            _since = now;
        }

        if (key is null || _done.Contains(key) || now - _since < SettleSeconds) return false;

        return !_tried.TryGetValue(key, out var tried) || (tried.Tries < MaxTries && now >= tried.NextAt);
    }

    /// <summary>Records a try: a good picture ends the place's tries, an empty one waits for the next.</summary>
    public void Record(string key, bool isGood, float now)
    {
        if (isGood)
        {
            _done.Add(key);

            return;
        }

        var tries = _tried.TryGetValue(key, out var tried) ? tried.Tries + 1 : 1;

        _tried[key] = (tries, now + RetrySeconds);
    }

    /// <summary>True once a place has had all its tries and is still empty, so the log says so once.</summary>
    public bool HasGivenUp(string key) => !_done.Contains(key) && _tried.TryGetValue(key, out var tried) && tried.Tries >= MaxTries;
}
