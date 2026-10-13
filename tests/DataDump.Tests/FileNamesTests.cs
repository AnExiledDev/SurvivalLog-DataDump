using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class FileNamesTests
{
    [Fact]
    public void A_plain_table_name_is_kept()
    {
        Assert.Equal("Config_Action.jsonl", FileNames.Safe("Config_Action.jsonl"));
    }

    // Page keys and table names come from the game; none of them may write outside the dump folder.
    [Theory]
    [InlineData("../evil", "_._evil")]
    [InlineData("ui/root.html", "ui_root.html")]
    [InlineData("C:\\x", "C__x")]
    [InlineData("..", "_.")]
    [InlineData("", "_")]
    public void Path_characters_and_leading_dots_become_underscores(string name, string safe)
    {
        Assert.Equal(safe, FileNames.Safe(name));
    }

    [Fact]
    public void Non_ascii_names_become_underscores()
    {
        Assert.Equal("page__", FileNames.Safe("page" + ((char)0x4E2D).ToString() + ((char)0x6587).ToString()));
    }

    [Fact]
    public void Long_names_are_cut_to_eighty_characters()
    {
        Assert.Equal(new string('a', 80), FileNames.Safe(new string('a', 100)));
    }
}
