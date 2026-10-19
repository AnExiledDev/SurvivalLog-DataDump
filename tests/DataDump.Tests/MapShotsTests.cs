using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class MapShotsTests
{
    private static WorldPoint At(float x, float y, float z) => new(x, y, z);

    private static MapArea Area(string name, float x, float z) => new(name, At(x, 0, z), At(x + 10, 3, z + 10));

    [Fact]
    public void Every_area_gets_a_plan_and_a_top_picture()
    {
        var shots = MapShots.For(new[] { Area("Hall", 0, 0) }, Array.Empty<WorldPoint>(), 2.2f, 16, 4096);

        Assert.Equal(new[] { "Hall.plan", "Hall.top" }, shots.Select(shot => shot.FileStem));
    }

    // The plan camera sits just under the ceiling, so it sees the rooms and not the roof.
    [Fact]
    public void The_plan_camera_sits_the_cut_height_above_the_area_floor()
    {
        var nodes = new[] { At(5, 4, 5), At(6, 4, 6), At(7, 4, 7) };
        var shots = MapShots.For(new[] { Area("Upstairs", 0, 0) }, nodes, 2, 16, 4096);
        var plan = shots.Single(shot => shot.Mode == MapShots.PlanMode);
        var top = shots.Single(shot => shot.Mode == MapShots.TopMode);

        Assert.Equal(6, plan.CameraY);
        Assert.Equal(2 + MapShots.BelowFloor, plan.Far);
        Assert.Equal(4 + MapShots.TopHeight, top.CameraY);
    }

    // Two pictures with one name would overwrite each other on disk.
    [Fact]
    public void A_name_used_twice_gets_a_number()
    {
        var shots = MapShots.For(new[] { Area("Room", 0, 0), Area("Room", 20, 0), Area("../Room", 40, 0) }, Array.Empty<WorldPoint>(), 2, 16, 4096);

        Assert.Equal(new[] { "Room", "Room_2", "_._Room" }, shots.Select(shot => shot.Area).Distinct());
    }

    [Fact]
    public void A_map_with_no_areas_is_pictured_whole_from_its_nodes()
    {
        var nodes = new[] { At(0, 0, 0), At(30, 0, 12) };
        var shots = MapShots.For(Array.Empty<MapArea>(), nodes, 2, 16, 4096);

        Assert.Equal(2, shots.Count);
        Assert.All(shots, shot => Assert.Equal(MapShots.AllNodesArea, shot.Area));
        Assert.Equal(-1, shots[0].Frame.MinX);
        Assert.Equal(31, shots[0].Frame.MaxX);
    }

    [Fact]
    public void A_map_with_no_areas_and_no_nodes_gets_no_pictures()
    {
        Assert.Empty(MapShots.For(Array.Empty<MapArea>(), Array.Empty<WorldPoint>(), 2, 16, 4096));
    }
}
