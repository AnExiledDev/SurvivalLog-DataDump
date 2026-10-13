using System.Globalization;

namespace DataDump.Core;

/// <summary>Game time as the game's GameTimeManager gives it.</summary>
public static class GameClock
{
    /// <summary>
    /// GameTimeManager.GetSecond (rva 0x25536f0) as hh:mm:ss. GetTotalSeconds (rva 0x2553780) is that plus
    /// 86400 per day, so it is a second of the day; anything outside one day is wrapped into it.
    /// </summary>
    public static string TimeOfDay(int secondOfDay)
    {
        var second = ((secondOfDay % 86400) + 86400) % 86400;

        return (second / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" +
               (second / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
               (second % 60).ToString("00", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// The game speed: GameTimeManager.GetSpeedLevel (rva 0x2553740; 1 to 3, or 4 while the slow gear is on) and the
/// BattleTimeScale it is set from.
/// </summary>
public sealed record GameSpeed(int Level, float Scale)
{
    public override string ToString() => "L" + Level.ToString(CultureInfo.InvariantCulture) + " x" + Scale.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>One speed when it held for the whole action, else where it started and where it ended.</summary>
    public static string Across(GameSpeed start, GameSpeed end) => start == end ? start.ToString() : start + " -> " + end;
}
