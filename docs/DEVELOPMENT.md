# Development / 开发指南

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

Register notices in `src/Desktop/Localization/notices.json` with all three locales. Dynamic notices declare numbered placeholders and typed `parameters`: `number`, `value` (opaque account/item/path text), `stage`, or `message` (nested registered notice). An optional `source` holds an existing execution format when the display can omit an internal identifier; the original remains in diagnostics. Do not add unrestricted string replacement or translate arbitrary captured values.

The desktop `--smoke` check exercises every registered notice in all three languages, refreshes existing WPF bindings, and verifies nested messages, account names, paths and original diagnostics. Inspect `notice-languages.json`, `localization.json` and the `locale-*-notice.png` captures. New backend progress formats need a corresponding notice and a rendered sample in this check; adding translations must not alter game actions.
Scheduling, release notes and OTA packaging are documented in [SCHEDULING_AND_UPDATES.md](SCHEDULING_AND_UPDATES.md).

Game client updates follow [CLIENT_UPDATES.md](CLIENT_UPDATES.md). Recheck affected stages after a changed client/data release; do not add full compatibility scans to every launch.

## Scoped verification / 按改动范围验证

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

Normal, account-check, selected-queue and scheduled launches open the UI in the current process without an Explorer COM relaunch. Keep the explicit developer relay and legacy child switch. Game launch identity/sandbox validation and separately elevated connection helpers are unchanged. Managed startup stages and failures are recorded before the window opens, with a user-temp fallback; OS blocks and failures before managed entry still require OS/host diagnostics.
## Game launch location / 游戏启动位置

`Accounts/GameInstallation` stores one per-Windows-user setting at `%LOCALAPPDATA%/BD2AccountSessionManager/game-installation.json`. It is outside the application directory and account slots. Normal launches, new-account launches, recovery and scheduled execution resolve through `GameLauncher`; a validated path is held through session switching. A missing explicit choice fails with an actionable message rather than falling back to another installation. No game registry values or launcher settings are changed when selecting a path.

日常设置顶部提供统一路径选择，三种界面语言和深浅主题共用现有组件。读取与保存异步执行；独立账号窗口只读。主窗口构造隔离启动请求时携带已解析的路径，隔离启动在核对请求、账号空间与客户端版本后更新该空间的设置副本，避免虚拟化目录遗留旧位置。已有游戏连接仍按实际进程路径读取，不会因修改下次启动的位置而改连其他进程。

Focused verification: `test.ps1 -Groups GameInstallation,LoginIdentity,AccountIdentity,Sandbox`. For an isolated settings-only UI smoke, set `DUSTWEAVE_UI_SMOKE_SCOPE=game-location`, `DUSTWEAVE_PLUGIN=none` and an isolated `BD2_DAILY_DATA_ROOT`, then invoke the built desktop with `--smoke <new-output-directory>`. This uses demonstration accounts and synthetic game files, never launches or connects to the game. Omit the scope variable for the full desktop smoke.

## First-run setup / 启动引导

`DailyFirstRun` uses the existing identity guard: two advancing fresh game snapshots, exact process identity, and a matching complete local login. It tries one launch without switching accounts, or observes the already running game. Skipping cancels the observation and leaves the game running. Saving requires the game and launcher to be closed, rechecks the full identity and writes through the existing account vault; read-back must find the same account and slot before the tour begins.

`FirstRunWindow` provides setup and skip confirmation. `PageTour` highlights real pages with an input-blocking adorner; schedules and other automation wait while the guide is open. `first-run.json` records started, tour, completed or skipped, without login credentials. Existing accounts are not forced to redo setup; interrupted tours can resume. The account page can reopen the guide.

Scoped checks: `FirstRun,GameInstallation,LoginIdentity,AccountIdentity,AccountRestart,Startup,Sandbox`. Use `DUSTWEAVE_UI_SMOKE_SCOPE=first-run` with the isolated `--smoke` command for the wizard and tour in all three locales and both themes. Synthetic verification and rendered UI checks do not claim a fresh user's actual game launch / connection / exit / save flow has been runtime-tested.

