using System;
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
/// RemoveActionCall). The hooks sample the character only at those two calls and only for the three kinds of character;
/// any failure is logged once and never reaches the game. Static because Harmony patches are.
/// </summary>
internal static class ActionLedger
{
    public const string FileName = "action-ledger.tsv";
    public const string OldFileName = "action-ledger.old.tsv";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Gate = new();
    private static readonly LedgerBook Book = new();
    private static readonly Stopwatch RealClock = Stopwatch.StartNew();
    private static string _path = "";
    private static string _oldPath = "";
    private static bool _isOn;

    /// <summary>The action an ending call removes, kept from the prefix to the postfix.</summary>
    private sealed record Ending(long AgentId, int ActionId);

    public static void Apply(Harmony harmony, string folder, Action<string> log)
    {
        _path = Path.Combine(folder, FileName);
        _oldPath = Path.Combine(folder, OldFileName);

        try
        {
            Directory.CreateDirectory(folder);
            PatchKit.Patch(harmony, typeof(ActionLedger), typeof(ActionComponent), nameof(ActionComponent.PlayAction), null, prefix: nameof(BeforePlay));
            PatchKit.Patch(harmony, typeof(ActionLedger), typeof(ActionComponent), nameof(ActionComponent.RemoveActionCall), null, prefix: nameof(BeforeRemove), postfix: nameof(AfterRemove));
            _isOn = true;
            log("action ledger: writing to " + _path);
        }
        catch (Exception ex)
        {
            log("action ledger off, the game's actions could not be hooked: " + ex.Message);
        }
    }

    /// <summary>A prefix, so the character is sampled before the action's first effects: PlayAction adds them itself.</summary>
    private static void BeforePlay(ActionComponent __instance, Config_Action config)
    {
        if (!_isOn || config == null) return;

        try
        {
            var agentId = __instance.ownerInstanceId;

            if (AgentOf(agentId) is not { } agent || GameState.RoleOrNull(agent) is null || Sample(agent) is not { } at) return;

            var start = new ActionStart(GameState.WhoIs(agent), config.ID, config.Name ?? "", GameState.ActionTypeName(config.ActionType), config.During, at);

            lock (Gate)
            {
                if (Book.Start(agentId, start) is { } superseded) Append(superseded);
            }
        }
        catch (Exception ex)
        {
            PatchKit.Fail("in the action ledger at the start of an action", ex);
        }
    }

    /// <summary>Notes which action is ending, before RemoveActionCall clears the component's action.</summary>
    private static void BeforeRemove(ActionComponent __instance, out Ending? __state)
    {
        __state = null;

        if (!_isOn) return;

        try
        {
            var agentId = __instance.ownerInstanceId;
            int? openAction;

            lock (Gate) openAction = Book.OpenActionOf(agentId);

            if (openAction is not { } actionId || __instance.ActionConfig?.ID != actionId) return;

            __state = new Ending(agentId, actionId);
        }
        catch (Exception ex)
        {
            PatchKit.Fail("in the action ledger before the end of an action", ex);
        }
    }

    /// <summary>A postfix, so the end reward the removal gives is already in the attribute changes.</summary>
    private static void AfterRemove(bool forceBroken, Ending? __state)
    {
        if (__state is null || !_isOn) return;

        try
        {
            if (AgentOf(__state.AgentId) is not { } agent || Sample(agent) is not { } at) return;

            lock (Gate)
            {
                if (Book.End(__state.AgentId, __state.ActionId, at, forceBroken) is { } entry) Append(entry);
            }
        }
        catch (Exception ex)
        {
            PatchKit.Fail("in the action ledger at the end of an action", ex);
        }
    }

    /// <summary>AgentManager.GetAgent (rva 0x2e1abe0): a dictionary lookup; only for the player missing from it does the game re-add it.</summary>
    private static BaseAgent? AgentOf(long agentId) => GameSingleton.Existing<BattleLogicWorld>()?._AgentManager?.GetAgent(agentId);

    private static Moment? Sample(BaseAgent agent) => GameState.MomentOf(agent, DateTime.Now, RealClock.Elapsed.TotalSeconds);

    /// <summary>One line, under the gate. The file moves aside before it would pass its cap; a new file starts with the header.</summary>
    private static void Append(LedgerEntry entry)
    {
        var line = LedgerTsv.Line(entry) + "\n";
        var file = new FileInfo(_path);
        var currentBytes = file.Exists ? file.Length : 0;

        if (LedgerRotation.ShouldRotate(currentBytes, Utf8.GetByteCount(line)))
        {
            File.Move(_path, _oldPath, overwrite: true);
            currentBytes = 0;
        }

        File.AppendAllText(_path, currentBytes == 0 ? LedgerTsv.Header + "\n" + line : line, Utf8);
    }
}
