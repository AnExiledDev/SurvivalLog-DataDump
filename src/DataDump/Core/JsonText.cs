using System;
using System.Globalization;
using System.Text;

namespace DataDump.Core;

/// <summary>JSON string and number text. Non-ASCII text is kept as it is, so the game's Chinese stays greppable.</summary>
public static class JsonText
{
    /// <summary>The value as a quoted JSON string.</summary>
    public static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2);

        AppendQuoted(text, value);

        return text.ToString();
    }

    public static void AppendQuoted(StringBuilder text, string value)
    {
        text.Append('"');

        foreach (var c in value) AppendEscaped(text, c);

        text.Append('"');
    }

    /// <summary>
    /// A number as JSON. NaN and the infinities have no JSON form, so they are written as strings rather than
    /// breaking the line for every reader.
    /// </summary>
    public static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return Quote(value.ToString(CultureInfo.InvariantCulture));

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string Number(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return Quote(value.ToString(CultureInfo.InvariantCulture));

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void AppendEscaped(StringBuilder text, char c)
    {
        switch (c)
        {
            case '"':
                text.Append("\\\"");
                break;
            case '\\':
                text.Append("\\\\");
                break;
            case '\n':
                text.Append("\\n");
                break;
            case '\r':
                text.Append("\\r");
                break;
            case '\t':
                text.Append("\\t");
                break;
            case '\b':
                text.Append("\\b");
                break;
            case '\f':
                text.Append("\\f");
                break;
            default:
                if (c < ' ') text.Append('\\').Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else text.Append(c);

                break;
        }
    }
}
