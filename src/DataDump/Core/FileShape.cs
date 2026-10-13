namespace DataDump.Core;

/// <summary>
/// How a dump file joins the items it is written in, so a file can be written one item at a time across frames and
/// still come out as valid JSON.
/// </summary>
public sealed record FileShape(string Start, string BetweenItems, string AfterLastItem, string End)
{
    /// <summary>One JSON value per line.</summary>
    public static readonly FileShape JsonLines = new("", "\n", "\n", "");

    /// <summary>One JSON object, one member per line; each item is a <see cref="Entry"/>.</summary>
    public static readonly FileShape JsonObject = new("{\n", ",\n", "\n", "}\n");

    public string Before(int index) => index == 0 ? "" : BetweenItems;

    public string Finish(int count) => (count > 0 ? AfterLastItem : "") + End;

    /// <summary>One member of a <see cref="JsonObject"/> file.</summary>
    public static string Entry(string key, string? value) =>
        "  " + JsonText.Quote(key) + ": " + (value is null ? "null" : JsonText.Quote(value));
}
