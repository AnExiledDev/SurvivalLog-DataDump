using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class LanguageFileTests
{
    private static (string Text, int Count) Write(params (string Key, string? Value)[] entries)
    {
        var writer = new StringWriter();
        var count = LanguageFile.Write(writer, entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)).ToList());

        return (writer.ToString(), count);
    }

    // The wiki reads these files, so the text must stay exactly what the dump wrote before it moved to the worker.
    [Fact]
    public void Writes_one_entry_per_line_sorted_by_key()
    {
        var (text, count) = Write(("b", "Bee"), ("a", "Ay \"quoted\""), ("c", null));

        Assert.Equal("{\n  \"a\": \"Ay \\\"quoted\\\"\",\n  \"b\": \"Bee\",\n  \"c\": null\n}\n", text);
        Assert.Equal(3, count);
    }

    // Two dumps diff line by line only if the order never depends on the culture: ordinal, so capitals come first.
    [Fact]
    public void The_order_is_ordinal()
    {
        var (text, _) = Write(("b", "1"), ("B", "2"), ("a", "3"));

        Assert.Equal("{\n  \"B\": \"2\",\n  \"a\": \"3\",\n  \"b\": \"1\"\n}\n", text);
    }

    [Fact]
    public void An_empty_language_is_an_empty_object()
    {
        var (text, count) = Write();

        Assert.Equal("{\n}\n", text);
        Assert.Equal(0, count);
    }
}
