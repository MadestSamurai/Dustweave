# Development / 开发指南

## Saffron: skip excess potato planting

When authoritative inventory shows insufficient potato stack capacity, skip the remaining potato planting for the current cycle instead of ending the saffron task. The skip route is resource gathering → cooking → selling → saffron replanting → completion. Cooking/selling still follow the existing processing preference; disabling them goes directly from gathering to replanting. Normal-capacity cycles retain their existing order. Reserve currency only for work still planned, including final saffron replanting.

Persist the skip decision and downstream work phase independently of the actual completed potato-round count. Reconnection uses fresh native crops and the current cycle to resume: already planted crops are preserved, partial saffron replanting fills empty fields, and newly freed potato capacity does not restart skipped planting. Missing inventory metadata is not proof of excess stock. Existing pending-action reconciliation and account/control ownership remain unchanged. Completion checks for leftover ingredients run after final replanting on the skip route, so limited cooking capacity does not itself leave fields empty.

2026-11-09: 2,260 affected `Saffron,QueueEngine,LocalizationArchitecture,UserText` checks passed, including serialized restart, downstream continuation, disabled processing and normal-flow regressions. Desktop build, current-client territory compilation and isolated `localization` smoke passed. These were offline checks; no game actions were performed.

## Free distribution notice

The main-window header initially renders the free-software and refund guidance from embedded stable localization keys. The right-side dismissal button requires a second dialog and the exact displayed phrase (English is case-insensitive; surrounding whitespace is ignored). `DailyFreeDistributionAcknowledgement` stores a DPAPI-protected REG_BINARY receipt outside application/cache paths, with an additional hashed machine-installation identity and Windows SID binding. Writes flush and read back before hiding; malformed, foreign-machine/user, unavailable or obsolete receipts show the notice without interrupting use. The record has no product-version expiry and is never exported or migrated. Reinstalling Windows, deleting/restoring registry data or cloning an entire system is outside the guarantee; this is not tamper-proof DRM. The official `build.ps1` and `check-source.ps1` (also used by packaging) run `scripts/check-free-distribution.ps1`: it checks shell placement/visibility, bindings, embedded resources and the essential guidance in all three languages. Every desktop smoke checks the rendered notice; scope `free-notice` additionally covers all languages, both themes, minimum/regular window sizes, page changes, busy state and dismissing an unrelated operational notice. These are build/release checks, never runtime enforcement or a dependency of login, automation or updates.

2026-11-09: Desktop build passed, 2,018 `LocalizationArchitecture,UserText` assertions passed, `localization` smoke passed, and `free-notice` passed 48 rendered checks (12 captures). Isolated source mutations verified rejection of a removed notice, hidden ancestor, empty translation and missing embedded catalog. Representative Chinese/light and English/dark compact captures were inspected. Public source can still be modified; these measures prevent accidental omissions and simple repackaging, not determined forks. The maintenance policy does not amend the MIT license or declare all resale illegal.

2026-11-09 dismissal follow-up: 2,058 checks in `FreeDistribution,LocalizationArchitecture,UserText` passed, including real DPAPI/REG_BINARY roundtrip under an isolated test key and tamper rejection. The `free-notice` UI scope passed 114 checks across three languages and both themes, including the actual close button/dialog, wrong input, cancellation, denied persistence, verified save and store reopen. Only synthetic test keys and memory stores were used; the real user acknowledgement was not changed.

## Event preset binding and tactics guidance

`EventBattle.PresetSlot` is an optional, account-scoped one-based ordinary game preset number; zero preserves the existing behavior. Before each new native event batch, read the server's current preset list, then apply the selected nonempty slot through the native preset-use callback. Do not trust the selected-slot marker or cache an old formation. Empty slots leave the team untouched; unknown reads do not imply empty. An already running battle or settlement is reconciled without reapplying a preset, and free-AP exhaustion finishes without preset work. The optional event extension prepares its preset before entering its executor, except while resuming an existing battle/result.

Preset read/apply waits are bounded and do not create a consuming transaction. A failed preparation explains that combat was not started; continuing performs a fresh read and can actually proceed. Commands retain account/process ownership and are limited to the idle event lobby. The `EventPreset` and `BattleArrivalRecovery` synthetic cases cover changed saved contents, same-slot reapplication, empty slots, failure/recovery, normal-to-challenge transitions, exhausted attempts and resumed settlements. Current-client `prepare` compiles the new native adapter without connecting to the game; it is not a live battle test.

The `event-plugin-settings` desktop smoke scope renders the marketplace, installed-plugin guide, install/sharing dialog and account settings in all three languages and both themes. Tactics instructions identify the weekly Today task, account enable/save step and custom task preset inclusion. Sharing headings and settings labels explicitly use the theme's `Ink` resource.

## Event stage selection

`EventBattleUI.SetSelectItem` changes the selected ID before its same-row early return. A recycled `EventBattleStageScrollItem` can therefore report stage 5 while the detail and sweep button still show unpassed stage 15. Both native auto-battle and sweep selection use `EventStageSelection` to invalidate only the same selected-row presentation reference and run the native full refresh. Resolve that reference by its unique row type, not an obfuscated name. Do not write progress, enable buttons, or infer sweep eligibility from the selected ID. Sweep planning still requires the matching server progress and all native challenge conditions; confirmation revalidates the actual popup stage and free AP.

Always explicitly refresh a sweep target even when its selected ID already matches; only the scroll may be skipped. `EventSweepCount` includes pooled-row reproduction, failed-refresh continuation, normal deselection, a full stage-15-to-cleared-stage-5 sweep, same-ID stale detail, and repeated continuation after free AP is exhausted. Run it with `EventPreview,EventTradeBoundary,NativeDailyBattle` and offline current-client `prepare`. These checks do not operate the game.

## Bounded menu-open retries

The managed UI driver may repeat only the reviewed `GameFieldDefaultUI` pack-list/menu openers, `MenuUI` inventory/missions/event/pass/minigame/quick-hunt openers, the hunting-selection preview pointer, and the event skip/auto-battle preview openers. These inputs open pages; they do not submit hunting or battles. The first click retains the normal input contract without an additional parent-readiness query. Only when the destination has not appeared does recovery require a matching completed native receipt plus a fresh, ready original page. The destination is checked first and both submission reads revalidate the retry. There are at most two extra submissions, after 2 seconds and then at least 4 more seconds, within the original result-observation deadline. Known field HUDs behind MenuUI, observed non-popup source ancestors and passive notices are not blockers. Hunting preview retries additionally wait for the observed `WorldMapUI` input owner; this local rule does not gate unrelated children. Loading, a battle, a new modal/page, disabled/ambiguous targets, and changed scene/account/process do not authorize another click. A native transport/receipt uncertainty is never automatically replayed.

This is not a generic retry for `Step`: purchases, claims, guild entry, battle starts, talents, back/close and other pointer/owned inputs keep their existing contracts. Hunting submission still requires the native free-only switch and current free budget; reopening a preview neither changes that switch nor spends resources. Each successful retry links the prior result/receipt to its next command; timeout failures carry the last receipt. Continuing or restarting reobserves the game and can proceed without deleting previous journals. `MenuOpenRetry`, `HuntingInventory`, `EventPreview`, `TravelTransition`, `CommandDriver`, `ManagedInputs` and `HomeNavigation` provide synthetic regressions; these are not live-game verification.

The 2026-11-04 live preview-only A/B measured 40 opens, ten per policy in settled and immediately-after-tab-switch scenarios, interleaved ABBA. Both policies used the same production driver and ownership/identity checks. Median time to an input-ready preview was 738/746 ms (extra precheck/direct) when settled and 1,147/744 ms after a tab switch. The settled extra query cost about 3 ms; the transition precheck could wait about 0.5 seconds. Nine of ten direct transition clicks succeeded while the observed parent still reported `InputReady=false`, so that flag is not a necessary first-click condition or proof of a failed callback. All 40 succeeded without retry, resources were unchanged, and the game returned to MenuUI. This does not measure rare failure probability or establish an optimal retry delay. Actual driver submit-to-receipt elapsed was about 0.21 seconds; subtracting `Command.ObservedUtcTicks` instead also counts cached-frame age. Raw timing and readiness evidence are retained outside the source tree under `artifacts/research/hunt-preview-latency-20261104` in the parent research workspace.

## Entry points / 入口

从本仓库根目录运行根目录脚本。若调用方必须保持另一个工作目录，也可用脚本完整路径调用；脚本通过 `$PSScriptRoot` 定位源码，不改变调用方的工作目录。开发所需 SDK 由 `global.json` 指定；从其他目录调用时也须使用同一 SDK。

Run root scripts from this repository, or invoke them by full path while retaining your required working directory. Source paths resolve from the script location. Use the SDK pinned by `global.json` in either case.

| Command | Purpose |
| --- | --- |
| `build.ps1` | Locked restore and build of the host and all required utilities/tests |
| `build.ps1 -RefreshDependencyLocks` | Deliberate dependency-lock refresh; inspect the diff afterward |
| `test.ps1` / `test.ps1 -NoBuild` | Build/run or run the synthetic suite in isolated directories |
| `scripts/check-layout.ps1` | Validate solution coverage and project/source/resource paths |
| `check-source.ps1 -AfterBuild` | Validate allowed source inputs and restored/compiled boundaries |
| `build.ps1 -Mode Smoke` | UI checks using isolated demonstration data |
| `package.ps1` | Complete release package; requires locally installed client data |

`Dustweave.slnx` lists every project for IDE navigation. RID-specific command-line builds use project entry points because MSBuild does not accept `-r` at solution scope. Owned projects and managed host assemblies use `Dustweave.*`; the desktop assembly and executable are `Dustweave` / `Dustweave.exe`.