## Game screen terminology / 游戏界面用语

Use 主菜单 / 主選單 / main menu for the game's MenuUI. Cartridge towns, the plaza and individual lobbies keep their actual names. DailyIdentityGuard checks fresh character identity and does not require a particular town or map. A process-locator error should only ask users to start the game. Keep interface strings, embedded tool labels and both getting-started documents aligned.

## Per-account task selection / 按账号选择任务

“今日任务”的任务账号独立于当前游戏账号。切换账号仅展示对应设置和历史，后台刷新不改变用户正在查看的账号；本次计划勾选按账号分别保留。明确点击执行后才走既有账号启动及身份核对流程，再执行勾选、补跑或接续。队列运行期间禁止切换任务账号。

隔离批量完成或停止后，每个账号的“选择任务”打开其已有隔离空间中的任务页。此入口不导入凭据、不启动游戏，不把隔离执行记录复制到主窗口；账号归属检查和单实例锁仍生效。真实 Sandboxie 窗口打开需要运行时验收，合成界面检查只验证入口与身份路由。

The task account is a browsing choice, separate from the signed-in game account. Background refresh preserves it; drafts remain separate per account. An explicit run switches and verifies the selected account through the existing coordinator before dispatch. Isolated batch entries reopen their existing account workspace without credential import or game launch.

Use `DUSTWEAVE_UI_SMOKE_SCOPE=task-navigation` with the desktop `--smoke` entry for focused UI verification: account history, separate draft selections, refresh, targeted startup, three languages, both themes and parallel-row routing. The fixture never operates a real game. Related core groups: `QueueSession`, `Parallel`, `Sandbox`, `AccountIdentity`.

## Session diagnostics / 会话诊断

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

### Daily Talent Pill policy / 日常天赋药限制

General settings are account-scoped: reserve defaults to 0, automatic refill is off, and the initial refill target is 1000. Only the daily queue supplies `PotionPolicy`; embedded and standalone tool sessions keep their own settings. Schema 16 migrates without changing task selections.

Check the complete native action cost before dispatch, including batch cooking/crafting and collect-all dispatch with automatic renewal. Free actions remain possible below the reserve; spending exactly down to it is allowed. The native command also carries the reserve and performs a final check after identity validation, before any dispatch marker. A missing cost is a explained, unsubmitted failure, not permission to consume. Refresh the binding contract from its matching baseline when adding native members.

A confirmed shortage becomes a visible auxiliary queue row. If refill is enabled, clean up only confirmed unsubmitted previews, use the plaza merchant's current weekly NPC-task 10% discount, and buy only to the target while respecting reserved gold. A missing discount, inadequate stock/gold or an undersized target gives an actionable restriction. Unknown purchases retain their accounting records and block all consuming queue stages. After verified replenishment, reopen the original stage and recompute from current game state; never replay its previous consuming command. Auxiliary refill rows cannot be selected or resumed as independent purchases. Trading also observes the budget and reports when its allowed potion budget is reached.

Focused verification: `TalentPotion,Preference,QueueEngine,CommandDriver,BusinessJournal,BusinessScope,DispatchRecovery,TradeOptimizer`; game-location/settings desktop smoke covers the compact launch row and modal in both themes and all three languages. Native `prepare` compiles against the installed client without connecting to or operating the game. Real discounted purchases and automatic return to a live task require separate runtime validation.

### Manual bargaining training

The settings dialog shares the daily controller and native evidence adapter. See [Bargaining training](BARGAIN_TRAINING.md) for scope, resource limits and validation. Run the focused BargainTraining group when changing the training loop; it exercises real workflow commands against a deterministic native mailbox, including failed discounts, stop during preview/settlement and lost receipts.

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

