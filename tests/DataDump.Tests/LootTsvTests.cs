using System.Globalization;
using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class LootTsvTests
{
    private static readonly DateTime Noon = new(2026, 10, 19, 12, 30, 5);

    [Fact]
    public void A_roll_is_one_line_under_the_header_columns()
    {
        var line = LootTsv.Line(new LootRoll(Noon, "School", 3001, 2, 1, 0, new[] { 101, 102, 102 }));

        Assert.Equal("2026-10-19 12:30:05\tSchool\t3001\t2\t1\t0\t101,102,102", line);
        Assert.Equal(LootTsv.Header.Split('\t').Length, line.Split('\t').Length);
    }

    // A tab or line break in a map name would shift every column after it or split the row.
    [Fact]
    public void Tabs_and_line_breaks_in_the_map_name_become_spaces()
    {
        var line = LootTsv.Line(new LootRoll(Noon, "Office\tB1\r\n2F", 1, 1, 0, 0, Array.Empty<int>()));

        Assert.Equal("2026-10-19 12:30:05\tOffice B1  2F\t1\t1\t0\t0\t", line);
    }

    [Fact]
    public void Numbers_ignore_the_players_regional_format()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            Assert.StartsWith("2026-10-19 12:30:05\tX\t-5\t", LootTsv.Line(new LootRoll(Noon, "X", -5, 1, 0, 0, new[] { 1 })));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
