using System;
using System.Collections.Generic;
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
/// Config_Map object means a new location, so each one is written once, to maps/&lt;name&gt;.json, together with the
/// NPC dialogue the world has loaded (BattleLogicWorld._NpcDialogueManager.dialogueConfigCache) to npc-dialogue/.
/// </summary>
internal sealed class MapWatcher
{
    public const string MapsFolder = "maps";
    public const string DialogueFolder = "npc-dialogue";

    private const int MaxDepth = 10;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _folder;
    private readonly Action<string> _log;
    private readonly ValueWriter _values = new(new Il2CppValueReader(), MaxDepth, isIndented: true, isNestedIdOnly: false);
    private IntPtr _lastMap;

    public MapWatcher(string folder, Action<string> log)
    {
        _folder = folder;
        _log = log;
    }

    /// <summary>The current location's map, or null while none is loaded.</summary>
    public static Config_Map? Current() => GameSingleton.Existing<ConfigManager>()?.customCache?.MapConfig;

    /// <summary>The ticker's check: writes a location the first frame its map is there.</summary>
    public void Check(float now)
    {
        var map = Current();

        if (map is null || map.Pointer == _lastMap) return;

        _lastMap = map.Pointer;

        var name = string.IsNullOrEmpty(map.Name) ? "unnamed" : map.Name;

        WriteMap(map, name);
        WriteDialogueSoftly();
    }

    private void WriteMap(Config_Map map, string name)
    {
        var path = Path.Combine(_folder, MapsFolder, FileNames.Safe(name) + ".json");
        var document = new RecordView("map", null, new List<MemberView>
        {
            new("scene", ActiveScene),
            new("writtenAt", () => DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")),
            new("config", () => map),
        });

        WriteFile(path, _values.WriteView(document));
        _log($"map data: {name} written to {path}");
    }

    /// <summary>Dialogue is a side file; a failure there is logged and the map data still counts as written.</summary>
    private void WriteDialogueSoftly()
    {
        try
        {
            WriteDialogue();
        }
        catch (Exception ex)
        {
            _log("npc dialogue not written: " + ex.Message);
        }
    }

    private void WriteDialogue()
    {
        var cache = GameSingleton.Existing<BattleLogicWorld>()?._NpcDialogueManager?.dialogueConfigCache;

        if (cache is null) return;

        var written = 0;

        foreach (var pair in cache)
        {
            if (string.IsNullOrEmpty(pair.Key)) continue;

            var config = pair.Value;
            var path = Path.Combine(_folder, DialogueFolder, FileNames.Safe(pair.Key) + ".json");

            WriteFile(path, _values.Write(config));
            written++;
        }

        if (written > 0) _log($"npc dialogue: {written} files in {Path.Combine(_folder, DialogueFolder)}");
    }

    private static object? ActiveScene() => SceneManager.GetActiveScene().name;

    /// <summary>Written beside the final name and moved there, so a half-written file never looks whole.</summary>
    private static void WriteFile(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        var partPath = path + ".part";

        File.WriteAllText(partPath, json + "\n", Utf8);
        File.Move(partPath, path, overwrite: true);
    }
}