### Installed resource paths and observations

The single-file executable is at the installation root; shipped `flows`, `data`, `connection/specs` and other content live under `app`. Resolve these through `DailyTools.PackageDirectory` at resource-reader boundaries. Do not treat `AppContext.BaseDirectory` as the content root or fall back to stale sibling files. Development assets and verified cache bundles retain their own roots. Plugin discovery, user data and self-update executable paths have separate contracts and must not be redirected to `app`. `PackagedUtility` exercises actual flow, rule, free-draw and trade readers with both layouts, including stale root files.

Authenticated account observations, startup UI observations and input readiness are separate. A transient loading frame or startup UI read error must not discard a freshly read account identity or an attendance receipt. Wait within the stage deadline; keep observed login pages and input errors blocking their own actions. The guild attendance popup is a presentation-only close action, and recovery must not re-enter or claim attendance again. Cover these boundaries with `IdentityObserver`, `ManagedInputs` and `HomeNavigation`.

### Restarting for updates

An update-ready notice stays open while the restart request is validated and the helper is prepared. Every rejected request reports its reason in that notice and on Updates. Fixed startup connection/account observations are read-only; they neither gate the restart nor refresh the UI once it is reserved. An in-flight update or schedule check may finish within a bounded 15-second wait; the request then revalidates the package version and due schedule. Closing the notice cancels a waiting request. Active automation, mutable settings operations and due scheduled runs retain their guards. The `update-discovery` desktop smoke exercises real buttons with synthetic checks and an injected helper boundary; it does not install a release or touch the game.

## Naming and compatibility / 命名与兼容

| Layer | Name |
| --- | --- |
| Solution | `Dustweave.slnx` |
| Desktop project / executable | `src/Desktop/Dustweave.Desktop.csproj` / `Dustweave.exe` |
| Host projects | `Dustweave.Core`, `Dustweave.Accounts`, `Dustweave.Connection`, `Dustweave.Compatibility`, `Dustweave.ToolHost` |
| Development utilities / tests | `Dustweave.Compatibility.Cli`, `Dustweave.RuntimeCheck`, `Dustweave.TableExporter`, `Dustweave.TradeData`, `Dustweave.Tests` |
| Release archives | `Dustweave-<version>-Portable-win-x64.zip` / `Dustweave-<version>-Lite-win-x64.zip` |

主体命名空间使用 `Dustweave`、`Dustweave.Desktop`、`Dustweave.Accounts`、`Dustweave.Connection` 和 `Dustweave.Compatibility`。现有游戏内入口、序列化通信类型、账号加密盐、存储目录、环境变量、互斥锁及内置工具宿主键保留原标识；这些是兼容协议，不是遗漏的品牌名。独立工具继续使用各自的名称与版本。不要对仓库执行无差别的 `BD2` 文本替换。

Host namespaces follow the product name. Runtime entry points, serialized contracts, account encryption entropy, storage directories, environment variables, mutexes and integrated-tool host keys retain their existing identities. These are compatibility contracts. Independent tools retain their own names and versions.

0.8.15 的托管插件接口为 API 4：插件需引用 `Dustweave.Core` 并按新命名空间重新构建；旧 API 3 在加载前标记为不兼容，不尝试加载旧程序集。游戏内扩展接口仍为 1。插件实现不包含在本仓库或默认发行包中。

Managed extensions targeting 0.8.15 use API 4 and must be rebuilt against the renamed host. API 3 packages are rejected before assembly loading; the runtime extension interface stays at 1. Implementations remain outside this repository and default packages.

## Normal change / 常规修改

### Recovery review / 恢复逻辑评审

新增或修改检查、超时、重试、暂停、待确认记录时，必须按 [核心要求](../AGENTS.md) 审查其可恢复性。错误过保护属于功能缺陷，不能以“更安全”作为无证据扩大限制的理由。

1. 写清检查防止的真实问题与证据；区分暂时观察不到和已确认失败，确认游戏现有数据是否已经足够。
2. 列出等待／暂停／待确认状态的进入条件、恢复条件、时限与退出路径；本地持久化标记也必须有恢复出口。
3. 验证条件恢复后实际继续，不只是验证触发了阻断。重复点继续和程序重启后也必须能重新核对，不能被旧标记直接挡回。
4. 确认不会重复提交未知结果的消耗动作，不会抹去已完成进度，不会因局部问题禁用无关功能。
5. 无法自动恢复时提供具体、可执行且不会破坏账号和记录的处理入口；诊断导出用于研究，不能代替恢复设计。明确离线验证与实机验证的边界。

只运行改动涉及的恢复与共享边界测试；不得以此要求为由扩大每次本地修改的全量验收范围。

### Cache and sandbox relocation

`DailyStorageMigration` moves the existing cache allowlist plus the complete owned sandbox store. Host account indexes, IPC/control locks, logs, runtime extraction and OTA journals stay at their existing paths. The stable `storage-location.json` record redirects cache and sandbox lookups together; isolated processes continue using private caches inside their relocated sandbox. Missing destinations fail explicitly rather than creating empty replacement sandboxes. Previously moved cache-only installations remain supported.

The host control/storage leases and sandbox configuration gate serialize migration with automation, update handoff and sandbox management. Only idle, uniquely registered account sandboxes may move. Their names and registry/data bytes remain unchanged. Sandbox launch is disabled temporarily while copying; streamed SHA-256 verification precedes rebinding and atomic routing publication. `storage-migration.json` records the original roots and enabled states before changes. Startup recovery runs after the main instance lock and before account loading: uncommitted moves restore original bindings, committed moves finish on the verified destination. Normal update recovery still runs first at its established path. Never substitute directory junctions for this routing protocol.

An account state directory can contain only historical launch preferences or notes without ever having created a sandbox. Migration copies these files with the store, without requiring or creating a binding, only when no registration marker, removal transaction, populated game root, unknown subdirectory or matching Sandboxie registration exists. The zero-byte `loading-experiment-recovery/transaction.lock` left by a no-op recovery check is also metadata; pending journals or backups are not. A renamed registration pointing at that root also prevents metadata classification. Real missing, corrupt, duplicate or mismatched bindings remain blocking; their errors identify the account and directory, and `live/diagnostics/storage-migration-check.json` records the expected/actual roots and reason before any routing change.

Diagnostic storage must be audited by contents and producer, separately from expiry. The 2026-11-02 read-only local audit found repeated UI trees (especially button paths) in preflight, transport, native receipts and queue results. A ZIP exports a bounded subset of recent evidence; its size is not the compressed size of all update payloads, plugin versions, compiled components or sandbox system/game caches. Local research history is not a representative size estimate for every user's daily run.

Client OTA retention keeps one recent terminal backup per installation target, preferring the latest confirmed upgrade over failed retries that duplicate the restored executable. Prepared downloads retain the existing two-release limit; they cannot evict a recent installation backup. Backups still expire after the configured 2–7 days. A confirmed update with no recovery marker permits removal of its exact ZIP/partial payloads, helpers and staging copies. Recent terminal attempts retain small jobs, journals and error receipts after their duplicate program payload is removed; receipt-only attempts cannot occupy the retained-backup slot. Unfinished, damaged or unknown recovery records remain protected, and failed-only downloads remain available for retry. Independent installation directories retain independent backups. Both storage leases and legacy download locks exclude cleanup, and the marker/journal state is rechecked before deletion.

A short update-only idle pass first becomes due one minute after startup and then every 30 minutes (five minutes after a skipped/blocked pass). It does not scan or compress other caches, does not depend on automatic diagnostic expiry, and does not postpone the twelve-hour log-retention clock. Foreground work, tool windows, active updates and restarts defer/cancel it through the existing maintenance controls. The 2026-11-02 local update cache had already shrunk from about 511 MB to 210 MB under the previous cleanup; the remaining large items were two approximately 99 MB backups and about 13 MB of used deltas.

New command steps keep the pre-dispatch intent and frame until a complete transport record is flushed. The transport then owns that original frame; a completed observed result carries forward the original frame, command, elapsed time and full native receipt before superseding transport. Retiring a busy copy is optional. No native receipt, in-memory result or pending-operation reader changes format. Queue persistence strips only redundant UI/navigation trees from completed/skipped result copies, retaining identity, native values, quantities and retry progress; incomplete results stay full. Do not add a format requiring a database/blob reference to recover a pending transaction.

Soul routine reads update the current raw and parsed state, with one content-named observer configuration per distinct configuration. Action/battle/executor checkpoints still retain raw sources and parsed states; failures freeze the captured raw input. Full observation rows must not also be printed into the console log. `archiveRead` is an explicit full-read capture option, not the production polling default. The source audit's normalized JSON comparison reduced 2,544 sampled step groups from 215,975,208 to 163,325,967 bytes and 184 queue records from 30,950,288 to 14,456,936 bytes. These are read-only projections, not deletion of local user history or estimates for the entire data directory. Focused coverage: CommandDriver, ManagedInputs, LogEvidence, QueueEngine, QueueSession, FieldTalent, PickupRecovery, LogCleanup, StorageMaintenance, StorageOptimization, DiagnosticExport and Soul.

`DailyStorageOverview.Disks` groups unique measured files by their physical root and stored byte size. It includes main data, current/retired cache roots, complete current/retired sandboxes, owned runtime extraction and crash files. A retired sandbox with retained files remains counted, but is not adopted or automatically deleted as an active account. Cleanup and migration remain separate operations.

Affected checks: `StorageMigration`, `CacheStorage`, `StorageMaintenance`, `StoragePath`, `Sandbox`, `SandboxScope`, `AccountProgress`, `LoginIdentity`, `LoginRecovery`, `UpdateSafety`, `UpdateRetention`; desktop smoke scope `support`. `StorageMigrationNative` is an explicit opt-in with `DUSTWEAVE_NATIVE_STORAGE_TEST=1`: it creates a disposable Sandboxie registration, writes synthetic HKCU/file state, relocates it, verifies reopening, then removes only that registration. It never launches the game or imports real accounts.

