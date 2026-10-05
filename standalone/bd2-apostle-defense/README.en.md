# BD2 Apostle Defense

> **Free & open source:** Official releases are provided free by GitHub **MadestSamurai** · Bilibili **MadSamurai**. [Official downloads](https://github.com/MadestSamurai/bd2-apostle-defense/releases) · [Source and risk notice](DISTRIBUTION.md#english). Third-party fees do not imply the author’s involvement, endorsement or support.
>
> **Risk notice:** This is an unofficial community tool. Use may result in account penalties, bans, game errors or data loss. Follow the game rules and accept responsibility for the risks of use. The MIT license remains unchanged.

English · [简体中文](README.md)

[Download latest release](https://github.com/MadestSamurai/bd2-apostle-defense/releases/latest) · [Report an issue](https://github.com/MadestSamurai/bd2-apostle-defense/issues)

A standalone Apostle Defense assistant for the BrownDust II Windows client. Works toward wave-50 and top-tier-summon achievements with automatic summons, upgrades, repositioning, rerolls and next rounds.

## Download

Current version: **0.3.7**. Both editions have the same features and include Simplified Chinese / English.

| Edition | Runtime requirement | Recommended for |
| --- | --- | --- |
| **Portable** | .NET included | Most users; download and run |
| **Lite** | [.NET Desktop Runtime 8 x64](https://dotnet.microsoft.com/download/dotnet/8.0) | Smaller download if the runtime is installed |

Download one edition: the EXE runs on its own; ZIPs include both READMEs and licenses. No Python, development SDK or other BD2 tools are required. Lite needs the **Desktop Runtime**, not just .NET Runtime or ASP.NET Runtime. Verify downloads against `SHA256SUMS.txt`.

## Quick start

**Before upgrading:** pause and close the old assistant. From 0.3.4 or later, keep the game running and connect with the new version. Migration from 0.3.3 or earlier needs one normal game restart.

1. Enter the Apostle Defense lobby, open the assistant, and click **Connect game**.
2. Choose goals, check your account and server progress, then click **Start**. By default, it pursues wave 50 before top-tier summons.
3. The assistant acts on the live board, exits completed rounds, checks achievements, and continues according to your settings.
4. Pause at any time or choose **Stop after this round**. Pausing the assistant does not pause the game timer.

## Features and settings

- Retains confirmed end-of-round evidence through scene teardown. A remaining result popup can retry Exit even after native flags reset; live-round result menus are not treated as defeat.
- Connection, status reads and control writes run off the UI thread. Pause immediately revokes local automation; closing cancels pending connection preparation and uses a bounded shutdown.

- Brief network interruptions preserve the task until the connection and board recover. Normal disconnection after the entire round ends no longer blocks settlement or Exit.
- Missing acceptance or overdue readback first retires the old command with component confirmation, then replans from the current screen. Temporary file contention gets bounded retries; a diagnostic-write failure does not block state updates.

- The 68-cell board shows actual positions, elements, tiers, enemies and move arrows. Click a unit to inspect its stats, range and route coverage. Inspection does not control the game; arrow keys navigate and Esc clears selection.
- English cells use Wa / Fi / Wi / Li / Da for Water / Fire / Wind / Light / Dark, followed by tier. Tooltips and selection details show full names.
- Ordinary waves compare executable moves and swaps, upgrades, summons and low-risk rerolls. A confirmed sale is followed by a refill before other spending.
- Before the first boss, build a three-unit starting lineup and save toward summons. After that, upgrades are evaluated against current-wave hits to kill; overkill does not count as extra clearing speed. When saving for the next summon, an upgrade must earn that summon back sooner through extra kills. This is a decision estimate, not a full battle simulation.
- Boss waves use enemy position, speed and route to predict short-term damage and move attackers to stay in range. The planner handles multiple bosses, deducts displaced allies' losses and preserves attack progress. Static repositioning and selling are suspended during boss waves.
- Action interval: 100–3000 ms, default 500 ms. Commands wait for game confirmation rather than accumulating. Pursuit also checks the exact live target, wave and snapshot age.
- Results, Exit, confirmation and return to lobby follow the game's own flow. Achievement progress is read back from the server before continuing. Disabling auto-next still allows the current round to exit normally.

The planner uses short-horizon estimates. **It is not a full battle simulator and does not guarantee wave 50 or a specific summon.** Boss pursuit has offline regression coverage; actual results also depend on units, frame rate and latency.

## Language

Use **语言 / Language** in the top bar to switch between Simplified Chinese and English. The first launch uses Chinese on Chinese systems and English otherwise, then remembers your choice. Switching does not restart automation or change settings. Game-provided names and images keep their game language; raw diagnostics remain unchanged.

See [translation maintenance](docs/LOCALIZATION.md).

## Compatibility and limits

Supports the official Windows x64 PC client, one game process at a time, with the same privilege level as the game. Mobile and Android emulator clients are not supported. First connection resolves local interfaces and builds the component, which may take a few seconds. Uncertain interface matches stop connection with a diagnostic; adaptation does not guarantee every future update will work without maintenance.

Releases contain no game DLLs, resources, account inventories or private captures. Uses short-horizon estimates and does not guarantee wave 50 or a specific summon; it does not abandon a live round.

## Diagnostics and feedback

Settings, language and diagnostics are under `%LOCALAPPDATA%\BD2ApostleDefense`; click **Open diagnostics**. Existing goals and preferences are retained.

| File | Purpose |
| --- | --- |
| `connection.log` | Connection stages, timed waits and failures; uses `%TEMP%\BD2ApostleDefense` if the normal log directory is unavailable |
| `decisions-date.log` | Decisions, readbacks, recovery and stop reasons |
| `flow-current.jsonl` / `flow-previous.jsonl` | Matching, battle, results and popup transitions |
| `network-current.jsonl` / `network-previous.jsonl` | Connectivity checks, recovery and waiting reasons |
| `io-errors-process-id.log` | File operation paths, error codes and stack traces, with repeated errors rate-limited |
| `last-action.json` | Before/after snapshots and the latest action outcome |
| `compatibility.json` / `runtime.json` | Component compatibility and status |

Brief missing snapshots preserve automation while waiting for a heartbeat. Recoverable clicks and readback timeouts replan after a cooldown; failed movement does not block summoning. Matching can resume from the lobby or failure screen. Lobby arrival confirms settlement, then a normally dismissible event overlay is closed. Unknown popups wait for you; account/process changes and unclassified component errors still stop the assistant.

When reporting an issue, include the version, visible message and relevant log excerpts. Remove account information and personal paths first. Do not upload game DLLs, complete inventories or connection credentials.

## Development and contributions

Requires Windows x64, PowerShell and the .NET 8 SDK. Normal builds and regression tests do not need or connect to the game.

```powershell
.\build.ps1 -Locked
.\package.ps1 -Locked
```

Assets are written to `dist/v<version>/`. Packaging checks both runtime configurations and runs UI checks.

[Development and release workflow](docs/DEVELOPMENT.md) · [Documentation and release format](docs/PUBLICATION_STYLE.md) · [Current release notes](docs/RELEASE_NOTES.md)

## License

Project code is [MIT licensed](LICENSE). Dependencies retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md). This project is not affiliated with the game developer or publisher.

## Connection and tool switching

When upgrading from an older release for the first time, close the old tools and restart the game once. These updated tools can then update and switch within the same game process: pending game operations finish before control changes. Settings and records are retained. Live communication uses local named pipes. Modules used by the daily workflow are coordinated separately by its scheduler.
