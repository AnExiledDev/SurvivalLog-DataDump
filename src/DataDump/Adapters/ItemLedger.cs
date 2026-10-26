using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DataDump.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using HarmonyLib;

namespace DataDump.Adapters;

/// <summary>
/// Writes item-ledger.tsv: one line for every item the game puts into a bag. ItemManager.TryAddItemToBag (rva 0x2728960)
/// is where the game makes an item in a bag: loot filling a container, a story drop, a reward, a pickup, a craft. Moving
/// an item between bags does not pass through it. It calls itself for an overflow split, so only the outermost call is
/// written. Its first 14 bytes are five pushes and a lea (GameAssembly.dll.asm), no relative jump or call.
///
/// The giver hooks say which of the game's item givers made the call; each one only names itself on the way in and out.
/// Every giver target was checked the same way: its first 14 bytes are movs, pushes, a sub and at most one cmp against a
/// static flag, no relative jump or call. A giver that cannot be hooked only costs the context column.
/// </summary>
internal static class ItemLedger
{
    public const string FileName = "item-ledger.tsv";

    private const double FlushSeconds = 5;
    private const int MaxWaiting = 10_000;

    /// <summary>Every game method that calls TryAddItemToBag to give items (callers.tsv), but the cheat panel.</summary>
    private static readonly (Type Type, string Name, Type[]? Arguments)[] Givers =
    {
        (typeof(QuestItemDrop), nameof(QuestItemDrop.ApplyOnEnterMap), null),
        (typeof(TruthRecorderManager), nameof(TruthRecorderManager.ApplyGapItemsOnEnterMap), null),
        (typeof(FurnitureLootTools), nameof(FurnitureLootTools.FillRandomLoot), null),
        (typeof(FurnitureLootManager), nameof(FurnitureLootManager.ApplyRoleFixedItems), null),
        (typeof(StrangerSupportManager), nameof(StrangerSupportManager.GrantGiftItemsToPlayer), null),
        (typeof(StrangerTradeActiveManager), nameof(StrangerTradeActiveManager.TryGrantBonusGift), null),
        (typeof(DyeBookDrop), nameof(DyeBookDrop.ApplyExploreDrop), null),
        (typeof(DropItemManager), nameof(DropItemManager.TryPickupItem), new[] { typeof(long), typeof(long), typeof(int).MakeByRefType(), typeof(bool) }),
        (typeof(ToolTableManager), nameof(ToolTableManager.TryCreateItem), null),
        (typeof(AE_AddItemInBag), nameof(AE_AddItemInBag.Run), null),
        (typeof(AE_AddRandomItem), nameof(AE_AddRandomItem.Run), null),
        (typeof(RobotComponent), nameof(RobotComponent.TryGiveModuleToLeadingRole), null),
        (typeof(DisasterNodeManager), nameof(DisasterNodeManager.RollChaosLoot), null),
        (typeof(CampManager), nameof(CampManager.EnsureDroneHubForLeader), null),
        (typeof(NeighborBasketReturnManager), nameof(NeighborBasketReturnManager.DeliverGiftsToBasket), null),
        (typeof(AidPlatformManager), nameof(AidPlatformManager.TakeRecv), null),
    };

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Gate = new();
    private static readonly GiverStack Giving = new();
    private static readonly Dictionary<MethodBase, string> GiverNames = new();
    private static List<ItemGain> _waiting = new();
    private static Action<string> _log = _ => { };
    private static SerialWorker? _worker;
    private static string _path = "";
    private static double _lastFlush;
    private static int _dropped;
    private static int _addDepth;
    private static bool _isOn;

    /// <returns>Whether the bag hook is in place; only then does <see cref="Tick"/> have anything to do.</returns>
    public static bool Apply(Harmony harmony, string folder, Action<string> log, SerialWorker worker)
    {
        _log = log;
        _worker = worker;
        _path = Path.Combine(folder, FileName);

        try
        {
            Directory.CreateDirectory(folder);
            PatchKit.Patch(harmony, typeof(ItemLedger), typeof(ItemManager), nameof(ItemManager.TryAddItemToBag), null, prefix: nameof(BeforeAdd), postfix: nameof(AfterAdd));
            _isOn = true;
        }
        catch (Exception ex)
        {
            log("could not hook adding items to bags, the item ledger is off: " + ex.Message);

            return false;
        }

        var missed = new List<string>();

        foreach (var (type, name, arguments) in Givers) TryGiver(harmony, type, name, arguments, nameof(EnterGiver), missed);

        TryGiver(harmony, typeof(RewardSetManager), nameof(RewardSetManager.ExecuteItemReward), null, nameof(EnterReward), missed);
        TryGiver(harmony, typeof(RewardSetManager), nameof(RewardSetManager.ExecuteRandomGroupReward), null, nameof(EnterReward), missed);
        TryGiver(harmony, typeof(ItemManager), nameof(ItemManager.TryAddPackageItems), null, nameof(EnterPackage), missed);

        log("item ledger: writing to " + _path + (missed.Count == 0 ? "" : ", no context from " + string.Join(", ", missed)));

        return true;
    }