Storage relocation must distinguish active recovery from a terminal legacy experiment journal. `loading-experiment-recovery/pending.json` remains after successful restoration; its existence alone cannot block a move. Validate the journal and every entry as `restored`, archive it durably, then retire the active marker under its transaction lock. Preserve the archived evidence and program backups during relocation. Leaving the terminal marker active at the destination would fail its absolute `BoxRoot` check on the next launch. Incomplete, malformed or inconsistent journals and pending removal remain protected. Focused regression covers real synthetic preparation/restoration, interrupted recovery, migration cancellation, retry, repeated relocation and the next-launch recovery call without game access.

Storage-window errors and relocation phase/source/target failures are persisted in bounded diagnostic reports; preserve both the original and rollback exceptions. Export these reports, binding-preflight details and the last migration result ahead of historical step archives. Never export raw registry hives, login stores or the complete file-by-file relocation journal. The 2026-11-10 15:38 diagnostic package predates this coverage: the matching recovery-required screenshot narrows the failure, but does not identify which user's sandbox journal triggered it.

1. 在 `src` 或 `assets` 修改对应模块；合成用例在 `tests/Dustweave.Tests/Cases`。
2. 新增源码、配置、依赖锁或项目时登记 `source-manifest.json`；新增项目也加入 `Dustweave.slnx`。清单只记录本仓库内的源码输入，不添加外部工作目录。
3. 运行构建、对应回归与来源边界检查。目录/资源移动时也验证独立打包和嵌入组件。
4. 源码验证、成品验证与实机验证分别记录。普通构建和合成测试不连接游戏。

Maintain host changes directly in this repository. Register new inputs in the source manifest and add new projects to the solution. Keep the manifest limited to repository-local inputs. Keep source, package and live-game verification distinct.

## Independent tools / 独立工具

`standalone/` 中的六个目录是经过边界检查的源码输入，不依赖父研究仓库，也不是构建时在线下载。它们保留自己的版本、依赖锁、说明和许可证。此处的主程序接入层在 `src/Desktop`、`src/Core`、`src/ToolHost`。

装备助手、使徒运气防守与无限抽抽乐已分别移入 [`src/Equipment`](../src/Equipment)、[`src/ApostleDefense`](../src/ApostleDefense) 和 [`src/InfiniteGacha`](../src/InfiniteGacha)，与 `src/Mansion`、`src/Pandora` 一样在本仓库直接维护。各自的三个 `Dustweave.Equipment.*` / `Dustweave.ApostleDefense.*` / `Dustweave.InfiniteGacha.*` 工程保留原有程序集名、依赖身份、命名空间、连接与数据标识，避免影响宿主加载和历史设置。原独立仓库已归档；无需再从原仓库导入或向其发布。

更新独立工具时先在其独立项目验证，按明确版本导入需要的源码差异，再登记新文件、检查源码边界、构建并运行主程序的语言/控制锁/工具宿主回归。不要覆盖账号、连接所有权或把独立工具用户数据一起导入。不要为了统一目录而改写其 GitHub 发行版本。

The six standalone source inputs retain their versions, locks and notices. Import reviewed upstream changes, then verify source boundaries and host language/ownership integration. Do not import user data or alter upstream release identities merely to align directories.

Equipment Assistant, Apostle Defense and Infinite Gacha are maintained directly under [`src/Equipment`](../src/Equipment), [`src/ApostleDefense`](../src/ApostleDefense) and [`src/InfiniteGacha`](../src/InfiniteGacha), alongside Mansion and Pandora. Their `Dustweave.Equipment.*`, `Dustweave.ApostleDefense.*` and `Dustweave.InfiniteGacha.*` projects retain existing assembly/package identities, namespaces, connection contracts and data locations for compatibility. These original repositories are archived and no longer import or release targets.

## Packaging / 打包

`package.ps1` 从 `assets/flows`、`assets/specs`、本机客户端规则和许可目录组装完整包。Portable/Lite 共享同一应用入口。产品版本号与 0.7.27 连接基线分离；打包使用隔离依赖锁，验证不会倒写旧候选。

旧版 `source-staging` 是初次建立仓库的过程，不再是日常开发入口。现有发行包留在原交付位置；源码搬迁本身不需要用户重启游戏或迁移账号数据。
## 版本策略 / Version policy

版本号支持 X.Y.Z 及语义化后缀（如 1.0.0-beta）。后缀不决定 GitHub 的 Release 类型；本次 1.0.0-beta 使用普通 Release。分发包不包含外部插件实现。

Semantic release labels such as 1.0.0-beta are supported. This release uses a normal GitHub Release despite its beta label. Packages exclude external plugin implementations.

## Passive UI and preview recovery / 背景提示与预览收尾

LivePolicy.BuildUiToken is the shared native input token source. Passive surfaces stay in observations and diagnostics, but do not participate in the token, input blockers or navigation stability. Keep its passive list aligned with ui-policy.json; the regression suite checks this contract. Do not add retry loops to compensate for HUD notices.

MonsterHuntUI returns through its observed _objBackButton. A rejected business preview is cancellable only with explicit local non-submission proof, unchanged process/account/cycle and popup content, an enabled native Cancel button and no related transaction events. Cleanup has its own durable result and never marks the business complete. Unknown dispatches and unrelated dialogs are not cancelled.

背景提示不进入操作校验，不能靠增加等待或重试处理。确认失败的收尾与业务成功分开记录；已有消费或结果不明不能通过取消弹窗来推定成功。本轮回归使用合成现场；真实每日免费抽取与魔兽返回仍需后续正常使用验收。
## User-visible notices

Keep execution errors, queue records, action IDs and game-provided names unchanged. Translate at the desktop presentation boundary through `DailyUiText` / `DailyLanguage`; do not localize protocol or recovery values.

Register notices in `src/Desktop/Localization/notices.json` with all three locales. New dynamic notices use `DailyText` and declare numbered placeholders and typed `parameters`: `number`, `value` (opaque account/item/path text), `stage`, or `message` (nested registered notice). They do not need a prose matcher. Only legacy inputs retain an explicit, frozen `source` grammar. Never derive that grammar from editable `zh-CN`, and do not add unrestricted replacement of captured values.

### Localization identities (2026-11-03)

`LocalizedTextCatalog` owns key lookup, three-language validation, search text and bounded missing-text diagnostics. `DailyLanguage.Get/Text/Bind` are the UI path. `Translate` is a legacy runtime/evidence adapter, not a UI-label convenience API. Static settings and trade preview labels/columns use explicit keys; `DailySettingsCatalog` and tool category IDs classify independently of display wording. Same Chinese with different meaning keeps separate keys (for example Close versus Off).

`legacy-inputs.json` freezes the compatibility decisions made before this migration. It is not regenerated from current translations. Opaque `message.legacy.*` IDs preserve old message identities without retaining Chinese as a key. Core `Messages.zh-CN.txt` is a readable diagnostic fallback, not the source of truth for UI copy; changing a localized display does not require changing execution messages. New coordinator progress carries `DailyText` beside the unchanged raw `Message`, state and counters. Old records without display metadata remain readable; unknown old text is retained, not migrated and not a queue blocker.

Run `test.ps1 -Groups LocalizationArchitecture,UserText` for catalog changes. Add login/queue groups when touching metadata transport. The synthetic host scope `DUSTWEAVE_UI_SMOKE_SCOPE=localization` exercises three locales, actual WPF bindings, category/search, unsaved preferences, account files and task IDs, dynamic/structured messages, trade columns, and an audit after rendering. No real game is accessed. A missing translation or argument is a test failure, never a new production stopping condition.

The diagnostic export button passes `DailyLanguage.Diagnostics()` to the exporter. `localization.json` contains a bounded, deduplicated snapshot of missing keys, missing locale values and formatting faults for the exporting process; it does not include message argument values or create permanent cache files. `unavailable` means the caller supplied no snapshot, not that no faults occurred. The snapshot is not cleared by export, so `Audit()` continues to fail during development. Validate this path with `DiagnosticExport` and the `localization` UI smoke, which uses the actual export button.

The desktop `--smoke` check exercises every registered notice in all three languages, refreshes existing WPF bindings, and verifies nested messages, account names, paths and original diagnostics. Inspect `notice-languages.json`, `localization.json` and the `locale-*-notice.png` captures. New backend progress formats need a corresponding notice and a rendered sample in this check; adding translations must not alter game actions.
Scheduling, release notes and OTA packaging are documented in [SCHEDULING_AND_UPDATES.md](SCHEDULING_AND_UPDATES.md).

Game client updates follow [CLIENT_UPDATES.md](CLIENT_UPDATES.md). Recheck affected stages after a changed client/data release; do not add full compatibility scans to every launch.

## Scoped verification / 按改动范围验证

Task preset exclusions survive both expired-history loading and direct server-cycle rollover. Clearing a day's temporary task choices must refill from the assigned preset, never from all enabled tasks; reenabling an account setting follows the same rule. Explicit temporary additions remain valid within that cycle. The `presets` UI smoke reproduces rollover through the real task panel, checks the emitted execution request, repeated refresh, completion filtering, reopening and the no-preset default. It performs no game operations.

日常迭代先运行 `test.ps1 -Groups Startup,LoginIdentity` 等直接相关分组，不因修改一个组件而反复运行全套。`test.ps1` 不指定分组才运行全部回归；不存在的组名报错，不能出现零测试通过。

