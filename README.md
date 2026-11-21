# SharpTimer

SharpTimer is a CounterStrikeSharp timer plugin for Counter-Strike 2 Surf, Bhop, Minigame and Deathrun servers.

This fork includes a server master switch, shot-based zone editing, replay bot and camera improvements, and separate appearance files for the normal and replay HUDs. The original project was created by [DEA](https://github.com/girlglock); see the credits below for the upstream maintainers.

## Features

- Map and bonus timers, personal bests, server records, stages and checkpoints.
- Speed, strafe synchronization, rank icons and key input displays.
- Separate normal and replay HUD colors, font classes and row layouts.
- Player replays and a looping server-record replay bot.
- Zone editing using player position or gunshot impact points.
- SQLite storage, optional MySQL/PostgreSQL support and JSON records.
- Server points, map ranks, styles, movement modes and optional Discord webhooks.
- Per-map configuration, custom player models and VIP features.

## Requirements

- [MetaMod](https://cs2.poggu.me/metamod/installation/).
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/releases), compatible with the API version used by this build (`1.0.375`).
- [cs2-tags](https://github.com/CounterStrike2-Plugins-Archive/cs2-tags).
- .NET 10 SDK when building from source.

Recommended movement extensions: [MovementUnlocker](https://github.com/Source2ZE/MovementUnlocker), [RampBugFix](https://github.com/Interesting-exe/CS2Fixes-RampbugFix/) and [STFixes](https://github.com/SharpTimer/STFixes-metamod). Optional integrations include [WallLists](https://github.com/SharpTimer/WallLists) and [Trails](https://github.com/SharpTimer/Trails).

## Installation

1. Download a package from this fork's [releases](https://github.com/TheIriya/SharpTimer/releases), or build from source as described below.
2. Copy the package's `addons/` and `cfg/` folders into your server's `game/csgo/` directory. Release archives may contain a versioned outer folder; copy its contents.
3. Start the server or load SharpTimer. Missing configuration files are automatically copied from their `example.*` templates. Existing files are preserved.
4. Edit `game/csgo/cfg/SharpTimer/config.cfg` and your server's movement settings. Set up a database or webhook only if needed.

Keep the `example.*` files when deploying updates so new settings and templates remain available.

## Configuration files

All paths below are relative to `game/csgo/cfg/SharpTimer/`.

| File | Purpose |
| --- | --- |
| `config.cfg` | Plugin settings, feature switches and legacy HUD color convars |
| `hud.json` | Normal gameplay HUD appearance |
| `replay_hud.json` | Replay playback and replay spectator HUD appearance |
| `custom_exec.cfg` | Custom server commands executed after map startup |
| `mysqlConfig.json` / `postgresConfig.json` | Optional database credentials |
| `discordConfig.json` | Optional Discord webhook settings |
| `ranks.json` | Rank titles, colors and icons |
| `admessages.txt` | Advertisement messages |
| `MapData/` | Map zones, metadata and per-map configuration |

### HUD colors and layout

The repository ships [`example.hud.json`](cfg/SharpTimer/example.hud.json) and [`example.replay_hud.json`](cfg/SharpTimer/example.replay_hud.json). On first load, these become `hud.json` and `replay_hud.json`. Edit the generated files on your server. To prepare them manually, copy each example and remove the `example.` prefix.

Each HUD file defines the text, colors, font sizes, CSS positions and styles for each HUD item. The two files load separately, so replay and normal HUDs can have different settings.

| Section | Settings |
| --- | --- |
| `Texts` | Text templates for the replay header, timer, speed, sync, records, map/mode/bonus details, keys, replay progress and the no-record label |
| `Colors` | Independent colors for each item, including replay, timer, speed, pre-speed, sync, info, server record, keys and progress |
| `FontSizes` | An independent CSS font size for each layout item |
| `FontClasses` | Optional CS2 font classes for the same text groups |
| `Position` | CSS position/style string for the container, rows and every layout item |
| `SectionStyles` | Optional CSS class and style for the container, rows and every layout item |
| `Layout` | Ordered array of rows. Each row can arrange one or more placeholders |

Use HTML color names or hex colors such as `#00FF00`. Per-item colors in the JSON control the HUD. Older `Primary`, `Secondary`, `Tertiary`, `Muted` and `Replay` values are accepted for compatibility, but new configurations should use the per-item fields shown in the examples.

Font classes use CS2's center HTML HUD classes, such as `fontSize-s`, `fontSize-l`, `fontSize-ml`, `stratum-bold-italic`, `stratum-light-mono` and `horizontal-center`. This controls text styling inside CS2's center HUD; screen coordinates are determined by the game.

| Placeholder | Content |
| --- | --- |
| `{replay}` | Replay title and playback time |
| `{timer}` | Running map or bonus timer |
| `{speed}` / `{preSpeed}` | Current speed and previous speed |
| `{sync}` | Strafe synchronization |
| `{placement}` / `{recordPlacement}` / `{stage}` | Current timer placement, server-record placement and map stage |
| `{info}` / `{mapInfo}` / `{style}` / `{mode}` / `{bonus}` | Personal/record time, map details, style/mode and bonus information |
| `{serverRecord}` | Current server-record holder and time for the selected map, bonus, style and mode |
| `{serverRecordTime}` | Current server-record first-place time only, for the selected map, bonus, style and mode |
| `{keys}` | Configurable movement key labels (`J` = jump and `C` = crouch by default) |
| `{progress}` | Current replay frame and total frame count |

Rows are joined with `<br>`; empty rows are skipped. Reorder rows to move items or put multiple placeholders on one line. `Texts` supports value placeholders including `{time}`, `{speed}`, `{preSpeed}`, `{sync}`, `{placement}`, `{recordPlacement}`, `{stage}`, `{info}`, `{mapInfo}`, `{style}`, `{mode}`, `{bonus}`, `{serverRecord}`, `{serverRecordTime}`, and `{progress}`. The `Keys` template also supports `{left}`, `{forward}`, `{right}`, `{back}`, `{jump}` and `{duck}`. Text values are rendered as plain text. CSS position declarations are applied independently to every item; for example, `"Replay": "position:relative; top:-12px; left:8px"` moves the replay header within the center HUD. Available CSS behavior depends on the CS2 center HTML renderer.

Font sizes accept CSS values such as `18px` and `1.2em`. `SectionStyles` can add a class and style per item. For example, this compact layout puts keys above the speed and replay heading:

```json
{
  "Colors": { "ReplayHeader": "#FF3030", "SpeedValue": "#FFD166", "Keys": "#FFFFFF" },
  "FontSizes": { "ReplayHeader": "20px", "Speed": "18px", "Keys": "14px" },
  "Position": { "Replay": "position:relative; top:-12px", "Speed": "position:relative; left:8px" },
  "Texts": { "ReplayTime": "● RUN {time}", "Speed": "Velocity {speed}" },
  "Layout": ["{keys}", "{replay}", "{speed} {preSpeed}", "{info}"]
}
```

Omitted properties use built-in defaults. JSON comments and trailing commas are accepted. Unknown layout tokens and invalid JSON are reported in the server log; the affected HUD keeps its previous valid configuration, or built-in defaults on first load.

Reload both files from the server console or RCON:

```cfg
sharptimer_hud_reload
```

The existing `sharptimer_enable_*_hud` switches and player visibility preferences still apply. Players can toggle keys with `!keys`; the active layout must also include `{keys}`. Replay spectators use their own key visibility preference; playback input comes from recorded frames. HUD configuration is read at plugin load and by the reload command.

### Master switch

Use the server console or RCON:

```cfg
sharptimer_enabled 0
sharptimer_enabled 1
```

The default is `1`; `false` and `true` are also accepted. Run the command without an argument to inspect the current state. Players cannot change this setting. Set it in `config.cfg` to choose the startup state. Runtime changes persist across rounds and maps until changed again or the plugin is reloaded.

Disabling cancels timers and pending callbacks, stops gameplay hooks, commands, HUDs, replays, advertisements and API actions, and restores saved player/server settings. Enabling initializes the current map and connected players with fresh timers. Completed records are retained. Configuration commands remain available while disabled.

### Zone editing with gunshots

Open the regular zone tool with `!zone`: `!1` selects the start zone, `!2` the end zone, `!3` the respawn point and `!4` saves. `!fakezones` is an alias.

Draw bonus zones with `!bounszone [index]`. Without an index, the first unused bonus index is selected. Passing an index selects that bonus to edit or replace. Its menu uses the same `!1` start, `!2` end, `!3` respawn and `!4` save flow as `!zone`.

Player-position selection is the default and follows your feet for the second corner. Use `!s` before confirming a zone corner to choose it with a gunshot, then use `!d` to return to feet selection. Respawn points always use your current feet position, regardless of the selected corner method. Use `!c` to cancel the active region edit; previously completed points stay intact. `!9` clears all unsaved zone and respawn points, resets the selection method and exits; it does not modify the map file. Use `!zone` to reopen the tool.

- **Player position:** use `!1` to confirm the first corner, move, then use `!1` again to confirm the second corner.
- **Gunshots:** use `!s`, shoot to choose a candidate point, then use `!1` to confirm it. Shoot again and use `!1` again for the second corner.

Gunshots update the candidate point and wireframe preview; they do not confirm points automatically. Each zone-corner confirmation consumes its candidate. Use `!2` in the same way for the end zone. `!3` always records your feet position and never needs a shot. Only the first impact from each shot is used, avoiding duplicate selections from penetration or shotguns.

After confirming the first corner of a zone, use `.h <height>` to set the second corner's height. Then confirm the second corner with the usual menu command (`!1` for start, `!2` for end, or the corresponding bonus command). The second corner's Z coordinate becomes the first corner's Z plus the requested height, while its X and Y come from the usual feet or gunshot selection. In shooting mode, shoot to select the second corner before confirming it. This works for start, end, and bonus zones.

The selected corner method remains active across start/end zones until `!4` saves successfully, then resets to player position. Failed saves retain the method. Switching methods preserves confirmed points and clears the unconfirmed shot candidate. Bonus zone corners use the same shooting behavior, while bonus respawn points always use feet. Zone editing is inactive while SharpTimer is disabled.

### Server-record replay bot

Enable both settings in `config.cfg`:

```cfg
sharptimer_replays_enabled true
sharptimer_replay_bot_enabled true
```

The bot loops the current map's local server-record replay for the server's default mode and Normal style. A matching recording file is required; a leaderboard time alone is insufficient. SharpTimer first adds the bot to CT and falls back to T when CT is full or has no usable spawn. A bot created with ordinary `bot_add` does not become a replay bot.

Run `sharptimer_replay_bot_restart` from the server console to reload it without changing maps. Startup waits for custom/map configuration, then waits up to approximately 20 seconds for a live bot, team assignment and movement services. Repeated requests do not create duplicate bots. After respawning, the bot refreshes its movement services and resumes looping.

If startup fails, check the `Replay bot` console messages. They distinguish missing record times, missing files and unsupported replay versions, and include the searched path. Enable `sharptimer_debug_enabled true` for loaded-frame and mode details. If `!replay` works but the bot cannot find a recording, compare the player's selected mode with the server's default mode.

Playback follows recorded foot positions, crouch state and standing/crouching view height using a movement type that does not add physics motion. Map teleports, replay loops, frame skips and pawn changes reset camera interpolation; ordinary movement remains smooth. Playback clears stale ground/crouch camera offsets and marks specific network components when updating view height. Normal posture is restored when playback stops or the plugin is disabled. Older replay formats remain supported by the existing loader.

## Building and checks

Build the API and plugin with the .NET 10 SDK:

```sh
dotnet build SharpTimerAPI/SharpTimerAPI.csproj -c Release
dotnet build src/SharpTimer.csproj -c Release
```

The deployable files are placed under `Build/`, with `addons/` and `cfg/` ready to copy into `game/csgo/`. The plugin project copies configuration templates, translations, gamedata and database migrations. The tracked `src/API/TagsApi.dll` is a required build dependency.

Run the standalone regression checks:

```sh
dotnet run --project tests/RuntimeGate.Tests/RuntimeGate.Tests.csproj
dotnet run --project tests/ZoneToolShotInput.Tests/ZoneToolShotInput.Tests.csproj
dotnet run --project tests/ReplayBotStartup.Tests/ReplayBotStartup.Tests.csproj
dotnet run --project tests/ReplayCamera.Tests/ReplayCamera.Tests.csproj
dotnet run --project tests/HudAppearance.Tests/HudAppearance.Tests.csproj
```

These checks run without a CS2 server. Verify appearance and playback on a running server before deploying to players.

## Publishing to GitHub

Commit source code, tests, documentation and `example.*` configuration templates. The [`.gitignore`](.gitignore) excludes build output, IDE files, release packages, generated local configurations, credentials, logs, databases, player records and replay recordings. Existing sample map data remains included.

Ignore rules do not remove files already tracked by Git or erase past commits. Review the staged changes before publishing, especially if local server files were previously committed. Keep credentials and webhook URLs in the ignored local configuration files.

## Credits and license

Original creator: [DEA](https://github.com/girlglock).

Upstream maintainer: [Marchand](https://github.com/M-archand). Previous maintainers: [rcnoob](https://github.com/rcnoob) and [exkludera](https://github.com/exkludera).

Upstream community: [Discord](https://discord.com/invite/SmQXeyMcny).

SharpTimer is distributed under the [GNU General Public License v3.0](LICENSE), or any later version as specified in the source headers.
