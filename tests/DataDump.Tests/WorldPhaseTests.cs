using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class WorldPhaseTests
{
    // Scene_AfterTheDisaster02 runs with WorldStateType 1; its maps belong with the pre-disaster world.
    [Fact]
    public void World_state_1_is_before_the_disaster()
    {
        Assert.Equal("pre-disaster", WorldPhase.FolderOf(1));
    }

    [Fact]
    public void World_state_2_is_after_the_disaster()
    {
        Assert.Equal("post-disaster", WorldPhase.FolderOf(2));
    }

    // A clock not set up yet (0), no clock at all, or a value the game adds later must not be filed under either world.
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(3)]
    public void An_unset_or_unknown_world_state_is_neither_phase(int? worldState)
    {
        Assert.Equal(WorldPhase.Unknown, WorldPhase.FolderOf(worldState));
    }

    [Fact]
    public void A_known_phase_is_written_at_once()
    {
        Assert.True(WorldPhase.IsReady(1, firstSeen: 100, now: 100));
    }

    // Writing before the clock is loaded would put a pre-disaster map under unknown-phase for no reason.
    [Fact]
    public void An_unknown_phase_waits_for_the_clock()
    {
        Assert.False(WorldPhase.IsReady(null, firstSeen: 100, now: 100 + WorldPhase.WaitSeconds - 1));
    }

    // A clock that never comes must not keep a location's map data from being written.
    [Fact]
    public void An_unknown_phase_is_written_once_the_wait_is_over()
    {
        Assert.True(WorldPhase.IsReady(null, firstSeen: 100, now: 100 + WorldPhase.WaitSeconds));
    }
}
