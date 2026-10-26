using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using DataDump.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using UnityEngine.SceneManagement;

namespace DataDump.Adapters;

/// <summary>
/// Writes each location's map data when the game loads it. The game keeps only the current scene's Config_Map, in
/// ConfigManager.customCache.MapConfig (CustomCache offset 0x50), filled by ConfigManager._InitMapSearcher; that is
/// every furniture spot, building slot, package spot and camera area of the scene with its world position. A new
/// Config_Map object means a new location, so each one is written once, to maps/&lt;phase&gt;/&lt;name&gt;.json, together
/// with the NPC dialogue the world has loaded (BattleLogicWorld._NpcDialogueManager.dialogueConfigCache) to
/// npc-dialogue/. The phase (pre-disaster or post-disaster) comes from the game clock, which may load a moment after
/// the map, so a new location waits for it a few seconds (WorldPhase.WaitSeconds) before it is written.
/// A location is read from the game a slice per frame (about 2 ms) and its files are written on the worker.
/// </summary>
internal sealed class MapWatcher
{
    public const string MapsFolder = "maps";
    public const string DialogueFolder = "npc-dialogue";

    private const int MaxDepth = 10;

    /// <summary>Each item of the map's lists (furniture spots, slots, areas) is its own step.</summary>
    private const int SliceDepth = 4;

    /// <summary>A new location is noticed within half a second; each look reads three game objects.</summary>
    private const double LookSeconds = 0.5;

    private static readonly long FrameBudgetTicks = Stopwatch.Frequency * 2 / 1000;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _folder;
    private readonly Action<string> _log;
    private readonly SerialWorker _worker;
    private readonly ValueWriter _values = new(new Il2CppValueReader(), MaxDepth, isIndented: true, isNestedIdOnly: false);
    private readonly Every _look = new(LookSeconds);

    // The maps compared by address are held, so the game cannot free one and reuse its address for the next location.
    private Config_Map? _lastMap;
    private Config_Map? _pendingMap;
    private float _pendingSince;
    private LocationWrite? _writing;

    public MapWatcher(string folder, Action<string> log, SerialWorker worker)
    {
        _folder = folder;
        _log = log;
        _worker = worker;
    }

    /// <summary>The current location's map, or null while none is loaded.</summary>
    public static Config_Map? Current() => GameSingleton.Existing<ConfigManager>()?.customCache?.MapConfig;

    /// <summary>
    /// GameTimeManager's WorldStateType and ChapterId (backing fields 0x64 and 0x58), or nulls while no world or clock
    /// is loaded. Reads fields only.
    /// </summary>
    public static (int? WorldState, int? Chapter) Phase()
    {
        var time = GameSingleton.Existing<BattleLogicWorld>()?._GameTimeManager;

        return time is null ? (null, null) : ((int)time._WorldStateType_k__BackingField, time._ChapterId_k__BackingField);
    }

    /// <summary>The ticker's check: goes on with a location being written, or looks for a new one twice a second.</summary>
    public void Check(float now)
    {
        if (_writing is not null)
        {
            Continue(_writing);

            return;
        }

        if (!_look.IsDue(now)) return;

        var map = Current();

        if (map is null || map.Pointer == _lastMap?.Pointer) return;

        if (map.Pointer != _pendingMap?.Pointer)
        {
            _pendingMap = map;
            _pendingSince = now;
        }

        var (worldState, chapter) = Phase();

        if (!WorldPhase.IsReady(worldState, _pendingSince, now)) return;

        _lastMap = map;
        _pendingMap = null;
        _writing = StartLocation(map, worldState, chapter);
        Continue(_writing);
    }

