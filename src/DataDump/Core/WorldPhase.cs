namespace DataDump.Core;

/// <summary>
/// Before or after the disaster, from the game clock's WorldStateType (PreDisaster 1, PostDisaster 2). Scene names do
/// not tell: Scene_AfterTheDisaster02 is the pre-disaster world. A location's map data and pictures go under its
/// phase's folder, so the two worlds never mix.
/// </summary>
public static class WorldPhase
{
    public const string PreDisaster = "pre-disaster";
    public const string PostDisaster = "post-disaster";
    public const string Unknown = "unknown-phase";

    /// <summary>How long a new location's map data waits for the clock to name its phase before it is written as unknown.</summary>
    public const float WaitSeconds = 10;

    public static string FolderOf(int? worldState) => worldState switch
    {
        1 => PreDisaster,
        2 => PostDisaster,
        _ => Unknown,
    };

    public static bool IsKnown(int? worldState) => FolderOf(worldState) != Unknown;

    /// <summary>A location seen at <paramref name="firstSeen"/> is written once its phase is known or the wait is over.</summary>
    public static bool IsReady(int? worldState, float firstSeen, float now) => IsKnown(worldState) || now - firstSeen >= WaitSeconds;
}