`package.ps1` 默认 `-ValidationScope Auto`。账号、登录、隔离窗口范围的修改可复用最近一个完整验证包：比较完整源码输入、去除产品版本号后的构建设置、当前客户端代码指纹，以及已通过的组件验证记录。此时只运行账号相关回归和 Portable/Lite 成品启动、身份及边界检查，不重复准备和检查九个独立工具。`validation-scope.json` 明确记录本次范围、复用基线与改动文件，不冒充本次完整验证。

连接、运行时、共享依赖、业务或未识别范围发生变化时，自动选择完整检查。可显式使用 `-ValidationScope Full`；强制 `Accounts` 但不满足复用条件时直接说明原因。更多组件后续可增加对应分组映射，不能用账号范围掩盖其他代码变更。

Use targeted `test.ps1 -Groups ...` during iteration. Packaging defaults to automatic scope selection: account-only changes reuse unchanged component evidence from a completed full baseline, with targeted account regressions and both packaged host smoke checks. Unknown/shared changes require full validation. Recorded scope distinguishes newly executed checks from reused evidence.

`Navigation` 范围覆盖本轮后台提示处理：运行相关导航、命令驱动、托管输入与背景界面回归，并重新编译当前客户端的日常执行组件，执行原生输入策略检查；未改动的观察配置与独立小游戏模块复用完整基线。范围匹配不足时仍回到完整验证。

账号范围也包含 `DailyRunPanel.cs` 和 `MainWindow.Presentation.cs` 的任务界面调整：追加每日刷新回归，并在两个实际发行包中检查任务选择、账号切换、批量返回和三语言／深浅界面。公共执行模型、主题及其他运行逻辑不在这一豁免范围内。

The Navigation scope rechecks navigation, command dispatch, managed inputs and passive UI, plus the current-client daily runtime compilation and native input policy. Unchanged independent tools reuse the recorded full baseline.

`package.ps1 -ValidationScope Rewards` extends the navigation scope for pass/workflow and observation-schema changes. It runs affected workflow, binding, data, readiness and localization cases, recompiles the daily bridge and regenerates all daily evidence configurations. Independent tools reuse the unchanged full baseline. Unknown changes outside the declared boundary still require broader validation.
The Rewards scope also covers event puzzle batches, hunting policy and per-account preferences. It rechecks preference migration and the desktop settings smoke in addition to workflow/command regressions; a changed binding contract is regenerated from the matching baseline client, then adapted and compiled against the current client. Independent tool payloads remain unchanged.

`Weekly` also covers managed pickup reconciliation and queue recovery. Changes to these shared host boundaries run queue, business journal, mirror, free draw, dispatch, management, trade-resume and weekly-route regressions. Client bindings, runtime, transport and independent tool sources must remain identical to the full baseline; they are not exempted by this scope.

## Desktop startup / 桌面启动

普通双击、账号检查、选中队列及计划启动直接进入当前进程的界面入口，不依赖 Explorer COM 再启动一次自身。保留显式开发入口 --launch-desktop 和既有 --desktop-session 参数；游戏启动仍由 Accounts/DesktopGameLaunch 校验 Windows 用户、会话与沙箱归属，连接组件按需要单独提权。

从开发执行环境打开真实主窗口时，必须使用成品 `Dustweave.exe --launch-desktop`，不能直接 `Start-Process` 成品主窗口。2026-10-23 的实机复现确认：开发环境与 Windows 桌面可在相同 SID、相同 HKCU 路径下读到不同登录数据；工具写入和回读成功，并不代表桌面启动的游戏读到了同一份数据。账号槽位没有损坏，此时应正常退出织尘，再经桌面入口打开。显式账号检查应通过 `DesktopGameLaunch` 转交 `--desktop-session --inspect-account <account-key>`，只核对所选账号，不运行日常。不要放宽账号校验，也不要把普通用户的双击启动重新改为强制依赖 Explorer。合成测试窗口继续使用隔离测试环境。

主界面前的托管启动阶段记录到用户数据目录的 startup-state.json；失败写入 startup-error.json，并显示记录路径。主目录不可写时尝试当前用户临时目录的 Dustweave-startup。日志不记录命令行、环境变量内容或账号凭据。Windows 拒绝执行和 .NET 宿主尚未进入托管入口的失败仍需系统事件／宿主诊断。

游戏登录后的更新介绍页 `UpdateUI` 使用 `EntranceBackgroundCoverUI` 作为背景；两者同时可见且没有真实加载指示时，公共启动观察器必须允许日常模块准备，否则会形成“等待背景消失 → 负责关闭介绍页的模块无法启动”的互锁。此时不代表已在主菜单，也不授权点击其他界面。真正的 `LoadingUI`、原生加载指示、以及单独出现的背景遮罩仍按加载处理；介绍页关闭后立即恢复普通遮罩判断，不保留豁免状态。回归必须把实际 `StartupRuntime` 的观察结果传给 `DailyGamePreparation`，不能仅测试模块启动后的导航。针对性检查：`test.ps1 -Groups IdentityObserver,GamePreparation,HomeNavigation,Startup`；实机账号检查仅验证连接，不执行日常。

Normal, account-check, selected-queue and scheduled launches open the UI in the current process without an Explorer COM relaunch. Keep the explicit developer relay and legacy child switch. Game launch identity/sandbox validation and separately elevated connection helpers are unchanged. Managed startup stages and failures are recorded before the window opens, with a user-temp fallback; OS blocks and failures before managed entry still require OS/host diagnostics.
## Sandbox runtime startup recovery / 沙盒驱动启动恢复

Explicit sandbox launch, opening its task window, creation/repair, and ordinary launch's existing-sandbox sign-in handoff call `DailySandboxStartup` before configuration/ownership reads. `SandboxRuntimeStartup` recovers only the typed missing-driver status `0xC0000080`; denied process inspection, malformed replies and account/ownership mismatches are not treated as a stopped driver. Passive discovery and read-only management never initiate recovery or elevation.

Recovery starts the existing `SbieSvc` and, if still needed, `SbieDrv` through Windows `sc.exe start`. It does not stop/restart services, install missing services, change disabled startup types, edit sandbox configuration, import credentials or launch a game. Windows consent is requested only after a start returns access denied. A shared gate rechecks readiness for concurrent callers; a 45-second deadline and cancellation bound service transitions and driver reobservation. The service command's success alone is not readiness: the real process-scope API must answer before the original operation continues. Failures/cancellation release the gate and leave no persistent refusal flag. Only the owned `sc.exe` helper may be stopped on cancellation; service processes are preserved.

The latest recovery phase is exported as `sandbox-runtime-recovery.json`; detailed Windows errors remain in the original operation error. `SandboxStartup`, `SandboxScope`, `SandboxInstallation`, `Sandbox`, `UserText` and `LocalizationArchitecture` cover recovery/continuation, concurrency, deadline/cancellation/retry, no repeated service starts, ownership boundaries and localized error propagation. These synthetic checks do not claim a real user's driver recovery or game login was tested.

## Game launch location / 游戏启动位置

`Accounts/GameInstallation` stores one per-Windows-user setting at `%LOCALAPPDATA%/BD2AccountSessionManager/game-installation.json`. It is outside the application directory and account slots. Normal launches, new-account launches, recovery and scheduled execution resolve through `GameLauncher`; a validated path is held through session switching. A missing explicit choice fails with an actionable message rather than falling back to another installation. No game registry values or launcher settings are changed when selecting a path.

日常设置顶部提供统一路径选择，三种界面语言和深浅主题共用现有组件。读取与保存异步执行；独立账号窗口只读。主窗口构造隔离启动请求时携带已解析的路径，隔离启动在核对请求、账号空间与客户端版本后更新该空间的设置副本，避免虚拟化目录遗留旧位置。已有游戏连接仍按实际进程路径读取，不会因修改下次启动的位置而改连其他进程。

Focused verification: `test.ps1 -Groups GameInstallation,LoginIdentity,AccountIdentity,Sandbox`. For an isolated settings-only UI smoke, set `DUSTWEAVE_UI_SMOKE_SCOPE=game-location`, `DUSTWEAVE_PLUGIN=none` and an isolated `BD2_DAILY_DATA_ROOT`, then invoke the built desktop with `--smoke <new-output-directory>`. This uses demonstration accounts and synthetic game files, never launches or connects to the game. Omit the scope variable for the full desktop smoke.

## First-run setup / 启动引导

`DailyFirstRun` uses the existing identity guard: two advancing fresh game snapshots, exact process identity, and a matching complete local login. It tries one launch without switching accounts, or observes the already running game. Skipping cancels the observation and leaves the game running. Saving requires the game and launcher to be closed, rechecks the full identity and writes through the existing account vault; read-back must find the same account and slot before the tour begins.

`FirstRunWindow` provides single-account setup and skip confirmation, followed by the page tour. `first-run.json` records started, tour, completed or skipped, without login credentials. `PageTour` highlights real pages with an input-blocking adorner; schedules and other automation wait while the guide is open. Existing accounts are not forced to redo setup; interrupted tours can resume. The account page can reopen the guide.

`DailyMultiAccountSetup.AfterSave` offers `MultiAccountSetupWindow` only when a newly saved distinct account increases the valid account count from one to two. Both the account page and a manually reopened setup guide use this boundary. Before adding the second account, the idle saved-account card provides an inline sign-in/normal-exit/save instruction; active-game and unsaved-sign-in instructions retain priority. Credential updates and subsequent additions do not repeat it. `multi-account-setup.json` independently records pending, completed or dismissed, with only the pending execution choice. Sequential execution needs no additional installation; parallel execution uses the shared Sandboxie installation guide. Cancelling installation leaves execution preferences unchanged; Not now dismisses the prompt, while an interrupted pending setup can resume at ordinary startup. Confirmation preserves concurrency limits, refreshes the main window's mode, and starts no queue.

