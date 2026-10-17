# Data Dump

**A developer tool, not a player mod.** The source is public, but it is not released on Nexus. It writes the data Survival Log loads at startup, and bug snapshots on request, to plain files that are easy to search.

The game's code can be read in a disassembler, but the data it loads cannot: item stack limits, action durations, payout intervals, interest rates, the Chinese and English text, and the default key bindings. This plugin writes all of it out once per launch.

Built for Survival Log 1.1.18317 with BepInEx 6 (IL2CPP) `6.0.0-be.788`. Install it like any other mod: put `DataDump.dll` in `BepInEx/plugins`.

## What it writes

Everything goes to `BepInEx/data-dump/` in the game folder.

| File | What is in it |
| --- | --- |
| `<Table>.jsonl` | One file per config table in `ConfigManager` (`_Config_Action_Dict` becomes `Config_Action.jsonl`). One row per line, as a JSON object of every public field and property, with the row's id first. Enums are written as their names, lists as arrays, and other config objects as their id, or a short type name when they have none. Nesting stops at a fixed depth. |
| `lang-zh.json`, `lang-en.json` | The game's text: key to text, sorted by key. |
| `input.json` | Each hotkey action with its default key and the key it is bound to now. |
| `manifest.json` | The game version, when the dump ran and why, the current language, each table's row count, and every table or file that was skipped or failed, with the reason. |
| `mod-health.json`, `mod-health.txt` | See [Mod health](#mod-health). |
| `action-ledger.tsv` | See [Action ledger](#action-ledger). |
| `snapshots/<yyyyMMdd-HHmmss>/` | See [Bug snapshots](#bug-snapshots). |

The plugin's own log is `BepInEx/data-dump.log`. It is kept across launches and trimmed when it passes 1 MB.

A table keyed by something other than a single id (`_Config_DailyWeather_DayHour_Dict`) is not written as its own file; the manifest says so. Its rows are the same as `Config_DailyWeather.jsonl`.

## When it runs, and how to dump again

- **Once per launch**, a couple of seconds after the game has finished loading its configs. The work is spread over frames, a few milliseconds each, so the game does not hitch.
- **Ctrl+F5** dumps the tables again and rewrites the mod health. Use it after changing the language, a key binding, or anything a mod rewrites at runtime.

Each file is written beside its final name and moved into place when it is complete, so a half-written file never looks whole.

## Bug snapshots

Press **Ctrl+F10** when something looks wrong. A folder `snapshots/<yyyyMMdd-HHmmss>/` gets:

- `screenshot.png`: the frame the key was pressed in.
- `state.json`: the game day, time of day, game speed and pause; then the player, every survivor and the robot, each with name, id, position and floor, every attribute, active buffs with time left, the current action and the queued actions. The player is marked.
- `ui.json`: the open web pages (key, URL, type, whether the game counts each as shown), and what decides whether clicks reach the world (CameraTouch's raycast target, the object menu flag, the camera pause).
- `redux-<page>.json`: each open page's Redux state.
- `root.html` and `page-<page>.html`: the HTML of the web UI and of each open page, as the web view returns it within 10 seconds. `page-capture.json` lists which pages were saved and why any were not.
- `logs/`: the last 300 lines of every `BepInEx/*.log` and of the game's `Player.log`.
- `mod-health.json`, `mod-health.txt`: the mod health at that moment. The copy in `data-dump/` is refreshed too.

Only the 50 newest snapshots are kept. A line in `data-dump.log` says where each one went. A second press while the last snapshot is still waiting for its page HTML is refused with a log line.

## Action ledger

`action-ledger.tsv` gets one line each time the player, a survivor or the robot finishes or breaks off an action. Tab-separated, with a header line. When it would pass 5 MB it moves to `action-ledger.old.tsv` (replacing any older one) and a new file starts.

| Column | Meaning |
| --- | --- |
| `real_time` | The computer's clock when the action ended. |
| `game_day`, `game_time` | The game day and time of day when it ended. |
| `who` | `player`, `robot`, or the survivor's name. |
| `action_id`, `action_name`, `action_type` | From `Config_Action`. |
| `during_at_start` | `Config_Action.During` when the action started. Speed mods rewrite it, so it is read then. |
| `game_seconds`, `real_seconds` | How long it took in game seconds and in real seconds. Real seconds do not stop when the game is paused. |
| `game_speed` | The game speed at the start, or `start -> end` if it changed. |
| `attribute_changes` | Every attribute that changed between the start and the end. |
| `buffs_gained`, `buffs_lost` | Buffs that appeared or went away meanwhile. |
| `outcome` | `finished`, `cancelled` (the game removed it as broken off, so it gave no end reward), or `superseded` (the same character started another action before this one was seen to end). |

The attribute changes are the whole difference between the start and the end. They include anything else that happened during the action, such as hunger ticking down, a buff running out, or damage taken. Compare like with like: the same action at the same time of day, with and without a speed mod.

Walking is left out. Lines are written in batches, at most every 5 seconds, so the lines of the last few seconds before the game closes may be missing. Actions still running when you load another save or go back to the title screen get no line. While the file is open in a program that locks it (Excel does), up to 10,000 lines wait and are written once it is closed. Once a minute, `data-dump.log` gets a line with what the ledger cost the game: how many calls, the longest and the total, in milliseconds.

## Mod health

`mod-health.json` and the readable `mod-health.txt` list:

- every BepInEx plugin that loaded: GUID, name, version, DLL file, and whether it started;
- every plugin BepInEx refused to load, with its reason;
- every method patched through Harmony, with each owner's prefixes, postfixes, transpilers and finalizers;
- a "shared" list of methods patched by more than one owner. These are the places two mods can clash, third-party mods included.

They are written 30 seconds after the game has loaded, with every snapshot, and on Ctrl+F5.

## Keys and config

Config file: `BepInEx/config/com.dev.survivallog.datadump.cfg`.

| Section | Key | Default | What it does |
| --- | --- | --- | --- |
| `General` | `Enabled` | `true` | Off: the plugin does nothing at all. |
| `General` | `DumpKey` | `F5` | Ctrl plus this key dumps again. |
| `Snapshot` | `Key` | `F10` | Ctrl plus this key saves a bug snapshot. |
| `Ledger` | `Enabled` | `true` | Writes the action ledger. |

Other keys nearby:

- The other mods in this collection use F6 to F9, and WebUI Doctor uses Ctrl+F11 and Ctrl+F12. F5 and F10 are free of them. Camera keeps F10 out of the keys it lets you bind.
- The game lets you bind F keys in its own key settings. If you bind F5 or F10 to a game action there, that action may run on the same press.

## Planned

Not built yet:

- **Game update diff**: keep each game version's dump in its own folder, and compare the two after a game patch to see which tables, rows and text changed.
- **Call tracer**: an opt-in list of game functions to log each time they run. Before hooking a function it must check the function's first 14 bytes and refuse any that start with a relative jump or call, which the hooking library cannot move safely.
