using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class SnapshotDocsTests
{
    [Fact]
    public void A_buff_with_no_end_has_no_time_left()
    {
        Assert.Null(BuffDoc.SecondsLeftOf(-1, 1000));
    }

    [Fact]
    public void A_buff_has_its_end_time_minus_now_left()
    {
        Assert.Equal(200, BuffDoc.SecondsLeftOf(500, 300));
    }

    // A buff past its end that the game still holds is exactly the kind of state a bug snapshot has to show.
    [Fact]
    public void A_buff_past_its_end_shows_negative_time_left()
    {
        Assert.Equal(-50, BuffDoc.SecondsLeftOf(250, 300));
    }

    [Fact]
    public void An_action_has_its_length_minus_elapsed_left()
    {
        Assert.Equal(7f, ActionDoc.LeftOf(3f, 10f));
    }

    [Fact]
    public void An_action_that_runs_until_stopped_or_has_no_timing_has_nothing_left()
    {
        Assert.Null(ActionDoc.LeftOf(3f, 0f));
        Assert.Null(ActionDoc.LeftOf(null, 10f));
        Assert.Null(ActionDoc.LeftOf(3f, null));
    }

    [Fact]
    public void An_attribute_document_carries_what_the_game_shows_and_its_parts()
    {
        var doc = AttrDoc.From(new AttrSample(250, 10, 5, 0, 1000, 100f));

        Assert.Equal(new AttrDoc(260, 260f / 100f, 250, 10, 5, 0, 1000, true), doc);
    }
}