Scoped checks: `FirstRun,GameInstallation,LoginIdentity,AccountIdentity,AccountRestart,Startup,Sandbox`. Use `DUSTWEAVE_UI_SMOKE_SCOPE=first-run` with the isolated `--smoke` command for the wizard and tour in all three locales and both themes; `multi-account-setup` checks the second-account prompt, actual save-handler integration, dismissal, pending setup recovery and installation cancellation in the same locales and themes. Synthetic verification and rendered UI checks do not claim a fresh user's actual game launch / connection / exit / save flow has been runtime-tested.

## Game screen terminology / 游戏界面用语

Use 主菜单 / 主選單 / main menu for the game's MenuUI. Cartridge towns, the plaza and individual lobbies keep their actual names. DailyIdentityGuard checks fresh character identity and does not require a particular town or map. A process-locator error should only ask users to start the game. Keep interface strings, embedded tool labels and both getting-started documents aligned.

## Per-account task selection / 按账号选择任务

“今日任务”的任务账号独立于当前游戏账号。切换账号仅展示对应设置和历史，后台刷新不改变用户正在查看的账号；本次计划勾选按账号分别保留。明确点击执行后才走既有账号启动及身份核对流程，再执行勾选、补跑或接续。队列运行期间禁止切换任务账号。

隔离批量完成或停止后，每个账号的“选择任务”打开其已有隔离空间中的任务页。此入口不导入凭据、不启动游戏，不把隔离执行记录复制到主窗口；账号归属检查和单实例锁仍生效。真实 Sandboxie 窗口打开需要运行时验收，合成界面检查只验证入口与身份路由。

The task account is a browsing choice, separate from the signed-in game account. Background refresh preserves it; drafts remain separate per account. An explicit run switches and verifies the selected account through the existing coordinator before dispatch. Isolated batch entries reopen their existing account workspace without credential import or game launch.

Use `DUSTWEAVE_UI_SMOKE_SCOPE=task-navigation` with the desktop `--smoke` entry for focused UI verification: account history, separate draft selections, refresh, targeted startup, three languages, both themes and parallel-row routing. The fixture never operates a real game. Related core groups: `QueueSession`, `Parallel`, `Sandbox`, `AccountIdentity`.

## Account list scrolling / 账号列表滚动

The account grid retains recycling virtualization and uses pixel scrolling. Its row-selection handler accepts only events originating from the grid itself: recycled preset ComboBoxes emit bubbling selection events even when the user only scrolls. Their two-way bindings handle actual preset edits independently. Do not route those events through whole-page account presentation, login recovery reads or control-lock probes.

Use desktop smoke scope `accounts-scroll` with an isolated data root for 100 accounts with alternating presets, 198 scroll steps, unchanged highlighted/checked accounts and persisted settings, editable presets, real row selection, pixel scrolling and bounded realized rows. The `accounts` smoke includes this regression. It writes timings but does not use hardware-sensitive timing thresholds. On 2026-11-02 the local comparison reduced whole-page refreshes from 191 to 0 and median step time from 15.13 ms to 6.86 ms (11.65 ms on the final repeat); these are synthetic WPF measurements, not a guarantee for every user's machine. The timers are paused only in the synthetic scrolling measurement; production polling and action ownership checks remain enabled.

## Session diagnostics / 会话诊断

### Recoverability audit (2026-11-02)

Shared hosted tools validate identity and suite readiness within a rollbackable activity-lease entry. If validation fails before dispatch, release only a newly acquired lease. Never release existing ownership that may cover an unsettled action. A missing identity observation does not consume the automatic handoff attempt; an actual activation does. Explicit Connect permits another attempt, while the automatic Connect event must not reset the latch into a reconnect loop.

Startup download retries distinguish rejected-before-dispatch from uncertain dispatch. Preserve uncertain retries until an exact native rejection or a fresh changed presentation from the same actor resolves the old presentation; do not infer successful download. Three retries apply to each explicit connection attempt, not the entire lifetime of a saved journal. Pending command-mailbox and active-coordinator checks remain in force.

Mail cleanup waits for MenuUI as well as disappearance of MailUI, without repeating the confirmed claim. Old business records missing their original observation remain unknown and are reported to the owning stage, rather than crashing queue preflight. A fresh usable stage can build a new plan; no saved action is replayed or marked successful. Room visits preserve the response window for a possibly active request, including incomplete historical observations, and can perform a fresh read-only visit afterwards.

Focused verification: `Startup,ActivityLease,ManagedInputs,CommandDriver,ObservationWait`, `BusinessJournal,BusinessScope`, and isolated desktop `tool-activity`. Cover failure, restored evidence, actual continuation, repeated polling, no duplicate consuming input, existing ownership, and missing historical observations. Account changes, uncertain consuming transactions, active foreign ownership and unsafe update/storage targets remain real boundaries; never clear their flags merely to make a button succeed. This audit is offline/synthetic and does not claim all game/network failures have been reproduced.

Normal desktop, updated and isolated-worker processes own a fresh control identity. Only explicit hosted tool/utility/connection-helper entry points inherit the parent owner. Module activation requires a fresh, identified, nonempty account and player. Runtime revocation keeps separate owner-exit, unreadable-owner, missing-identity, actual-account-change and handoff codes; pending receipts retain their original stop reason until drained.

`DailyIssues` classifies failures without inventing a cause for legacy messages. The desktop localizes reason and next action, while raw errors remain in diagnostic records. See [DIAGNOSTICS.md](DIAGNOSTICS.md) for export boundaries and targeted checks. Regenerate the Windows icon with `scripts/build-app-icon.ps1` from the supplied branding mark; all sizes remain bundled in the executable.

### Trading validation scope

`-ValidationScope Trading` is a narrow follow-up scope for `DailyTradeOptimizer`, its forecast/drop model, plan/execution integration and the corresponding optimizer tests. It reuses a completed full baseline only when the actual source difference is wholly inside this allowlist, documentation and packaging metadata, and the installed client code fingerprint is unchanged. Changes to the shared linear solver, connection, dependencies or UI require full checks. Trading and product-identity regressions plus both packaged startup/identity/boundary gates still run; the recorded scope does not claim to rerun unchanged tool checks.

### Weekly route validation scope

`-ValidationScope Weekly` covers only `DailyWeeklyRoute`, its isolation regressions, notice translations and release metadata. It checks the exact input difference against a completed full baseline and verifies the unchanged game code fingerprint. Weekly, talent, command/identity and localization regressions run again, together with both packaged startup and boundary checks. Native components and unrelated tools reuse their recorded baseline; shared runtime or connection changes require full validation.

跑商专项复用已完成的完整基线，当前源文件逐一比较；共享求解器、连接、界面或未知范围的改动会拒绝复用。年度分析见 [365天对照](TRADING_ANNUAL_2026-10-10.md)，日志维护见 [诊断说明](DIAGNOSTICS.md)。

### Restart and recovery boundaries / 重启与恢复边界

Account, player, server and reset identity remain mandatory. Closing a game ends its process/session; reconnecting a component in the same process does not create a fresh retry budget. A new run keeps confirmed progress and accounting evidence, but old non-consuming reads must not remain permanent execution locks.

- `room.info` is a room visit/read, not a reward claim. An unresolved visit can be superseded from a clean main menu after the original process ends, or after its response deadline. The new visit needs its own response and room-owner/cache proof. The old outcome stays unknown.
- Guild entry is server-idempotent. A single request that timed out without a response permits at most two fresh entries in the same process. An identified new game process can independently refresh an orphaned query. Failed, interleaved or unattributed response evidence is not treated as success. Native receipts, managed attempts and command guards apply the same policy.
- Mailbox submission and game execution are separate facts. Only a matching native rejection explicitly reporting no dispatch permits re-selection of a vanished target. Missing receipts and changed account identity remain non-replayable.
- Weekly collection, NPC quests and theft retain independent server-proven completion when a sibling fails. Progress totals include all available cartridge targets before traversal; an encountered subset is not a full-route completion proof.
- NPC object pickup and carrying use the target's native readiness and interaction range. The navigation-mode flag is not arrival proof. Cancel the navigation callback before one explicit interaction; preserve acknowledged object IDs and never query away partial pickup progress. Native lease/target checks precede the dispatch marker. Only definitely unsubmitted rejections may renew navigation, at most twice; unresolved local tasks defer their cartridge's NPC work while the other categories continue. Identity changes and unknown consuming results retain guarded handling.
- New NPC quests prioritize the sum of actual gold rewards across the whole chain, across owned available cartridges. Display-only reward previews are excluded. Hunting permission, supported objectives, current target availability and weekly quota remain eligibility gates. Existing accepted quests take precedence over accepting another quest. NPC query and recovery records are included in diagnostic exports.

小屋旧访问、公会旧读取与购买等消耗操作分开恢复。旧记录保留诊断意义；新进程不能借用旧进程的回执冒充成功。地图加载期间已确认未点击的页面变化应重新观察，不应把整条路线标成失败。这里只处理确认属于当前界面的提示收尾，不自动重复结果未知的交易。

Focused groups: `Guild,ManagedInputs,CommandDriver,TravelTransition,CollectionReadiness,CafeteriaRecovery,QueueEngine,ManagedBootstrap,DiagnosticExport,LogCleanup,LogEvidence`. Connection `self-test` and offline `prepare` / compatibility checks compile against installed client assemblies without attaching to the game. These do not replace a real dual-instance rerun.

### Pass isolation and theft readiness / 通行证隔离与偷窃就绪

