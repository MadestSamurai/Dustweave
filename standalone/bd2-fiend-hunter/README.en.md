# BD2 Fiend Hunter

> **Free and open source:** Official releases are provided free by GitHub **MadestSamurai** · Bilibili **MadSamurai**. [Official downloads](https://github.com/MadestSamurai/bd2-fiend-hunter/releases) · [Source and risk notice](DISTRIBUTION.md). Third-party charges do not imply the author's involvement, endorsement or support.
>
> **Risk notice:** This tool is not affiliated with the game. Use may result in account penalties, bans, game errors or data loss. Follow the game rules and use at your own risk. The MIT license is unchanged.

English · [简体中文](README.md)

[Latest release](https://github.com/MadestSamurai/bd2-fiend-hunter/releases/latest) · [Report an issue](https://github.com/MadestSamurai/bd2-fiend-hunter/issues)

A standalone Fiend Hunter assistant for the BrownDust II Windows client, using Blade to challenge solo Very Hard Berserker automatically.

## Download

Current version: **0.1.2**. Both editions have the same features and built-in Chinese/English switching.

| Edition | Runtime requirement | Recommended for |
| --- | --- | --- |
| **Portable** | .NET runtime included | First-time users; ready to run |
| **Lite** | [.NET Desktop Runtime 8 x64](https://dotnet.microsoft.com/download/dotnet/8.0) required | Users who already have the runtime; smaller download |

Download one edition. The EXE runs independently; the ZIP also includes bilingual documentation and licenses. No Python, development SDK or other BD2 tool is required. Lite needs the **Desktop Runtime**, not the base .NET or ASP.NET runtime. Verify downloads using `SHA256SUMS.txt`.

## Quick start

1. Open the official Windows game client and enter the Fiend Hunter lobby.
2. Open the assistant and click **Start round**. Allow a few seconds for the first connection.
3. The tool selects Blade and solo Very Hard Berserker, fights, exits settlement and stops. It can also resume an active solo round with the same setup.
4. Click **Pause** to stop further inputs. This does not pause the game's timer.

Close the old tool before updating. Compatible versions support component updates and control handoff in the same game process. A one-time game restart may be needed when replacing older tools that do not support handoff.

## Features and settings

- Approaches the boss, chains normal attacks, uses the special skill and heals.
- Times guards, counters and dodges while retaining the game's stamina and skill rules.
- Shows both health bars, distinguishes victory from defeat, and stops after settlement.
- Currently supports **Blade against solo Very Hard Berserker**. Other setups and multiplayer are not controlled.

Results depend on game updates, frame rate and latency. A clear is not guaranteed on every run.

## Language

Select Chinese or English at the top right. The first launch follows the system language; later launches remember your choice. Switching language does not restart an active task. Original game prompts and diagnostic details retain their original text.

See [localization notes](docs/LOCALIZATION.md) for translation maintenance.

## Compatibility and limits

Supports the official Windows x64 PC client and one game process at a time. Run the tool and game with matching permissions. Mobile and Android emulator clients are not supported.

At connection time, the tool reads local interfaces and adapts recognized renamed members. It is not locked to the client version available at release time. Uncertain matches stop the connection with diagnostics; future updates may still require maintenance. Releases include no game DLLs, assets or account data.

## Diagnostics and feedback

Click **Diagnostics** to open `%LOCALAPPDATA%\BD2FiendHunter`.

| File | Purpose |
| --- | --- |
| `events.jsonl` / `events-previous.jsonl` | Recent actions, results and background recovery records |
| `compatibility.json` | Local interface adaptation result |
| `desktop-error.txt` | Most recent operation error |
| `preferences.json` | Language preference |

Include the version, message and relevant log excerpt when reporting an issue. Remove account information and personal paths first. Do not upload game DLLs, complete inventories or connection credentials.

## Development and contributions

Requires Windows x64, PowerShell and .NET 8 SDK. Normal builds and tests require no game installation and do not connect to the game.

```powershell
.\build.ps1 -Locked
.\package.ps1 -Locked
```

Assets are written to `dist/vVERSION/`. Version tags trigger GitHub Actions to build, verify and publish both editions.

[Development and release process](docs/DEVELOPMENT.md) · [Publication style](docs/PUBLICATION_STYLE.md) · [Current release notes](docs/RELEASE_NOTES.md)

## License

Project code is licensed under [MIT](LICENSE). Dependencies retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md). This project is not affiliated with the game developer or publisher.
