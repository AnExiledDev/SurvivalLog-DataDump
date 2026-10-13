using System;
using System.Collections.Generic;
using System.Globalization;
using DataDump.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.Battle.Show;
using DynamicNpc = GameCore.HotUpdate.Battle.Logic.DynamicNpc;

namespace DataDump.Adapters;

/// <summary>
/// Reads the world and its characters for a snapshot's state.json, and one character's attributes and buffs for the
/// action ledger. Only reads: every game call here is a getter or a lookup (each named where it is called). A read
/// that fails is written as a problem beside the value it would have given, and the rest is still read.
/// Main thread only, like every Il2Cpp read.
/// </summary>
internal static class GameState
{
    private static readonly WorldDoc NoWorld = new(null, null, null, null, null, null, null, null, null, null);

    /// <summary>What the agent reads share: the managers and the clock, read once per snapshot.</summary>
    private sealed record Context(ConfigManager? Configs, ActionManager? Actions, BTEffectManager? Effects, AgentManager Agents, int? TotalSeconds);

    public static StateDocument Read(DateTime now)
    {
        var problems = new ProblemList();
        var takenAt = now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var world = GameSingleton.Existing<BattleLogicWorld>();

        if (world is null)
        {
            problems.Add("no game world: no save is loaded");

            return new StateDocument(takenAt, NoWorld, Array.Empty<AgentDoc>(), problems.Lines);
        }

        var time = problems.Object("the game clock", () => world._GameTimeManager);
        var worldDoc = World(world, time, problems);

        return new StateDocument(takenAt, worldDoc, Agents(world, worldDoc.TotalSeconds, problems), problems.Lines);
    }

    /// <summary>
    /// One character for the action ledger. Null when no world or clock is loaded; a failed read throws, for the
    /// ledger's hook to catch and report.
    /// </summary>
    public static Moment? MomentOf(BaseAgent agent, DateTime wall, double realSeconds)
    {
        var time = GameSingleton.Existing<BattleLogicWorld>()?._GameTimeManager;

        if (time is null) return null;

        var speed = new GameSpeed(time.GetSpeedLevel(), time.BattleTimeScale);

        return new Moment(wall, realSeconds, time.GetDay(), time.GetSecond(), time.GetTotalSeconds(), speed, AttrSamples(agent), BuffNames(agent));
    }

    /// <summary>Who an agent is, for the ledger: its role and its Config_Player name.</summary>
    public static string WhoIs(BaseAgent agent)
    {
        var role = RoleOf(agent);
        var name = PlayerConfigOf(GameSingleton.Existing<ConfigManager>(), agent.AgentConfigId)?.Name;

        return string.IsNullOrEmpty(name) ? role + ":" + agent.InstanceId.ToString(CultureInfo.InvariantCulture) : role + ":" + name;
    }

    /// <summary>player, robot or survivor; null for any other agent (furniture, zombies, static NPCs).</summary>
    public static string? RoleOrNull(BaseAgent agent)
    {
        if (agent.TryCast<LeadingRole>() is not null) return "player";

        return agent.TryCast<DynamicNpc>() is { } npc ? (npc.IsRobot ? "robot" : "survivor") : null;
    }

    private static string RoleOf(BaseAgent agent) => RoleOrNull(agent) ?? "other";

    /// <summary>
    /// GameTimeManager's getters (GetDay rva 0x2553580, GetHour24 0x25535d0, GetSecond 0x25536f0, GetSpeedLevel
    /// 0x2553740, GetTotalSeconds 0x2553780, IsPausedScale 0x2553f50) read the timer and fields only.
    /// </summary>
    private static WorldDoc World(BattleLogicWorld world, GameTimeManager? time, ProblemList problems)
    {
        var isWorldPaused = problems.Value("world pause", () => world.IsPause);
        var isCameraPaused = problems.MaybeValue<bool>("camera pause", () => GameSingleton.Existing<BattleShowWorld>()?._CameraManager?.isTimePaused);

        if (time is null)
        {
            problems.Add("the world has no game clock");

            return NoWorld with { WorldIsPause = isWorldPaused, CameraTimePaused = isCameraPaused };
        }

        var second = problems.Value("second of day", () => time.GetSecond());

        return new WorldDoc(
            problems.Value("day", () => time.GetDay()),
            problems.Value("hour", () => time.GetHour24()),
            second,
            second is { } ofDay ? GameClock.TimeOfDay(ofDay) : null,
            problems.Value("total seconds", () => time.GetTotalSeconds()),
            problems.Value("speed level", () => time.GetSpeedLevel()),
            problems.Value("time scale", () => time.BattleTimeScale),
            problems.Value("paused scale", () => time.IsPausedScale()),
            isWorldPaused,
            isCameraPaused);
    }