Pass mission definitions follow `PassTable.ExpEventMissionGroupId` and the native mission-group links. `EventMissionGroupTable.UsePass` is not an eligibility requirement: guide/newbie passes can legitimately omit that protobuf default. Read the active catalogue independently of any selected page and refresh it after claims. A server-confirmed `PassRewardResponse.NewbiePassStep` advancement can change the selected pass and active list; account identity, mission completion and reward-cache proof still apply.

A settled, incompatible pass is recorded individually as pending review, with its definition evidence. Other readable passes and proven daily/weekly equipment tasks continue. Schema errors are not loading delays, and unrecognized pages cannot authorize claims. A claim already dispatched with an unknown result remains pending and must not be replayed. Tests cover both inspection and claim modes, catalogue advancement, stop/identity propagation and unknown-claim boundaries.

Weekly theft uses the same read-only native talent gates as field collection. Short animation, menu-timeline, cooldown or attempt-latch waits are bounded. Only a native rejection explicitly confirming no dispatch permits another selection attempt; the actual theft confirmation retains its accounting proof. Unavailable-talent diagnostics include the native gates and pending groups without inferring a missing resource from an absent popup.

Focused groups: `PassRecovery,Workflow,FieldTalent,CollectionReadiness,DiagnosticExport`. Synthetic tests contain no user captures; captured-report replay and current-client compilation belong under ignored investigation artifacts.

The field talent menu opens asynchronously. A matching command receipt is not proof that `QuickMenuUI` has appeared. The readiness loop must accept the unchanged field before the first menu frame, then verify menu identity, input readiness and native talent gates. Do not extend a fixed delay or resend the open command. A lost/replaced menu or battle returns to guarded interruption handling; account, scene, map and week changes remain boundaries. Failure snapshots retain the opening receipt and before/after evidence even when ordinary step history is omitted from an export. `CollectionReadiness` covers delayed first appearance, passive overlays, input animation, closure, replacement, battle, unknown popups, timeout, stop and context changes.

### Journal lookup performance / 操作记录查询性能

Scheduled takeover is off by default. When enabled, it closes the registered tool, waits for pending execution to settle, then normally closes only the game instance captured at the start of the handoff. The existing account queue performs a fresh launch and identity verification, including when the tool was already idle in a minigame. Process replacement or close failure must prevent queue startup, never trigger process-wide killing. `SchedulingUpdates` covers restart gating; the packaged scheduling smoke covers localized controls.

`DailyManagedBusiness` builds routing summaries once per queue session. Subsequent checks consume Windows directory-change notifications and inspect only changed records; do not reintroduce a directory enumeration or per-file identity scan on every action. Own saves update the summary directly. External writes, deletion, atomic replacement and notification overflow must remain visible. Overflow forces fresh parsing even if file identity, length and timestamps match; unsupported notifications fall back to fresh reads. Queue and temporary utility sessions must dispose their directory observers.

Use `BusinessJournal` to verify a 5,000-record history, unchanged-file access counts, same-stamp updates, overflow, directory replacement and duplicate evidence. Benchmark both the installed baseline and the candidate inside the same Sandboxie merged filesystem: testing only the physical sandbox directory misses inherited host history. Report cold discovery separately from repeated lookup latency; lookup timing is not an end-to-end map or game benchmark.

餐厅的下一气泡来自新的当前观察，已确认领取后不增加空闲轮询等待。周收集仅在同一流程实例、同一地图、两秒内且没有后续输入或待恢复操作时复用刚收到的服务器结果；显式恢复查询始终重新请求。相关回归为 `CafeteriaRecovery`、`WeeklyIsolation` 和 `CollectionReadiness`，账户、周期及未知回执保护保持不变。

### Observation metadata performance / 观察元数据性能

`Connection/ObservationMetadata` caches CLR field/property/method descriptors by actual `Type` identity and static/instance scope. Resolve current-client aliases before lookup. Never store observed instances, field values, getter results, account identities or click readiness in this cache. Preserve private-base lookup, property-before-field lookup within each declaring type, indexer exclusion and zero-argument method selection. The existing read-only method allowlist and return-type checks remain in `Evidence.Member`; metadata reuse does not authorize new calls. A failed getter must be called again on the next read and be able to recover.

UI hierarchy paths are reused only inside one synchronous `Bridge.Observe` call. Reparented/replaced objects must be observed again on the next frame. Pointer de-duplication is per surface and must retain separate `ui` and `pointer` routes for the same object. The evidence configuration digest is cached only after applying the matching configuration and reset together with it. Observation intervals, scene enumeration, identity checks, receipts, navigation and simulation scheduling are unchanged.

Affected checks: `ObservationPerformance,LiveBindings,CommandDriver,ObservationWait,EvidenceReadiness,ManagedInputs,UiInteractionReadiness,PassiveUi`, plus connection `self-test` and offline `prepare` against the current client. The synthetic performance report compares changing-state checksums, elapsed time and allocation against the prior reflection lookup; it is not evidence of a live Unity frame-rate improvement. Keep live comparisons separate and explicitly authorized.

### Daily Talent Pill policy / 日常天赋药限制

General settings are account-scoped: reserve defaults to 0, automatic refill is on, and the initial target is 5000. `PotionSupplyRevision=1` enables the policy once for older profiles and raises targets below 5000, retaining larger targets. Later opt-outs and smaller targets persist. Never reduce a protected reserve during migration; when needed the target is raised above it. An already maximal integer reserve cannot be made spendable and retains its disabled policy. Only the daily queue supplies `PotionPolicy`; embedded and standalone tool sessions keep their own settings. Task selections are unchanged.

Two fixed checkpoints run before selected tasks and immediately before final mail collection (or at the end if mail is not selected). They top up only the missing amount; sufficient stock does not cause shop navigation. An active task or unresolved operation takes precedence over the opening checkpoint. Cancellation does not run the closing purchase. Persist each checkpoint separately, including uncertain purchases; explicit continuation reconciles against current state and does not replay completed checkpoints or tasks. These are two routine checks, not a two-purchase cap: the existing shortage-triggered refill remains a fallback.

Monday is not a special calendar branch. Require the current native 45-gold offer with the weekly NPC reputation discount. A missing quote or insufficient spendable gold skips that refill and leaves other work available. If a task itself needs Talent Pills and no discounted supply is available, defer it with manual refill/retry advice. Do not reorder NPC tasks, recursively request their completion, or silently buy full-price pills. The weekly quest may itself require a talent. The optional movement-speed talent must remain optional when pills are short.

`Trade.RefillCookingPotions` is an independent, account-scoped opt-in, default off. It funds only the executable cooking allocation plus the protected pill reserve, not future forecast recipes, and may exceed the ordinary target. Keep the existing joint optimizer, dated price strategy, storage constraints and recipe order. Gold tied up in restoring a depleted pill reserve must be reserved before planning; compare against the unchanged no-extra-purchase candidate so this funding option cannot suppress a better unfunded allocation. Procurement and pills share the same upfront cash budget. Disabled automatic processing never authorizes cooking purchases. Custom cooking uses current, unlocked recipes in the user's order, shared ingredients, protected inventory, output space and spendable gold. One logical funding phase may use several native purchase batches, with a separate durable receipt for each; resumption starts from the remaining live inventory. Enabling cooking funding does not enable selling or automatic processing.

Check the complete native action cost before dispatch, including batch cooking/crafting and collect-all dispatch with automatic renewal. Free actions remain possible below the reserve; spending exactly down to it is allowed. The native command also carries the reserve and performs a final check after identity validation, before any dispatch marker. A missing cost is a explained, unsubmitted failure, not permission to consume. Refresh the binding contract from its matching baseline when adding native members.

A confirmed shortage becomes a visible auxiliary queue row. If refill is enabled, clean up only confirmed unsubmitted previews, use the plaza merchant's current weekly NPC-task 10% discount, and buy only to the target while respecting reserved gold. A missing discount, inadequate stock/gold or an undersized target gives an actionable restriction. Unknown purchases retain their accounting records and block all consuming queue stages. After verified replenishment, reopen the original stage and recompute from current game state; never replay its previous consuming command. Auxiliary refill rows cannot be selected or resumed as independent purchases. Trading also observes the budget and reports when its allowed potion budget is reached.

Focused verification: `TalentPotion,Preference,QueueEngine,CommandDriver,BusinessJournal,BusinessScope,DispatchRecovery,TradeOptimizer`; game-location/settings desktop smoke covers the compact launch row and modal in both themes and all three languages. Native `prepare` compiles against the installed client without connecting to or operating the game. Real discounted purchases and automatic return to a live task require separate runtime validation.

### Selected high-price sales with favorite purchases

`Trade.Sales` is an account-scoped opt-in list independent of `AutoCookAndSell`, used only by `FavoritesOnly`. Existing settings deserialize to an empty list. Buy existing game favorites, cook the chosen recipes in order, then sell checked dishes and ingredients; sale-only selections work with an empty cooking list or exhausted purchase stock. Do not change the normal optimizer, its dated strategy, or in-game favorites.

`DailyCustomSales` joins the full cooking item catalog with the optimizer subset. It selects only current native 120% quotes from available shops; below-maximum, unknown, expiring and locked items are not sold. Ingredient reserves apply to post-sale permanent total stock, including locked stacks, and do not constrain custom cooking. Re-read quotes, locks and inventory after each confirmed sale, with a finite initial quantity budget. Reuse the existing native sale preview/confirmation, protected-inventory accounting and journal. An uncertain receipt is not replayed; a later run plans from current inventory. The recipe dialog separates cooking order, dish sales and ingredient sales, retaining filtered-out selections and per-item reserves. Scoped checks: `CustomSales,TradeReplan,CookingTravel,Preference,LocalizationArchitecture,UserText`; desktop `cooking,simple-settings,localization` across all languages and both themes.

### Manual talent practice

