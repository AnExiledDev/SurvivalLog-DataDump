using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class MapShotsTests
{
    private static WorldPoint At(float x, float y, float z) => new(x, y, z);

    private static MapView View(string area, int floorId, float floorY) => new("Town", new MapArea(area, At(0, 0, 0), At(10, 3, 10)), floorId, floorY);

    [Fact]
    public void A_view_gets_a_plan_and_a_top_picture()
    {
        var shots = MapShots.For(View("Hall", 0, 0), 2.2f, 16, 4096);

        Assert.Equal(new[] { "Hall.plan", "Hall.top" }, shots.Select(shot => shot.FileStem));
    }

    // Each floor of a home is drawn on its own, so each needs its own files.
    [Fact]
    public void A_floor_is_named_in_its_pictures()
    {
        var shots = MapShots.For(View("Home", 102, 7.1f), 2.2f, 16, 4096);

        Assert.Equal(new[] { "Home.floor102.plan", "Home.floor102.top" }, shots.Select(shot => shot.FileStem));
    }

    // The plan camera sits just under the ceiling of the floor on view, so it sees that floor's rooms and not the roof.
    [Fact]
    public void The_plan_camera_sits_the_cut_height_above_the_view_floor()
    {
        var shots = MapShots.For(View("Upstairs", 102, 4), 2, 16, 4096);
        var plan = shots.Single(shot => shot.Mode == MapShots.PlanMode);
        var top = shots.Single(shot => shot.Mode == MapShots.TopMode);

        Assert.Equal(6, plan.CameraY);
        Assert.Equal(2 + MapShots.BelowFloor, plan.Far);
        Assert.Equal(4 + MapShots.TopHeight, top.CameraY);
    }

    // A building standing at an angle is pictured square: both pictures are turned to the view's angle.
    [Fact]
    public void A_turned_view_gets_turned_pictures()
    {
        var view = View("Tool_Store", 0, 0) with { Yaw = 15 };

        Assert.All(MapShots.For(view, 2.2f, 16, 4096), shot => Assert.Equal(15, shot.Frame.Yaw));
    }

    [Fact]
    public void A_view_with_no_size_gets_no_pictures()
    {
        var flat = new MapView("Town", new MapArea("Line", At(0, 0, 0), At(float.PositiveInfinity, 0, 0)), 0, 0);

        Assert.Empty(MapShots.For(flat, 2, 16, 4096));
    }
}
