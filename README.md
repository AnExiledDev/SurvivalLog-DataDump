# Data Dump

**A developer tool, not a player mod.** The source is public, but it is not released on Nexus. It writes the data Survival Log loads at startup, and bug snapshots on request, to plain files that are easy to search.

The game's code can be read in a disassembler, but the data it loads cannot: item stack limits, action durations, payout intervals, interest rates, the Chinese and English text, and the default key bindings. This plugin writes all of it out once per launch.

Built for Survival Log 1.1.18902 with BepInEx 6 (IL2CPP) `6.0.0-be.788`. It works with [Exiled's Toolkit](https://github.com/AnExiledDev/SurvivalLog-ExiledToolkit) 0.4.0 or newer, which is optional. With it, settings changed on the Mod Manager's Mods screen take effect without restarting the game. Without it, they take effect at the next game start. Install it like any other mod: put `DataDump.dll` in `BepInEx/plugins`.

## What it writes

Everything goes to `BepInEx/data-dump/` in the game folder.

| File | What is in it |
| --- | --- |
| `<Table>.jsonl` | One file per config table in `ConfigManager` (`_Config_Action_Dict` becomes `Config_Action.jsonl`). One row per line, as a JSON object of every public field and property, with the row's id first. Enums are written as their names, lists as arrays, and other config objects as their id, or a short type name when they have none. Nesting stops at a fixed depth. |
| `lang-zh.json`, `lang-en.json` | The game's text: key to text, sorted by key. |
| `input.json` | Each hotkey action with its default key and the key it is bound to now. |
| `manifest.json` | The game version, when the dump ran and why, the current language, each table's row count, and every table or file that was skipped or failed, with the reason. |
| `mod-health.json`, `mod-health.txt` | See [Mod health](#mod-health). |
| `enums.json` | Every enum in the game's code, value to name. Config fields hold plain numbers; this names them. |
| `maps/<phase>/<location>.json` | See [Maps](#maps). |
| `maps/<phase>/<location>/` | See [Maps](#maps). |
| `npc-dialogue/<key>.json` | The NPC dialogue the game had loaded when a location was written. |
| `action-ledger.tsv` | See [Action ledger](#action-ledger). |
| `loot-ledger.tsv` | See [Loot ledger](#loot-ledger). |
| `bundles/` | See [Asset bundles](#asset-bundles). Off by default. |
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

## Maps

The game loads one location at a time: the home, before or after the disaster, or an expedition. Once a new location is there, `maps/<phase>/<location>.json` gets its map data: every furniture spot, building slot, package spot and camera area, each with its world position and rotation, plus the scene name, the game's world state and chapter.

`<phase>` is `pre-disaster` or `post-disaster`, read from the game's clock (scene names do not tell: `Scene_AfterTheDisaster02` is the world before the disaster). If the clock does not say within 10 seconds, the folder is `unknown-phase`.

The plugin also takes top-down pictures into `maps/<phase>/<location>/`, of the place the game shows. The game only draws the part of a location you are at (your home, or the one store you walked into) and only the floor you are viewing, so a picture of anything else would be black. Once the view has stayed on a place for 10 seconds, its pictures are taken, once per game session. To get them all, walk into each store, and in a home switch to each floor with the game's floor views (F1 to F3) and wait about 10 seconds on each. **Ctrl+F4** retakes the place and floor on view now. Each place gets two:

- `<area>.plan.png` looks down from a little above the floor (2.2 m by default), so ceilings and upper floors are cut away and the rooms show.
- `<area>.top.png` looks down from high above, roofs and all.

The game already hides the roof of the building you are in, so the two differ mostly outside it: the plan picture also leaves out trees and the roofs of the buildings next door.

While the pictures are taken, the game's light is switched to midday and then back (`Daylight`), so a place visited at night still comes out lit. A building that stands at an angle (the Tool Store and the Small Market stand at 15 degrees) is pictured turned to its angle, so it lies square in the picture and none of it is cut off. The angle is the one most of the place's furniture stands at.

A home floor adds `.floor<id>` to the name: `Home.floor102.plan.png` is the second floor and `floor101` the basement. A picture that comes out black (the place was not drawn yet, or a menu was open) is not saved and is tried again 10 seconds later, up to 3 times; the log says when a place gave up.

Each picture has a `.json` beside it with the frame: its turn `Yaw` in degrees, the box it covers in turned meters (`MinU` to `MaxU`, `MinV` to `MaxV`), the floor and its height, and the pixels per meter. A world position X, Z turns into `U = X cos(Yaw) - Z sin(Yaw)` and `V = X sin(Yaw) + Z cos(Yaw)`, and lands on the picture at `x = (U - MinU) * PixelsPerMeter` from the left and `y = (MaxV - V) * PixelsPerMeter` from the top. With `Yaw` 0, U is X and V is Z. Large pictures are rendered in pieces and joined. The game stops for a few seconds while a large picture is taken.

## Loot ledger

`loot-ledger.tsv` gets one line every time the game rolls a loot group: furniture loot, reward sets, stranger trades and the like. Tab-separated, with a header line.

| Column | Meaning |
| --- | --- |
| `time` | The computer's clock. |
| `map` | The location loaded at the time. |
| `group` | The `Config_RandomGroup` id. |
| `count` | How many picks were asked for. |
| `discover_lv`, `guarantee_lv` | The discovery and guarantee levels it rolled at. A `discover_lv` of 2147483647 means no discovery limit: every entry of the group can come up. Shops, random-item effects and items an event puts into furniture roll this way. |
| `items` | The item ids it gave, comma-separated. |

Lines are written every 5 seconds, so the last few before the game closes may be missing. Up to 10,000 wait while the file is locked by another program.

## Asset bundles

With `Bundles.Export` on, the plugin writes the game's asset bundles at game start, decrypted, to `bundles/<name>.bundle`, where asset tools such as AssetRipper can open them. It runs beside the game and logs when it is done. All of them take about 4 GB; `Bundles.Filter` limits it to names containing one of its words. A bundle already written at its full size is skipped.

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

Config file: `BepInEx/config/com.dev.survivallog.datadump.cfg`. The three keys can also be changed in game on the Mod Manager's Mods screen, where they take effect at once with Exiled's Toolkit installed; the other settings take effect at the next game start.

| Section | Key | Default | What it does |
| --- | --- | --- | --- |
| `General` | `Enabled` | `true` | Off: the plugin does nothing at all. |
| `General` | `DumpKey` | `F5` | Ctrl plus this key dumps again. |
| `Snapshot` | `Key` | `F10` | Ctrl plus this key saves a bug snapshot. |
| `Ledger` | `Enabled` | `true` | Writes the action ledger. |
| `Maps` | `Enabled` | `true` | Writes each location's map data and the NPC dialogue. Also needed for the pictures. |
| `Maps` | `AutoPictures` | `true` | Takes the pictures of each place and floor on view once per session, after the view stays on it 10 seconds. |
| `Maps` | `PictureKey` | `F4` | Ctrl plus this key retakes the pictures of the place and floor on view. |
| `Maps` | `PixelsPerMeter` | `120` | Picture scale. A large location is made smaller so a picture is at most 8192 pixels across. Higher is sharper and stops the game longer. |
| `Maps` | `Daylight` | `true` | Switches the game's light to midday while pictures are taken, then back. |
| `Maps` | `CutHeight` | `2.2` | How far above the floor the plan pictures start, in meters. |
| `Loot` | `Enabled` | `true` | Writes the loot ledger. |
| `Bundles` | `Export` | `false` | Writes the decrypted asset bundles at game start. |
| `Bundles` | `Filter` | empty | Only bundles whose name contains one of these comma-separated words. Empty: all. |

Other keys nearby:

- The other mods in this collection use F6 to F9, and WebUI Doctor uses Ctrl+F11 and Ctrl+F12. F4, F5 and F10 are free of them. The game binds F1 to F3 to its floor views. Camera keeps F10 out of the keys it lets you bind.
- The game lets you bind F keys in its own key settings. If you bind F4, F5 or F10 to a game action there, that action may run on the same press.

## Contact

To reach me directly, message `exiled.dev` on Discord; you can find me in the [official Survival Log Discord](https://discord.gg/MZGg4gQAUe). I'm also on X as [@AnExiledDev](https://x.com/AnExiledDev).

A Survival Log wiki with detailed documentation for all of my mods is coming soon.

## Planned

Not built yet:

- **Game update diff**: keep each game version's dump in its own folder, and compare the two after a game patch to see which tables, rows and text changed.
- **Call tracer**: an opt-in list of game functions to log each time they run. Before hooking a function it must check the function's first 14 bytes and refuse any that start with a relative jump or call, which the hooking library cannot move safely.
