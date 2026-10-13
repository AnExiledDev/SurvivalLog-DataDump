using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

// The game's own calls only queue rows; the disk is touched at most once per flush interval, from the ticker.
public class LedgerQueueTests
{
    private static readonly IReadOnlyDictionary<string, AttrSample> NoAttrs = new Dictionary<string, AttrSample>();
    private static readonly IReadOnlyDictionary<int, string> NoBuffs = new Dictionary<int, string>();

    private static LedgerEntry Row(int actionId)
    {
        var at = new Moment(new DateTime(2026, 10, 13, 1, 0, 0), 0, 3, 28800, 3 * 86400 + 28800, new GameSpeed(1, 1f), NoAttrs, NoBuffs);

        return new LedgerEntry(new ActionStart("Alice", actionId, "Eat", "Food", 1800f, at), at, LedgerOutcome.Finished);
    }

    private static int[] Ids(LedgerBatch? batch) => batch?.Rows.Select(row => row.Start.ActionId).ToArray() ?? Array.Empty<int>();

    [Fact]
    public void Nothing_is_written_until_the_flush_interval_has_passed()
    {
        var queue = new LedgerQueue();

        Assert.Null(queue.TakeIfDue(100));

        queue.Add(Row(1));

        Assert.Null(queue.TakeIfDue(100 + LedgerQueue.FlushSeconds - 0.1));
        Assert.Equal(new[] { 1 }, Ids(queue.TakeIfDue(100 + LedgerQueue.FlushSeconds)));
    }

    [Fact]
    public void A_batch_holds_every_waiting_row_oldest_first()
    {
        var queue = new LedgerQueue();

        queue.TakeIfDue(0);
        queue.Add(Row(1));
        queue.Add(Row(2));
        queue.Add(Row(3));

        var batch = queue.TakeIfDue(LedgerQueue.FlushSeconds);

        Assert.Equal(new[] { 1, 2, 3 }, Ids(batch));
        Assert.Equal(0, batch?.Dropped);
    }

    // Many actions ending in one burst still cost one write, and the next write waits a whole interval again.
    [Fact]
    public void After_a_batch_the_next_one_waits_a_full_interval()
    {
        var queue = new LedgerQueue();

        queue.TakeIfDue(0);
        queue.Add(Row(1));
        queue.TakeIfDue(LedgerQueue.FlushSeconds);
        queue.Add(Row(2));

        Assert.Null(queue.TakeIfDue(LedgerQueue.FlushSeconds * 2 - 0.1));
        Assert.Equal(new[] { 2 }, Ids(queue.TakeIfDue(LedgerQueue.FlushSeconds * 2)));
    }

    [Fact]
    public void An_empty_queue_writes_nothing()
    {
        var queue = new LedgerQueue();

        queue.TakeIfDue(0);

        Assert.Null(queue.TakeIfDue(LedgerQueue.FlushSeconds * 10));
    }

    // A ledger that cannot write must not grow the game's memory without end.
    [Fact]
    public void Past_the_cap_rows_are_dropped_and_counted()
    {
        var queue = new LedgerQueue();

        queue.TakeIfDue(0);

        for (var id = 0; id < LedgerQueue.MaxRows + 3; id++) queue.Add(Row(id));

        var batch = queue.TakeIfDue(LedgerQueue.FlushSeconds);

        Assert.Equal(LedgerQueue.MaxRows, batch?.Rows.Count);
        Assert.Equal(3, batch?.Dropped);
        Assert.Equal(LedgerQueue.MaxRows - 1, batch?.Rows[LedgerQueue.MaxRows - 1].Start.ActionId);
    }

    // A file held open by another program keeps its rows for the next try, in the order they happened.
    [Fact]
    public void A_batch_that_could_not_be_written_comes_back_ahead_of_newer_rows()
    {
        var queue = new LedgerQueue();

        queue.TakeIfDue(0);
        queue.Add(Row(1));
        queue.Add(Row(2));

        var failed = queue.TakeIfDue(LedgerQueue.FlushSeconds);

        Assert.NotNull(failed);

        queue.Add(Row(3));
        queue.Return(failed);

        Assert.Equal(new[] { 1, 2, 3 }, Ids(queue.TakeIfDue(LedgerQueue.FlushSeconds * 2)));
    }

    [Fact]
    public void A_returned_batch_past_the_cap_drops_the_newest_rows()
    {
        var queue = new LedgerQueue();

        queue.TakeIfDue(0);

        for (var id = 0; id < LedgerQueue.MaxRows; id++) queue.Add(Row(id));

        var failed = queue.TakeIfDue(LedgerQueue.FlushSeconds);

        Assert.NotNull(failed);

        queue.Add(Row(-1));
        queue.Add(Row(-2));
        queue.Return(failed);

        var batch = queue.TakeIfDue(LedgerQueue.FlushSeconds * 2);

        Assert.NotNull(batch);
        Assert.Equal(LedgerQueue.MaxRows, batch.Rows.Count);
        Assert.Equal(2, batch.Dropped);
        Assert.Equal(0, batch.Rows[0].Start.ActionId);
        Assert.DoesNotContain(batch.Rows, row => row.Start.ActionId < 0);
    }
}
