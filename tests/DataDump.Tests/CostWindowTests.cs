using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

// The ledger's hooks run inside the game's calls; one line a minute in data-dump.log shows what they cost a session.
public class CostWindowTests
{
    [Fact]
    public void No_line_before_a_full_minute()
    {
        var costs = new CostWindow();

        costs.TakeIfDue(100);
        costs.Add("hook", 0.5);

        Assert.Null(costs.TakeIfDue(100 + CostWindow.WindowSeconds - 0.1));
    }

    [Fact]
    public void The_line_gives_calls_longest_and_total_per_part_in_name_order()
    {
        var costs = new CostWindow();

        costs.TakeIfDue(0);
        costs.Add("b hook", 0.25);
        costs.Add("a hook", 1.5);
        costs.Add("b hook", 2);
        costs.Add("b hook", 0.125);

        Assert.Equal(
            "a hook 1 calls, max 1.5 ms, total 1.5 ms; b hook 3 calls, max 2 ms, total 2.375 ms",
            costs.TakeIfDue(CostWindow.WindowSeconds));
    }

    // At the title screen nothing runs, and a line of zeros every minute would only bury the rest of the log.
    [Fact]
    public void A_minute_in_which_nothing_ran_gives_no_line()
    {
        var costs = new CostWindow();

        costs.TakeIfDue(0);

        Assert.Null(costs.TakeIfDue(CostWindow.WindowSeconds));
    }

    // Each line covers its own minute only, so a slow spike shows in the minute it happened and then clears.
    [Fact]
    public void Each_minute_starts_from_nothing()
    {
        var costs = new CostWindow();

        costs.TakeIfDue(0);
        costs.Add("hook", 9);
        costs.TakeIfDue(CostWindow.WindowSeconds);
        costs.Add("hook", 1);

        Assert.Null(costs.TakeIfDue(CostWindow.WindowSeconds * 2 - 0.1));
        Assert.Equal("hook 1 calls, max 1 ms, total 1 ms", costs.TakeIfDue(CostWindow.WindowSeconds * 2));
    }
}
