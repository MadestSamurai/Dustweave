# Infinite Gacha · Dustweave

[简体中文](README.md) · English · [Back to Dustweave](../../README.en.md)

Infinite Gacha is maintained as a built-in Dustweave module. The original standalone repository is archived. Use [Dustweave](https://github.com/MadestSamurai/Dustweave) for current source, downloads and issues; no separate tool installation is required.

## Use

Open **Tools → Infinite Gacha** in Dustweave. The window appears before shared connection preparation, showing progress with cancel, close and retry actions. Once connected, configure the A/B targets and stop rules, then choose “Start rerolling”. Opening the window never starts rolling.

- **Targets:** the existing A/B lists, search, enhancement filters, Ctrl/Shift selection, double-click transfers and per-account/per-pool preferences are preserved.
- **Stop rules:** A is checked first, followed by enabled A/B combinations. Duplicate copies can count individually, with optional exclusion of copies beyond +5. A match stops rerolling; the user confirms the retained result in the game.
- **Alerts:** Windows notifications, popup and sound remain independently selectable. Testing alerts does not start rolling.
- **Connection and presentation:** uses Dustweave's game identity, control ownership, language and theme. Preparation, automation and settlement exclude competing automation; an idle connected window releases control.

Existing preferences remain in `%LOCALAPPDATA%/BD2InfiniteGacha`, with unchanged account and pool keys. This source migration does not move user data. The window does not poll an old module during preparation or discover a different game process.

## Source layout

| Directory | Responsibility |
| --- | --- |
| [desktop](desktop) | A/B configuration, results and notifications |
| [core](core) · [shared](shared) | Client controller, rules, preferences and state machine |
| [compatibility](compatibility) · [hook](hook) · [ipc](ipc) | Client adaptation, game actions and shared communication |
| [localization](localization) · [vendor](vendor) | Translation resources and dependencies |

The three `Dustweave.InfiniteGacha.*.csproj` projects belong to the root `Dustweave.slnx`. Existing assembly names, namespaces, IPC and dependency identities are retained. Launching, preparation, ownership and presentation use the shared host under [`src/Desktop`](../Desktop).

Build from the repository root with `build.ps1`, `test.ps1` and `package.ps1`. Focused regressions: `UnifiedSuite`, `ToolMenu`, `ActivityLease`. UI checks use the `tool-preparation` scope and `--tool infinite-gacha --smoke <isolated-output>`, with synthetic data only.

Original attribution to MadestSamurai and the [MIT License](LICENSE) are retained. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependencies.
