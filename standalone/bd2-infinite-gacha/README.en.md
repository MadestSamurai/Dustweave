# BD2 Infinite Gacha

> **Free & open source:** Official releases are provided free by GitHub **MadestSamurai** · Bilibili **MadSamurai**. [Official downloads](https://github.com/MadestSamurai/bd2-infinite-gacha/releases) · [Source and risk notice](DISTRIBUTION.md#english). Third-party fees do not imply the author’s involvement, endorsement or support.
>
> **Risk notice:** This is an unofficial community tool. Use may result in account penalties, bans, game errors or data loss. Follow the game rules and accept responsibility for the risks of use. The MIT license remains unchanged.

English · [简体中文](README.md)

[Download latest release](https://github.com/MadestSamurai/bd2-infinite-gacha/releases/latest) · [Report an issue](https://github.com/MadestSamurai/bd2-infinite-gacha/issues)

A standalone Infinite Gacha assistant for the BrownDust II Windows client. Reads available costumes and enhancement levels, rerolls against A/B targets, skips animations, and keeps matching results for your confirmation.

## Download

Source version: **0.2.6**. Both editions have the same features and include Simplified Chinese / English.

| Edition | Runtime requirement | Recommended for |
| --- | --- | --- |
| **Portable** | .NET included | Most users; download and run |
| **Lite** | [.NET Desktop Runtime 8 x64](https://dotnet.microsoft.com/download/dotnet/8.0) | Smaller download if the runtime is installed |

Download one edition: the EXE runs on its own; ZIPs include both READMEs and licenses. No Python, development SDK or other BD2 tools are required. Lite needs the **Desktop Runtime**, not just .NET Runtime or ASP.NET Runtime. Verify downloads against `SHA256SUMS.txt`.

## Quick start

**Before upgrading:** stop and close the old assistant. Follow the connection and tool switching section below; current components support same-process handoff.

1. Start the game and log in. Open Infinite Gacha, make the first 10-pull manually, and stay on the result screen.
2. Open the assistant and select **Connect game**. For legacy components, follow the upgrade instructions below.
3. Select **Add unmaxed → B** to add unowned and +0 through +4 costumes to the left-hand B list.
4. Select priority targets in B and click **To A →**. Ctrl/Shift supports multiple selection; double-click or Enter transfers between groups, and Delete removes a target. Use **Add costumes…** for ungrouped costumes.
5. Set the top A threshold and any lower-tier rules, then select **Start rerolling**. A match stops the assistant. Keeping or purchasing the result remains a manual decision in the game.

## Features and settings

### Pool selection

The pool selector identifies events by **end date**, newest first. Dates use your computer’s local time zone; hover to see the time zone and the in-game event name. **Current result** marks the pool shown in the game. Pool IDs distinguish matching dates, and pools without a supplied end date stay at the bottom. Reordering preserves your targets and stop rules.

### Target reached alerts

The bottom of the window offers **Windows notification, Popup and Sound**. Combine them freely or turn them all off; only Windows notification is on by default. **Test alerts** checks the selected methods without starting draws or changing an active run.

Preferences are saved across accounts and pools, and can be adjusted while rerolling. Alerts fire once after this run reaches its target and stops, with A/B counts and the reroll count. Manual stops, errors and old results after reopening the app do not trigger completion alerts. The popup only shows the result; confirm keeping/purchasing it in the game.

Windows notification and Do not disturb settings may suppress notifications. Sound follows your system volume and sound scheme. The notification and popup are silent unless the separate Sound option is enabled.

### Stop rules

A contains priority targets; B contains secondary targets. A costume belongs to at most one group. Set an unconditional A threshold from 1 to 10. Lower tiers appear automatically and can each be enabled separately.

For example, set the top threshold to 3:

| Actual A count | Stop requirement |
| --- | --- |
| ≥ 3 | Stop unconditionally |
| = 2 | B ≥ 3 |
| = 1 | B ≥ 5 |
| = 0 | Disabled; keep rerolling |

Any enabled rule can stop the run. Lower tiers require an **exact A count**. A+B cannot exceed 10. Invalid or unfinished input is flagged and preserved, including values in temporarily hidden tiers.

- **Count duplicate copies separately** is enabled by default. Turn it off to count distinct costume IDs instead.
- **Exclude excess copies** is enabled by default in copy-counting mode. Count only copies needed to reach +5: up to 6 if unowned, 5 at +0, 1 at +4, and none at +5. You can disable this independently.
- **Reroll interval** defaults to 1000ms, adjustable from 100 to 60000ms. The previous complete result must be checked before the next request. Network and game animations can make the actual interval longer.
- Targets and rules are saved separately per account and pool. Closing or stopping the assistant does not enable automatic restarting.

## Language

Use **语言 / Language** in the top bar to switch between Simplified Chinese and English. The first launch uses Chinese on Chinese systems and English otherwise, then remembers your choice. Switching does not restart automation or change settings. Game-provided names and images keep their game language; raw diagnostics remain unchanged.

See [translation maintenance](docs/LOCALIZATION.md).

## Compatibility and limits

Supports the official Windows x64 PC client, one game process at a time, with the same privilege level as the game. Mobile and Android emulator clients are not supported. First connection resolves local interfaces and builds the component, which may take a few seconds. Uncertain interface matches stop connection with a diagnostic; adaptation does not guarantee every future update will work without maintenance.

Releases contain no game DLLs, resources, account inventories or private captures. Stops and keeps a matching preview; final confirmation or purchase remains your decision in the game.

## Diagnostics and feedback

Brief missing-state or UI transitions wait up to 15 seconds for recovery. Temporary file locks are retried without discarding the current task. Close game popups to continue automatically. Animation skipping uses the game's normal skip action.

Account, process or pool changes, locked results and explicit server errors still stop the run. A server request with no response after 30 seconds is not blindly resent: it might already have produced a result. Stopping or closing the assistant prevents new requests; an outstanding request can still complete normally.

Local data is stored in `%LOCALAPPDATA%\BD2InfiniteGacha\`:

| File | Purpose |
| --- | --- |
| `settings.json` | Targets and rules per account/pool |
| `language.json` | Interface language |
| `desktop-events.jsonl` / `runtime-events.jsonl` | Recovery, stop and error events; size-limited automatically |
| `snapshot.json` | Latest inventory, result and animation state |
| `connection.json` / `compatibility.json` / `runtime.json` | Connection diagnostics |

When reporting an issue, include the tool version, visible error and relevant event-log lines. Do not publish complete inventories, account files or personal data.

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
