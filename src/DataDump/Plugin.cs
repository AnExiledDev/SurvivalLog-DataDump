using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using DataDump.Adapters;
using HarmonyLib;
using UnityEngine;

namespace DataDump;

[BepInPlugin(Guid, "Data Dump", Version)]
[BepInDependency(OptionalLive.ToolkitGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    private const string Guid = "com.dev.survivallog.datadump";
    private const string Version = "0.3.6";

    /// <summary>How long quitting waits for the files still being written.</summary>
    private static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(3);

    public override void Load()
    {
        if (!OptionalLive.IsToolkitInstalled) Log.LogInfo(OptionalLive.MissingNote);

        var isEnabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
            "Write the game's data tables, its text in both languages and its key bindings to BepInEx/data-dump once the game has loaded. Off: this tool does nothing at all. Default: on. Takes effect at the next game start.\n" +
            "游戏载入完成后，把游戏的数据表、中英文文本和按键设置写入 BepInEx/data-dump 文件夹。关闭后，这个工具什么都不做。默认开启。下次启动游戏时生效。", null, "ModManager.Restart")).Value;
        var dumpKey = Config.Bind("General", "DumpKey", KeyCode.F5,
            new ConfigDescription("Hold Ctrl and press this key to write the data tables and the mod health again. Default: F5 (Ctrl+F5).\n" +
            "按住 Ctrl 再按这个键，重新写入数据表和模组状态。默认 F5，即 Ctrl+F5。", null, "ModManager.ShowWhen=Enabled"));
        var snapshotKey = Config.Bind("Snapshot", "Key", KeyCode.F10,
            "Hold Ctrl and press this key to save a bug snapshot to BepInEx/data-dump/snapshots: a screenshot, the game state, the open pages, the logs and the mod health. Default: F10 (Ctrl+F10).\n" +
            "按住 Ctrl 再按这个键，把一份问题快照保存到 BepInEx/data-dump/snapshots 文件夹：截图、游戏状态、打开的页面、日志和模组状态。默认 F10，即 Ctrl+F10。");
        var isLedgerOn = Config.Bind("Ledger", "Enabled", true, new ConfigDescription(
            "Write one line to BepInEx/data-dump/action-ledger.tsv for every action the player, a survivor or a robot finishes or breaks off, with how long it took and what changed on them meanwhile. Default: on. Takes effect at the next game start.\n" +
            "玩家、幸存者或机器人每完成或中断一个动作，就在 BepInEx/data-dump/action-ledger.tsv 里写一行，记下用时和期间身上的变化。默认开启。下次启动游戏时生效。", null, "ModManager.Restart")).Value;

        var areMapsOn = Config.Bind("Maps", "Enabled", true, new ConfigDescription(
            "Write each location's map data (every furniture spot, building slot and camera area, with its position) to BepInEx/data-dump/maps when the game loads it, and the NPC dialogue loaded there to BepInEx/data-dump/npc-dialogue. Also needed for the map pictures. Default: on. Takes effect at the next game start.\n" +
            "游戏载入每个地点时，把该地点的地图数据（每个家具位、建筑位和镜头区域及其位置）写入 BepInEx/data-dump/maps 文件夹，并把那里载入的 NPC 对话写入 BepInEx/data-dump/npc-dialogue 文件夹。地图图片也需要它。默认开启。下次启动游戏时生效。", null, "ModManager.Restart")).Value;
        var isAutoPictures = Config.Bind("Maps", "AutoPictures", true, new ConfigDescription(
            "Take top-down pictures of each place the game shows (each home floor, each store) once per game session, after the view has stayed on it for 10 seconds, to BepInEx/data-dump/maps/<location>. The game only draws the place you are at and the floor you are viewing, so visit each place and switch to each floor to get them all. A black picture is not saved and is tried again a little later. The game may stop for a moment while they are taken. Default: on. Takes effect at the next game start.\n" +
            "每次启动游戏后，游戏显示的每个地方（家里的每一层、每家商店）在镜头停留 10 秒后自动拍一次俯视图片，存到 BepInEx/data-dump/maps/<地点> 文件夹。游戏只绘制你所在的地方和正在查看的楼层，所以要拍全，请走到每个地方并切换到每一层。全黑的图片不会保存，稍后会再试。拍摄时游戏可能会停顿片刻。默认开启。下次启动游戏时生效。", null, "ModManager.Restart")).Value;
        var pictureKey = Config.Bind("Maps", "PictureKey", KeyCode.F4,
            "Hold Ctrl and press this key to take top-down pictures of the place and floor on view now, replacing its earlier ones. Default: F4 (Ctrl+F4).\n" +
            "按住 Ctrl 再按这个键，立即给当前显示的地方和楼层拍俯视图片，替换之前的图片。默认 F4，即 Ctrl+F4。");
        var pixelsPerMeter = Config.Bind("Maps", "PixelsPerMeter", 120f, new ConfigDescription(
            "How many pixels a meter of the game world gets in the map pictures. A large location is made smaller so its picture is at most 8192 pixels wide. Higher values make sharper pictures and a longer stop while they are taken. Default: 120. Takes effect at the next game start.\n" +
            "地图图片里游戏世界每米占多少像素。较大的地点会缩小，使图片最宽 8192 像素。数值越高，图片越清晰，拍摄时的停顿也越长。默认 120。下次启动游戏时生效。", new AcceptableValueRange<float>(2f, 200f), "ModManager.Restart")).Value;
        var isDaylight = Config.Bind("Maps", "Daylight", true, new ConfigDescription(
            "Switch the game's light to midday while the map pictures are taken, then back, so pictures taken at night are not dark. If the game does not allow it, the pictures use the light there is. Default: on. Takes effect at the next game start.\n" +
            "拍地图图片时把游戏光照切换到正午，拍完再切回，这样夜里拍的图片也不会很暗。如果游戏不允许切换，就用当时的光照拍。默认开启。下次启动游戏时生效。", null, "ModManager.Restart")).Value;
        var cutHeight = Config.Bind("Maps", "CutHeight", 2.2f, new ConfigDescription(
            "For the plan pictures: how many meters above the floor the view starts, so ceilings and upper floors are cut away and the rooms show. The top pictures show roofs and all. Default: 2.2. Takes effect at the next game start.\n" +
            "平面图片的视角从地面以上多少米开始，这样天花板和楼上会被切掉，能看到房间。俯视图片则包含屋顶。默认 2.2。下次启动游戏时生效。", new AcceptableValueRange<float>(0.5f, 10f), "ModManager.Restart")).Value;
        var isLootLedgerOn = Config.Bind("Loot", "Enabled", true, new ConfigDescription(
            "Write one line to BepInEx/data-dump/loot-ledger.tsv for every roll of a loot group: the group, the location, the discovery level and the items it gave. Default: on. Takes effect at the next game start.\n" +
            "每次随机掉落组抽取物品时，在 BepInEx/data-dump/loot-ledger.tsv 里写一行：掉落组、地点、探索等级和得到的物品。默认开启。下次启动游戏时生效。", null, "ModManager.Restart")).Value;
        var isItemLedgerOn = Config.Bind("Items", "Enabled", true, new ConfigDescription(
            "Write one line to BepInEx/data-dump/item-ledger.tsv for every item the game puts into a bag (yours, a container's, a robot's): the day, the location, whose bag, the item, how many, and what gave it, such as a reward, a story drop or container loot. Default: on. Takes effect at the next game start.\n" +
            "游戏每把一件物品放进背包（你的、容器的、机器人的）时，在 BepInEx/data-dump/item-ledger.tsv 里写一行：天数、地点、谁的背包、物品、数量，以及来源，例如奖励、剧情掉落或容器战利品。默认开启。下次启动游戏时生效。", null, "ModManager.Restart")).Value;
        var isBundleExportOn = Config.Bind("Bundles", "Export", false, new ConfigDescription(
            "At game start, write the game's asset bundles decrypted to BepInEx/data-dump/bundles, for asset tools. All of them take about 4 GB; bundles already written are skipped. Default: off. Takes effect at the next game start.\n" +
            "游戏启动时，把游戏的资源包解密后写入 BepInEx/data-dump/bundles 文件夹，供资源工具使用。全部约 4 GB；已写入的资源包会跳过。默认关闭。下次启动游戏时生效。", null, "ModManager.Restart")).Value;
        var bundleFilter = Config.Bind("Bundles", "Filter", "", new ConfigDescription(
            "Only export bundles whose name contains one of these words, separated by commas (for example: texture_item, battlescene). Empty: every bundle. Takes effect at the next game start.\n" +
            "只导出名称包含这些词之一的资源包，用英文逗号分隔（例如：texture_item, battlescene）。留空：全部资源包。下次启动游戏时生效。", null, "ModManager.Restart", "ModManager.ShowWhen=Export")).Value;

        var folder = Path.Combine(Paths.BepInExRootPath, "data-dump");
        var log = new DumpLog(Log, Path.Combine(Paths.BepInExRootPath, "data-dump.log"));

        log.TrimIfLarge();
        log.Write($"===== Data Dump {Version} started, enabled {isEnabled}, dump key Ctrl+{dumpKey.Value}, snapshot key Ctrl+{snapshotKey.Value}, action ledger {isLedgerOn}, " +
                  $"maps {areMapsOn} (auto pictures {isAutoPictures}, daylight {isDaylight}, picture key Ctrl+{pictureKey.Value}), loot ledger {isLootLedgerOn}, item ledger {isItemLedgerOn}, bundle export {isBundleExportOn}");

        if (!isEnabled) return;

        var worker = log.Worker;

        PatchKit.Log = log.Write;
        DumpTicker.Log = log.Write;
        DumpTicker.OnQuit = () => worker.Finish(QuitWait);

        // First, so the lines other threads left and this frame's key state are there for every check after them.
        DumpTicker.Checks.Add(log.Drain);
        DumpTicker.Checks.Add(CtrlHotkey.ReadFrame);
        TableDumper.WarmUp(worker);

        var harmony = new Harmony(Guid);

        if (isLedgerOn && ActionLedger.Apply(harmony, folder, log.Write, worker)) DumpTicker.Checks.Add(ActionLedger.Tick);
        if (isLootLedgerOn && LootLedger.Apply(harmony, folder, log.Write, worker)) DumpTicker.Checks.Add(LootLedger.Tick);
        if (isItemLedgerOn && ItemLedger.Apply(harmony, folder, log.Write, worker)) DumpTicker.Checks.Add(ItemLedger.Tick);
        if (isBundleExportOn) BundleExport.Start(Paths.GameDataPath, folder, bundleFilter, log.Write);
        if (areMapsOn) WatchMaps(folder, isAutoPictures, isDaylight, pictureKey, pixelsPerMeter, cutHeight, log);

        var health = new ModHealthProbe(log.Write, worker);
        var dumpHotkey = new CtrlHotkey(dumpKey.Value, log.Write);
        var snapshotHotkey = new CtrlHotkey(snapshotKey.Value, log.Write);
        var session = new DumpSession(
            new TableDumper(folder, $"Data Dump {Version}", log.Write, worker),
            health,
            new SnapshotTaker(Path.Combine(folder, "snapshots"), health, folder, log.Write, worker),
            dumpHotkey,
            snapshotHotkey,
            folder);

        WatchKey(dumpKey, dumpHotkey, "dump key", log);
        WatchKey(snapshotKey, snapshotHotkey, "snapshot key", log);

        DumpTicker.Checks.Add(session.WatchLoad);
        DumpTicker.Checks.Add(session.TickDump);
        DumpTicker.Checks.Add(session.CheckDumpKey);
        DumpTicker.Checks.Add(session.CheckHealthDue);
        DumpTicker.Checks.Add(session.CheckSnapshotKey);
        DumpTicker.Checks.Add(session.TickSnapshot);
        AddComponent<DumpTicker>();
    }

    /// <summary>Map data on each location load, and the map pictures on their key and once per place on view.</summary>
    private static void WatchMaps(string folder, bool isAutoPictures, bool isDaylight, ConfigEntry<KeyCode> pictureKey, float pixelsPerMeter, float cutHeight, DumpLog log)
    {
        var pictureHotkey = new CtrlHotkey(pictureKey.Value, log.Write);
        var capture = new MapCapture(Path.Combine(folder, MapWatcher.MapsFolder), pixelsPerMeter, cutHeight, isAutoPictures, pictureHotkey, new Daylight(isDaylight, log.Write), log.Write, log.Worker);
        var watcher = new MapWatcher(folder, log.Write, log.Worker);

        WatchKey(pictureKey, pictureHotkey, "map picture key", log);

        DumpTicker.Checks.Add(watcher.Check);
        DumpTicker.Checks.Add(capture.CheckKey);
        DumpTicker.Checks.Add(capture.CheckAuto);
    }

    private static void WatchKey(ConfigEntry<KeyCode> entry, CtrlHotkey hotkey, string what, DumpLog log) =>
        OptionalLive.Watch(entry, key =>
        {
            hotkey.Use(key);
            log.Write($"{what} {hotkey.Name}");
        });
}
