# BD2 Rhythm

> **Free & open source:** Official releases are provided free by GitHub **MadestSamurai** · Bilibili **MadSamurai**. [Official downloads](https://github.com/MadestSamurai/bd2-rhythm/releases) · [Source and risk notice](DISTRIBUTION.md#english). Third-party fees do not imply the author’s involvement, endorsement or support.
>
> **Risk notice:** This is an unofficial community tool. Use may result in account penalties, bans, game errors or data loss. Follow the game rules and accept responsibility for the risks of use. The MIT license remains unchanged.

English · [简体中文](README.md)

[Download latest](https://github.com/MadestSamurai/bd2-rhythm/releases/latest) · [Report an issue](https://github.com/MadestSamurai/bd2-rhythm/issues)

A standalone rhythm-game assistant for BrownDust II on Windows. It reads the current chart when a song starts and plays automatically, with adjustable random jitter and timing calibration.

## Download

Source version: **0.2.2**. Both Simplified Chinese and English are built into the same app; switch in the upper-right corner.

| Edition | Runtime requirement | Recommended for |
| --- | --- | --- |
| **Portable** | Includes .NET; no separate runtime installation | Most users |
| **Lite** | Requires [.NET Desktop Runtime 8 x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) | Users who already have the desktop runtime and want a smaller download |

Windows x64 only. Both editions have the same features. The EXE works on its own; the ZIP includes bilingual documentation, licenses and maintenance notes. Use `SHA256SUMS.txt` to verify downloads. Running the app does not require Python, the .NET SDK or another BD2 tool. Lite needs the **Desktop Runtime**, not just the base .NET or ASP.NET runtime.

## Quick start

1. Launch the app. For upgrades, stop and close the old app and follow the connection and tool switching section.
2. Start the Windows version of BrownDust II. Keep one game instance open and run the app with the same privileges as the game.
3. Click **Connect**, wait for confirmation, then click **Enable auto-play**.
4. Select a song and difficulty in the game and start it. The app displays the song, progress, played inputs and skipped notes.
5. The app follows game pauses. Click **Stop** or close the window to end automatic input.

Random jitter defaults to **±50 ms**. Set it to `0` to disable jitter. Settings changes apply to the next song.

## Features and settings

| Setting / feature | Behavior |
| --- | --- |
| Random jitter | Sampled independently per note; ±0–200 ms, default ±50 ms. Close notes in the same lane constrain jitter to preserve the press/release sequence |
| Timing calibration | -500–500 ms, default 0. Positive values delay input; negative values advance it |
| Chart reading | Reads the currently loaded Normal/Hard chart from the game. No exported chart files are needed; new songs work when they use supported chart structures |
| Inputs | Tap, hold, slide, multi-tap and Fever |
| Pause / retry | Pauses preserve the timeline; restarting a song creates a new input plan |
| Enabling mid-song | Past notes are skipped instead of replaying stale inputs |
| Stopping | Ends automatic input and releases held notes. Holds during a game pause are released when the game resumes |

The app calls the game's existing input callbacks. It does not modify scores, health, charts or the song clock. It does not select songs, buy attempts, dismiss results or start another round. Jitter and frame rate affect timing judgments; a full combo or perfect score is not guaranteed.

## Language

The first launch follows the system language: Simplified Chinese on Chinese systems, English elsewhere. Your selection is saved separately. Switching language does not stop playback, change timing settings or reset the song. Song asset names, low-level exceptions and diagnostic evidence keep their original text.

## Compatibility and limits

Supports the official Windows x64 client, not Android emulators. At connection time, the app reads local game interfaces and builds an adapted component instead of requiring a release-specific client version. Incompatible interfaces, note types or chart structures stop playback with an explanation. Compatibility with every future update cannot be guaranteed.

No game DLLs, charts, audio, images, account information or private captures are included. Source builds work without installing the game; connecting requires a locally installed and running game.

## Diagnostics and feedback

Click **Diagnostics**, or open `%LOCALAPPDATA%\BD2Rhythm`. Settings, connection records and logs stay on your computer and are not uploaded.

Include the app version, Windows version, game version, error text and reproduction steps in an issue. Share only relevant logs when needed, without game resources, credentials or private data.

If another tool holds control, stop it and reconnect. Legacy components may require one restart as described below. Preserve the original connection error for diagnostics.

## Development and contributions

Requires Windows, PowerShell and the .NET 8 SDK. From the repository root:

```powershell
.\build.ps1 -Locked
.\package.ps1 -Locked
```

Building and running synthetic-chart tests do not require a game installation, game account or private repository data. Release assets appear in `dist/v0.2.2/`. Optional local-client checks are described in [Development](docs/DEVELOPMENT.md).

[Localization](docs/LOCALIZATION.md) · [Publication style](docs/PUBLICATION_STYLE.md) · [Release notes](docs/RELEASE_NOTES.md)

## License

Project code is licensed under the [MIT License](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and `licenses/` for dependencies. The game and its content belong to their respective owners.

## Connection and tool switching

When upgrading from an older release for the first time, close the old tools and restart the game once. These updated tools can then update and switch within the same game process: pending game operations finish before control changes. Settings and records are retained. Live communication uses local named pipes. Modules used by the daily workflow are coordinated separately by its scheduler.
