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
}