The host-owned **Tools → Talent practice** window shares the daily controller and native evidence adapter. Bargaining uses the plaza merchant; Search, Rush, Swiftness, Danger Detection and Stealth alternate the outdoor safe areas of Chapters 1 and 2 through normal cartridge entry. The former trading-settings entry is removed. The host tracks the window for tool switching, shutdown and update preparation; idle windows hold no control lease. See [Talent practice](BARGAIN_TRAINING.md) for scope, resource limits and validation. The focused `BargainTraining,TalentPractice` groups exercise real workflow commands against a deterministic native mailbox, including failed discounts, selected-character ownership, stop during preview/settlement, sparse native map defaults, effect reset, no XP growth, insufficient medicine and lost-response continuation across component/game restarts. The `bargain` UI smoke checks skill/character filtering, native game-name language selection without changing identity, three languages, both themes, compact sizing, discovery acknowledgement and host-managed closing without game access.

### Delayed Last Night record presentation

`TotalWarNewcordPopupUI` is a foreground `UIBase` even when its observation says `Popup=false`. It can appear on returning to the field after the weekly battle has already been confirmed complete. Home navigation and cartridge travel share its explicit native return handling; animation/readiness is awaited, unknown modals and battles are preserved, and uncertain submissions are never replayed. Closing this score display must not restart a completed weekly battle. Synthetic delayed-arrival and command-boundary regressions live in `HomeNavigation` and `TravelTransition`. Narrow host routing changes can reuse a completed full package baseline; native inputs are still identity-checked and the changed routes rerun.

## Mirror schedule and interrupted NPC navigation

Mirror reads the native schedule manager's content 1 state, matching the game's own PVP matching button. Only ScheduleSeason permits a new match. Known off-season, calculation, coming-soon and training states produce an explained skip; absent or unknown data is not classified as closed. A submitted journal must settle first. Native entry and auto-confirmation recheck the schedule before the dispatch marker. This guard is scoped to Mirror, not the shared hunting auto popup.

Native environmental knockback temporarily uses DontMove, then stops and resumes a cached touch-move destination; a task navigation latch can remain active independently of actual movement. Weekly NPC navigation now records position and movement readiness. UI-token changes do not count as progress. After six stationary, controllable seconds, at most three explicit native cancel/start sequences re-resolve the same quest or carry source. Input locks, pending pickup/delivery acknowledgements and native object interactions prevent recovery from interrupting an action. Persistent failure defers that NPC task through the existing cartridge isolation path.

Validation is synthetic plus compilation against the installed client, separate from an actual train/trap or off-season game run. Private decompilation evidence is retained outside this repository.

## Walking collection: pickup areas and local deferrals

Native walk path checks run before the dispatch marker. A definitely unsubmitted target/path rejection is a local navigation failure, not an uncertain reward. Walking collection defers that object for the current map visit, processes other objects, and continues subsequent maps. Deferred targets retain their map, region, positions and rejection reason. They never count as collected; server collection state remains authoritative. Missing player/navigation state and unknown submitted pickup results are not silently converted into target deferrals.

Upgrade recovery can retire a v115 walk rejected before movement only with the durable matching original command receipt, audited failure code, matching account/server identities and no pickup events. An empty event list alone is insufficient. The original receipt and observations remain unchanged.

A native reward collider defines an acceptance area rather than a mandatory object-coordinate destination. Selected-reward approach checks bounded ground candidates using the game's own collision-area predicate, including its height-ignore and angle rules, then requires a complete native path. This does not flatten terrain navigation or permit traversal through walls, water or other floors. Geometry checks and current-client compilation are distinct from real-map validation.

### Next route layer: region goals (design, not yet enabled)

The existing atlas learns directed gate-to-region links, but collection planning and `GoTo` still consider a map ID sufficient for arrival. A disconnected second region of the same map therefore needs a different goal representation. Model a goal as `(pack, map, object, acceptable regions)`; model travel nodes as `(map, connected region)` and edges as a specific native gate with its observed landing region. The valid route can be A1 -> B -> A2. Visiting A1 must neither fulfill nor blacklist the goal in A2.

Use weighted shortest paths over the small directed region graph, with native local paths inside each region. Compare gate walking and loading costs with usable waypoint costs. An unknown landing is an exploration frontier, never assumed to be every region in its destination map. Exploration must have a per-run door/time budget and must not repeat an already observed no-progress region/door pair. Returning to a genuinely new region rechecks temporarily deferred targets. Stable topology may be cached by game/map geometry version, but availability, scene instances, account unlocks and pickup completion must be refreshed from the game. Remaining validation needs native entry/exit identities and landing observations from disconnected-map examples; a local path failure alone does not prove that a pickup is globally unreachable.

### Optional mail retention

`Stages.PreserveBloodCocktailMail` defaults to false for existing and new accounts. When enabled, mail containing native PvP currency types 15 or 24 is retained as a whole, including mixed attachments and Sundays. Event-token mail collection uses the same account policy. The setting remains editable with the final mailbox stage disabled, since activities can still request mail collection.

Filtered collection reads every attachment, loads additional native mailbox pages, and claims at most 10 selected instances per request. The native dispatcher rechecks attachments and never supplies `ReceiveAllMails` as its callback. Missing attachment data or incomplete pagination cannot fall back to Claim All. Receipt verification and interrupted-operation reconciliation use the selected instance scope and require unselected mail to remain. Test this with `ManagedInputs,Preference,UserText,Workflow,LiveBindings`, the native command self-test, and offline compilation/evidence generation against the current client; these checks do not claim a real mailbox run.

### Tower sweep policies

`Salvation.UseFixedCount` defaults to false; existing accounts retain growth-funding sweeps. When enabled, `SweepCount` (1–999, default 1) requests that many sweeps per execution even if growth is already funded or maxed. Both policies require a cleared difficulty, an idle active tower season and valid resource data. The daily-key reserve caps the count; paid keys are never substituted.

Fixed-count plans persist `fixedCount` in the transaction scope. Native bridge 124 accepts it as the sixth `salvation_confirm` item and rechecks the live popup, requested count and free-key budget before clicking. Older five-item growth plans retain their original verification shape. Validate with `NativeDailyBattle,Preference,BattleArrivalRecovery,UserText`, the connection self-test, current-client offline bridge compilation and UI smoke scope `salvation` (three languages, both themes). Offline checks do not consume game keys or establish a real sweep result.

### Native event-battle result controls

Event victories expose `_goWinSafeArea`; `_objectWinExitButton` can remain hidden even after the last consecutive battle. Bridge 125 shares `EventBattleResultRules` between completion observation and exit execution, retaining the visible native control in `event.native.ExitField`. It requires event battle mode, a single visible outcome pane, an available return button and no native continuation. A next-stage or retry button cannot substitute for returning. Both victory and defeat layouts are covered.

An owned terminal result can resume when the event HUD has already disappeared, but not with a different account, process, cycle or another battle HUD. `NativeDailyBattle,BattleArrivalRecovery,EventTradeBoundary` cover result selection and complete workflow recovery of five confirmed victories, including an intermediate result that must continue waiting. Use the native self-test and current-client offline compilation separately; these are not live combat validation.

Event result exit has a separate arrival boundary: `CheckEventHubEnd` opens the event lobby while `ReturnUIField` is still executing its asynchronous exit sequence. The lobby and outgoing `BattleUI_EventBattle` can therefore both be active. A successful exit receipt or visible lobby must not immediately enter lobby navigation or be treated as an unfinished battle. Observe the outgoing battle/result surfaces until they disappear (bounded to 60 seconds, no fixed sleep or repeated exit). Resume from a lobby with only the outgoing HUD follows the same observation path instead of waiting 900 seconds for an already closed result. `BattleArrivalRecovery` covers delayed return, resume without a journal, cancellation, timeout followed by continuation, and identity changes; no consuming input is replayed. This fixes the observed 2026-11-05 local queue where five wins and zero free AP were followed by a successful exit and overlapping lobby/HUD; synthetic recovery does not constitute a new live combat trial.

### Weekly NPC guide destinations versus completion targets

A kill quest's configured map can be a hunting ground while its monster IDs also occur in regular suppressible encounters. These are different inputs: the host must use fresh `Wild` availability for completion planning, while the game's `StartQuestNav` follows its configured gate paths and can resume after a map load or an animation. Blocking only the host's explicit hunting command does not control an already active game guide.

Bridge 126 adds `weekly_npc_plan` before travel. Accepting or claiming a step cancels the old native guide, including pending navigation coroutines. During the owned plan, a scoped native prefix permits quest navigation only for that exact, still-active non-kill step on its ordinary destination map. Kill steps use host routing plus suppression. Map-load resumption and stale step IDs cannot substitute a hunting destination; field-object travel for doors, waypoints and carry sources remains independent. A zero-valued plan releases ownership when the quest finishes or is deferred. The guard is also released on pause, component handoff, account/cartridge change or lease expiry. It does not change quest tables, kill counts or server completion state.

Replanning compares remaining kill counts with distinct available monsters, including partial progress. Insufficient targets defer only the NPC task through the existing weekly isolation path. Diagnostics include the guide map, selected completion maps and intercepted guide IDs. Validate with `WeeklyNpcInteraction,WeeklyNpcBoard,WeeklyIsolation`, native command self-tests and passive compilation against the installed client. Synthetic dialogue/suppression/return sequences are not a substitute for live-game validation.

### Dispatch talent entry resources

