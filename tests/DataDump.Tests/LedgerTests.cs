using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class LedgerTests
{
    private const long Alice = 1;
    private const long Bob = 2;
    private const int Eat = 101;
    private const int Sleep = 102;

    private static readonly IReadOnlyDictionary<string, AttrSample> NoAttrs = new Dictionary<string, AttrSample>();
    private static readonly IReadOnlyDictionary<int, string> NoBuffs = new Dictionary<int, string>();

    private static Moment At(double realSeconds) =>
        new(new DateTime(2026, 10, 13, 1, 0, 0), realSeconds, 3, 28800, 3 * 86400 + 28800, new GameSpeed(1, 1f), NoAttrs, NoBuffs);

    private static ActionStart Started(int actionId, double realSeconds) => new("Alice", actionId, "Eat", "Food", 1800f, At(realSeconds));

    [Fact]
    public void An_action_that_ends_is_finished_and_closed()
    {
        var book = new LedgerBook();

        book.Start(Alice, Started(Eat, 0));

        var entry = book.End(Alice, Eat, At(5), isCancelled: false);

        Assert.Equal(LedgerOutcome.Finished, entry?.Outcome);
        Assert.Null(book.OpenActionOf(Alice));
        Assert.Null(book.End(Alice, Eat, At(6), isCancelled: false));
    }

    [Fact]
    public void A_forced_end_is_cancelled()
    {
        var book = new LedgerBook();

        book.Start(Alice, Started(Eat, 0));

        Assert.Equal(LedgerOutcome.Cancelled, book.End(Alice, Eat, At(5), isCancelled: true)?.Outcome);
    }

    // The end hook sees every action's effect ending; one that is not the open action must not close it.
    [Fact]
    public void An_end_for_another_action_leaves_the_open_one_open()
    {
        var book = new LedgerBook();

        book.Start(Alice, Started(Eat, 0));

        Assert.Null(book.End(Alice, Sleep, At(5), isCancelled: false));
        Assert.Equal(Eat, book.OpenActionOf(Alice));
    }

    // A character that dies gets no end call; its action closes when it next starts one, never silently vanishes.
    [Fact]
    public void A_new_start_closes_the_earlier_action_as_superseded_at_that_moment()
    {
        var book = new LedgerBook();
        var sleep = Started(Sleep, 9);

        book.Start(Alice, Started(Eat, 0));

        var superseded = book.Start(Alice, sleep);

        Assert.Equal(Eat, superseded?.Start.ActionId);
        Assert.Equal(LedgerOutcome.Superseded, superseded?.Outcome);
        Assert.Same(sleep.At, superseded?.End);
        Assert.Equal(Sleep, book.OpenActionOf(Alice));
    }

    [Fact]
    public void Each_character_has_its_own_open_action()
    {
        var book = new LedgerBook();

        book.Start(Alice, Started(Eat, 0));

        Assert.Null(book.Start(Bob, Started(Eat, 1)));
        Assert.NotNull(book.End(Bob, Eat, At(2), isCancelled: false));
        Assert.Equal(Eat, book.OpenActionOf(Alice));
    }

    [Fact]
    public void A_ledger_line_has_every_column_in_header_order()
    {
        var start = new Moment(
            new DateTime(2026, 10, 13, 1, 0, 0), 10.0, 3, 28800, 3 * 86400 + 28800, new GameSpeed(1, 1f),
            new Dictionary<string, AttrSample> { ["Hunger"] = new(50, 0, 0, 0, 100, null) },
            new Dictionary<int, string> { [1] = "Hungry" });
        var end = new Moment(
            new DateTime(2026, 10, 13, 1, 0, 12), 22.25, 3, 30600, 3 * 86400 + 30600, new GameSpeed(2, 2f),
            new Dictionary<string, AttrSample> { ["Hunger"] = new(80, 0, 0, 0, 100, null) },
            new Dictionary<int, string> { [2] = "Fed" });
        var entry = new LedgerEntry(new ActionStart("Alice", Eat, "Eat\tmeal", "Food", 1800f, start), end, LedgerOutcome.Finished);
        var expected = string.Join("\t",
            "2026-10-13 01:00:12", "3", "08:30:00", "Alice", "101", "Eat meal", "Food", "1800", "1800", "12.25",
            "L1 x1 -> L2 x2", "Hunger +30", "Fed(2)", "Hungry(1)", "finished");

        var line = LedgerTsv.Line(entry);

        Assert.Equal(expected, line);
        Assert.Equal(LedgerTsv.Header.Split('\t').Length, line.Split('\t').Length);
    }

    [Fact]
    public void Line_breaks_in_a_value_cannot_split_a_line()
    {
        Assert.Equal("a b c d", LedgerTsv.Cell("a\tb\rc\nd"));
    }

    // Moving the file aside before the line keeps it under the cap; an empty file never moves, however long the line.
    [Theory]
    [InlineData(0L, 10L, false)]
    [InlineData(4L, 1L, false)]
    [InlineData(4L, 2L, true)]
    public void The_ledger_moves_aside_only_when_the_next_line_would_pass_the_cap(long current, long line, bool rotates)
    {
        Assert.Equal(rotates, LedgerRotation.ShouldRotate(current, line, maxBytes: 5));
    }

    [Fact]
    public void The_cap_is_five_megabytes()
    {
        Assert.Equal(5L * 1024 * 1024, LedgerRotation.MaxBytes);
    }
}
