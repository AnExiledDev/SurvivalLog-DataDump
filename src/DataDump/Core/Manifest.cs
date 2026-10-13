using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DataDump.Core;

/// <summary>How one output of a dump went: written with a count, failed with a reason, or left out on purpose.</summary>
public abstract record DumpOutcome(string Name);

public sealed record Dumped(string Name, int Count) : DumpOutcome(Name);

public sealed record DumpFailed(string Name, string Reason) : DumpOutcome(Name);

public sealed record DumpSkipped(string Name, string Reason) : DumpOutcome(Name);

/// <param name="Tables">One outcome per config table.</param>
/// <param name="Files">One outcome per other file (language maps, input bindings), counted in entries.</param>
public sealed record ManifestInput(
    string Plugin,
    string? GameVersion,
    string Trigger,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? Language,
    IReadOnlyList<DumpOutcome> Tables,
    IReadOnlyList<DumpOutcome> Files);

/// <summary>manifest.json: what one dump wrote, what failed and why. Everything is sorted so two dumps diff cleanly.</summary>
public static class Manifest
{
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    public sealed record Problem(string Name, string Reason);

    public sealed record Document(
        string Plugin,
        string? GameVersion,
        string Trigger,
        string StartedAt,
        string FinishedAt,
        double Seconds,
        string? Language,
        int TableCount,
        long RowCount,
        SortedDictionary<string, int> Tables,
        SortedDictionary<string, int> Files,
        IReadOnlyList<Problem> Failed,
        IReadOnlyList<Problem> Skipped);

    public static Document Build(ManifestInput input)
    {
        var tables = Counts(input.Tables);
        var everything = input.Tables.Concat(input.Files).ToList();

        return new Document(
            input.Plugin,
            input.GameVersion,
            input.Trigger,
            input.StartedAt.ToString(TimeFormat, CultureInfo.InvariantCulture),
            input.FinishedAt.ToString(TimeFormat, CultureInfo.InvariantCulture),
            Math.Round((input.FinishedAt - input.StartedAt).TotalSeconds, 3),
            input.Language,
            tables.Count,
            tables.Values.Sum(count => (long)count),
            tables,
            Counts(input.Files),
            Problems(everything.OfType<DumpFailed>().Select(failed => new Problem(failed.Name, failed.Reason))),
            Problems(everything.OfType<DumpSkipped>().Select(skipped => new Problem(skipped.Name, skipped.Reason))));
    }

    public static string ToJson(ManifestInput input) => new ValueWriter(new PlainValueReader(), 6, isIndented: true).Write(Build(input));

    private static SortedDictionary<string, int> Counts(IEnumerable<DumpOutcome> outcomes)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var dumped in outcomes.OfType<Dumped>()) counts[dumped.Name] = dumped.Count;

        return counts;
    }

    private static IReadOnlyList<Problem> Problems(IEnumerable<Problem> problems) =>
        problems.OrderBy(problem => problem.Name, StringComparer.Ordinal).ToList();
}
