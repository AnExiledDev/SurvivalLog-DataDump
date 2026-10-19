using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DataDump.Core;

/// <summary>
/// One roll of a loot group (ShopRandomTools.GetUniqueRandomItems): the group, how many picks were asked for, the
/// discovery level and guarantee level it rolled at, and the item ids it gave, one id per unit.
/// </summary>
public sealed record LootRoll(DateTime At, string Map, int GroupId, int Count, int DiscoverLevel, int GuaranteeLevel, IReadOnlyList<int> Items);

/// <summary>loot-ledger.tsv: one line per roll, so the odds worked out from the tables can be checked against real rolls.</summary>
public static class LootTsv
{
    public const string Header = "time\tmap\tgroup\tcount\tdiscover_lv\tguarantee_lv\titems";

    public static string Line(LootRoll roll) =>
        string.Join("\t",
            roll.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Cell(roll.Map),
            Number(roll.GroupId),
            Number(roll.Count),
            Number(roll.DiscoverLevel),
            Number(roll.GuaranteeLevel),
            string.Join(",", roll.Items.Select(Number)));

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A tab or a line break in a name would split the line, so they become spaces.</summary>
    private static string Cell(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
