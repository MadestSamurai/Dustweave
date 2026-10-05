# BD2 Secret Vision

> **Free and open source:** Official author releases are free. GitHub **MadestSamurai** · Bilibili **MadSamurai**. [Official downloads](https://github.com/MadestSamurai/bd2-secret-vision/releases) · [Source and risk information](DISTRIBUTION.md). Third-party fees do not imply the author's involvement, endorsement or support.
>
> **Risk notice:** This project is not affiliated with the game developer or publisher. Helper tools may cause account penalties, bans, game errors or data loss. Follow the game rules and take responsibility for your use. The MIT License remains unchanged.

English · [简体中文](README.md)

[Download releases](https://github.com/MadestSamurai/bd2-secret-vision/releases/latest) · [Report an issue](https://github.com/MadestSamurai/bd2-secret-vision/issues)

An automated territory-claiming assistant for the SECRET VISION minigame in the BrownDust II Windows client.

## Download

Source version **0.1.7**: a single EXE with built-in Simplified Chinese and English.

| Edition | Runtime | Recommended for |
| --- | --- | --- |
| **Portable** | Includes the .NET runtime | First-time users |
| **Lite** | Requires [.NET Desktop Runtime 8 x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) | Users with the runtime installed |

Download standalone EXEs or ZIPs with both READMEs and licenses from the [v0.1.6 release page](https://github.com/MadestSamurai/bd2-secret-vision/releases/tag/v0.1.6). Verify files with `SHA256SUMS.txt`. Windows x64 only. Running the tool does not require Python, a development SDK, the workbench or another BD2 tool. Lite needs **Desktop Runtime**, not just the regular .NET Runtime.

## Quick start

Use the following steps:

1. Start the game, sign in and open the SECRET VISION home screen.
2. Open the assistant, connect to the game and select stages.
3. Choose a 100% or normal 80% target and whether failed attempts should retry.
4. The planner chooses a reachable edge, collects useful items and extends the outer rim before closing it.
5. A stage counts as complete only after a natural finish and a matching new server record. Keep the success result visible for 8 seconds before advancing or returning home. Stop remains immediate; failed-attempt retry timing is unchanged.

Starting a new task ends the current round through the normal game exit and starts the selected stage again. It plans from a live board without an opening pause, and completed routes do not pause the game. A manual pause or interrupted control state stops the task instead of triggering repeated new rounds. Stopping or closing the assistant should release held input and pause the round. It does not automatically revive.

## Features and settings

The planning core implements:

- Edge selection based on actual reachable safe-boundary walking and drawing time.
- Route decisions using current attack timing, enemy motion, collision bounds and remaining boost duration.
- Independent safe repositioning after closure, without charging it against the next drawing window.
- Repair using native cells and edges, replanning after early closure and checking actual progress.
- Short drawing escapes from approaching border lightning, with return-point checks and safe-border fallback.
- Ordered stage selection, failed-attempt retries, 80% / 100% targets and a per-stage attempt limit; 0 means unlimited attempts.
- Success verification using the current round identity and a fresh server record, never an old clear.

## Interface language

Simplified Chinese and English are included in the same executable, with system-language detection and a saved preference. Switching languages during a run does not restart the task.

## Compatibility and limitations

Targets the official Windows x64 PC client, with one game process at a time. Connection resolves local interface structures and compiles the required component against the installed assemblies. It does not authorize clients by a fixed SHA and does not distribute game DLLs.

Structural matching supports renamed obfuscated symbols, reordered members and unrelated additions. Missing interfaces, changed signatures or ambiguous matches stop connection instead of guessing. Future updates may still require maintenance.

The planner uses normal input, speed, items and settlement. It does not change damage, collisions, speed, timers or scores. Random enemy combinations can still fail; retries do not guarantee every round clears.

## Diagnostics and feedback

Expand Diagnostics and data location to read the original error and copy the data path. About & source contains selectable repository URLs. The app does not launch an external file browser or web browser.

For upgrades, follow the connection and tool switching section below. File contention does not require elevation.

Local data uses `%LOCALAPPDATA%\BD2SecretVisionAssistant\`. Settings, connection diagnostics and attempts are stored separately. Restarting the program never automatically replays an old route.

A changed process or component session, or an expired control heartbeat, should release held input. An uncertain network result preserves the state instead of blindly issuing another start. Diagnostics include the operation, path, error code and original stack in `diagnostics.jsonl`. Brief file contention is retried within a bounded interval; persistent renewal failure stops control explicitly. A diagnostic-file failure does not abort normal gameplay.

Report the version, visible error and relevant log lines after removing account details and personal paths. Do not upload game DLLs, account inventories or credentials.

## Development and contributions

Requires Windows x64, PowerShell and .NET 8 or newer SDK. Core checks neither require nor connect to the game:

```powershell
./build.ps1 -Locked
./package.ps1 -Locked
```

[Development](docs/DEVELOPMENT.md) · [Compatibility](docs/COMPATIBILITY.md) · [Release checklist](docs/RELEASE_CHECKLIST.md) · [Release notes](docs/RELEASE_NOTES.md)

## License

Project code uses the [MIT License](LICENSE). Dependencies retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md). This project is not affiliated with the game's developer or publisher.

## Connection and tool switching

When upgrading from an older release for the first time, close the old tools and restart the game once. These updated tools can then update and switch within the same game process: pending game operations finish before control changes. Settings and records are retained. Live communication uses local named pipes. Modules used by the daily workflow are coordinated separately by its scheduler.
