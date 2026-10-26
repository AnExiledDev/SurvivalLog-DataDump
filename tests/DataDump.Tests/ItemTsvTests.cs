using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class ItemTsvTests
{
    private static readonly DateTime Noon = new(2026, 10, 26, 12, 30, 5);

    [Fact]
    public void A_gain_is_one_line_under_the_header_columns()
    {
        var line = ItemTsv.Line(new ItemGain(Noon, 12, "Abandoned School", "furniture:3105", 9044, 1, true, "story drop"));

        Assert.Equal("2026-10-26 12:30:05\t12\tAbandoned School\tfurniture:3105\t9044\t1\tyes\tstory drop", line);
        Assert.Equal(ItemTsv.Header.Split('\t').Length, line.Split('\t').Length);
    }

    // An item that did not fit is still worth a line: it shows where the game tried to give it.
    [Fact]
    public void An_item_that_did_not_fit_says_no_and_an_unknown_day_is_empty()
    {
        var line = ItemTsv.Line(new ItemGain(Noon, null, "", "player", 27015, 2, false, ""));

        Assert.Equal("2026-10-26 12:30:05\t\t\tplayer\t27015\t2\tno\t", line);
    }

    [Fact]
    public void Tabs_and_line_breaks_in_names_become_spaces()
    {
        var line = ItemTsv.Line(new ItemGain(Noon, 1, "Office\tB1", "player", 1, 1, true, "reward\r\n1"));

        Assert.Equal("2026-10-26 12:30:05\t1\tOffice B1\tplayer\t1\t1\tyes\treward  1", line);
    }
}

public class GiverStackTests
{
    [Fact]
    public void Nested_givers_read_outermost_first_and_leaving_drops_the_innermost()
    {
        var givers = new GiverStack();

        givers.Enter("reward 71110");
        givers.Enter("package 5");

        Assert.Equal("reward 71110 > package 5", givers.Current);

        givers.Leave();

        Assert.Equal("reward 71110", givers.Current);

        givers.Leave();

        Assert.Equal("", givers.Current);
    }

    // A giver whose game call threw never left; without the cap its name would pile up for the rest of the session.
    [Fact]
    public void Past_the_cap_names_are_not_kept_but_leaving_still_unwinds_in_order()
    {
        var givers = new GiverStack();

        for (var i = 0; i < GiverStack.MaxDepth + 3; i++) givers.Enter("g" + i);

        Assert.Equal(GiverStack.MaxDepth, givers.Current.Split(" > ").Length);

        for (var i = 0; i < 3; i++) givers.Leave();

        Assert.Equal(GiverStack.MaxDepth, givers.Current.Split(" > ").Length);

        givers.Leave();

        Assert.Equal(GiverStack.MaxDepth - 1, givers.Current.Split(" > ").Length);
    }

    [Fact]
    public void Leaving_with_nothing_entered_does_nothing_and_clear_empties_it()
    {
        var givers = new GiverStack();

        givers.Leave();
        givers.Enter("a");
        givers.Clear();
        givers.Enter("b");

        Assert.Equal("b", givers.Current);
    }
}
