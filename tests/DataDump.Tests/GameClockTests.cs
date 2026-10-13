using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class GameClockTests
{
    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(3661, "01:01:01")]
    [InlineData(86399, "23:59:59")]
    [InlineData(86460, "00:01:00")]
    [InlineData(-1, "23:59:59")]
    public void A_second_of_the_day_reads_as_a_clock(int second, string clock)
    {
        Assert.Equal(clock, GameClock.TimeOfDay(second));
    }

    [Fact]
    public void A_speed_that_held_is_written_once()
    {
        Assert.Equal("L1 x1", GameSpeed.Across(new GameSpeed(1, 1f), new GameSpeed(1, 1f)));
    }

    // An action that ran across a speed change took fewer real seconds than its game seconds suggest.
    [Fact]
    public void A_speed_that_changed_is_written_as_where_it_started_and_ended()
    {
        Assert.Equal("L1 x1 -> L3 x4.5", GameSpeed.Across(new GameSpeed(1, 1f), new GameSpeed(3, 4.5f)));
    }
}