The native Dispatch talent handler checks CatalystValue before opening DispatchSelectUI; an enabled talent button is not evidence that this resource condition is satisfied. The 2026-11-05 13:34 diagnostic export contains repeated native `物资不足。` notices immediately after both attempted entries, with 3 Catalyst displayed. Checking potions only inside ClaimCharacter is too late: the rejected entry never reaches that code and becomes an unnecessary UI timeout. Read the already captured talent cost/balance before entry and route a real shortage through the existing queue refill policy; without a policy, finish this character as partial and return normally. Opening the page does not consume potions, so do not enforce a reserve against an otherwise available preview. Consuming claims/restarts retain their own resource policy and reconciliation. `DispatchRecovery,TalentPotion,QueueEngine` passed 216 offline checks, including repeated shortages and actual continuation after balance restoration. No live game operation was performed for this change.

### Owned Fiend Hunter integration

The `src/FiendHunter` projects preserve their existing assembly and pipe identities while using the host preparation, activity lease, theme, language, suite compilation cache and diagnostic retention. The monster selector defaults to Berserker and also offers Fiery Tiger; only Blade / solo / Very Hard rounds matching the selection can resume. Selecting or connecting never starts combat. Confirmed settlement disables input before character objects disappear.

Tiger timing uses a separate attack map and distinct contact/explosion windows. The defender can respond to a later window in the same animation; healing does not override an incoming threat. On 2026-10-19, live solo Very Hard runs cleared Tiger in approximately 75 seconds (1053/1800 HP remaining) and Berserker in approximately 105 seconds (1197/1800 HP). Both had successful server settlement and no network errors. These are individual successful trials, not a measured win rate.

Validation: `FiendHunter,UnifiedSuite,ToolMenu,ActivityLease` (189 checks); desktop smoke `tool-preparation` (262 checks, three languages and both themes), `tool-menu` (single-column layout), current-client passive compilation, and source boundary checks. UI smoke also covers cancel/retry and closing before connection, preventing reentrant window-close errors. Known legacy diagnostic logs move into `tools/fiend-hunter/diagnostics`, preserving timestamps; settings and transport files are not swept as logs. Runtime logs rotate at 8 MiB and the host applies normal retention.

### Owned Rhythm integration

The `src/Rhythm` projects retain their assembly, runtime and pipe identities. The tool opens immediately with the shared preparation overlay, uses the host theme and language, and acquires the same connection/activity lease as other integrated tools. Preparing or connecting does not enable playback. Status polling begins only after connection; connection, pipe writes and shutdown run outside the UI thread. Closing an inactive window does not send a stop command to another session.

Settings live in `tools/rhythm`; a valid bounded legacy settings file is imported only when no host settings exist. Known old diagnostic logs move into `tools/rhythm/diagnostics` with their original timestamps, so host retention still applies. Transport and unknown files are left untouched. Runtime logs retain their existing rotation limit and compilation uses the suite cache.

Validation: `Rhythm,UnifiedSuite,ToolMenu,ActivityLease,TravelTransition,CommandDriver,ManagedInputs,DiagnosticExport,WeeklyIsolation` (504 checks), desktop smoke scopes `tool-preparation`, `rhythm` and `tool-activity`, three languages, both themes, and closing during preparation. Synthetic timing/input checks and passive client compilation are separate from live-game playback validation.

### Holy Stone attribute selection

Crystal tabs in `HuntOrAirwayUI` use native `OnPress` to record the scroll offset before `OnClick` checks whether the gesture was a drag. The pointer adapter invokes that press preparation only for a target present in the current owner's `_crystalButtons`; dispatch, preview and unrelated controls retain their original input behavior. Never replace this with global pointer-down events or bypass the native locked-tab check. The hunting workflow avoids reopening the crystal category when it is already selected. Attribute changes are confirmed by the selected native table, with at most three non-consuming attempts and two seconds of observation per attempt. Identity changes, cancellation and observation failures are not swallowed; only the explicit unchanged-selection deadline permits retry. Exhaustion reports a retryable attribute-selection failure before any Holy Stone preview/dispatch. Existing receipt/free-resource reconciliation preserves a completed rice hunt on the next run.

Focused checks: `HuntingInventory`, `UiInteractionReadiness`, `Workflow`, `ManagedInputs`, `LocalizationArchitecture`, `UserText`; the native adapter also requires offline compilation against the installed client. Synthetic scroll-offset/press tests and workflow recovery tests do not establish live-game reliability.

### Owned Soul challenge integration

`src/Soul` owns the five-dungeon coordinator, native execution adapter, bounded planner, battle kernel and compressed whole-dungeon growth library. `SoulToolWindow` opens from the host Tools list. Start and Resume share `SoulSession` with the real-game verification entry; neither uses screen coordinates or computer-use automation. Purchases immediately enter native storage, upgrades update their original views, and layout application verifies view uniqueness before starting battle. See `src/Soul/README.md` for the calibrated client/model identities and native pool recovery boundary.

Resume explicitly adopts the game's current season and cleared dungeons, even without a tool record. An active manual run/rechallenge takes priority; new manual progress replaces stale local assumptions. All-clear ends immediately. On the preparation page, native Back calls `ReturnToLobbyPublic` → `SaveCurrentDeck`, preserving the manual layout before the season/progress read; wait for that navigation instead of repeatedly clicking Back. Resume never calls GiveUp. A current season change creates a new campaign while the previous record is retained in the resume journal. These semantics are distinct from explicit Start all five, which deliberately replays historical clears. Render completed chapter IDs, not just the first N checkmarks, because manual progress can be nonsequential.

Soul reconnect recovery must not reject Resume merely because `PendingDungeon` exists. Transient identity gaps use the shared fresh-observation preflight and a bounded recovery loop; actual account/process changes still require reconnecting the intended session. An existing battle is observed without clicking Start again. At a Soul lobby, preparation or result page, `soul_progress` sends the native read-only `IbMainInfo` request; it does not reload shop/deck objects or send a battle transaction. Its accepted response reconciles the saved attempt, including legacy records. The native callback treats absent `PlayInfo` as no active dungeon, not a malformed response. A current settlement or advanced progress can establish the result; otherwise an idle server state permits replanning from current progress while preserving the unknown result in the recovery journal, without fabricating a win/loss. Historical clears do not prove that a replay's final wave succeeded. Completed campaign dungeons remain intact. Validate failure → reconnect → next decision, repeated Resume, persistence, active battle, null PlayInfo and old-receipt rejection with `Soul,ObservationWait,StageProtocol`.

Affected synthetic groups are `Soul,ToolMenu`. `--tool soul --check-client <Managed> <output>` compiles against the current client without connecting; `--tool soul --smoke <output>` renders three languages and two themes offline. `--soul-verify-launch <saved account name> <output>` is an explicitly authorized live test: it uses the validated desktop relay, preserves the single-instance/control locks, can switch to the named saved account, and runs the same five-dungeon tool. Never use that entry as an offline build check.

### Optional refill observations

Fixed Talent Pill checks read the current balance before navigation or purchase. If the scoped balance is missing or invalid, request one fresh observation, then record a visible skipped refill and continue independent tasks. A missing balance is never zero and never authorizes buying. Account changes, cancellation, observer transport errors and uncertain purchases retain their existing boundaries. ManagedBootstrap covers both checkpoints, repeated missing/invalid/restored observations and actual daily continuation.

### Trading settings transfer (2026-11-10)

The daily trading section exports/imports a versioned `Dustweave.TradePreset` JSON file. It carries trading switches, cooking order, custom dish/ingredient sales and reserves, gold reserve and spending budget. It contains no identity, credentials, inventory, execution orders, game favorites or other daily preferences. Import previews the destination account, counts, budget and consuming options, then saves only that account's trading policy. A failed account save restores the prior budget. Missing switches/limits, unknown formats, malformed or oversized files are rejected before import; zero and unlimited budgets stay distinct. `TradePreset` tests and the isolated `localization` smoke cover cross-account import/export and actual UI refresh, including favorites mode and later switching back to automatic trading.

### Runtime security servicing (2026-11-10)

PR #16 alone does not update the shipping Portable runtime: the former publishing script, production/acceptance descriptors and apphost template still pinned 8.0.30. The SDK change also requires refreshed ILLink locks for Desktop, Equipment and OtaChannel. These now follow the runtime pin; retain historical fixtures/bootstrap bytes. The package runtime probe validates actual CLR/core/Brotli/WPF versions without opening accounts or game connections. Lite retains the established 8.0.0 startup floor to avoid stranding existing users during OTA. Its external runtime is not serviced by Dustweave; recommend Desktop Runtime 8.0.33 or a later 8.0 patch. Formal packaging probes use the patched runtime, with an additional actual packaged startup/update check on an older .NET 8 installation. Advisory: https://github.com/advisories/GHSA-p3jp-9v83-76w9 . Neither a dependency diff nor a successful restore by itself proves the shipped executable contains the fix.

### Storage progress and resource review (2026-11-10 local.2)

Storage migration reports real copy plus target-hash work, source verification, binding switch and post-commit cleanup separately. Reports are throttled; hashing reuses a pooled buffer, and pre-commit verification honors cancellation. No ownership, commit or rollback policy changed. The storage window animates progress and keeps activity visible through long-file checks; Windows animation preferences are honored.

The one-time resource settings review now accepts interactive `--updated` restarts and is offered before ordinary startup auto-run. Scheduled and other directed jobs remain excluded. Only explicit keep/save acknowledgement persists the receipt; programmatic close/replacement does not. Update acknowledgement still precedes this optional UI. Synthetic UI checks cover replacement, reopening, per-account saving and startup continuation; no live accounts or login state are touched.

Validation: six affected groups (StorageMigration, DiagnosticExport, CacheStorage, TradeBoxes, LocalizationArchitecture, UserText), 2,305 checks; actual Portable package `support`, `resource-review`, and `localization` UI scopes, plus runtime/resource/archive checks. Unchanged resources are reused from the hash-verified 1.6.9 release. No full-suite, live-game or OTA acceptance is claimed for this local package.