    private static void TryGiver(Harmony harmony, Type type, string name, Type[]? arguments, string prefix, List<string> missed)
    {
        try
        {
            PatchKit.Patch(harmony, typeof(ItemLedger), type, name, arguments, prefix: prefix, postfix: nameof(LeaveGiver));
        }
        catch (Exception ex)
        {
            missed.Add(type.Name + "." + name + " (" + ex.Message + ")");
        }
    }

    /// <summary>
    /// Every frame, clears the givers: none can be running between frames, so any name still there belongs to a call
    /// that threw. Every few seconds, hands the gains that came in to the worker, which builds their lines and appends them.
    /// </summary>
    public static void Tick(float now)
    {
        Giving.Clear();
        _addDepth = 0;

        if (now - _lastFlush < FlushSeconds || _worker is not { } worker) return;

        _lastFlush = now;

        List<ItemGain> gains;
        int dropped;

        lock (Gate)
        {
            if (_waiting.Count == 0 && _dropped == 0) return;

            gains = _waiting;
            dropped = _dropped;
            _waiting = new List<ItemGain>();
            _dropped = 0;
        }

        if (dropped > 0) _log($"item ledger: {dropped} items dropped, too many waiting to be written");

        if (gains.Count > 0) PostWrite(worker, gains);
    }

    /// <summary>Its own method, so the closure over the gains is made only when there are some.</summary>
    private static void PostWrite(SerialWorker worker, List<ItemGain> gains) => worker.Post(() => Write(gains));

    /// <summary>On the worker.</summary>
    private static void Write(List<ItemGain> gains)
    {
        var text = new StringBuilder();

        if (!File.Exists(_path)) text.Append(ItemTsv.Header).Append('\n');

        foreach (var gain in gains) text.Append(ItemTsv.Line(gain)).Append('\n');

        File.AppendAllText(_path, text.ToString(), Utf8);
    }

    private static void BeforeAdd() => _addDepth++;

    private static void AfterAdd(long ownerId, int itemConfigId, int itemCount, bool __result)
    {
        if (_addDepth > 0) _addDepth--;

        if (!_isOn || _addDepth > 0) return;

        try
        {
            var world = GameSingleton.Existing<BattleLogicWorld>();
            int? day = world?._GameTimeManager is { } time ? time.GetDay() : null;

            Add(new ItemGain(DateTime.Now, day, MapWatcher.Current()?.Name ?? "", OwnerOf(world, ownerId), itemConfigId, itemCount, __result, Giving.Current));
        }
        catch (Exception ex)
        {
            PatchKit.Fail("reading an item put into a bag", ex);
        }
    }

    /// <summary>AgentManager.GetAgent (rva 0x3032540) is a dictionary lookup, then a search of the agent list for the player.</summary>
    private static string OwnerOf(BattleLogicWorld? world, long ownerId)
    {
        var agent = world?.__AgentManager_k__BackingField?.GetAgent(ownerId);

        if (agent is null) return "id:" + ownerId;

        if (GameState.RoleOrNull(agent) is { } role) return role;

        return (agent.TryCast<Furniture>() is null ? "agent:" : "furniture:") + agent.AgentConfigId;
    }

    private static void EnterGiver(MethodBase __originalMethod)
    {
        try
        {
            if (!GiverNames.TryGetValue(__originalMethod, out var name))
            {
                name = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
                GiverNames[__originalMethod] = name;
            }

            Giving.Enter(name);
        }
        catch (Exception ex)
        {
            Giving.Enter("");
            PatchKit.Fail("naming an item giver", ex);
        }
    }

    /// <summary>A reward set names its id, so a line can be matched to the RewardSet table.</summary>
    private static void EnterReward(MethodBase __originalMethod, Config_RewardSet __0)
    {
        Giving.Enter(__originalMethod.Name + " " + (__0 == null ? "?" : __0._ID_k__BackingField.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>A package names its item id.</summary>
    private static void EnterPackage(int __0) => Giving.Enter("TryAddPackageItems " + __0.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void LeaveGiver() => Giving.Leave();

    private static void Add(ItemGain gain)
    {
        lock (Gate)
        {
            if (_waiting.Count >= MaxWaiting) _dropped++;
            else _waiting.Add(gain);
        }
    }
}
