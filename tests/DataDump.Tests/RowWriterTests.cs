using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class RowWriterTests
{
    private static readonly RowWriter Rows = new(new FakeValueReader());

    // grep '"ID":7,' has to find the row, so the id leads the line whatever order the game declares it in.
    [Fact]
    public void The_id_comes_first_and_the_table_key_copy_is_left_out()
    {
        var row = new FakeObject("Config_Action")
            .With("Name", "Eat")
            .With("ID", 7)
            .With("MainKey", 7)
            .With("During", 1.5f);

        Assert.Equal("{\"ID\":7,\"Name\":\"Eat\",\"During\":1.5}", Rows.Write(7, row));
    }

    [Fact]
    public void ID_wins_over_Id_and_both_are_kept()
    {
        var row = new FakeObject("Config_Odd").With("Id", 1).With("ID", 2);

        Assert.Equal("{\"ID\":2,\"Id\":1}", Rows.Write(2, row));
    }

    [Fact]
    public void A_row_without_an_id_takes_its_table_key()
    {
        var row = new FakeObject("Config_Text").With("Text", "hello");

        Assert.Equal("{\"id\":\"greeting\",\"Text\":\"hello\"}", Rows.Write("greeting", row));
    }

    [Fact]
    public void A_plain_value_row_is_still_one_object()
    {
        Assert.Equal("{\"id\":3,\"value\":\"text\"}", Rows.Write(3, "text"));
    }

    [Fact]
    public void A_row_that_cannot_be_read_says_why_under_its_key()
    {
        Assert.Equal("{\"id\":5,\"error\":\"!error InvalidOperationException: unreadable\"}", Rows.Write(5, new Unreadable()));
    }

    [Fact]
    public void A_row_never_recurses_past_the_depth_limit()
    {
        var loop = new FakeObject("Loop");

        loop.WithGetter("Self", () => loop);

        Assert.Equal("{\"id\":\"k\",\"Self\":{\"Self\":{\"Self\":{\"Self\":\"Loop\"}}}}", Rows.Write("k", loop));
    }
}
