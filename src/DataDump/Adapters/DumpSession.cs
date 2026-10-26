using System;
using DataDump.Core;
using GameCore.HotUpdate;

namespace DataDump.Adapters;

/// <summary>
/// What the plugin does each frame: the table dump once the game has loaded and on its key, the mod health 30 seconds
/// after that load and with each dump by key, and a snapshot on its key. Each step is its own ticker check, so one that
/// fails does not stop the others.
/// </summary>
internal sealed class DumpSession
{
    private const double SettleSeconds = 2;
    private const double HealthDelaySeconds = 30;
    private const double LoadCheckSeconds = 0.25;

    /// <summary>Held once, so the per-frame load check hands the gate no new delegate.</summary>
    private static readonly Func<bool> IsLoadedCheck = IsLoaded;

    private readonly TableDumper _dumper;
    private readonly ModHealthProbe _health;
    private readonly SnapshotTaker _snapshots;
    private readonly CtrlHotkey _dumpKey;
    private readonly CtrlHotkey _snapshotKey;
    private readonly string _folder;
    private readonly LoadGate _loadGate = new(SettleSeconds);
    private readonly Every _loadEvery = new(LoadCheckSeconds);
    private double? _healthDueAt;

    public DumpSession(TableDumper dumper, ModHealthProbe health, SnapshotTaker snapshots, CtrlHotkey dumpKey, CtrlHotkey snapshotKey, string folder)
    {
        _dumper = dumper;
        _health = health;
        _snapshots = snapshots;
        _dumpKey = dumpKey;
        _snapshotKey = snapshotKey;
        _folder = folder;
    }

    public void WatchLoad(float now)
    {
        if (!_loadGate.Poll(IsLoadedCheck, _loadEvery, now)) return;

        _dumper.Start("load");
        _healthDueAt = now + HealthDelaySeconds;
    }

    public void TickDump(float now) => _dumper.Tick();

    public void CheckDumpKey(float now)
    {
        if (!_dumpKey.WasPressed()) return;

        _dumper.Start(_dumpKey.Name);
        _health.Post(_dumpKey.Name, _folder);
    }

    public void CheckHealthDue(float now)
    {
        if (_healthDueAt is not { } dueAt || now < dueAt) return;

        _healthDueAt = null;
        _health.Post("30 s after load", _folder);
    }

    public void CheckSnapshotKey(float now)
    {
        if (_snapshotKey.WasPressed()) _snapshots.Take(DateTime.Now);
    }

    /// <summary>DateTime.Now reads the time zone, so it is read only while a snapshot is waiting.</summary>
    public void TickSnapshot(float now)
    {
        if (_snapshots.IsWaiting) _snapshots.Tick(DateTime.Now);
    }

    /// <summary>
    /// The game's config load is over: ConfigManager's load (its _Initialize state machine) fills the tables, then the
    /// language maps (OnInitLocalTxt), then the tower wave reward tiers (InitTowerWaveRewardTiers) last of the steps it
    /// awaits, so the last table and the reward tiers both being there means the whole load has run.
    /// </summary>
    private static bool IsLoaded()
    {
        try
        {
            var configs = GameSingleton.Existing<ConfigManager>();
            var cache = configs?.customCache;

            return configs?._Config_Wish_Dict is not null && cache?.ChineseLanguageMap is not null && cache.EnglishLanguageMap is not null &&
                   configs._towerWaveRewardTiers is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
