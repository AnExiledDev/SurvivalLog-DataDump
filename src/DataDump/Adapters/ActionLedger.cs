using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using DataDump.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using HarmonyLib;

namespace DataDump.Adapters;

/// <summary>
/// Writes action-ledger.tsv: one line for every action the player, a survivor or a robot finishes or breaks off, with
/// how long it took and what changed on the character meanwhile. An action starts at ActionComponent.PlayAction
/// (rva 0x2ddc2d0) and ends at ActionComponent.RemoveActionCall (rva 0x2ddcc20), which BTEffectManager.DestroyBtArgs
/// calls with the effect's ForceBroken flag (BTEffectManager.c:481-484); the game gives an action's end reward only when
/// that flag is off (ActionManager.c:6678), so the flag is what "cancelled" means here. Neither target starts with a
/// relative jump or call in its first 14 bytes (GameAssembly.dll.asm: push/sub rsp for PlayAction, mov/push/sub rsp for
/// RemoveActionCall).
/// <para>
/// The hooks run inside the game's own calls, so they do only what a line needs: walking is skipped before anything is
/// read, a character is sampled only at a start and at the end of its open action, and finished lines wait in memory.
/// The ticker writes them at most every few seconds, notices a save being left, and logs what the hooks cost once a
/// minute. Any failure is logged once and never reaches the game. Static because Harmony patches are.
/// </para>
/// </summary>
internal static class ActionLedger
{
    public const string FileName = "action-ledger.tsv";
    public const string OldFileName = "action-ledger.old.tsv";

    private const string StartSampled = "PlayAction prefix";
    private const string StartCounted = "PlayAction postfix";
    private const string EndNoted = "RemoveActionCall prefix";
    private const string EndSampled = "RemoveActionCall postfix";
    private const string Flushed = "file write";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Gate = new();
    private static readonly LedgerBook Book = new();
    private static readonly LedgerQueue Queue = new();
    private static readonly CostWindow Costs = new();
    private static readonly Stopwatch RealClock = Stopwatch.StartNew();
    private static Action<string> _log = _ => { };
    private static string _path = "";
    private static string _oldPath = "";
    private static bool _isOn;

    /// <summary>A sampled start, kept from PlayAction's prefix to its postfix.</summary>
    private sealed record Starting(long WorldId, long AgentId, ActionStart Start);

    /// <summary>The action an ending call removes, kept from the prefix to the postfix.</summary>
    private sealed record Ending(long AgentId, int ActionId);

    /// <returns>Whether the hooks are in place; only then does <see cref="Tick"/> have anything to do.</returns>
    public static bool Apply(Harmony harmony, string folder, Action<string> log)
    {
        _log = log;
        _path = Path.Combine(folder, FileName);
        _oldPath = Path.Combine(folder, OldFileName);

        try
        {
            Directory.CreateDirectory(folder);
            PatchKit.Patch(harmony, typeof(ActionLedger), typeof(ActionComponent), nameof(ActionComponent.PlayAction), null, prefix: nameof(BeforePlay), postfix: nameof(AfterPlay));
            PatchKit.Patch(harmony, typeof(ActionLedger), typeof(ActionComponent), nameof(ActionComponent.RemoveActionCall), null, prefix: nameof(BeforeRemove), postfix: nameof(AfterRemove));
            _isOn = true;
            log("action ledger: writing to " + _path);
        }
        catch (Exception ex)
        {
            log("action ledger off, the game's actions could not be hooked: " + ex.Message);
        }

        return _isOn;
    }

    /// <summary>The ticker's check: drops open actions of a save that was left, writes waiting lines when due, logs the minute's cost.</summary>
    public static void Tick(float now)
    {
        EnterWorld(WorldIdOf(GameSingleton.Existing<BattleLogicWorld>()));
        Flush(now);

        string? costs;

        lock (Gate) costs = Costs.TakeIfDue(now);

        if (costs is not null) _log("action ledger cost, last minute: " + costs);
    }