### Weekly NPC guide destinations versus completion targets

A kill quest's configured map can be a hunting ground while its monster IDs also occur in regular suppressible encounters. These are different inputs: the host must use fresh `Wild` availability for completion planning, while the game's `StartQuestNav` follows its configured gate paths and can resume after a map load or an animation. Blocking only the host's explicit hunting command does not control an already active game guide.

Bridge 126 adds `weekly_npc_plan` before travel. Accepting or claiming a step cancels the old native guide, including pending navigation coroutines. During the owned plan, a scoped native prefix permits quest navigation only for that exact, still-active non-kill step on its ordinary destination map. Kill steps use host routing plus suppression. Map-load resumption and stale step IDs cannot substitute a hunting destination; field-object travel for doors, waypoints and carry sources remains independent. A zero-valued plan releases ownership when the quest finishes or is deferred. The guard is also released on pause, component handoff, account/cartridge change or lease expiry. It does not change quest tables, kill counts or server completion state.

Replanning compares remaining kill counts with distinct available monsters, including partial progress. Insufficient targets defer only the NPC task through the existing weekly isolation path. Diagnostics include the guide map, selected completion maps and intercepted guide IDs. Validate with `WeeklyNpcInteraction,WeeklyNpcBoard,WeeklyIsolation`, native command self-tests and passive compilation against the installed client. Synthetic dialogue/suppression/return sequences are not a substitute for live-game validation.

### Owned Fiend Hunter integration

The `src/FiendHunter` projects preserve their existing assembly and pipe identities while using the host preparation, activity lease, theme, language, suite compilation cache and diagnostic retention. The monster selector defaults to Berserker and also offers Fiery Tiger; only Blade / solo / Very Hard rounds matching the selection can resume. Selecting or connecting never starts combat. Confirmed settlement disables input before character objects disappear.

Tiger timing uses a separate attack map and distinct contact/explosion windows. The defender can respond to a later window in the same animation; healing does not override an incoming threat. On 2026-10-19, live solo Very Hard runs cleared Tiger in approximately 75 seconds (1053/1800 HP remaining) and Berserker in approximately 105 seconds (1197/1800 HP). Both had successful server settlement and no network errors. These are individual successful trials, not a measured win rate.

Validation: `FiendHunter,UnifiedSuite,ToolMenu,ActivityLease` (189 checks); desktop smoke `tool-preparation` (262 checks, three languages and both themes), `tool-menu` (single-column layout), current-client passive compilation, and source boundary checks. UI smoke also covers cancel/retry and closing before connection, preventing reentrant window-close errors. Known legacy diagnostic logs move into `tools/fiend-hunter/diagnostics`, preserving timestamps; settings and transport files are not swept as logs. Runtime logs rotate at 8 MiB and the host applies normal retention.

### Owned Rhythm integration

The `src/Rhythm` projects retain their assembly, runtime and pipe identities. The tool opens immediately with the shared preparation overlay, uses the host theme and language, and acquires the same connection/activity lease as other integrated tools. Preparing or connecting does not enable playback. Status polling begins only after connection; connection, pipe writes and shutdown run outside the UI thread. Closing an inactive window does not send a stop command to another session.

Settings live in `tools/rhythm`; a valid bounded legacy settings file is imported only when no host settings exist. Known old diagnostic logs move into `tools/rhythm/diagnostics` with their original timestamps, so host retention still applies. Transport and unknown files are left untouched. Runtime logs retain their existing rotation limit and compilation uses the suite cache.

Validation: `Rhythm,UnifiedSuite,ToolMenu,ActivityLease,TravelTransition,CommandDriver,ManagedInputs,DiagnosticExport,WeeklyIsolation` (504 checks), desktop smoke scopes `tool-preparation`, `rhythm` and `tool-activity`, three languages, both themes, and closing during preparation. Synthetic timing/input checks and passive client compilation are separate from live-game playback validation.
