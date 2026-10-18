# Apostle Defense · Dustweave

English · [简体中文](README.md) · [Back to Dustweave](../../README.en.md)

Apostle Defense is now a built-in Dustweave module. Its standalone repository is archived and no longer maintained. Current source, downloads and issue reports belong in [Dustweave](https://github.com/MadestSamurai/Dustweave); no separate tool installation is needed.

## Use

Open **Tools → Apostle Defense**. The window appears before background connection preparation, with stage text, a loading animation and cancellation. Closing also cancels preparation. Connecting does not start gameplay: review your goals, then choose Start. A failed connection can be retried in the same window.

- **Decisions:** an offline-annealed policy handles summoning, merging, attribute investment and placement, with stage-based restart rules. Random outcomes and character conditions still affect results; a clear is not guaranteed.
- **Goals:** choose wave 50 and rare-character goals, automatic continuation or stopping after the current round. Opening the window or changing language/theme never enables automation.
- **Ownership:** preparation, automation and pending settlement exclude daily tasks and competing tools. An idle connected window releases ownership. Pause stops further decisions while an already-submitted action settles.

The tool follows Dustweave's language and light/dark theme. Settings live at `tools/apostle-defense/settings.json` under the current Dustweave data directory; valid standalone preferences can be imported on first use. Logs use `live/diagnostics/apostle-defense`, with host-managed export and cleanup.

## Source layout

| Directory | Responsibility |
| --- | --- |
| [planner](planner) | Annealed decisions, stage restarts, investment and placement |
| [core](core) | Controller, connection client and recovery |
| [shared](shared) | Data contracts, round flow and settlement rules |
| [compatibility](compatibility) · [hook](hook) · [ipc](ipc) | Installed-client adaptation, in-game operations and shared transport |
| [desktop](desktop) | Board visualization and retained compatibility entry point |
| [localization](localization) · [vendor](vendor) | Localization and dependencies with retained licenses |

Dustweave's actual tool window and loading/session management are in [`src/Desktop`](../Desktop): `ApostleToolWindow`, `ApostleToolPanel` and `ApostleToolSession`. They reuse this module's controller and board control.

The three `Dustweave.ApostleDefense.*.csproj` projects belong to the root `Dustweave.slnx`. Original assembly names, namespaces, connection identifiers and compatible settings paths remain unchanged. The displayed product version follows Dustweave.

Use the root `build.ps1`, `test.ps1` and `package.ps1`. Targeted test groups are `ApostleIntegrated`, `ToolMenu`, `ActivityLease`, `ApostleAnnealing`, `ApostlePerformance` and `ApostleTimeToClear`; desktop checks use the `apostle` and `tool-preparation` scopes. See [architecture](../../docs/ARCHITECTURE.md) and the [development guide](../../docs/DEVELOPMENT.md).

The module retains MadestSamurai's original attribution and [MIT license](LICENSE). Dependencies are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
