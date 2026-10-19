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
