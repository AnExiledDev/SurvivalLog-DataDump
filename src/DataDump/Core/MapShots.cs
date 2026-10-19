using System.Collections.Generic;

namespace DataDump.Core;

/// <summary>A named box of a map: one of the game's camera areas.</summary>
public sealed record MapArea(string Name, WorldPoint Min, WorldPoint Max);

/// <summary>
/// One picture to take. <paramref name="Mode"/> "plan" looks down from <c>cut</c> meters above the floor, so ceilings
/// and upper floors are cut away and the rooms show; "top" looks down from high above, roofs and all.
/// </summary>
public sealed record MapShot(string Area, string Mode, MapFrame Frame, float CameraY, float Near, float Far)
{
    public string FileStem => Area + "." + Mode;
}

public static class MapShots
{
    public const string PlanMode = "plan";
    public const string TopMode = "top";

    /// <summary>How far under the floor a plan picture still sees, so a floor a little below the median still shows.</summary>
    public const float BelowFloor = 2;

    /// <summary>How high above the floor a top picture starts; above any building in the game.</summary>
    public const float TopHeight = 120;

    /// <summary>The plan and top pictures of a view; none when its box has no size.</summary>
    public static IReadOnlyList<MapShot> For(MapView view, float cutHeight, float pixelsPerMeter, int maxPixels)
    {
        var floor = view.FloorY;
        var (min, max) = (view.Area.Min, view.Area.Max);
        var corners = new[] { min, max, new WorldPoint(min.X, floor, max.Z), new WorldPoint(max.X, floor, min.Z) };

        if (MapFrame.For(corners, view.Yaw, floor, pixelsPerMeter, maxPixels) is not { } frame) return new List<MapShot>();

        return new List<MapShot>
        {
            new(view.Stem, PlanMode, frame, floor + cutHeight, 0.05f, cutHeight + BelowFloor),
            new(view.Stem, TopMode, frame, floor + TopHeight, 0.3f, TopHeight + BelowFloor),
        };
    }
}
