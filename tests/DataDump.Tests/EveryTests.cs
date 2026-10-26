using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class EveryTests
{
    // A check that reads the game every frame costs a wrapper per read; it reads once per interval instead.
    [Fact]
    public void Is_due_once_per_interval()
    {
        var every = new Every(0.5);

        Assert.True(every.IsDue(10));
        Assert.False(every.IsDue(10.1));
        Assert.False(every.IsDue(10.49));
        Assert.True(every.IsDue(10.5));
        Assert.False(every.IsDue(10.6));
    }

    // The first look must not wait a whole interval: a location loaded at start is seen at once.
    [Fact]
    public void The_first_call_is_due()
    {
        Assert.True(new Every(60).IsDue(0));
    }

    [Fact]
    public void A_late_call_starts_the_next_interval_from_itself()
    {
        var every = new Every(1);

        every.IsDue(0);

        Assert.True(every.IsDue(5));
        Assert.False(every.IsDue(5.9));
        Assert.True(every.IsDue(6));
    }
}