    /// <summary>
    /// Samples the character before PlayAction adds the action's first effects. PlayAction throws on a missing config,
    /// world, CharacterTalkManager or AgentManager (ActionComponent.c:816-848); the prefix makes the same checks and
    /// samples nothing when one fails, and the start only counts in the postfix, which a throw skips.
    /// Walking is skipped before any read (ActionType.Move, which PlayAction itself treats apart at line 818).
    /// </summary>
    private static void BeforePlay(ActionComponent __instance, Config_Action config, out Starting? __state)
    {
        __state = null;

        if (!_isOn || config == null) return;

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var actionType = config._ActionType_k__BackingField;

            if (!LedgerActions.IsLogged(actionType)) return;

            var world = GameSingleton.Existing<BattleLogicWorld>();
            var agentId = __instance._ownerInstanceId_k__BackingField;

            if (world is null || world.__CharacterTalkManager_k__BackingField == null) return;

            if (AgentOf(world, agentId) is not { } agent || GameState.RoleOrNull(agent) is null || Sample(agent) is not { } at) return;

            var start = new ActionStart(GameState.WhoIs(agent), config._ID_k__BackingField, config._Name_k__BackingField ?? "", GameState.ActionTypeName(actionType), config._During_k__BackingField, at);

            __state = new Starting(world._InstanceId_k__BackingField, agentId, start);
        }
        catch (Exception ex)
        {
            PatchKit.Fail("in the action ledger at the start of an action", ex);
        }
        finally
        {
            Measure(StartSampled, startedAt);
        }
    }

    /// <summary>PlayAction returned, so the action started: it opens, closing any action the character still had open.</summary>
    private static void AfterPlay(Starting? __state)
    {
        if (__state is null) return;

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            EnterWorld(__state.WorldId);

            lock (Gate)
            {
                if (Book.Start(__state.AgentId, __state.Start) is { } superseded) Queue.Add(superseded);
            }
        }
        catch (Exception ex)
        {
            PatchKit.Fail("in the action ledger after the start of an action", ex);
        }
        finally
        {
            Measure(StartCounted, startedAt);
        }
    }

    /// <summary>Notes which action is ending, before RemoveActionCall clears the component's action. Most calls end at the first lookup.</summary>
    private static void BeforeRemove(ActionComponent __instance, out Ending? __state)
    {
        __state = null;

        if (!_isOn) return;

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var agentId = __instance._ownerInstanceId_k__BackingField;
            int? openAction;

            lock (Gate) openAction = Book.OpenActionOf(agentId);

            if (openAction is null) return;

            EnterWorld(WorldIdOf(GameSingleton.Existing<BattleLogicWorld>()));

            lock (Gate) openAction = Book.OpenActionOf(agentId);

            if (openAction is not { } actionId || __instance._ActionConfig_k__BackingField?._ID_k__BackingField != actionId) return;

            __state = new Ending(agentId, actionId);
        }
        catch (Exception ex)
        {
            PatchKit.Fail("in the action ledger before the end of an action", ex);
        }
        finally
        {
            Measure(EndNoted, startedAt);
        }
    }

    /// <summary>A postfix, so the end reward the removal gives is already in the attribute changes.</summary>
    private static void AfterRemove(bool forceBroken, Ending? __state)
    {
        if (__state is null || !_isOn) return;

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var world = GameSingleton.Existing<BattleLogicWorld>();

            if (world is null || AgentOf(world, __state.AgentId) is not { } agent || Sample(agent) is not { } at) return;

            lock (Gate)
            {
                if (Book.End(__state.AgentId, __state.ActionId, at, forceBroken) is { } entry) Queue.Add(entry);
            }
        }
        catch (Exception ex)
        {
            PatchKit.Fail("in the action ledger at the end of an action", ex);
        }
        finally
        {
            Measure(EndSampled, startedAt);
        }
    }

    /// <summary>
    /// Each BattleLogicWorld gets a new InstanceId when it is built (BaseSingleton ctor, rva 0x1f4f670:
    /// InstanceIdFactory.GenerateId, a process-wide counter nothing sets back), and BattleMain.OnDestroy (rva 0x2cdb5d0)
    /// clears the world when a save is left. So a different id, or none, means a different save.
    /// </summary>
    private static long? WorldIdOf(BattleLogicWorld? world) => world?._InstanceId_k__BackingField;

    private static void EnterWorld(long? worldId)
    {
        int dropped;

        lock (Gate) dropped = Book.EnterWorld(worldId);

        if (dropped > 0) _log($"action ledger: the save was left; {dropped} unfinished actions get no line");
    }

    /// <summary>AgentManager.GetAgent (rva 0x2e1abe0): a dictionary lookup; only for the player missing from it does the game re-add it.</summary>
    private static BaseAgent? AgentOf(BattleLogicWorld world, long agentId) => world.__AgentManager_k__BackingField?.GetAgent(agentId);

    private static Moment? Sample(BaseAgent agent) => GameState.MomentOf(agent, DateTime.Now, RealClock.Elapsed.TotalSeconds);

    private static void Measure(string part, long startedAt)
    {
        var milliseconds = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;

        lock (Gate) Costs.Add(part, milliseconds);
    }

    /// <summary>
    /// Writes the waiting lines once they are due. A file that cannot be written (open in a program that locks it)
    /// keeps its lines queued for the next try; the queue's cap bounds how many.
    /// </summary>
    private static void Flush(float now)
    {
        LedgerBatch? batch;

        lock (Gate) batch = Queue.TakeIfDue(now);

        if (batch is null) return;

        if (batch.Dropped > 0) _log($"action ledger: {batch.Dropped} lines were dropped while the file could not be written");

        if (batch.Rows.Count == 0) return;

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            Write(batch.Rows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (Gate) Queue.Return(batch);

            PatchKit.Fail("writing the action ledger (lines are kept and retried)", ex);
        }
        finally
        {
            Measure(Flushed, startedAt);
        }
    }

    /// <summary>One append per batch. The file moves aside before the batch would pass its cap; a new file starts with the header.</summary>
    private static void Write(IReadOnlyList<LedgerEntry> rows)
    {
        var lines = new StringBuilder();

        foreach (var row in rows) lines.Append(LedgerTsv.Line(row)).Append('\n');

        var text = lines.ToString();
        var file = new FileInfo(_path);
        var currentBytes = file.Exists ? file.Length : 0;

        if (LedgerRotation.ShouldRotate(currentBytes, Utf8.GetByteCount(text)))
        {
            File.Move(_path, _oldPath, overwrite: true);
            currentBytes = 0;
        }

        File.AppendAllText(_path, currentBytes == 0 ? LedgerTsv.Header + "\n" + text : text, Utf8);
    }
}
