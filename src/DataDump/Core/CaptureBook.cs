using System;
using System.Collections.Generic;
using System.Linq;

namespace DataDump.Core;

public enum CaptureState
{
    Pending,
    Saved,
    Failed,
    TimedOut,
}

public sealed record CaptureResult(CaptureState State, string Detail);

/// <summary>
/// The page captures one snapshot asked the web view for. Answers arrive in callbacks on later frames; nothing waits
/// for them. The book closes when every page has answered or the deadline passes, and an answer after that is refused.
/// </summary>
public sealed class CaptureBook
{
    private readonly Dictionary<string, CaptureResult> _results = new(StringComparer.Ordinal);
    private readonly DateTime _deadline;

    public CaptureBook(IEnumerable<string> pages, DateTime deadline)
    {
        foreach (var page in pages) _results[page] = new CaptureResult(CaptureState.Pending, "");

        _deadline = deadline;
    }

    public bool IsClosed { get; private set; }

    /// <returns>False when the book is closed or the page was never asked for; the answer is dropped then.</returns>
    public bool Answer(string page, CaptureResult result)
    {
        if (IsClosed || !_results.TryGetValue(page, out var current) || current.State != CaptureState.Pending) return false;

        _results[page] = result;

        return true;
    }

    public bool IsDue(DateTime now) => !IsClosed && (now >= _deadline || _results.Values.All(result => result.State != CaptureState.Pending));

    /// <summary>Closes the book; pages still waiting are marked timed out.</summary>
    public SortedDictionary<string, CaptureResult> Close()
    {
        IsClosed = true;

        foreach (var page in _results.Keys.ToList())
        {
            if (_results[page].State == CaptureState.Pending) _results[page] = new CaptureResult(CaptureState.TimedOut, "no answer before the deadline");
        }

        return new SortedDictionary<string, CaptureResult>(_results, StringComparer.Ordinal);
    }
}
