# Getting started with Dustweave

[简体中文](GETTING_STARTED.md) · [Home](../README.en.md)

## Requirements

- Windows x64 with a working native BrownDust II PC client and a normal game login.
- A complete Dustweave package. Portable includes the runtime; Lite requires .NET 8 Desktop Runtime x64.
- Source is MIT licensed: [Dustweave](https://github.com/MadestSamurai/Dustweave). First installations and manual upgrades use complete packages from [GitHub Releases](https://github.com/MadestSamurai/Dustweave/releases). The domestic route is reserved for in-app OTA. GitHub's Source code archive is not an application package.

Run `Dustweave.exe`. Keep the accompanying `data`, `flows`, `connection` and other package directories. A source-build output folder is for development and is not a complete distribution.

## First run

Use **Language** at the top right of setup to choose **简体中文 / 繁體中文 / English** before starting. The choice applies throughout the app and is remembered for future launches. Switching keeps your current step and account label.

1. **Open Getting started and check the game location.** Setup appears automatically when no account has been saved, and can be reopened from **Accounts → Getting started**. Select the game's `BrownDust II.exe`, then continue to try one launch. An already running game is verified directly.
2. **Enter the game and wait for detection.** Sign in and enter the main menu. Dustweave connects and checks your actual character identity, then displays the detected name.
3. **Exit normally, then save.** Close the game and launcher, enter an account label and save. This captures the latest sign-in state after normal exit. A changed account must be verified again.
4. **Take the page tour.** Highlights introduce tasks, settings, tools, accounts, schedules, plugins, diagnostics and updates. The tour does not execute tasks.
5. **Launch through Dustweave from now on.** Use **Play** in Accounts instead of the desktop game shortcut or an accelerator's launch button; enable only its acceleration service if needed.
6. Configure each account and test a few tasks before enabling multiple accounts or schedules.

Setup can be skipped after a reminder about unfinished configuration. Skipping never closes the game or clears a sign-in. Existing saved accounts are not forced through setup after an upgrade. Change the shared location later in **Daily settings → Game location**; it applies to the next launch.

Start with a few familiar stages. Locked content, changing events and unknown dialogs may need human attention; unattended completion is not guaranteed in every situation.

## Tools and appearance

Open fishing, territory or minigames from the tools page. An open window is not the same as active automation. The host coordinates control while a tool is running or settling an operation. Finish its active operation before starting another task.

Choose Simplified Chinese, Traditional Chinese or English in the main window; integrated tools follow that choice. Light, dark and system themes are available. Documentation screenshots use demonstration data, not real account results.

## Stop and rerun

Use the application's stop control and check whether an already submitted operation is still settling. To rerun, select the affected stages and verify the current account and game state. Do not repeatedly confirm an unknown resource-consuming action merely because an earlier record is incomplete.

If the Windows desktop itself is elevated, Dustweave uses the caller's existing token within the same user and logon session without requiring system setting changes. A normal desktop is still preferred for unelevated launches; connection helpers request elevation separately when needed.

## Updates and local data

Users of the first public release, 1.0.0-beta, and later versions can open Updates at the bottom left. Signed differential packages download in the background and require confirmation to restart while idle. Accounts, settings and plugins are preserved; failed updates restore the previous version. Earlier internal versions are no longer maintained for upgrade compatibility; extract a complete package into a new folder after exiting the old app. Ordinary UI updates do not require restarting the game; follow specific component instructions if shown.

Daily data defaults to `%LOCALAPPDATA%\BD2DailyAssistant`; protected account sessions use `%LOCALAPPDATA%\BD2AccountSessionManager`. An explicit custom data directory overrides the default. Changing the application folder does not automatically erase this data.

Account sessions are protected for the current Windows user. They are not ordinary portable configuration files: do not assume copying them to another computer will work, and never upload them with an issue.

If an older updater reports an incompatible plugin when none is installed, manually download the full 1.0.7-beta or later package, close the old app and extract into a new folder. No plugin is required; existing accounts and settings are retained.

## Troubleshooting

| Symptom | Check first |
| --- | --- |
| Lite asks for a runtime | Install **.NET 8 Desktop Runtime x64**, or use Portable |
| Missing data or components | Extract the complete matching package rather than copying just its EXE |
| Cannot connect | Check that the game is running, whether its client just updated, and the tool's exact preparation/permission error |
| Another task owns the connection | Stop that tool's active automation and wait for settlement before reconnecting |
| A stage waits or stops | Record the version, stage, time, last message and current game screen; verify the account and pending dialogs |
| An optional feature is not shown | Only available features are shown; check plugin version and installation location |

Do not treat administrator mode or restarting the game as universal fixes. A game running with elevated privileges may require corresponding connection privileges; follow the specific diagnostic message.

If the issue persists, [open an issue](https://github.com/MadestSamurai/Dustweave/issues/new/choose) with your tool/Windows versions, stage, expected and actual results, and reproduction steps. Attach only relevant logs after checking for personal information. Never upload whole account directories or captured user libraries. See [SECURITY.md](../SECURITY.md) for security reports.

Upgrading from an earlier version: extract the complete package into a new folder and launch `Dustweave.exe`. Existing accounts, settings and history are reused without data migration. Keep old and new program files in separate folders.
