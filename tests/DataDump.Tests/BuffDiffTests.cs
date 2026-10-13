using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class BuffDiffTests
{
    [Fact]
    public void Buffs_gained_and_lost_are_named_with_their_ids_in_id_order()
    {
        var start = new Dictionary<int, string> { [1] = "Hungry", [2] = "Tired" };
        var end = new Dictionary<int, string> { [3] = "Fed", [2] = "Tired", [0] = "Calm" };

        var diff = BuffDiff.Between(start, end);

        Assert.Equal(new[] { "Calm(0)", "Fed(3)" }, diff.Gained);
        Assert.Equal(new[] { "Hungry(1)" }, diff.Lost);
    }

    [Fact]
    public void Unchanged_buffs_give_an_empty_diff()
    {
        var buffs = new Dictionary<int, string> { [2] = "Tired" };

        var diff = BuffDiff.Between(buffs, buffs);

        Assert.Empty(diff.Gained);
        Assert.Empty(diff.Lost);
    }
}
