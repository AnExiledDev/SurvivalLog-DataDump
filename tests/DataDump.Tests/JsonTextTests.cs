using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class JsonTextTests
{
    private const char Backslash = '\\';

    [Fact]
    public void Quotes_and_backslashes_are_escaped()
    {
        Assert.Equal("\"say \\\"hi\\\" C:\\\\game\"", JsonText.Quote("say \"hi\" C:\\game"));
    }

    [Fact]
    public void Line_breaks_and_tabs_never_split_a_json_line()
    {
        Assert.Equal("\"one\\ntwo\\r\\tthree\"", JsonText.Quote("one\ntwo\r\tthree"));
    }

    [Fact]
    public void Other_control_characters_are_written_as_unicode_escapes()
    {
        var expected = "\"a" + Backslash + "u0001b\"";

        Assert.Equal(expected, JsonText.Quote("a" + ((char)1).ToString() + "b"));
    }

    // The game's text is Chinese; kept as it is, a dump line can be grepped for the word players see.
    [Fact]
    public void Chinese_text_is_kept_as_it_is()
    {
        var word = ((char)0x4E2D).ToString() + ((char)0x6587).ToString();

        Assert.Equal("\"" + word + "\"", JsonText.Quote(word));
    }

    [Fact]
    public void Numbers_round_trip_in_invariant_text()
    {
        Assert.Equal("0.1", JsonText.Number(0.1f));
        Assert.Equal("2.5", JsonText.Number(2.5));
        Assert.Equal("-3", JsonText.Number(-3.0));
    }

    [Fact]
    public void Numbers_with_no_json_form_are_written_as_strings()
    {
        Assert.Equal("\"NaN\"", JsonText.Number(double.NaN));
        Assert.Equal("\"Infinity\"", JsonText.Number(float.PositiveInfinity));
    }
}
