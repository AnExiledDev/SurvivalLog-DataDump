using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DataDump.Core;

/// <summary>
/// Writes any value as JSON through an <see cref="IValueReader"/>. Enums are written as names, lists as arrays,
/// dictionaries as objects with text keys, and nested objects with an id as that id. Nothing recurses past
/// <c>maxDepth</c>: a list or dictionary there is written as <see cref="DepthLimitText"/> and an object as its type
/// name. A value that cannot be read is written as an <c>!error</c> string, so one bad member costs only itself.
/// </summary>
public sealed class ValueWriter
{
    public const string DepthLimitText = "(depth limit)";
    public const string ErrorPrefix = "!error ";

    private readonly IValueReader _reader;
    private readonly int _maxDepth;
    private readonly bool _isIndented;
    private readonly bool _isNestedIdOnly;

    /// <param name="isIndented">One member per line, for files people read; false keeps a value on one line.</param>
    /// <param name="isNestedIdOnly">
    /// A nested object with an id is written as that id, which keeps a table row on one line. False writes it whole,
    /// for files whose nested objects are not rows of a table that is dumped anyway.
    /// </param>
    public ValueWriter(IValueReader reader, int maxDepth, bool isIndented = false, bool isNestedIdOnly = true)
    {
        _reader = reader;
        _maxDepth = maxDepth;
        _isIndented = isIndented;
        _isNestedIdOnly = isNestedIdOnly;
    }

    public string Write(object? value)
    {
        var text = new StringBuilder();

        Append(text, value, 0);

        return text.ToString();
    }

    public string WriteView(ValueView view)
    {
        var text = new StringBuilder();

        AppendView(text, view, 0);

        return text.ToString();
    }

    /// <summary>
    /// Writes <paramref name="view"/> into <paramref name="text"/> a part at a time, for a caller that spreads a large
    /// value over frames: a step ends after each member, item or pair shallower than <paramref name="sliceDepth"/>,
    /// and anything at that depth or deeper is written whole within its step. Once every step has run, the text is
    /// the same as <see cref="WriteView"/>'s.
    /// </summary>
    public IEnumerable<bool> WriteViewSteps(StringBuilder text, ValueView view, int sliceDepth) => ViewSteps(text, view, 0, sliceDepth);

    /// <summary>As <see cref="WriteViewSteps"/>, for any value; the finished text is the same as <see cref="Write"/>'s.</summary>
    public IEnumerable<bool> WriteSteps(StringBuilder text, object? value, int sliceDepth) => ValueSteps(text, value, 0, sliceDepth);

