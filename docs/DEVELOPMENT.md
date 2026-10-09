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

`standalone/` 中的九个目录是经过边界检查的源码输入，不依赖父研究仓库，也不是构建时在线下载。它们保留自己的版本、依赖锁、说明和许可证。此处的主程序接入层在 `src/Desktop`、`src/Core`、`src/ToolHost`。

更新独立工具时先在其独立项目验证，按明确版本导入需要的源码差异，再登记新文件、检查源码边界、构建并运行主程序的语言/控制锁/工具宿主回归。不要覆盖账号、连接所有权或把独立工具用户数据一起导入。不要为了统一目录而改写其 GitHub 发行版本。

The nine source inputs retain their versions, locks and notices. Import reviewed upstream changes, then verify source boundaries and host language/ownership integration. Do not import user data or alter upstream release identities merely to align directories.

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

The Navigation scope rechecks navigation, command dispatch, managed inputs and passive UI, plus the current-client daily runtime compilation and native input policy. Unchanged independent tools reuse the recorded full baseline.

`package.ps1 -ValidationScope Rewards` extends the navigation scope for pass/workflow and observation-schema changes. It runs affected workflow, binding, data, readiness and localization cases, recompiles the daily bridge and regenerates all daily evidence configurations. Independent tools reuse the unchanged full baseline. Unknown changes outside the declared boundary still require broader validation.
The Rewards scope also covers event puzzle batches, hunting policy and per-account preferences. It rechecks preference migration and the desktop settings smoke in addition to workflow/command regressions; a changed binding contract is regenerated from the matching baseline client, then adapted and compiled against the current client. Independent tool payloads remain unchanged.

## Desktop startup / 桌面启动

普通双击、账号检查、选中队列及计划启动直接进入当前进程的界面入口，不依赖 Explorer COM 再启动一次自身。保留显式开发入口 --launch-desktop 和既有 --desktop-session 参数；游戏启动仍由 Accounts/DesktopGameLaunch 校验 Windows 用户、会话与沙箱归属，连接组件按需要单独提权。

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