    /// <summary>
    /// The player (AgentManager.GetLeadingRole, rva 0x2e1c6b0: a lookup of LeadingRoleId) and every survivor and robot
    /// (GetDynamicNpcs, rva 0x2e1b5c0: a new list of the DynamicNpc agents).
    /// </summary>
    private static List<AgentDoc> Agents(BattleLogicWorld world, int? totalSeconds, ProblemList problems)
    {
        var docs = new List<AgentDoc>();
        var agents = problems.Object("the agent manager", () => world._AgentManager);

        if (agents is null)
        {
            problems.Add("the world has no agent manager");

            return docs;
        }

        var context = new Context(
            GameSingleton.Existing<ConfigManager>(),
            problems.Object("the action manager", () => world._ActionManager),
            problems.Object("the effect manager", () => world._BTEffectManager),
            agents,
            totalSeconds);
        var player = problems.Object("the player", () => agents.GetLeadingRole());

        if (player is not null) docs.Add(Agent(player, "player", context));

        var npcs = problems.Object("the survivors and robots", () => agents.GetDynamicNpcs());

        if (npcs is null) return docs;

        try
        {
            for (var i = 0; i < npcs.Count; i++)
            {
                var npc = npcs[i];

                if (npc is not null) docs.Add(Agent(npc, problems.Value("robot or survivor", () => npc.IsRobot) == true ? "robot" : "survivor", context));
            }
        }
        catch (Exception ex)
        {
            problems.Add("the survivors and robots: " + ProblemList.Reason(ex));
        }

        return docs;
    }

    private static AgentDoc Agent(BaseAgent agent, string role, Context context)
    {
        var problems = new ProblemList();
        var id = problems.Value("id", () => agent.InstanceId) ?? 0;
        var configId = problems.Value("config id", () => agent.AgentConfigId) ?? 0;
        var config = problems.Object("name", () => PlayerConfigOf(context.Configs, configId));

        return new AgentDoc(
            role,
            role == "player",
            id,
            configId,
            config?.Name,
            config?.Name_Local,
            problems.Object("position", () => PositionOf(agent)),
            problems.Value("floor", () => context.Agents.GetAgentFloor(id)),
            problems.Value("dead", () => agent.IsDead),
            problems.Object("attributes", () => AttrDocs(agent)) ?? new SortedDictionary<string, AttrDoc>(StringComparer.Ordinal),
            problems.Object("buffs", () => BuffDocs(agent, context.TotalSeconds)) ?? new List<BuffDoc>(),
            problems.Object("current action", () => CurrentAction(agent, id, context)),
            problems.Object("action queue", () => Queue(id, context)) ?? new List<QueuedDoc>(),
            problems.Lines);
    }

    /// <summary>
    /// ConfigManager._Config_Player_Dict looked up directly: Get_Config_Player (rva 0x2cad010) writes an error to the
    /// game's log for an id it does not have, and a robot's config id may be one.
    /// </summary>
    private static Config_Player? PlayerConfigOf(ConfigManager? configs, int configId)
    {
        var table = configs?._Config_Player_Dict;

        return table is not null && table.TryGetValue(configId, out var config) ? config : null;
    }

    private static Config_Action? ActionConfigOf(ConfigManager? configs, int actionId)
    {
        var table = configs?._Config_Action_Dict;

        return table is not null && table.TryGetValue(actionId, out var config) ? config : null;
    }

    /// <summary>TransformComponent.Position (rva 0x2e2f200) reads the Unity transform's position.</summary>
    private static float[]? PositionOf(BaseAgent agent)
    {
        var transform = AgentTools.GetAgentComponent<TransformComponent>(agent);

        if (transform is null) return null;

        var position = transform.Position;

        return new[] { position.x, position.y, position.z };
    }

    private static SortedDictionary<string, AttrDoc> AttrDocs(BaseAgent agent)
    {
        var docs = new SortedDictionary<string, AttrDoc>(StringComparer.Ordinal);

        foreach (var attr in AttrSamples(agent)) docs[attr.Key] = AttrDoc.From(attr.Value);

        return docs;
    }

