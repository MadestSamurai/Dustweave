# Getting started with Dustweave

[简体中文](GETTING_STARTED.md) · [Home](../README.en.md)

## Requirements

- Windows x64 with a working native BrownDust II PC client and a normal game login.
- A complete Dustweave package. Portable includes the runtime; Lite requires .NET 8 Desktop Runtime x64.
- The repository remains private. Users with access can download complete packages from Releases. GitHub's Source code archive is not an application package.

Run `Dustweave.exe`. Keep the accompanying `data`, `flows`, `connection` and other package directories. A source-build output folder is for development and is not a complete distribution.

## First run

1. Enter the game normally. Save the current account in account management and verify its identity.
2. Enable the desired stages in daily settings. Review hunting, Mirror multipliers, equipment budgets and other relevant options.
3. Select this run's stages on the task page. Temporary run selection is separate from the account's saved preferences.
4. Start the selected stages and follow completion, skipped work or items needing attention on the timeline.
5. Before using multiple accounts, save and verify each account and its settings, then run the selected queue.

Start with a few familiar stages. Locked content, changing events and unknown dialogs may need human attention; unattended completion is not guaranteed in every situation.

## Tools and appearance

Open fishing, territory or minigames from the tools page. An open window is not the same as active automation. The host coordinates control while a tool is running or settling an operation. Finish its active operation before starting another task.

Choose Simplified Chinese, Traditional Chinese or English in the main window; integrated tools follow that choice. Light, dark and system themes are available. Documentation screenshots use demonstration data, not real account results.

## Stop and rerun

Use the application's stop control and check whether an already submitted operation is still settling. To rerun, select the affected stages and verify the current account and game state. Do not repeatedly confirm an unknown resource-consuming action merely because an earlier record is incomplete.

## Updates and local data

An online updater is not included yet. Stop the queue, exit the old tool normally, extract the new complete package into a new folder and launch it. Ordinary UI updates do not require restarting the game; follow specific component instructions if shown.

Daily data defaults to `%LOCALAPPDATA%\BD2DailyAssistant`; protected account sessions use `%LOCALAPPDATA%\BD2AccountSessionManager`. An explicit custom data directory overrides the default. Changing the application folder does not automatically erase this data.

Account sessions are protected for the current Windows user. They are not ordinary portable configuration files: do not assume copying them to another computer will work, and never upload them with an issue.

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
