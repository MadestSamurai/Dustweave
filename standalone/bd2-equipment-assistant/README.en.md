# BD2 Equipment Assistant

> **Free & open source:** Official releases are provided free by GitHub **MadestSamurai** · Bilibili **MadSamurai**. [Official downloads](https://github.com/MadestSamurai/bd2-equipment-assistant/releases) · [Source and risk notice](DISTRIBUTION.md#english). Third-party fees do not imply the author’s involvement, endorsement or support.
>
> **Risk notice:** This is an unofficial community tool. Use may result in account penalties, bans, game errors or data loss. Follow the game rules and accept responsibility for the risks of use. The MIT license remains unchanged.

English · [简体中文](README.md)

[Download releases](https://github.com/MadestSamurai/bd2-equipment-assistant/releases) · [Report an issue](https://github.com/MadestSamurai/bd2-equipment-assistant/issues)

A standalone equipment assistant for BrownDust II on Windows. Plan N-grade gear crafting, enhancement and dismantling with your existing resources, or batch-refine selected equipment within a budget.

## Download

Current version: **0.3.7**. One application includes Simplified Chinese and English. Start with a small budget when first using the tool.

| Edition | Runtime requirement | Recommended for |
| --- | --- | --- |
| **Portable** | Includes .NET; no separate installation | Most users |
| **Lite** | Requires [.NET Desktop Runtime 8 x64](https://dotnet.microsoft.com/download/dotnet/8.0) | Users with the desktop runtime who prefer a smaller download |

Windows x64 only. Both editions have the same features. Each EXE works on its own; ZIP files also include bilingual documentation and licenses. No Python, .NET SDK, private workbench or daily assistant is required. Lite needs **Desktop Runtime**, not just the regular .NET Runtime. Verify downloads with `SHA256SUMS.txt`.

## Quick start

1. Start the game normally and enter town. Keep one game instance and use the same Windows privileges for both programs.
2. Open the EXE and select **Connect and read inventory**. Opening the tool does not connect or consume resources.
3. In **Craft N gear for powder**, set the enhancement level, quantity or budget and calculate a plan. Alternatively, use **Batch refinement** to filter and select exact equipment instances, then calculate with your target and budget.
4. Review gold, materials, Ability Pills, expected powder or refinement budget, confirm in the review dialog, then execute.
5. **Stop** prevents further batches. An already-submitted game batch finishes and is reconciled first; consumed resources cannot be undone.

When upgrading from 0.3.1 or earlier, stop and close the old tool. If the old equipment assistant connected to this game session, restart the game normally once before connecting the new version. Existing plans and journals are preserved; read inventory, calculate and confirm again before executing a new plan.

## Features and settings

| Feature / setting | Behavior |
| --- | --- |
| Powder planning | Selects a combination of N-grade recipes using current gold, pills, materials and limits; shows full costs and expected powder before execution |
| Enhance then dismantle | Default +7; supports +1 through +9 using the game's native batch crafting and dismantling flow |
| 5-pill recipes | Off by default; enabling includes N-grade recipes that consume more pills |
| Quantity, gold and pill limits | Blank means limited by current inventory. No automatic pill purchases, material synthesis or paid-resource top-ups |
| Crafting batches | Verifies a small first batch, then uses the game's actual batch capacity; not fixed at 100 items |
| Equipment selection | Search by name, character or instance ID; filter by slot, type, rarity, quality, wearer, lock, keep flag, enhancement, refinement and main/substats |
| Identical names | Shows instance ID, wearer, stats and refinement components; only explicitly selected instances are processed |
| Refinement target | 1–24, default 24; only equipment already enhanced to +9 is eligible, with no automatic enhancement |
| Refinement budgets | Defaults: 100,000 gold and 10,000 powder, shared across all selected equipment |
| Native batch refinement | Default maximum 5,000 attempts per batch; reduced to fit the remaining budget |
| Stop at target | Default mode: sets the in-game target to your chosen level, allowing the game's early-stop rule |
| Aim higher | Optional: sets the in-game target to 24, then checks your chosen target after the batch. Can consume more resources to pursue a higher result |

Refinement is random; a budget does not guarantee the target. Locked, kept or equipped gear can still be deliberately selected for refinement, so review instance details. Account changes, inventory changes, price mismatches or unconfirmed results stop execution. Unknown outcomes retain their journal and are never automatically resent; do not delete records to force a retry.

## Language

First launch follows the system language. Use the top-right switch for Simplified Chinese or English. The preference is saved, and switching preserves the plan, selections and filters. Switching is disabled during execution. Character and equipment search supports Chinese and English. Low-level diagnostics, interface names and some exceptions retain their original text.

## Compatibility and limits

Requires the official Windows client, not an Android emulator. At connection time, the tool resolves local interfaces by structure, method-body fingerprints and call sites, then compiles the adapter. It does not pin the game DLL or data package shipped with a release. Inventory refresh and execution read the currently loaded equipment, recipe and cost tables. New equipment, price changes and resolvable symbol renaming do not require a new tool release.

This does not guarantee every future version. Changed batch APIs, unsupported cost structures or ambiguous bindings stop with diagnostics instead of guessing or using stale prices. Older clients without the required native batch features are also rejected. See [cross-version adaptation](docs/COMPATIBILITY.md).

The repository includes static equipment names, stats and crafting/refinement cost metadata. It does not include game DLLs, images, account inventories, private captures or automation for other modes. Building requires no game installation; connecting does.

## Diagnostics and feedback

Local data is stored in `%LOCALAPPDATA%\BD2EquipmentAssistant`, including language settings, inventory, plans, transaction journals and connection diagnostics. The tool does not upload it.

Report the version, full error text, steps and relevant diagnostic excerpts. Never submit credentials, full inventories or game resources. Reconcile unconfirmed consumption against the game and journal before trying again.

## Development and contributions

Requires Windows, PowerShell and .NET SDK 10.0.100. From this repository root:

```powershell
.\build.ps1 -Locked
.\package.ps1 -Locked
```

Packages go to `dist/v0.3.7/`. The public source builds independently, without other BD2 repositories or Python.

[Development](docs/DEVELOPMENT.md) · [Localization](docs/LOCALIZATION.md) · [Publication style](docs/PUBLICATION_STYLE.md) · [Release notes](docs/RELEASE_NOTES.md)

## License

Project code is licensed under [MIT](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and `licenses/` for dependencies. Game content belongs to its respective rights holders.

## Connection and tool switching

When upgrading from an older release for the first time, close the old tools and restart the game once. These updated tools can then update and switch within the same game process: pending game operations finish before control changes. Settings and records are retained. Live communication uses local named pipes. Modules used by the daily workflow are coordinated separately by its scheduler.
