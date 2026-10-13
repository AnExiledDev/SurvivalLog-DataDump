using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DataDump.Core;

/// <summary>Everything the ledger needs about one character at one moment.</summary>
/// <param name="RealSeconds">Seconds on a steady real clock that game speed and pauses do not touch.</param>
/// <param name="TotalSeconds">GameTimeManager.GetTotalSeconds: game seconds since day 0.</param>
/// <param name="Attrs">Attribute name to sample.</param>
/// <param name="Buffs">Active buff config id to name.</param>
public sealed record Moment(
    DateTime Wall,
    double RealSeconds,
    int Day,
    int SecondOfDay,
    int TotalSeconds,
    GameSpeed Speed,
    IReadOnlyDictionary<string, AttrSample> Attrs,
    IReadOnlyDictionary<int, string> Buffs);

/// <param name="During">Config_Action.During when the action started; speed mods rewrite it, so it is read then.</param>
public sealed record ActionStart(string Who, int ActionId, string ActionName, string ActionType, float During, Moment At);

public enum LedgerOutcome
{
    Finished,
    Cancelled,

    /// <summary>The same character started another action before this one was seen to end.</summary>
    Superseded,
}

public sealed record LedgerEntry(ActionStart Start, Moment End, LedgerOutcome Outcome);

/// <summary>Which actions the ledger writes.</summary>
public static class LedgerActions
{
    /// <summary>ActionType.Move. PlayAction itself treats it apart (ActionComponent.c:818).</summary>
    public const int MoveActionType = 7;

    /// <summary>Walking is most of what characters do and changes nothing the ledger is for, so it costs no sample and no line.</summary>
    public static bool IsLogged(int actionType) => actionType != MoveActionType;
}

/// <summary>
/// The open action of each character, between its start and its end. Holds one small record per character and does
/// nothing between those two calls. Character ids are only unique within one loaded save, so the book holds the
/// actions of one world at a time.
/// </summary>
public sealed class LedgerBook
{
    private readonly Dictionary<long, ActionStart> _open = new();
    private long? _worldId;

    /// <summary>
    /// Notes the world the next calls are about. A different world (another save loaded) or none (back at the title
    /// screen) drops every open action, so no line can start in one save and end in another.
    /// </summary>
    /// <returns>How many open actions were dropped.</returns>
    public int EnterWorld(long? worldId)
    {
        if (worldId == _worldId) return 0;

        var dropped = _open.Count;

        _worldId = worldId;
        _open.Clear();

        return dropped;
    }

    /// <summary>The action id open for the character, so a caller can skip sampling an end that matches nothing.</summary>
    public int? OpenActionOf(long agentId) => _open.TryGetValue(agentId, out var start) ? start.ActionId : null;

    /// <returns>The character's earlier action, closed as superseded at this start, if one was still open.</returns>
    public LedgerEntry? Start(long agentId, ActionStart start)
    {
        LedgerEntry? superseded = _open.TryGetValue(agentId, out var earlier) ? new LedgerEntry(earlier, start.At, LedgerOutcome.Superseded) : null;

        _open[agentId] = start;

        return superseded;
    }

    /// <returns>The finished entry; null when the ending action is not the one open for the character.</returns>
    public LedgerEntry? End(long agentId, int actionId, Moment at, bool isCancelled)
    {
        if (!_open.TryGetValue(agentId, out var start) || start.ActionId != actionId) return null;

        _open.Remove(agentId);

        return new LedgerEntry(start, at, isCancelled ? LedgerOutcome.Cancelled : LedgerOutcome.Finished);
    }
}

/// <summary>action-ledger.tsv: one tab-separated line per action.</summary>
public static class LedgerTsv
{
    public static readonly string Header = string.Join("\t",
        "real_time", "game_day", "game_time", "who", "action_id", "action_name", "action_type", "during_at_start",
        "game_seconds", "real_seconds", "game_speed", "attribute_changes", "buffs_gained", "buffs_lost", "outcome");

    public static string Line(LedgerEntry entry)
    {
        var start = entry.Start;
        var end = entry.End;
        var buffs = BuffDiff.Between(start.At.Buffs, end.Buffs);
        var changes = AttrDelta.Between(start.At.Attrs, end.Attrs);

        return string.Join("\t",
            end.Wall.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            end.Day.ToString(CultureInfo.InvariantCulture),
            GameClock.TimeOfDay(end.SecondOfDay),
            Cell(start.Who),
            start.ActionId.ToString(CultureInfo.InvariantCulture),
            Cell(start.ActionName),
            Cell(start.ActionType),
            start.During.ToString("R", CultureInfo.InvariantCulture),
            unchecked(end.TotalSeconds - start.At.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            Math.Round(end.RealSeconds - start.At.RealSeconds, 3).ToString("0.###", CultureInfo.InvariantCulture),
            GameSpeed.Across(start.At.Speed, end.Speed),
            Cell(string.Join("; ", changes.Select(change => change.ToString()))),
            Cell(string.Join(", ", buffs.Gained)),
            Cell(string.Join(", ", buffs.Lost)),
            entry.Outcome.ToString().ToLowerInvariant());
    }

    /// <summary>A tab or line break inside a value would split the line, so they become spaces.</summary>
    public static string Cell(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}

/// <summary>When the ledger file moves aside, so it never grows past its cap by more than one batch of lines.</summary>
public static class LedgerRotation
{
    public const long MaxBytes = 5L * 1024 * 1024;

    public static bool ShouldRotate(long currentBytes, long addedBytes, long maxBytes = MaxBytes) =>
        currentBytes > 0 && currentBytes + addedBytes > maxBytes;
}
