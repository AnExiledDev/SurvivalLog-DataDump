using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class AttributesTests
{
    private static AttrSample Whole(int baseValue, int min = 0, int max = 100) => new(baseValue, 0, 0, min, max, null);

    [Fact]
    public void The_total_is_base_plus_strengthening_without_condition()
    {
        Assert.Equal(60, new AttrSample(50, 10, 99, 0, 100, null).Total);
    }

    [Fact]
    public void The_total_is_held_to_the_attribute_bounds()
    {
        Assert.Equal(100, new AttrSample(90, 20, 0, 0, 100, null).Total);
        Assert.Equal(0, new AttrSample(-5, 0, 0, 0, 100, null).Total);
    }

    // The game checks the minimum first, so with bounds that cross the minimum is what it uses.
    [Fact]
    public void The_minimum_wins_when_the_bounds_cross()
    {
        Assert.Equal(10, new AttrSample(5, 0, 0, 10, 8, null).Total);
    }

    [Fact]
    public void A_fraction_attribute_is_shown_over_the_scaling_ratio()
    {
        Assert.Equal(2.5, new AttrSample(250, 0, 0, 0, 1000, 100f).Shown);
        Assert.Equal(250.0, Whole(250, max: 1000).Shown);
    }

    [Fact]
    public void Only_attributes_that_changed_and_exist_on_both_sides_are_reported_by_name()
    {
        var start = new Dictionary<string, AttrSample>
        {
            ["Hunger"] = Whole(50),
            ["Mood"] = Whole(30),
            ["Gone"] = Whole(1),
            ["Energy"] = new AttrSample(200, 0, 0, 0, 1000, 100f),
        };
        var end = new Dictionary<string, AttrSample>
        {
            ["Hunger"] = Whole(40),
            ["Mood"] = Whole(30),
            ["New"] = Whole(5),
            ["Energy"] = new AttrSample(350, 0, 0, 0, 1000, 100f),
        };

        var changes = AttrDelta.Between(start, end).Select(change => change.ToString());

        Assert.Equal(new[] { "Energy +150 (+1.5)", "Hunger -10" }, changes);
    }

    // A change the bounds swallow is no change: the ledger reports what the game's total did.
    [Fact]
    public void A_change_held_back_by_the_bounds_is_not_reported()
    {
        var start = new Dictionary<string, AttrSample> { ["Hunger"] = Whole(100) };
        var end = new Dictionary<string, AttrSample> { ["Hunger"] = Whole(130) };

        Assert.Empty(AttrDelta.Between(start, end));
    }
}
