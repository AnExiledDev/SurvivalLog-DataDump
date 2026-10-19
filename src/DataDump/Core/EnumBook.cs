using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DataDump.Core;

/// <summary>One enum of the game: its full type name and each value with its name.</summary>
public sealed record EnumType(string Name, IReadOnlyList<KeyValuePair<long, string>> Values);

/// <summary>
/// enums.json: every enum of the game as <c>{ "Full.Type.Name": { "value": "Name" } }</c>, types sorted by name and values
/// by number, so two dumps diff line by line. Config fields hold plain numbers; this is how a number gets its name.
/// </summary>
public static class EnumBook
{
    /// <summary>Names that share one value are joined with this, so no name is lost.</summary>
    public const string SharedValueSeparator = "|";

    public static string ToJson(IEnumerable<EnumType> types)
    {
        var text = new StringBuilder("{");
        var typeCount = 0;

        foreach (var type in types.OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            text.Append(typeCount++ == 0 ? "\n  " : ",\n  ");
            JsonText.AppendQuoted(text, type.Name);
            text.Append(": {");
            AppendValues(text, type.Values);
            text.Append('}');
        }

        text.Append(typeCount == 0 ? "}\n" : "\n}\n");

        return text.ToString();
    }

    private static void AppendValues(StringBuilder text, IReadOnlyList<KeyValuePair<long, string>> values)
    {
        var byValue = values.GroupBy(pair => pair.Key).OrderBy(group => group.Key).ToList();

        for (var i = 0; i < byValue.Count; i++)
        {
            text.Append(i == 0 ? "\n    " : ",\n    ");
            JsonText.AppendQuoted(text, byValue[i].Key.ToString(CultureInfo.InvariantCulture));
            text.Append(": ");
            JsonText.AppendQuoted(text, string.Join(SharedValueSeparator, byValue[i].Select(pair => pair.Value)));
        }

        if (byValue.Count > 0) text.Append("\n  ");
    }
}
