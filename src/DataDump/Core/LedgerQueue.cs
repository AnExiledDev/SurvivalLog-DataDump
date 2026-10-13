using System;
using System.Collections.Generic;

namespace DataDump.Core;

/// <summary>The rows to write in one go, and how many were dropped because the queue was full.</summary>
public sealed record LedgerBatch(IReadOnlyList<LedgerEntry> Rows, int Dropped);

/// <summary>
/// Finished ledger rows wait here so the game's own calls never touch the disk. They are taken at most once every
/// <see cref="FlushSeconds"/>, and the queue never holds more than <see cref="MaxRows"/>: past that, rows are counted
/// and dropped, so a ledger that cannot write cannot grow the game's memory either.
/// </summary>
public sealed class LedgerQueue
{
    public const double FlushSeconds = 5;
    public const int MaxRows = 10_000;

    private readonly List<LedgerEntry> _rows = new();
    private double? _lastTakenAt;
    private int _dropped;

    public void Add(LedgerEntry entry)
    {
        if (_rows.Count >= MaxRows)
        {
            _dropped++;

            return;
        }

        _rows.Add(entry);
    }

    /// <summary>
    /// Puts back a batch that could not be written, ahead of the rows added since, so the next batch retries it in
    /// order. Past <see cref="MaxRows"/> the newest rows are the ones dropped and counted. The batch's own dropped
    /// count is not added again: it was handed out with the batch.
    /// </summary>
    public void Return(LedgerBatch batch)
    {
        var rows = new List<LedgerEntry>(batch.Rows);

        rows.AddRange(_rows);
        _rows.Clear();

        var kept = Math.Min(rows.Count, MaxRows);

        _rows.AddRange(rows.GetRange(0, kept));
        _dropped += rows.Count - kept;
    }

    /// <summary>
    /// The waiting rows, oldest first, once <see cref="FlushSeconds"/> have passed since the last batch (or since the
    /// first call); null before that and when nothing is waiting. <paramref name="now"/> is any steady clock in seconds.
    /// </summary>
    public LedgerBatch? TakeIfDue(double now)
    {
        _lastTakenAt ??= now;

        if (now - _lastTakenAt < FlushSeconds || (_rows.Count == 0 && _dropped == 0)) return null;

        var batch = new LedgerBatch(_rows.ToArray(), _dropped);

        _lastTakenAt = now;
        _rows.Clear();
        _dropped = 0;

        return batch;
    }
}
