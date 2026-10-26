using System;
using System.Collections.Generic;
using System.Globalization;

namespace DataDump.Core;

/// <summary>
/// One item the game put into a bag (ItemManager.TryAddItemToBag): the game day, the map, whose bag (the player, a robot,
/// a survivor, or a piece of furniture by its config id), the item, how many, whether it fit, and which of the game's
/// item givers made the call (a reward set, a story drop on entering a map, furniture loot and so on).
/// </summary>
public sealed record ItemGain(DateTime At, int? Day, string Map, string Owner, int Item, int Count, bool IsAdded, string Context);

/// <summary>item-ledger.tsv: one line per item put into a bag, so the wiki can find where items with no listed source come from.</summary>
public static class ItemTsv
{
    public const string Header = "time\tday\tmap\towner\titem\tcount\tadded\tcontext";

    public static string Line(ItemGain gain) =>
        string.Join("\t",
            gain.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            gain.Day is { } day ? Number(day) : "",
            Cell(gain.Map),
            Cell(gain.Owner),
            Number(gain.Item),
            Number(gain.Count),
            gain.IsAdded ? "yes" : "no",
            Cell(gain.Context));

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A tab or a line break in a name would split the line, so they become spaces.</summary>
    private static string Cell(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}

/// <summary>
/// Which item givers are running right now, outermost first: a reward set that opens a package reads
/// "reward 71110 > package 5". Each giver's hook enters before the game's call and leaves after it. A giver that throws
/// never leaves, so the ledger clears this between frames, when no giver can be running.
/// </summary>
public sealed class GiverStack
{
    /// <summary>Past this, a giver that never left is the likelier story than real nesting; more names are not kept.</summary>
    public const int MaxDepth = 8;

    private readonly List<string> _names = new();
    private int _depth;

    public void Enter(string name)
    {
        if (_depth < MaxDepth) _names.Add(name);

        _depth++;
    }

    public void Leave()
    {
        if (_depth == 0) return;

        _depth--;

        if (_depth < _names.Count) _names.RemoveAt(_names.Count - 1);
    }

    public string Current => string.Join(" > ", _names);

    public void Clear()
    {
        _names.Clear();
        _depth = 0;
    }
}
