using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class LoadGateTests
{
    private const double Settle = 2;

    // Dumping while the loader still fills tables would write half tables; the load must hold for the settle time.
    [Fact]
    public void Opens_only_after_the_load_has_held_for_the_settle_time()
    {
        var gate = new LoadGate(Settle);

        Assert.False(gate.Poll(true, 10));
        Assert.False(gate.Poll(true, 11.9));
        Assert.True(gate.Poll(true, 12));
    }

    [Fact]
    public void Opens_once()
    {
        var gate = new LoadGate(Settle);

        gate.Poll(true, 0);
        gate.Poll(true, 2);

        Assert.False(gate.Poll(true, 5));
    }

    [Fact]
    public void A_poll_that_reads_not_loaded_starts_the_wait_again()
    {
        var gate = new LoadGate(Settle);

        gate.Poll(true, 0);
        gate.Poll(false, 1);

        Assert.False(gate.Poll(true, 1.5));
        Assert.False(gate.Poll(true, 3.4));
        Assert.True(gate.Poll(true, 3.5));
    }

    [Fact]
    public void Never_opens_while_the_load_is_unfinished()
    {
        var gate = new LoadGate(Settle);

        for (var now = 0; now < 100; now++) Assert.False(gate.Poll(false, now));
    }

    // Each look at the load reads the game through interop; a frame-by-frame poll looks only once per interval.
    [Fact]
    public void A_throttled_poll_reads_the_game_once_per_interval()
    {
        var gate = new LoadGate(Settle);
        var every = new Every(0.25);
        var reads = 0;
        Func<bool> isLoaded = () =>
        {
            reads++;

            return false;
        };

        for (var frame = 0; frame < 60; frame++) gate.Poll(isLoaded, every, frame / 60.0);

        Assert.Equal(4, reads);
    }

    // Once the dump has started, the load check has nothing left to say, so the game is never read for it again.
    [Fact]
    public void A_throttled_poll_never_reads_the_game_once_open()
    {
        var gate = new LoadGate(Settle);
        var every = new Every(0.25);
        var reads = 0;
        Func<bool> isLoaded = () =>
        {
            reads++;

            return true;
        };
        var opened = 0;

        for (var frame = 0; frame < 600; frame++)
        {
            if (gate.Poll(isLoaded, every, frame / 60.0)) opened++;
        }

        Assert.Equal(1, opened);
        Assert.Equal(9, reads);
    }

    // The throttle must not hide the load finishing: the gate still opens after the settle time.
    [Fact]
    public void A_throttled_poll_opens_after_the_settle_time()
    {
        var gate = new LoadGate(Settle);
        var every = new Every(0.25);
        double? openedAt = null;

        for (var frame = 0; frame < 600 && openedAt is null; frame++)
        {
            var now = frame / 60.0;

            if (gate.Poll(() => now >= 1, every, now)) openedAt = now;
        }

        Assert.NotNull(openedAt);
        Assert.InRange(openedAt.Value, 3, 3.25);
    }
}
