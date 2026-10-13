namespace DataDump.Core;

/// <summary>
/// Says once when the game's data has finished loading: the load has to read as finished for a settle time without a
/// break, so a dump never starts on tables the loader is still filling. Times are seconds on any steady clock.
/// </summary>
public sealed class LoadGate
{
    private readonly double _settleSeconds;
    private double? _readySince;
    private bool _hasOpened;

    public LoadGate(double settleSeconds) => _settleSeconds = settleSeconds;

    /// <returns>True exactly once: the first poll after the load has read as finished for the settle time.</returns>
    public bool Poll(bool isLoaded, double now)
    {
        if (_hasOpened) return false;

        if (!isLoaded)
        {
            _readySince = null;

            return false;
        }

        _readySince ??= now;

        if (now - _readySince.Value < _settleSeconds) return false;

        _hasOpened = true;

        return true;
    }
}
