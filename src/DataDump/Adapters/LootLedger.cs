using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DataDump.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using HarmonyLib;

namespace DataDump.Adapters;

/// <summary>
/// Writes loot-ledger.tsv: one line for every roll of a loot group. ShopRandomTools.GetUniqueRandomItems(group, count,
/// discoverLv, guaranteeLv) (rva 0x27fef50) is the one roll behind furniture loot, reward sets, stranger trades, stray
/// zombies and tower waves; its two-argument overload (rva 0x27ff720) only calls it. Its first 14 bytes are three movs
/// of the arguments to their home slots (GameAssembly.dll.asm), no relative jump or call. The postfix only copies the
/// result into memory; the ticker writes the lines every few seconds, so the game's call never waits on the disk.
/// </summary>
internal static class LootLedger
{
    public const string FileName = "loot-ledger.tsv";

    private const double FlushSeconds = 5;
    private const int MaxWaiting = 10_000;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Gate = new();
    private static List<LootRoll> _waiting = new();
    private static Action<string> _log = _ => { };
    private static SerialWorker? _worker;
    private static string _path = "";
    private static double _lastFlush;
    private static int _dropped;
    private static bool _isOn;

    /// <returns>Whether the hook is in place; only then does <see cref="Tick"/> have anything to do.</returns>
    public static bool Apply(Harmony harmony, string folder, Action<string> log, SerialWorker worker)
    {
        _log = log;
        _worker = worker;
        _path = Path.Combine(folder, FileName);

        try
        {
            Directory.CreateDirectory(folder);

            var arguments = new[] { typeof(Config_RandomGroup), typeof(int), typeof(int), typeof(int) };

            PatchKit.Patch(harmony, typeof(LootLedger), typeof(ShopRandomTools), nameof(ShopRandomTools.GetUniqueRandomItems), arguments, postfix: nameof(AfterRoll));
            _isOn = true;
            log("loot ledger: writing to " + _path);
        }
        catch (Exception ex)
        {
            log("could not hook the loot roll, the loot ledger is off: " + ex.Message);
        }

        return _isOn;
    }

    /// <summary>Every few seconds, hands the rolls that came in to the worker, which builds their lines and appends them.</summary>
    public static void Tick(float now)
    {
        if (now - _lastFlush < FlushSeconds || _worker is not { } worker) return;

        _lastFlush = now;

        List<LootRoll> rolls;
        int dropped;

        lock (Gate)
        {
            if (_waiting.Count == 0 && _dropped == 0) return;

            rolls = _waiting;
            dropped = _dropped;
            _waiting = new List<LootRoll>();
            _dropped = 0;
        }

        if (dropped > 0) _log($"loot ledger: {dropped} rolls dropped, too many waiting to be written");

        if (rolls.Count > 0) PostWrite(worker, rolls);
    }

    /// <summary>Its own method, so the closure over the rolls is made only when there are some.</summary>
    private static void PostWrite(SerialWorker worker, List<LootRoll> rolls) => worker.Post(() => Write(rolls));

    /// <summary>On the worker.</summary>
    private static void Write(List<LootRoll> rolls)
    {
        var text = new StringBuilder();

        if (!File.Exists(_path)) text.Append(LootTsv.Header).Append('\n');

        foreach (var roll in rolls) text.Append(LootTsv.Line(roll)).Append('\n');

        File.AppendAllText(_path, text.ToString(), Utf8);
    }

    private static void AfterRoll(Config_RandomGroup randomGroup, int count, int discoverLv, int guaranteeLv, Il2CppSystem.Collections.Generic.List<int> __result)
    {
        if (!_isOn || randomGroup == null) return;

        try
        {
            var items = new List<int>(__result?.Count ?? 0);

            // By index: an Il2Cpp foreach wraps a game enumerator per roll.
            for (var i = 0; __result != null && i < __result.Count; i++) items.Add(__result[i]);

            // The line is built on the worker at the next write, not inside the game's call.
            Add(new LootRoll(DateTime.Now, MapName(), randomGroup._ID_k__BackingField, count, discoverLv, guaranteeLv, items));
        }
        catch (Exception ex)
        {
            PatchKit.Fail("reading a loot roll", ex);
        }
    }

    private static string MapName()
    {
        var map = MapWatcher.Current();

        return map?.Name ?? "";
    }

    private static void Add(LootRoll roll)
    {
        lock (Gate)
        {
            if (_waiting.Count >= MaxWaiting) _dropped++;
            else _waiting.Add(roll);
        }
    }
}
