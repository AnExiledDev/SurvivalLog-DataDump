using System;
using System.Linq;
using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class SpotOutlinesTests
{
    private static WorldPoint At(float x, float y, float z) => new(x, y, z);

    /// <summary>The eight corners of a w by h by d box centred on (cx, cy, cz), turned <paramref name="yaw"/> degrees about the vertical.</summary>
    private static WorldPoint[] Box(float cx, float cy, float cz, float w, float h, float d, float yaw)
    {
        var radians = yaw * MathF.PI / 180;
        var (cos, sin) = (MathF.Cos(radians), MathF.Sin(radians));

        return (from x in new[] { -w / 2, w / 2 }
                from y in new[] { -h / 2, h / 2 }
                from z in new[] { -d / 2, d / 2 }
                select At(cx + x * cos + z * sin, cy + y, cz - x * sin + z * cos)).ToArray();
    }

    // The site draws the outline as the game's layout view does: a box seen from above is its four floor corners.
    [Fact]
    public void A_square_box_is_four_corners_counter_clockwise()
    {
        var spot = SpotOutlines.Of("Slot_1", 1, At(0, 0, 0), Box(1, 0.5f, 2, 2, 1, 4, 0));

        Assert.Equal(new[] { new FloorPoint(0, 0), new FloorPoint(2, 0), new FloorPoint(2, 4), new FloorPoint(0, 4) }, spot.Outline);
    }

    // A box turned on the floor keeps its turn in the outline, which a min/max rectangle would lose.
    [Fact]
    public void A_turned_box_keeps_its_four_turned_corners()
    {
        var spot = SpotOutlines.Of("Slot_2", 2, At(0, 0, 0), Box(0, 0, 0, 2, 1, 2, 45));
        var half = MathF.Round(MathF.Sqrt(2), 3);

        Assert.Equal(4, spot.Outline.Count);
        Assert.Contains(new FloorPoint(-half, 0), spot.Outline);
        Assert.Contains(new FloorPoint(half, 0), spot.Outline);
        Assert.Contains(new FloorPoint(0, half), spot.Outline);
        Assert.Contains(new FloorPoint(0, -half), spot.Outline);
    }

    // The Warehouse Manager spots sat off their furniture because the map data gave only the pivot.
    [Fact]
    public void The_center_is_the_middle_of_the_box_not_the_pivot()
    {
        var spot = SpotOutlines.Of("Slot_3", 3, At(10, 0, 10), Box(11.5f, 1, 9, 3, 2, 2, 0));

        Assert.Equal(At(10, 0, 10), spot.Pivot);
        Assert.Equal(At(11.5f, 1, 9), spot.Center);
        Assert.Equal(0, spot.MinY);
        Assert.Equal(2, spot.MaxY);
    }

    // A wall spot stood up on its side is a thin box from above; its outline is still a closed shape.
    [Fact]
    public void A_flat_box_seen_from_above_is_still_a_rectangle()
    {
        var spot = SpotOutlines.Of("Wall_1", 4, At(0, 0, 0), Box(0, 1, 0, 2, 1.5f, 0.1f, 90));

        Assert.Equal(4, spot.Outline.Count);
        Assert.Equal(-0.05f, spot.Outline.Min(p => p.X), 3);
        Assert.Equal(0.05f, spot.Outline.Max(p => p.X), 3);
        Assert.Equal(-1f, spot.Outline.Min(p => p.Z), 3);
        Assert.Equal(1f, spot.Outline.Max(p => p.Z), 3);
    }

    [Fact]
    public void Points_on_a_straight_edge_and_inside_are_left_out()
    {
        var hull = SpotOutlines.Hull(new[] { new FloorPoint(0, 0), new FloorPoint(1, 0), new FloorPoint(2, 0), new FloorPoint(2, 2), new FloorPoint(1, 1), new FloorPoint(0, 2) });

        Assert.Equal(new[] { new FloorPoint(0, 0), new FloorPoint(2, 0), new FloorPoint(2, 2), new FloorPoint(0, 2) }, hull);
    }

    // Floats from the game differ in the last digits between frames; the file should not.
    [Fact]
    public void Positions_are_rounded_to_the_millimeter()
    {
        var spot = SpotOutlines.Of("Slot_4", 1, At(1.23449f, 0.0001f, -2.00049f), Box(0, 0, 0, 1, 1, 1, 0));

        Assert.Equal(1.234, spot.Pivot.X, 6);
        Assert.Equal(0, spot.Pivot.Y, 6);
        Assert.Equal(-2, spot.Pivot.Z, 6);
    }

    [Fact]
    public void A_box_with_no_corners_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SpotOutlines.Of("Slot_5", 1, At(0, 0, 0), Array.Empty<WorldPoint>()));
    }
}
