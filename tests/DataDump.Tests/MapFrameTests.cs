using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class MapFrameTests
{
    private static WorldPoint At(float x, float y, float z) => new(x, y, z);

    // The site places nodes with this formula; the frame must cover the area plus its margin at the asked scale.
    [Fact]
    public void A_small_area_gets_the_asked_pixels_per_meter_with_a_margin()
    {
        var frame = MapFrame.For(At(0, 0, 0), At(10, 3, 20), 1.5f, 16, 4096);

        Assert.NotNull(frame);
        Assert.Equal(-1, frame.MinX);
        Assert.Equal(11, frame.MaxX);
        Assert.Equal(-1, frame.MinZ);
        Assert.Equal(21, frame.MaxZ);
        Assert.Equal(12 * 16, frame.Width);
        Assert.Equal(22 * 16, frame.Height);
        Assert.Equal(16, frame.PixelsPerMeter);
        Assert.Equal(5, frame.CenterX);
        Assert.Equal(10, frame.CenterZ);
        Assert.Equal(11, frame.HalfHeight);
    }

    [Fact]
    public void A_large_area_is_scaled_down_to_fit_the_largest_picture()
    {
        var frame = MapFrame.For(At(0, 0, 0), At(998, 0, 498), 0, 16, 1000);

        Assert.NotNull(frame);
        Assert.Equal(1000, frame.Width);
        Assert.Equal(500, frame.Height);
        Assert.Equal(1f, frame.PixelsPerMeter);
    }

    [Fact]
    public void Corners_given_the_wrong_way_round_give_the_same_frame()
    {
        Assert.Equal(MapFrame.For(At(0, 0, 0), At(4, 0, 6), 0, 8, 4096), MapFrame.For(At(4, 0, 6), At(0, 0, 0), 0, 8, 4096));
    }

    [Fact]
    public void A_box_that_is_not_a_number_or_a_bad_scale_gives_no_frame()
    {
        Assert.Null(MapFrame.For(At(float.NegativeInfinity, 0, 0), At(1, 0, 1), 0, 16, 4096));
        Assert.Null(MapFrame.For(At(0, 0, 0), At(1, 0, 1), 0, 0, 4096));
        Assert.Null(MapFrame.For(At(0, 0, 0), At(1, 0, 1), 0, 16, 0));
    }

    // One node on a roof must not lift the floor and cut the rooms out of the plan picture.
    [Fact]
    public void The_floor_is_the_median_height_of_the_nodes_inside_the_area()
    {
        var nodes = new[] { At(1, 0, 1), At(2, 0.2f, 2), At(3, 0.1f, 3), At(4, 9, 4), At(50, -30, 50) };

        Assert.Equal(0.2f, MapFrame.FloorOf(nodes, At(0, 0, 0), At(10, 0, 10), 99));
    }

    [Fact]
    public void An_area_with_no_nodes_inside_keeps_the_fallback_floor()
    {
        Assert.Equal(7f, MapFrame.FloorOf(new[] { At(50, 0, 50) }, At(0, 0, 0), At(10, 0, 10), 7));
    }

    [Fact]
    public void The_bounds_hold_every_node()
    {
        var bounds = MapFrame.BoundsOf(new[] { At(1, 5, -2), At(-3, 0, 4) });

        Assert.Equal((At(-3, 0, -2), At(1, 5, 4)), bounds);
        Assert.Null(MapFrame.BoundsOf(Array.Empty<WorldPoint>()));
    }
}
