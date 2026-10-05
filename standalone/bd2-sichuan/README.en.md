# BD2 Sichuan

> **Free & open source:** Official releases are provided free by GitHub **MadestSamurai** · Bilibili **MadSamurai**. [Official downloads](https://github.com/MadestSamurai/bd2-sichuan/releases) · [Source and risk notice](DISTRIBUTION.md#english). Third-party fees do not imply the author’s involvement, endorsement or support.
>
> **Risk notice:** This is an unofficial community tool. Use may result in account penalties, bans, game errors or data loss. Follow the game rules and accept responsibility for the risks of use. The MIT license remains unchanged.

English · [简体中文](README.md)

[Download latest release](https://github.com/MadestSamurai/bd2-sichuan/releases/latest) · [Report an issue](https://github.com/MadestSamurai/bd2-sichuan/issues)

A standalone tile-matching assistant for the BrownDust II Windows client. Reads the live tile board, suggests legal pairs, and plays a single pair or the current round.

## Download

Current version: **0.3.3**. Both editions have the same features and include Simplified Chinese / English.

| Edition | Runtime requirement | Recommended for |
| --- | --- | --- |
| **Portable** | .NET included | Most users; download and run |
| **Lite** | [.NET Desktop Runtime 8 x64](https://dotnet.microsoft.com/download/dotnet/8.0) | Smaller download if the runtime is installed |

Download one edition: the EXE runs on its own; ZIPs include both READMEs and licenses. No Python, development SDK or other BD2 tools are required. Lite needs the **Desktop Runtime**, not just .NET Runtime or ASP.NET Runtime. Verify downloads against `SHA256SUMS.txt`.

## Quick start

**Before upgrading:** stop and close the old assistant. Follow the connection and tool switching section below; current components support same-process handoff.

1. Open the actual tile-matching board.
2. Open the assistant, click **Connect game**, and wait for the board and hints. No manual pair is required first.
3. Click **Play next pair** or **Auto-play round**. Change the pair interval with **Apply interval** or Enter.
4. Click **Stop now** or close the window to stop. Reopening does not automatically resume execution.

## Features and settings

- **Pair interval:** default **1000 ms**, adjustable from **50 to 60000 ms**. Click **Apply interval** or press Enter. It can be changed during a run and is saved for next time.
- **Hints:** show a legal next pair first, then try to find a full route. Hints update after manual changes, combo rewards and shuffles. A complete static route is not a guarantee that the game will leave the board unchanged.
- **Auto-play round:** confirms each pair using client events and board readback, waits through pauses, shuffles and input locks, and stops when the round ends. It does not start the next level or spend items.
- **Always on top:** keeps the assistant visible over other windows.

The interval is measured between the start of consecutive pairs, not between the two clicks in one pair. Game frame rate and input locks still apply.

## Language

Use **语言 / Language** in the top bar to switch between Simplified Chinese and English. The first launch uses Chinese on Chinese systems and English otherwise, then remembers your choice. Switching does not restart automation or change settings. Game-provided names and images keep their game language; raw diagnostics remain unchanged.

See [translation maintenance](docs/LOCALIZATION.md).

## Compatibility and limits

Supports the official Windows x64 PC client, one game process at a time, with the same privilege level as the game. Mobile and Android emulator clients are not supported. First connection resolves local interfaces and builds the component, which may take a few seconds. Uncertain interface matches stop connection with a diagnostic; adaptation does not guarantee every future update will work without maintenance.

Releases contain no game DLLs, resources, account inventories or private captures. Automates the current round only; it does not start the next level or spend items.

## Diagnostics and feedback

Settings and diagnostics are under `%LOCALAPPDATA%\BD2Sichuan`; board files are in its `sichuan` subfolder.

- **Waiting for board:** connect and open the actual minigame; menus and loading screens have no playable board.
- **Another component is loaded:** stop the other tool and reconnect; see the legacy migration requirements below.
- **Compatibility check fails:** use the latest release and report the error plus a redacted `compatibility.json`.
- **Missing icons:** images are only visual aids; pairing uses the logical board.

When reporting an issue, include the version, visible message and relevant log excerpts. Remove account information and personal paths first. Do not upload game DLLs, complete inventories or connection credentials.

## Development and contributions

Requires Windows x64, PowerShell and the .NET 8 SDK. Normal builds and regression tests do not need or connect to the game.

```powershell
.\build.ps1 -Locked
.\package.ps1 -Locked
```

Assets are written to `dist/v<version>/`. Packaging checks both runtime configurations and runs UI checks.

[Development and release workflow](docs/RELEASING.md) · [Documentation and release format](docs/PUBLICATION_STYLE.md) · [Current release notes](docs/RELEASE_NOTES.md)

## License

Project code is [MIT licensed](LICENSE). Dependencies retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md). This project is not affiliated with the game developer or publisher.

## Connection and tool switching

When upgrading from an older release for the first time, close the old tools and restart the game once. These updated tools can then update and switch within the same game process: pending game operations finish before control changes. Settings and records are retained. Live communication uses local named pipes. Modules used by the daily workflow are coordinated separately by its scheduler.
