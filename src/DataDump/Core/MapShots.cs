using System.Collections.Generic;
using System.Linq;

namespace DataDump.Core;

/// <summary>A named box of a map: one of the game's camera areas, or the box round every node.</summary>
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
    public const string AllNodesArea = "all";
    public const string PlanMode = "plan";
    public const string TopMode = "top";

    /// <summary>How far under the floor a plan picture still sees, so a floor a little below the median still shows.</summary>
    public const float BelowFloor = 2;

    /// <summary>How high above the floor a top picture starts; above any building in the game.</summary>
    public const float TopHeight = 120;

    /// <summary>
    /// The pictures for a map: a plan and a top picture of every area. A map with no areas gets them of the box round
    /// all its nodes, so every location still gets a picture. Areas with no size are left out, and a name used twice
    /// gets a number, so no picture overwrites another.
    /// </summary>
    public static IReadOnlyList<MapShot> For(IReadOnlyList<MapArea> areas, IReadOnlyList<WorldPoint> nodes, float cutHeight, float pixelsPerMeter, int maxPixels)
    {
        var shots = new List<MapShot>();
        var usedNames = new HashSet<string>();

        foreach (var area in AreasToTake(areas, nodes))
        {
            var name = UniqueName(FileNames.Safe(area.Name), usedNames);
            var floor = MapFrame.FloorOf(nodes, area.Min, area.Max, area.Min.Y);

            if (MapFrame.For(area.Min, area.Max, floor, pixelsPerMeter, maxPixels) is not { } frame) continue;

            shots.Add(new MapShot(name, PlanMode, frame, floor + cutHeight, 0.05f, cutHeight + BelowFloor));
            shots.Add(new MapShot(name, TopMode, frame, floor + TopHeight, 0.3f, TopHeight + BelowFloor));
        }

        return shots;
    }

    private static string UniqueName(string name, HashSet<string> usedNames)
    {
        var unique = name;

        for (var copy = 2; !usedNames.Add(unique); copy++) unique = name + "_" + copy;

        return unique;
    }

    private static IEnumerable<MapArea> AreasToTake(IReadOnlyList<MapArea> areas, IReadOnlyList<WorldPoint> nodes)
    {
        if (areas.Count > 0) return areas;

        return MapFrame.BoundsOf(nodes) is { } bounds ? new[] { new MapArea(AllNodesArea, bounds.Min, bounds.Max) } : Enumerable.Empty<MapArea>();
    }
}
