namespace DataDump.Core;

/// <summary>
/// Lets a check that runs every frame do its work at most once per interval: the first call is due, then the first
/// call at least <c>seconds</c> after the last due one. Times are seconds on any steady clock.
/// </summary>
public sealed class Every
{
    private readonly double _seconds;
    private double? _nextAt;

    public Every(double seconds) => _seconds = seconds;

    public bool IsDue(double now)
    {
        if (_nextAt is { } nextAt && now < nextAt) return false;

        _nextAt = now + _seconds;

        return true;
    }
}
