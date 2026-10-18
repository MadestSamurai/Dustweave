# Equipment Assistant · Dustweave

English · [简体中文](README.md) · [Back to Dustweave](../../README.en.md)

Equipment Assistant is a built-in Dustweave module for N-grade powder planning and batch refinement. Its original standalone repository is archived. Updates, downloads and issue reports now belong in [Dustweave](https://github.com/MadestSamurai/Dustweave); this module has no separate release.

## Use

Connect the game in Dustweave, then open **Tools → Equipment Assistant**. The window displays loading progress before preparing the connection and reading inventory. Dustweave manages language, appearance and automation ownership.

- **Powder:** set enhancement level, quantity and budgets, optionally including 5-pill N-grade recipes. The main action first calculates the plan, then opens execution confirmation. Review gold, materials, Ability Pills and expected powder before proceeding. Planning uses available inventory and does not buy Ability Pills.
- **Batch refinement:** search, filter and select exact equipment instances, then choose a target and resource budgets. The default mode stops at the target. Alternatively, set the game's target to 24 and check your chosen target after each batch. Refinement is random; a budget does not guarantee success.
- **Stop:** prevent further batches while awaiting and reconciling an already-submitted batch. Account, inventory or price changes require a fresh read and plan. Consuming operations with unknown outcomes are not automatically resent.

Operation and error logs use Dustweave's diagnostics directory. Export them through **Diagnostics → Export logs and get help**; the host manages storage cleanup. Existing settings, connection identities and historical data paths remain compatible.

## Source layout

| Directory | Responsibility |
| --- | --- |
| [desktop](desktop) | WPF tool window, inventory filters and operation entry points |
| [core](core) | Powder planning, refinement execution, cost and receipt checks |
| [connection](connection) · [compatibility](compatibility) | Installed-client adaptation, connection and data reads |
| [hook](hook) · [ipc](ipc) · [shared](shared) | In-game operations, shared transport and data contracts |
| [localization](localization) · [vendor](vendor) | Localization and dependencies with retained licenses |

The three `Dustweave.Equipment.*.csproj` projects belong to the root `Dustweave.slnx`. Existing assembly names and connection contracts are retained for hosted loading, WPF resources and dependency locks; they do not imply separate releases.

Use `build.ps1`, `test.ps1` and `package.ps1` from the Dustweave repository root. Do not follow the archived standalone build instructions or download a separate EXE. See the [development guide](../../docs/DEVELOPMENT.md) and [architecture](../../docs/ARCHITECTURE.md).

The module retains MadestSamurai's original attribution and [MIT license](LICENSE). Dependencies are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