    /// <summary>Every attribute in AttributeComponent.AttrDict, by name; a fraction attribute carries GameKey.Attr_ScalingRatio.</summary>
    private static SortedDictionary<string, AttrSample> AttrSamples(BaseAgent agent)
    {
        var samples = new SortedDictionary<string, AttrSample>(StringComparer.Ordinal);
        var attrs = AgentTools.GetAgentComponent<AttributeComponent>(agent)?.AttrDict;

        if (attrs is null) return samples;

        var scale = GameKey.Attr_ScalingRatio;

        foreach (var entry in attrs)
        {
            var attr = entry.Value;

            if (attr is null) continue;

            samples[entry.Key.ToString()] = new AttrSample(attr.BaseValue, attr.StrengtheningValue, attr.ConditionValue, attr.Min, attr.Max, attr.isSingle ? scale : null);
        }

        return samples;
    }

    /// <summary>BuffComponent.GetEffectiveBuffList (rva 0x5ef770) returns the component's effective list as it is.</summary>
    private static List<BuffArgs> EffectiveBuffs(BaseAgent agent)
    {
        var buffs = new List<BuffArgs>();
        var list = AgentTools.GetAgentComponent<BuffComponent>(agent)?.GetEffectiveBuffList();

        if (list is null) return buffs;

        for (var i = 0; i < list.Count; i++)
        {
            var buff = list[i];

            if (buff is not null) buffs.Add(buff);
        }

        return buffs;
    }

    private static List<BuffDoc> BuffDocs(BaseAgent agent, int? totalSeconds)
    {
        var docs = new List<BuffDoc>();

        foreach (var buff in EffectiveBuffs(agent))
        {
            var config = buff.config;
            var endTime = buff.TimeEndTime;
            var secondsLeft = totalSeconds is { } now ? BuffDoc.SecondsLeftOf(endTime, now) : null;

            docs.Add(new BuffDoc(config?.ID ?? 0, config?.Name, config?.Name_Local, config?.IsGood, buff.BuffCount, endTime, secondsLeft, buff.releaserId));
        }

        return docs;
    }

    /// <summary>Buff config id to name, for the ledger's gained and lost buffs.</summary>
    private static Dictionary<int, string> BuffNames(BaseAgent agent)
    {
        var names = new Dictionary<int, string>();

        foreach (var buff in EffectiveBuffs(agent))
        {
            var config = buff.config;

            if (config is not null) names[config.ID] = config.Name ?? config.Name_Local ?? "";
        }

        return names;
    }

    /// <summary>
    /// The character's current action: AgentActionSource.CurrentAction (ActionManager.GetActionSource), which the game
    /// clears when the action is removed. Its progress is its effect, AgentActionSource.CurrentTaskBt, the id the game
    /// itself hands to BTEffectManager.GetProgress (ActionManager.c:8042-8053); GetBtArgsById (rva 0x2f42dc0) is a
    /// dictionary lookup. EffectiveDuring is the ActionComponent's, read only when it is playing the same action.
    /// </summary>
    private static ActionDoc? CurrentAction(BaseAgent agent, long id, Context context)
    {
        var source = context.Actions?.GetActionSource(id);
        var current = source?.CurrentAction;

        if (source is null || current is null) return null;

        var config = ActionConfigOf(context.Configs, current.ActionId);
        var component = AgentTools.GetAgentComponent<ActionComponent>(agent);
        float? effectiveDuring = component is not null && component.ActionConfig?.ID == current.ActionId ? component.EffectiveDuring : null;
        var effectId = source.CurrentTaskBt;
        var effect = effectId > 0 ? context.Effects?.GetBtArgsById(effectId) : null;
        float? elapsed = effect?.CurLifeDuration;
        float? length = effect?.MaxLifeDuration;

        return new ActionDoc(
            current.ActionId,
            config?.Name,
            config is null ? null : ActionTypeName(config.ActionType),
            config?.During,
            effectiveDuring,
            current.TargetId,
            effectId,
            elapsed,
            length,
            ActionDoc.LeftOf(elapsed, length),
            effect?.IsFinished);
    }

    /// <summary>The actions waiting in AgentActionSource.ActionList, in order.</summary>
    private static List<QueuedDoc> Queue(long id, Context context)
    {
        var docs = new List<QueuedDoc>();
        var queue = context.Actions?.GetActionSource(id)?.ActionList;

        if (queue is null) return docs;

        for (var i = 0; i < queue.Count; i++)
        {
            var action = queue[i];

            if (action is not null) docs.Add(new QueuedDoc(action.ActionId, ActionConfigOf(context.Configs, action.ActionId)?.Name, action.TargetId, action.ParentId));
        }

        return docs;
    }

    /// <summary>Config_Action.ActionType is an ActionType number; one the enum does not name is written as the number.</summary>
    public static string ActionTypeName(int actionType) => ((ActionType)actionType).ToString();
}
