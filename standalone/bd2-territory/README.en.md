# BD2 Territory

> **Free & open source:** Official releases are provided free by GitHub **MadestSamurai** · Bilibili **MadSamurai**. [Official downloads](https://github.com/MadestSamurai/bd2-territory/releases) · [Source and risk notice](DISTRIBUTION.md#english). Third-party fees do not imply the author’s involvement, endorsement or support.
>
> **Risk notice:** This is an unofficial community tool. Use may result in account penalties, bans, game errors or data loss. Follow the game rules and accept responsibility for the risks of use. The MIT license remains unchanged.

English · [简体中文](README.md)

[Download releases](https://github.com/MadestSamurai/bd2-territory/releases) · [Report an issue](https://github.com/MadestSamurai/bd2-territory/issues)

A standalone Fantasia Territory assistant for BrownDust II on Windows. Automate mining, logging, recipe-based planting and harvesting, with optional cooking and layout preview, purchasing and import.

## Sell surplus stock

Under **Surplus stock sales**, choose the quantity to keep per item (100–9900, default 9900), then start automation. Capacity protection is always enabled: only the excess is sold, and stock exactly at the threshold is retained. Selling can run on its own.

Only territory materials, produce and meals explicitly listed by the game's territory shop are eligible. Locked items, buildings, decorations, equipment and attribute stones are excluded. Each batch waits for confirmation; the UI shows confirmed sales and territory currency earned. Harvest the current crop batch and replant before mining or logging. Full inventory or a gathering result with no new rewards does not stop gathering.

If a sale confirmation is missing, the tool waits for game requests to finish, refreshes territory stock and currency from the server, and resumes planning from current stock. The old record is preserved and never replayed. Manually clearing inventory no longer requires deleting records. Unconfirmed sales are not added to confirmed totals.

## Download

Current stable version: **0.4.3**. One application includes Simplified Chinese and English, switchable from the top-right corner.

Connection freeze fix: state and diagnostic reads run in the background, polling pauses while connecting, and pause/close no longer wait on the pipe lock. `connection.log` is available from the start of each attempt.


| Edition | Runtime | Recommended for |
| --- | --- | --- |
| **Portable** | Includes .NET; no separate installation | Most users |
| **Lite** | Requires [.NET Desktop Runtime 8 x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) | Users with the desktop runtime who prefer a smaller download |

Windows x64 only. Both editions have the same features. Each EXE works on its own; ZIP files include bilingual documentation and licenses. Verify downloads with `SHA256SUMS.txt`. No Python, .NET SDK or other BD2 tools are required. Lite needs **Desktop Runtime**, not just the regular .NET Runtime.

## Quick start

1. Start the game and enter your own Fantasia Territory. Keep one game instance and use the same Windows privileges for both programs.
2. Select **Connect / update** and wait for “Territory detected”.
3. Choose activities and a recipe. The list shows **actual dish names and ingredient ratios**; unavailable recipes explain why they are locked.
4. Set the planting budget and select **Start automation**. Defaults: budget 1400, interval 500ms. A budget of 0 is unlimited.
5. Select **Pause** or close the tool to stop further operations. Submitted actions still finish normally.

Pause and close the previous tool, then open this version and connect. Standalone tools that support component handoff can switch within the same game process; older components without handoff support may require one normal game restart. Opening the EXE does not connect or start automation.

## Features and settings

| Setting / feature | Behavior |
| --- | --- |
| Mining and logging | Gather only mature resources; failed targets remain in a retry queue |
| Farming | Plant actual native preview groups, including smaller or disconnected fields; balance stock and expected growing yield against recipe requirements |
| Recipes | Read current names, ingredients, seeds and unlock status; changes wait for the current planting batch |
| Default recipe | Energizing Gnocchi, using a 5:3:2 ratio; the displayed name follows the game translation |
| Automatic cooking | Off by default. Uses existing ingredients for the selected dish. Batch limit: 1–1000, default 100. Other activities continue when ingredients or output capacity are insufficient |
| Movement | A* grid routing by default; predicts traversal using the game controller’s collision, step and slope settings, then uses directional movement. Vehicles and recovery dashes remain available |
| Use game NavMesh navigation | Optional and off by default. Turning off switches to A* immediately; turning on applies to the next route. The preference is saved |
| Action interval | 100–60000ms, default 500; still waits for animations, loading and server responses |
| Planting budget | Resets on each new run. Gathering continues when spent. Only planting uses this budget; cooking consumes existing ingredients |
| Layouts | Farm, mining and logging templates or JSON import. Preview placement and cost, then explicitly purchase and import |

New installs and upgrades without a saved navigation preference default to NavMesh off. If A* cannot find a route, the target remains queued for retry; NavMesh is never enabled automatically. Water is impassable except through existing bridges; detours, movement trials and recovery dashes all follow this rule. NPCs and workers no longer receive synthetic obstacle footprints. Prediction uses effective game collisions; uncertain routes can be tested by ordinary movement and are temporarily excluded only after 3 seconds without progress.

Cooking does not buy ingredients, sell items or switch dishes. Unknown results keep their journal and pause further operations to avoid duplicate consumption. Check inventory and diagnostics; do not delete records to blindly retry.

## Fixed crop planting and navigation

Under **Planting and cooking**, choose **Balance a recipe** or **Plant a fixed crop**. Connect to load crop names and unlock status, then choose the crop to keep planting in actual native batches. Changes wait for the current planting transaction to finish; budgets and batch progress are preserved. Automatic cooking has its own recipe selection and remains optional.

A* refines narrow passages when the coarse grid fails. Ground checks and discovered routes are cached and warmed incrementally while idle; upcoming edges are checked live. Scene, layout, character and local resource changes invalidate the relevant cache. NavMesh remains optional and off by default.

## Separate gathering and planting passes

The tool first finishes the mature, enabled and currently eligible resources captured at the start of a pass. It then plants the available field groups consecutively, followed by enabled cooking and sales. A few newly empty cells no longer interrupt harvesting. Crops that mature and resources that respawn during planting wait for the next pass.

Each gathering pass has a finite set of targets, so continuous respawns cannot postpone planting indefinitely. Unreachable targets keep their existing cooldown and retry policy without blocking the whole farm. Gathering continues when the planting budget is exhausted. The window shows the current phase and remaining targets.

Planting still uses actual native preview groups: no 100-field or connected-layout requirement. Recipe balancing, fixed crops, spending and receipt checks are preserved. When automatic surplus sales are enabled, excess inventory can still be processed between completed actions, trying enabled cooking first. This preserves the current pass instead of restarting gathering or opening planting early.

## Disconnected fields and recipe balance

No layout change or 100-field farm is required. The tool visits available field areas and confirms each actual native batch before moving on. Smaller groups and single isolated fields are supported. If the game previews several areas together, they are submitted together.

Each native batch uses one crop to reduce interactions. Recipe balance uses inventory plus the expected yield of growing crops. A 5:3:2 ratio targets ingredient output over repeated batches, not an exact split within every field group. Stock, growth times and yield affect the next crop. Charges use the actual field count; unaffordable groups are deferred while other eligible fields and gathering continue. Fixed-crop planting also supports scattered fields.

Existing completed or pending planting journals are retained on upgrade. Offline regressions and client interface checks cover this change; continuous planting on scattered fields still needs live verification.

## Layout tools

Pause automation and open **Layouts**. Choose an area and template or load a file. Select **Preview layout and cost**, review the quote, then **Purchase and import**.

Existing facilities are reused and moved first; only missing items are purchased. Decorations use normal game prices, buildings use owned materials, and crafted furniture must already be owned. Other buildings, workers and crops are preserved. No land purchases or demolition. Clear occupied destinations first. A backup is saved before import; each placement waits for confirmation.

**Export current territory** saves editable facility positions without account or worker identities. Layouts require the same terrain and unlocked areas.

## Language

First launch follows the system language. The top-right switch is saved separately and does not start, stop or reconfigure automation. Dish, crop and facility names follow the game language. Raw logs and low-level exceptions retain their original text.

## Compatibility and limits

The tool reads local client interfaces and generates an adapter when connecting, without locking to a release-date client version. Incompatible changes stop connection; compatibility with every future version is not guaranteed.

No game DLLs, data tables, images, account data or private layouts are included. Building requires no game installation. Connecting requires the official Windows client, not an Android emulator.

## Diagnostics and feedback

Open **Connection and diagnostics**. Local data is stored in `%LOCALAPPDATA%\BD2Territory` and is not uploaded. It includes settings, account progress, cooking journals, layout backups and obstacle snapshots.

Report the version, error text and reproduction steps. Share relevant logs only, never credentials or complete game resources. Keep the first connection error instead of repeatedly reconnecting.

## Development and contributions

Requires Windows, PowerShell and .NET 8 SDK. From this repository root:

```powershell
.\build.ps1 -Locked
.\package.ps1 -Locked
```

Packages go to `dist/v0.4.3/`. [Development](docs/DEVELOPMENT.md) · [Localization](docs/LOCALIZATION.md) · [Publication style](docs/PUBLICATION_STYLE.md) · [Release notes](docs/RELEASE_NOTES.md)

## License

Project code is licensed under [MIT](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and `licenses/` for dependencies. Game content belongs to its respective rights holders.

## Connection and tool switching

When upgrading from an older release for the first time, close the old tools and restart the game once. These updated tools can then update and switch within the same game process: pending game operations finish before control changes. Settings and records are retained. Live communication uses local named pipes. Modules used by the daily workflow are coordinated separately by its scheduler.

Excess territory stock protection is always on; only the retained quantity is configurable (100–9900). Harvest a crop batch and replant before mining or logging.
