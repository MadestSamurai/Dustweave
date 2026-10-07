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
| `package.ps1` | Complete private release package; requires locally installed client data |

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

默认使用正式 X.Y.Z 版本号，不使用 preview 后缀；版本号与仓库公开权限独立。Dustweave 保持私有，包不包含外部插件实现。

Use stable X.Y.Z version numbers by default. Version labels do not change repository visibility; Dustweave remains private and excludes external plugin implementations.

## Passive UI and preview recovery / 背景提示与预览收尾

LivePolicy.BuildUiToken is the shared native input token source. Passive surfaces stay in observations and diagnostics, but do not participate in the token, input blockers or navigation stability. Keep its passive list aligned with ui-policy.json; the regression suite checks this contract. Do not add retry loops to compensate for HUD notices.

MonsterHuntUI returns through its observed _objBackButton. A rejected business preview is cancellable only with explicit local non-submission proof, unchanged process/account/cycle and popup content, an enabled native Cancel button and no related transaction events. Cleanup has its own durable result and never marks the business complete. Unknown dispatches and unrelated dialogs are not cancelled.

背景提示不进入操作校验，不能靠增加等待或重试处理。确认失败的收尾与业务成功分开记录；已有消费或结果不明不能通过取消弹窗来推定成功。本轮回归使用合成现场；真实每日免费抽取与魔兽返回仍需后续正常使用验收。
## User-visible notices

Keep execution errors, queue records, action IDs and game-provided names unchanged. Translate at the desktop presentation boundary through `DailyUiText` / `DailyLanguage`; do not localize protocol or recovery values.

Register notices in `src/Desktop/Localization/notices.json` with all three locales. Dynamic notices declare numbered placeholders and typed `parameters`: `number`, `value` (opaque account/item/path text), `stage`, or `message` (nested registered notice). An optional `source` holds an existing execution format when the display can omit an internal identifier; the original remains in diagnostics. Do not add unrestricted string replacement or translate arbitrary captured values.

The desktop `--smoke` check exercises every registered notice in all three languages, refreshes existing WPF bindings, and verifies nested messages, account names, paths and original diagnostics. Inspect `notice-languages.json`, `localization.json` and the `locale-*-notice.png` captures. New backend progress formats need a corresponding notice and a rendered sample in this check; adding translations must not alter game actions.
Scheduling, release notes and OTA packaging are documented in [SCHEDULING_AND_UPDATES.md](SCHEDULING_AND_UPDATES.md).
