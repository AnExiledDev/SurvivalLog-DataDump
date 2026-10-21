using System;

namespace DataDump.Core;

/// <summary>
/// Every hook that cannot be placed is logged as "could not hook ...". Those lines go to the BepInEx log as errors, so
/// after a game update the Mod Manager can show that the mod may need an update. Keep that wording for new hooks.
/// </summary>
public static class HookFailure
{
    public const string Prefix = "could not hook";

    public static bool Is(string message) => message.StartsWith(Prefix, StringComparison.Ordinal);
}