    private LocationWrite StartLocation(Config_Map map, int? worldState, int? chapter)
    {
        var name = string.IsNullOrEmpty(map.Name) ? "unnamed" : map.Name;
        var phase = WorldPhase.FolderOf(worldState);
        var path = Path.Combine(_folder, MapsFolder, phase, FileNames.Safe(name) + ".json");
        var document = new RecordView("map", null, new List<MemberView>
        {
            new("scene", ActiveScene),
            new("phase", () => phase),
            new("worldState", () => worldState),
            new("chapterId", () => chapter),
            new("writtenAt", () => DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")),
            new("config", () => map),
        });
        var text = new StringBuilder();

        return new LocationWrite(name, phase, path, text, _values.WriteViewSteps(text, document, SliceDepth).GetEnumerator());
    }

    /// <summary>Reads for one frame's budget: the map's steps, then the dialogue, one file at a time.</summary>
    private void Continue(LocationWrite location)
    {
        var stopAt = Stopwatch.GetTimestamp() + FrameBudgetTicks;

        while (location.MapSteps is not null && Stopwatch.GetTimestamp() < stopAt)
        {
            if (!StepMap(location)) FinishMap(location);
        }

        while (location.MapSteps is null && Stopwatch.GetTimestamp() < stopAt)
        {
            if (!StepDialogue(location))
            {
                _writing = null;

                return;
            }
        }
    }

    /// <returns>False once the map's text is whole.</returns>
    private bool StepMap(LocationWrite location)
    {
        try
        {
            return location.MapSteps?.MoveNext() == true;
        }
        catch (Exception)
        {
            // As before the slicing, a map that cannot be read is the ticker's to report once; the location is not retried.
            _writing = null;
            location.MapSteps?.Dispose();

            throw;
        }
    }

    private void FinishMap(LocationWrite location)
    {
        location.MapSteps?.Dispose();
        location.MapSteps = null;
        PostMap(location.Path, location.Text, $"map data: {location.Name} ({location.Phase}) written to {location.Path}");
    }

    private void PostMap(string path, StringBuilder text, string written) =>
        _worker.Post(() =>
        {
            WriteFile(path, text.Append('\n').ToString());
            _log(written);
        });

    /// <returns>False once every dialogue file is handed to the worker, or the dialogue failed.</returns>
    private bool StepDialogue(LocationWrite location)
    {
        try
        {
            location.Dialogue ??= DialoguePairs();

            if (location.DialogueNext >= location.Dialogue.Count)
            {
                PostDialogueCount(location.DialogueWritten);

                return false;
            }

            var (key, config) = location.Dialogue[location.DialogueNext++];

            PostDialogue(Path.Combine(_folder, DialogueFolder, FileNames.Safe(key) + ".json"), _values.Write(config), location.DialogueWritten);

            return true;
        }
        catch (Exception ex)
        {
            // Dialogue is a side file; a failure there is logged and the map data still counts as written.
            _log("npc dialogue not written: " + ex.Message);

            return false;
        }
    }

    /// <summary>
    /// The loaded dialogue, taken whole in one step: the game may change its dictionary between frames, and walking it
    /// across them would then fail.
    /// </summary>
    private static List<(string Key, object? Config)> DialoguePairs()
    {
        var pairs = new List<(string, object?)>();
        var cache = GameSingleton.Existing<BattleLogicWorld>()?._NpcDialogueManager?.dialogueConfigCache;

        if (cache is null) return pairs;

        foreach (var pair in cache)
        {
            if (!string.IsNullOrEmpty(pair.Key)) pairs.Add((pair.Key, pair.Value));
        }

        return pairs;
    }

    private void PostDialogue(string path, string json, DialogueCount written) =>
        _worker.Post(() =>
        {
            try
            {
                WriteFile(path, json + "\n");
                written.Files++;
            }
            catch (Exception ex)
            {
                _log("npc dialogue not written: " + ex.Message);
            }
        });

    private void PostDialogueCount(DialogueCount written) =>
        _worker.Post(() =>
        {
            if (written.Files > 0) _log($"npc dialogue: {written.Files} files in {Path.Combine(_folder, DialogueFolder)}");
        });

    private static object? ActiveScene() => SceneManager.GetActiveScene().name;

    /// <summary>On the worker. Written beside the final name and moved there, so a half-written file never looks whole.</summary>
    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        var partPath = path + ".part";

        File.WriteAllText(partPath, text, Utf8);
        File.Move(partPath, path, overwrite: true);
    }

    /// <summary>Counted on the worker only, after each dialogue file is really written.</summary>
    private sealed class DialogueCount
    {
        public int Files;
    }

    private sealed class LocationWrite
    {
        public LocationWrite(string name, string phase, string path, StringBuilder text, IEnumerator<bool> mapSteps)
        {
            Name = name;
            Phase = phase;
            Path = path;
            Text = text;
            MapSteps = mapSteps;
        }

        public string Name { get; }

        public string Phase { get; }

        public string Path { get; }

        /// <summary>The map's JSON as its steps write it; the worker's once the steps are done.</summary>
        public StringBuilder Text { get; }

        /// <summary>Null once the map's text is whole.</summary>
        public IEnumerator<bool>? MapSteps { get; set; }

        public List<(string Key, object? Config)>? Dialogue { get; set; }

        public int DialogueNext { get; set; }

        public DialogueCount DialogueWritten { get; } = new();
    }
}
