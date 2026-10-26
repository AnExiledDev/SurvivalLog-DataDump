using System;

namespace DataDump.Core;

/// <summary>
/// Keeps the last answer worked out and gives it back while the key is the same, so a check that looks every second
/// does its work only when what it reads has changed. The key holds every input the answer is worked out from.
/// </summary>
public sealed class LastResult<TKey, TValue> where TKey : IEquatable<TKey>
{
    private readonly Func<TKey, TValue> _work;
    private bool _hasResult;
    private TKey _key = default!;
    private TValue _value = default!;

    /// <param name="work">Works out the answer for a key; keep it in a field, so no delegate is made per call.</param>
    public LastResult(Func<TKey, TValue> work) => _work = work;

    public TValue Get(TKey key)
    {
        if (_hasResult && _key.Equals(key)) return _value;

        _value = _work(key);
        _key = key;
        _hasResult = true;

        return _value;
    }

    /// <summary>Forgets the answer and its key, so nothing the key holds is kept alive.</summary>
    public void Clear()
    {
        _hasResult = false;
        _key = default!;
        _value = default!;
    }
}
