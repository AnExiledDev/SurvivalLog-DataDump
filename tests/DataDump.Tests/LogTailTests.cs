using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class LogTailTests
{
    [Fact]
    public void Keeps_the_last_lines_with_unix_line_ends()
    {
        Assert.Equal("b\nc\n", LogTail.LastLines("a\r\nb\r\nc\r\n", 2, isCutFromLongerFile: false));
    }

    // Reading from the middle of a file starts inside a line; that half line would read as a different message.
    [Fact]
    public void A_text_cut_from_a_longer_file_drops_its_partial_first_line()
    {
        Assert.Equal("full 1\nfull 2\n", LogTail.LastLines("tial line\nfull 1\nfull 2", LogTail.LineCount, isCutFromLongerFile: true));
    }

    [Fact]
    public void A_whole_file_keeps_its_first_line()
    {
        Assert.Equal("first\nsecond\n", LogTail.LastLines("first\nsecond", LogTail.LineCount, isCutFromLongerFile: false));
    }

    [Fact]
    public void Nothing_left_is_an_empty_tail()
    {
        Assert.Equal("", LogTail.LastLines("", LogTail.LineCount, isCutFromLongerFile: false));
        Assert.Equal("", LogTail.LastLines("partial", LogTail.LineCount, isCutFromLongerFile: true));
    }
}
