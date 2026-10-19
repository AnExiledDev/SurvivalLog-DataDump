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
    private const string Version = "0.2.0";

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

        var folder = Path.Combine(Paths.BepInExRootPath, "data-dump");
        var log = new DumpLog(Log, Path.Combine(Paths.BepInExRootPath, "data-dump.log"));

        log.TrimIfLarge();
        log.Write($"===== Data Dump {Version} started, enabled {isEnabled}, dump key Ctrl+{dumpKey.Value}, snapshot key Ctrl+{snapshotKey.Value}, action ledger {isLedgerOn}");

        if (!isEnabled) return;

        PatchKit.Log = log.Write;
        DumpTicker.Log = log.Write;

        if (isLedgerOn && ActionLedger.Apply(new Harmony(Guid), folder, log.Write)) DumpTicker.Checks.Add(ActionLedger.Tick);

        var health = new ModHealthProbe(log.Write);
        var dumpHotkey = new CtrlHotkey(dumpKey.Value, log.Write);
        var snapshotHotkey = new CtrlHotkey(snapshotKey.Value, log.Write);
        var session = new DumpSession(
            new TableDumper(folder, $"Data Dump {Version}", log.Write),
            health,
            new SnapshotTaker(Path.Combine(folder, "snapshots"), health, folder, log.Write),
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

    private static void WatchKey(ConfigEntry<KeyCode> entry, CtrlHotkey hotkey, string what, DumpLog log) =>
        OptionalLive.Watch(entry, key =>
        {
            hotkey.Use(key);
            log.Write($"{what} {hotkey.Name}");
        });
}
