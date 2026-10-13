using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataDump.Core;
using GameCore.HotUpdate;
using UnityEngine;

namespace DataDump.Adapters;

/// <summary>
/// One file of a dump, written one item per step so the runner can stop at any step and go on next frame. Reads the
/// game only inside <see cref="Steps"/>, so every read is inside the runner's catch.
/// </summary>
internal abstract class DumpJob
{
    protected DumpJob(string name, string fileName, bool isTable)
    {
        Name = name;
        FileName = fileName;
        IsTable = isTable;
    }

    public string Name { get; }

    public string FileName { get; }

    public bool IsTable { get; }

    /// <summary>Items written so far: rows for a table, entries for any other file.</summary>
    public int Count { get; protected set; }

    /// <summary>Set when the game has nothing for this file; the file is then not written.</summary>
    public string? SkipReason { get; protected set; }

    public abstract IEnumerable<bool> Steps(TextWriter writer);
}

/// <summary>One config table as JSON lines, one row per step, in the game's own order.</summary>
internal sealed class TableJob : DumpJob
{
    private readonly Func<object?> _readTable;
    private readonly RowWriter _rows;

    public TableJob(string name, Func<object?> readTable, RowWriter rows) : base(name, FileNames.Safe(name) + ".jsonl", isTable: true)
    {
        _readTable = readTable;
        _rows = rows;
    }

    public override IEnumerable<bool> Steps(TextWriter writer)
    {
        var table = _readTable();

        if (table is null)
        {
            SkipReason = "the game has not filled this table";

            yield break;
        }

        var shape = FileShape.JsonLines;

        writer.Write(shape.Start);

        foreach (var pair in Il2CppCollections.Pairs(table))
        {
            writer.Write(shape.Before(Count));
            writer.Write(_rows.Write(pair.Key ?? "null", pair.Value));
            Count++;

            yield return true;
        }

        writer.Write(shape.Finish(Count));
    }
}

/// <summary>A language map as one JSON object sorted by key, so two dumps diff line by line.</summary>
internal sealed class LanguageJob : DumpJob
{
    private const int EntriesPerStep = 500;

    private readonly Func<object?> _readMap;

    public LanguageJob(string name, Func<object?> readMap) : base(name, name + ".json", isTable: false) => _readMap = readMap;

    public override IEnumerable<bool> Steps(TextWriter writer)
    {
        var map = _readMap();

        if (map is null)
        {
            SkipReason = "the game has not loaded this language";

            yield break;
        }

        var entries = new List<KeyValuePair<string, string?>>();

        foreach (var pair in Il2CppCollections.Pairs(map))
        {
            if (pair.Key is not string key) continue;

            entries.Add(new KeyValuePair<string, string?>(key, pair.Value as string));

            if (entries.Count % EntriesPerStep == 0) yield return true;
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));

        var shape = FileShape.JsonObject;

        writer.Write(shape.Start);

        foreach (var entry in entries)
        {
            writer.Write(shape.Before(Count));
            writer.Write(FileShape.Entry(entry.Key, entry.Value));
            Count++;

            if (Count % EntriesPerStep == 0) yield return true;
        }

        writer.Write(shape.Finish(Count));
    }
}

/// <summary>
/// The game's hotkeys: each action's default key (HotKeyBinding.Defaults, HotKeyBinding.cs:41) and the key it is bound
/// to now, from SettingManager.GetKeyBind (rva 0x2785e70), which reads the player's custom binds and falls back to the
/// default without changing anything.
/// </summary>
internal sealed class InputJob : DumpJob
{
    public InputJob() : base("input", "input.json", isTable: false)
    {
    }

    private sealed record Binding(string? DefaultKey, string? BoundKey, bool? IsMouse);

    public override IEnumerable<bool> Steps(TextWriter writer)
    {
        var defaults = HotKeyBinding.Defaults;
        var settings = GameSingleton.Existing<SettingManager>();
        var bindings = new SortedDictionary<string, Binding>(StringComparer.Ordinal);

        foreach (var action in Enum.GetValues(typeof(HotKeyAction)).Cast<HotKeyAction>())
        {
            string? defaultKey = defaults != null && defaults.TryGetValue(action, out var key) ? key.ToString() : null;
            KeyCode? bound = settings?.GetKeyBind(action);

            bindings[action.ToString()] = new Binding(defaultKey, bound?.ToString(), bound is { } code ? HotKeyBinding.IsMouseKey(code) : null);
        }

        writer.Write(new ValueWriter(new PlainValueReader(), 3, isIndented: true).Write(bindings));
        writer.Write('\n');
        Count = bindings.Count;

        yield return true;
    }
}
