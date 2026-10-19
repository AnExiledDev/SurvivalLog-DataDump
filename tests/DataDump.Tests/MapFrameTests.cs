using System;
using System.Linq;
using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class MapFrameTests
{
    private const float M = MapFrame.Margin;

    private static WorldPoint At(float x, float y, float z) => new(x, y, z);

    private static WorldPoint[] Box(float x0, float z0, float x1, float z1) => new[] { At(x0, 0, z0), At(x1, 0, z1), At(x0, 0, z1), At(x1, 0, z0) };

    /// <summary>The four corners of a w by d building turned <paramref name="yaw"/> degrees about the origin.</summary>
    private static WorldPoint[] TurnedBox(float w, float d, float yaw)
    {
        var radians = yaw * MathF.PI / 180;
        var (cos, sin) = (MathF.Cos(radians), MathF.Sin(radians));

        // Inverse of the frame's U/V turn: a corner (u, v) of the building sits at this world X/Z.
        return new[] { (0f, 0f), (w, 0f), (0f, d), (w, d) }.Select(c => At(c.Item1 * cos + c.Item2 * sin, 0, -c.Item1 * sin + c.Item2 * cos)).ToArray();
    }

    // The site places nodes with this formula; the frame must cover the area plus its margin at the asked scale.
    [Fact]
    public void A_small_area_gets_the_asked_pixels_per_meter_with_a_margin()
    {
        var frame = MapFrame.For(Box(0, 0, 10, 20), 0, 1.5f, 16, 4096);

        Assert.NotNull(frame);
        Assert.Equal(-M, frame.MinU);
        Assert.Equal(10 + M, frame.MaxU);
        Assert.Equal(-M, frame.MinV);
        Assert.Equal(20 + M, frame.MaxV);
        Assert.Equal((int)((10 + 2 * M) * 16), frame.Width);
        Assert.Equal((int)((20 + 2 * M) * 16), frame.Height);
        Assert.Equal(16, frame.PixelsPerMeter);
        Assert.Equal(5, frame.CenterU);
        Assert.Equal(10, frame.CenterV);
        Assert.Equal(10 + M, frame.HalfHeight);
    }

    [Fact]
    public void A_large_area_is_scaled_down_to_fit_the_largest_picture()
    {
        var frame = MapFrame.For(Box(0, 0, 1000 - 2 * M, 500 - 2 * M), 0, 0, 16, 1000);

        Assert.NotNull(frame);
        Assert.Equal(1000, frame.Width);
        Assert.Equal(500, frame.Height);
        Assert.Equal(1f, frame.PixelsPerMeter, 4);
    }

    // The Tool Store stands at 15 degrees; an unturned picture of it is bigger than the store and cut its corners off.
    [Fact]
    public void A_building_at_an_angle_pictured_at_its_angle_gets_its_own_size()
    {
        var frame = MapFrame.For(TurnedBox(12, 8, 15), 15, 0, 10, 4096);

        Assert.NotNull(frame);
        Assert.Equal((12 + 2 * M) * 10, frame.Width, 0);
        Assert.Equal((8 + 2 * M) * 10, frame.Height, 0);
    }

    // A node must land on the picture where the camera drew it, whatever the turn.
    [Fact]
    public void A_point_turned_into_the_frame_and_back_is_where_it_was()
    {
        var frame = MapFrame.For(TurnedBox(12, 8, -20), -20, 0, 10, 4096);

        Assert.NotNull(frame);

        var (u, v) = frame.FrameOf(3, 7);
        var (x, z) = frame.WorldOf(u, v);

        Assert.Equal(3, x, 4);
        Assert.Equal(7, z, 4);
    }

    // Unity's camera turned by Euler(90, yaw, 0) has its right at (cos, 0, -sin); the frame's U must follow it.
    [Fact]
    public void Picture_right_follows_the_turned_camera()
    {
        var frame = MapFrame.For(Box(-1, -1, 1, 1), 90, 0, 10, 4096);

        Assert.NotNull(frame);

        var (u, v) = frame.FrameOf(0, -1);

        Assert.Equal(1, u, 4);
        Assert.Equal(0, v, 4);
    }

    // Every pixel of a large picture must be rendered by exactly one tile, or the picture has gaps or seams.
    [Fact]
    public void Tiles_cover_every_pixel_exactly_once()
    {
        var frame = MapFrame.For(Box(0, 0, 50, 30), 0, 0, 10, 8192);

        Assert.NotNull(frame);

        var covered = new int[frame.Width, frame.Height];

        foreach (var tile in frame.Tiles(256))
        {
            for (var x = tile.PixelX; x < tile.PixelX + tile.Width; x++)
            {
                for (var y = tile.PixelY; y < tile.PixelY + tile.Height; y++) covered[x, y]++;
            }
        }

        Assert.True(frame.Tiles(256).Count > 1);
        Assert.All(covered.Cast<int>(), count => Assert.Equal(1, count));
    }

    // A tile's camera is centred on the tile; the bottom-left tile's centre is half a tile in from the picture's corner.
    [Fact]
    public void A_tile_is_centred_on_its_part_of_the_picture()
    {
        var frame = MapFrame.For(Box(0, 0, 50, 30), 0, 0, 10, 8192);

        Assert.NotNull(frame);

        var first = frame.Tiles(256)[0];
        var whole = frame.Tiles(100_000).Single();

        Assert.Equal(frame.MinU + 12.8f, first.CenterU, 3);
        Assert.Equal(frame.MinV + 12.8f, first.CenterV, 3);
        Assert.Equal(frame.CenterU, whole.CenterU, 3);
        Assert.Equal(frame.CenterV, whole.CenterV, 3);
    }

    [Fact]
    public void Points_that_are_not_a_number_or_a_bad_scale_give_no_frame()
    {
        Assert.Null(MapFrame.For(new[] { At(float.NegativeInfinity, 0, 0), At(1, 0, 1) }, 0, 0, 16, 4096));
        Assert.Null(MapFrame.For(Box(0, 0, 1, 1), 0, 0, 0, 4096));
        Assert.Null(MapFrame.For(Box(0, 0, 1, 1), 0, 0, 16, 0));
        Assert.Null(MapFrame.For(Array.Empty<WorldPoint>(), 0, 0, 16, 4096));
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
}