    /// <summary>The text an exception is written as, with reflection's wrapper taken off.</summary>
    public static string ErrorText(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: { } inner }) ex = inner;

        return ErrorPrefix + ex.GetType().Name + ": " + ex.Message;
    }

    private void Append(StringBuilder text, object? value, int depth)
    {
        ValueView view;

        try
        {
            view = _reader.Describe(value);
        }
        catch (Exception ex)
        {
            JsonText.AppendQuoted(text, ErrorText(ex));

            return;
        }

        AppendView(text, view, depth);
    }

    private void AppendView(StringBuilder text, ValueView view, int depth)
    {
        switch (view)
        {
            case LeafView leaf:
                AppendLeaf(text, leaf.Value);
                break;
            case EnumView named:
                JsonText.AppendQuoted(text, named.Name);
                break;
            case ItemsView items:
                AppendItems(text, items.Items, depth);
                break;
            case PairsView pairs:
                AppendPairs(text, pairs.Pairs, depth);
                break;
            case RecordView record:
                AppendRecord(text, record, depth);
                break;
            default:
                throw new InvalidOperationException("no JSON form for " + view.GetType().Name);
        }
    }

    private void AppendItems(StringBuilder text, IEnumerable<object?> items, int depth)
    {
        if (depth >= _maxDepth)
        {
            JsonText.AppendQuoted(text, DepthLimitText);

            return;
        }

        var count = 0;

        text.Append('[');

        try
        {
            foreach (var item in items)
            {
                AppendSeparator(text, count++, depth + 1);
                Append(text, item, depth + 1);
            }
        }
        catch (Exception ex)
        {
            AppendSeparator(text, count++, depth + 1);
            JsonText.AppendQuoted(text, ErrorText(ex));
        }

        Close(text, ']', count, depth);
    }

    private void AppendPairs(StringBuilder text, IEnumerable<KeyValuePair<object?, object?>> pairs, int depth)
    {
        if (depth >= _maxDepth)
        {
            JsonText.AppendQuoted(text, DepthLimitText);

            return;
        }

        var count = 0;

        text.Append('{');

        try
        {
            foreach (var pair in pairs)
            {
                AppendSeparator(text, count++, depth + 1);
                AppendKey(text, KeyText(pair.Key));
                Append(text, pair.Value, depth + 1);
            }
        }
        catch (Exception ex)
        {
            AppendSeparator(text, count++, depth + 1);
            AppendKey(text, "!error");
            JsonText.AppendQuoted(text, ErrorText(ex));
        }

        Close(text, '}', count, depth);
    }

    private void AppendRecord(StringBuilder text, RecordView record, int depth)
    {
        if (_isNestedIdOnly && depth > 0 && record.Id is not null)
        {
            AppendLeaf(text, record.Id);

            return;
        }

        if (depth >= _maxDepth)
        {
            JsonText.AppendQuoted(text, record.TypeName);

            return;
        }

        var count = 0;

        text.Append('{');

        foreach (var member in record.Members)
        {
            AppendSeparator(text, count++, depth + 1);
            AppendKey(text, member.Name);
            AppendMember(text, member, depth + 1);
        }

        Close(text, '}', count, depth);
    }

    private void AppendMember(StringBuilder text, MemberView member, int depth)
    {
        object? value;

        try
        {
            value = member.Read();
        }
        catch (Exception ex)
        {
            JsonText.AppendQuoted(text, ErrorText(ex));

            return;
        }

        Append(text, value, depth);
    }

    // The steps below mirror Append, AppendView, AppendItems, AppendPairs, AppendRecord and AppendMember one for one.
    // An iterator cannot yield inside a try that has a catch, so the enumeration a catch guards there is done here by
    // hand, through Begin, Next and End, and a failure ends the loop the way the catch does.

    private IEnumerable<bool> ValueSteps(StringBuilder text, object? value, int depth, int sliceDepth)
    {
        if (depth >= sliceDepth)
        {
            Append(text, value, depth);

            yield break;
        }

        if (DescribeOrWriteError(text, value) is not { } view) yield break;

        foreach (var step in ViewSteps(text, view, depth, sliceDepth)) yield return step;
    }

    private IEnumerable<bool> ViewSteps(StringBuilder text, ValueView view, int depth, int sliceDepth)
    {
        switch (view)
        {
            case ItemsView items:
                return ItemsSteps(text, items.Items, depth, sliceDepth);
            case PairsView pairs:
                return PairsSteps(text, pairs.Pairs, depth, sliceDepth);
            case RecordView record:
                return RecordSteps(text, record, depth, sliceDepth);
            default:
                AppendView(text, view, depth);

                return Array.Empty<bool>();
        }
    }

    private IEnumerable<bool> ItemsSteps(StringBuilder text, IEnumerable<object?> items, int depth, int sliceDepth)
    {
        if (depth >= _maxDepth)
        {
            JsonText.AppendQuoted(text, DepthLimitText);

            yield break;
        }

        var count = 0;

        text.Append('[');

        var failure = Begin(items, out var each);

        try
        {
            while (each is not null && failure is null && Next(each, out var item, out failure))
            {
                AppendSeparator(text, count++, depth + 1);

                using var child = ValueSteps(text, item, depth + 1, sliceDepth).GetEnumerator();

                while (Next(child, out _, out failure)) yield return true;

                if (failure is null) yield return true;
            }

            failure ??= End(each);
            each = null;
        }
        finally
        {
            End(each);
        }

        if (failure is not null)
        {
            AppendSeparator(text, count++, depth + 1);
            JsonText.AppendQuoted(text, ErrorText(failure));
        }

        Close(text, ']', count, depth);
    }

    private IEnumerable<bool> PairsSteps(StringBuilder text, IEnumerable<KeyValuePair<object?, object?>> pairs, int depth, int sliceDepth)
    {
        if (depth >= _maxDepth)
        {
            JsonText.AppendQuoted(text, DepthLimitText);

            yield break;
        }

        var count = 0;

        text.Append('{');

        var failure = Begin(pairs, out var each);

        try
        {
            while (each is not null && failure is null && Next(each, out var pair, out failure))
            {
                AppendSeparator(text, count++, depth + 1);

                using var child = KeyThenValueSteps(text, pair, depth + 1, sliceDepth).GetEnumerator();

                while (Next(child, out _, out failure)) yield return true;

                if (failure is null) yield return true;
            }

            failure ??= End(each);
            each = null;
        }
        finally
        {
            End(each);
        }

        if (failure is not null)
        {
            AppendSeparator(text, count++, depth + 1);
            AppendKey(text, "!error");
            JsonText.AppendQuoted(text, ErrorText(failure));
        }

        Close(text, '}', count, depth);
    }

    /// <summary>A pair's key and value, in one step sequence so a failure in either ends the pairs as AppendPairs' catch does.</summary>
    private IEnumerable<bool> KeyThenValueSteps(StringBuilder text, KeyValuePair<object?, object?> pair, int depth, int sliceDepth)
    {
        AppendKey(text, KeyText(pair.Key));

        foreach (var step in ValueSteps(text, pair.Value, depth, sliceDepth)) yield return step;
    }

    private IEnumerable<bool> RecordSteps(StringBuilder text, RecordView record, int depth, int sliceDepth)
    {
        if (_isNestedIdOnly && depth > 0 && record.Id is not null)
        {
            AppendLeaf(text, record.Id);

            yield break;
        }

        if (depth >= _maxDepth)
        {
            JsonText.AppendQuoted(text, record.TypeName);

            yield break;
        }

        var count = 0;

        text.Append('{');

        foreach (var member in record.Members)
        {
            AppendSeparator(text, count++, depth + 1);
            AppendKey(text, member.Name);

            foreach (var step in MemberSteps(text, member, depth + 1, sliceDepth)) yield return step;

            yield return true;
        }

        Close(text, '}', count, depth);
    }

    private IEnumerable<bool> MemberSteps(StringBuilder text, MemberView member, int depth, int sliceDepth)
    {
        object? value;

        try
        {
            value = member.Read();
        }
        catch (Exception ex)
        {
            JsonText.AppendQuoted(text, ErrorText(ex));

            return Array.Empty<bool>();
        }

        return ValueSteps(text, value, depth, sliceDepth);
    }

    private ValueView? DescribeOrWriteError(StringBuilder text, object? value)
    {
        try
        {
            return _reader.Describe(value);
        }
        catch (Exception ex)
        {
            JsonText.AppendQuoted(text, ErrorText(ex));

            return null;
        }
    }

    private static Exception? Begin<T>(IEnumerable<T> items, out IEnumerator<T>? each)
    {
        try
        {
            each = items.GetEnumerator();

            return null;
        }
        catch (Exception ex)
        {
            each = null;

            return ex;
        }
    }

    /// <returns>True with the next element; false at the end, or with <paramref name="failure"/> set when moving on threw.</returns>
    private static bool Next<T>(IEnumerator<T> each, out T current, out Exception? failure)
    {
        failure = null;

        try
        {
            if (each.MoveNext())
            {
                current = each.Current;

                return true;
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        current = default!;

        return false;
    }

    private static Exception? End<T>(IEnumerator<T>? each)
    {
        try
        {
            each?.Dispose();

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>A dictionary key as text: leaves as their invariant text, enums as names, objects as their id or type.</summary>
    private string KeyText(object? key)
    {
        ValueView view;

        try
        {
            view = _reader.Describe(key);
        }
        catch (Exception ex)
        {
            return ErrorText(ex);
        }

        return view switch
        {
            LeafView leaf => LeafKeyText(leaf.Value),
            EnumView named => named.Name,
            RecordView { Id: { } id } => LeafKeyText(id),
            RecordView record => record.TypeName,
            _ => "(collection)",
        };
    }

    private static string LeafKeyText(object? value) => value switch
    {
        null => "null",
        string text => text,
        bool flag => flag ? "true" : "false",
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static void AppendLeaf(StringBuilder text, object? value)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                break;
            case string words:
                JsonText.AppendQuoted(text, words);
                break;
            case char letter:
                JsonText.AppendQuoted(text, letter.ToString());
                break;
            case bool flag:
                text.Append(flag ? "true" : "false");
                break;
            case float number:
                text.Append(JsonText.Number(number));
                break;
            case double number:
                text.Append(JsonText.Number(number));
                break;
            case IFormattable number when IsPlainNumber(number):
                text.Append(number.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                JsonText.AppendQuoted(text, LeafKeyText(value));
                break;
        }
    }

    private static bool IsPlainNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or decimal;

    private void AppendKey(StringBuilder text, string key)
    {
        JsonText.AppendQuoted(text, key);
        text.Append(_isIndented ? ": " : ":");
    }

    private void AppendSeparator(StringBuilder text, int index, int depth)
    {
        if (index > 0) text.Append(',');
        if (_isIndented) text.Append('\n').Append(' ', depth * 2);
    }

    private void Close(StringBuilder text, char closing, int count, int depth)
    {
        if (_isIndented && count > 0) text.Append('\n').Append(' ', depth * 2);

        text.Append(closing);
    }
}
