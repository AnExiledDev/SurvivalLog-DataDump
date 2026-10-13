using System.Collections.Generic;

namespace DataDump.Core;

// The shapes of a snapshot's state.json and ui.json. A null member is one the game did not have or would not give;
// what went wrong is in the nearest Problems list.

public sealed record StateDocument(string TakenAt, WorldDoc World, IReadOnlyList<AgentDoc> Agents, IReadOnlyList<string> Problems);

/// <param name="SecondOfDay">GameTimeManager.GetSecond.</param>
/// <param name="TotalSeconds">GameTimeManager.GetTotalSeconds, the clock buff end times are in.</param>
/// <param name="SpeedLevel">GameTimeManager.GetSpeedLevel: 1 to 3, 4 while the slow gear is on.</param>
public sealed record WorldDoc(
    int? Day,
    int? Hour24,
    int? SecondOfDay,
    string? TimeOfDay,
    int? TotalSeconds,
    int? SpeedLevel,
    float? TimeScale,
    bool? IsPausedScale,
    bool? WorldIsPause,
    bool? CameraTimePaused);

/// <param name="Role">player, survivor or robot.</param>
public sealed record AgentDoc(
    string Role,
    bool IsPlayer,
    long Id,
    int ConfigId,
    string? Name,
    string? NameLocal,
    float[]? Position,
    int? Floor,
    bool? IsDead,
    SortedDictionary<string, AttrDoc> Attributes,
    IReadOnlyList<BuffDoc> Buffs,
    ActionDoc? CurrentAction,
    IReadOnlyList<QueuedDoc> Queue,
    IReadOnlyList<string> Problems);

/// <param name="Shown">What the game shows: Total, or Total over the scaling ratio for a fraction attribute.</param>
public sealed record AttrDoc(int Total, double Shown, int Base, int Strengthening, int Condition, int Min, int Max, bool IsFraction)
{
    public static AttrDoc From(AttrSample sample) =>
        new(sample.Total, sample.Shown, sample.Base, sample.Strengthening, sample.Condition, sample.Min, sample.Max, sample.Scale is not null);
}

/// <param name="EndTime">BuffArgs.TimeEndTime, in GetTotalSeconds; -1 for a buff with no end.</param>
public sealed record BuffDoc(int Id, string? Name, string? NameLocal, bool? IsGood, int Count, int EndTime, int? SecondsLeft, long ReleaserId)
{
    /// <summary>
    /// Game seconds until the buff ends, as the game's buff tip works it out (Reducer_UI_BuffTips.c:254-273):
    /// none for an end time of -1, else end time minus the total seconds now.
    /// </summary>
    public static int? SecondsLeftOf(int endTime, int totalSecondsNow) => endTime == -1 ? null : unchecked(endTime - totalSecondsNow);
}

/// <param name="Elapsed">The action's effect CurLifeDuration.</param>
/// <param name="Length">The action's effect MaxLifeDuration; zero or less for an action that runs until stopped.</param>
public sealed record ActionDoc(
    int Id,
    string? Name,
    string? Type,
    float? During,
    float? EffectiveDuring,
    long TargetId,
    long EffectId,
    float? Elapsed,
    float? Length,
    float? Left,
    bool? IsFinished)
{
    /// <summary>Length minus elapsed; none for an action that runs until stopped.</summary>
    public static float? LeftOf(float? elapsed, float? length)
    {
        if (elapsed is not { } done || length is not { } total || total <= 0) return null;

        return total - done;
    }
}

public sealed record QueuedDoc(int ActionId, string? Name, long TargetId, long ParentId);

public sealed record UiDocument(
    string TakenAt,
    bool HasUiSystem,
    bool? LayerIsInitialized,
    bool? BrowserCrashed,
    string? WebViewUrl,
    IReadOnlyList<PageDoc> Pages,
    ClickDoc Clicks,
    IReadOnlyList<string> Problems);

/// <param name="Key">The page's key in WebUILayer.PageMap.</param>
public sealed record PageDoc(
    string Key,
    string? UIKey,
    string? Url,
    string? UIType,
    bool? IsUIActive,
    bool? IsPageActive,
    bool? IsReady,
    int? ZIndex,
    long? InstanceId,
    bool InPageList,
    string? StateType);

/// <param name="WorldImageRaycastTarget">CameraTouch's Image.raycastTarget: false means clicks on the world do nothing.</param>
public sealed record ClickDoc(bool? WorldImageRaycastTarget, bool? IsBattleSelectOpen, bool? CameraTimePaused);
