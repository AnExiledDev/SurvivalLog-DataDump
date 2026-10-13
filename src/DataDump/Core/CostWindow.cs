using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DataDump.Core;

/// <summary>
/// How long code running on the game's main thread took, per named part, gathered over a minute and given back as one
/// log line: calls, the longest call and the total, in milliseconds.
/// </summary>
public sealed class CostWindow
{
    public const double WindowSeconds = 60;

    private readonly SortedDictionary<string, Cost> _costs = new(StringComparer.Ordinal);
    private double? _startedAt;

    private sealed class Cost
    {
        public int Calls;
        public double MaxMs;
        public double TotalMs;
    }

    public void Add(string part, double milliseconds)
    {
        if (!_costs.TryGetValue(part, out var cost))
        {
            cost = new Cost();
            _costs[part] = cost;
        }

        cost.Calls++;
        cost.MaxMs = Math.Max(cost.MaxMs, milliseconds);
        cost.TotalMs += milliseconds;
    }

    /// <summary>
    /// The line for a window that has run its full length, after which a new window starts; null before that, and for
    /// a window in which nothing ran. The first call starts the first window. <paramref name="now"/> is any steady
    /// clock in seconds.
    /// </summary>
    public string? TakeIfDue(double now)
    {
        _startedAt ??= now;

        if (now - _startedAt < WindowSeconds) return null;

        _startedAt = now;

        if (_costs.Count == 0) return null;

        var line = string.Join("; ", _costs.Select(pair => $"{pair.Key} {pair.Value.Calls} calls, max {Ms(pair.Value.MaxMs)} ms, total {Ms(pair.Value.TotalMs)} ms"));

        _costs.Clear();

        return line;
    }

    private static string Ms(double milliseconds) => milliseconds.ToString("0.###", CultureInfo.InvariantCulture);
}
