# Repository architecture / 仓库架构

Dustweave 是一个日常应用。源码、账号、连接、配置与验证在同一仓库内维护；`standalone/` 中的小游戏与辅助工具保留独立项目边界。支持可选插件。

Dustweave is one daily application. Its accounts, workflows, connection, configuration and verification are maintained together. Independent games/tools retain their own boundaries under `standalone/`. Optional plugins are supported.

## Dependencies / 依赖方向

```text
Desktop ── Core ── Accounts
    │         └── Compatibility ── Runtime + Shared/Ipc (embedded source)
    ├── Connection ── Core + Shared/Ipc (bounded helper mode)
    ├── ToolHost ── standalone fishing / sichuan
    ├── tools/TableExporter + tools/RuntimeCheck
    └── standalone tool windows

assets/flows + assets/rules → Core resources and package data
assets/specs               → package connection/specs
Tests                     → Core + explicit shared source cases
```

`src/Runtime` 是运行时编译输入，不是遗漏了项目文件；这些源码由适配器根据用户当前客户端编译。`src/Shared/Ipc` 在桌面与游戏运行环境之间共享协议，不能直接把 .NET 8 桌面程序集注入游戏。`Connection` 与若干辅助模式仍受独立进程的生命周期约束，正式包通过同一 EXE 的 utility 入口调用它们。

Runtime sources are compiled by the adapter against the user's client. Shared IPC sources bridge the desktop and game runtimes. Connection and utility modes retain bounded process lifetimes; the distribution calls them through the same application executable.

## Ownership / 维护边界

| Area | Change here | Avoid |
| --- | --- | --- |
| `src/Desktop` | UI, themes, localization, tool coordination | Daily business duplicated in click handlers |
| `src/Core` | Stages, scheduling, recovery, trading, optional extension contracts | External plugin implementations |
| `src/Accounts` | Identity, protected sessions and launching | Copying user credentials into source |
| `src/Connection`, `Compatibility`, `Runtime`, `Shared` | Adaptation, command delivery, observations and shared IPC | Ad-hoc second connections bypassing host ownership |
| `assets` | Flow rules and observation specifications | Captures, inventories or whole client assemblies |
| `standalone` | Reviewed independent tool updates and host integration | Moving daily business into an unrelated tool |
| `tools` | Data generation and development/diagnostic entry points | Another user-facing daily application |

All integrated automation participates in the host's ownership and enabled-state rules. Directory isolation does not authorize concurrent gameplay automation. Main-window language remains the single language setting for hosted tools.

Hosted child windows reserve the same control lease before connection and start handlers run. Initial connection waits for the parent to finish recording the child, and asynchronous connection/start preparation retains ownership until completion. Occupancy is sampled again after background observations to avoid releasing a newly acquired lease from an older observation. Only conflicting command buttons are disabled; settings, pause and close remain available. The `tool-activity` desktop smoke scope checks connection contention, parent/child handoff, pending settlement, idle release and process exit using synthetic hosts for every catalog tool.

### Hosted tower defense / 内置塔防窗口

使徒运气防守通过 `src/Desktop/ApostleToolWindow` 展示独立、非模态的内置工具窗口，承载 `ApostleToolPanel`，复用原工具的控制器、退火决策器与只读盘面控件。窗口先完成首帧显示，再后台按需准备统一连接；显示加载动画和当前阶段，可随时取消。连接成功不启用自动化，仍由用户点击开始。界面使用主程序的语言、主题和控件，切换主程序页面不结束运行。

`ApostleToolSession` prepares the connection and runs the existing controller off the UI thread. `ApostleToolPort` selects only the host's exact process/start/account/player and checks the shared owner before enabled writes. Connection preparation, automation and settlement hold the shared lease, disabling account switches, duplicate connections and competing automation. Connected idle windows release it. Starting revalidates the connection in case daily tasks changed the active module or account. Scheduling, plugin changes, OTA, window close and app exit use the same asynchronous cancellation/stop path. An exited game is never rediscovered or reconnected automatically.

设置保存在当前主程序数据目录的 `tools/apostle-defense/settings.json`；首次读取可以沿用独立版的有效偏好，不导入账号或执行证据。决策与错误日志写入 `live/diagnostics/apostle-defense`，按主程序的导出与空间清理规则处理。独立项目的作者、许可证和版本记录仍保留。

Targeted checks: `test.ps1 -Groups ApostleIntegrated,ToolMenu,ActivityLease,ApostleAnnealing,ApostlePerformance,ApostleTimeToClear`. Set `DUSTWEAVE_UI_SMOKE_SCOPE=apostle` when running the isolated desktop `--smoke` entry to verify all three languages, both themes, compact board layout, paint-before-connect, loading animation, connection locks, idle release, start/pause and close during setup/run. These checks do not connect to a game.

## Local storage / 本地空间

“日志与缓存”统计当前数据目录的全部文件大小，以及迁移后的可重建缓存；日志清理白名单的大小不再充当总占用。`DailyCacheStorage` 单独管理连接组件、商品表、规则、预编译和绑定缓存。按组件保留最近使用的两代缓存；已结束的旧 OTA 备份可清理，最新备份、未完成及未知状态的更新保留。后台清理只扫描这些缓存目录。

缓存迁移在主窗口空闲、工具和隔离窗口关闭时执行，与更新下载和自动化互斥。先复制并逐文件校验 SHA-256，再原子切换 `cache-location.json`，最后清除原缓存；失败或取消不切换。账号、登录、操作归属、未确认回执及 OTA 恢复路径保持原位置。迁移后的规则使用相对路径，不触发重复导表。针对性检查：`CacheStorage`、`RuleData`、`TradeData`、`LiveBindings`、`LogCleanup`，以及 `support` 界面检查。

Storage totals cover all files in the active data root and relocated rebuildable caches. Cleanup retains two recent generations per component and the latest update backup; pending or unknown update transactions remain protected. Cache migration copies and verifies files before atomically switching the location. Account data, IPC ownership, operation journals and OTA recovery paths do not move. Maintenance runs off the UI thread and cannot overlap automation, tools or update downloads.

Low-space reminders check the data and current cache volumes without enumerating files every five minutes. Below 1 GiB, a visible primary window measures full usage asynchronously before showing modeless cleanup/migration advice; it never pauses active or scheduled work. Sandbox workers never duplicate this prompt. Dismissal persists in `storage-warning.json` and follows the local 08:00 day boundary; otherwise a continuing shortage is shown at most hourly. IO failures never become a task/connection failure. `StorageWarning` covers threshold, volume deduplication and local-day persistence; the `support` smoke renders and exercises the dialog in all three languages and both themes.

## Extension boundary / 扩展边界

本体提供通用插件接口。插件清单声明入口程序集、接口版本、文件哈希和适用环节；本体在加载前校验，缺少插件时隐藏依赖它的选项。插件不能绕过账号身份、操作归属或任务恢复检查。

The host exposes a generic plugin interface. A manifest declares its entry assembly, interface version, file hashes and applicable stages. The host validates it before loading and hides unavailable options. Plugins use the same account identity, command ownership and recovery rules as built-in tasks.

源码清单仅记录本仓库内的路径和校验值。内置工具保留原作者、许可证与版本记录。
The source manifest records repository-local paths and checksums. Integrated tools retain their authorship, licenses and version records.
## Isolated instances / 隔离实例

Optional Sandboxie-Plus instances keep one existing Dustweave pipeline per sandbox. The ordinary window remains the authority for account editing, schedules and OTA. Process selection and child launching verify the driver-reported sandbox, not a command-line hint. Connection ownership and command journals stay inside that instance. Account task summaries and validated weekly server collection snapshots are exchanged by the ordinary host before isolated launch, after a worker finishes, and before an ordinary queue starts. See [isolated instances](ISOLATED_INSTANCES.md) for tested scope and limitations.

可选的 Sandboxie-Plus 实例各自运行原有单游戏链路；普通窗口管理账号、计划和更新。进程查询及子进程启动均按驱动报告的沙箱归属检查。每个实例仍保持单一自动化所有者，不允许两个工具同时控制同一游戏。

Sandbox root lookup follows `Box_InitPaths` / `Conf_Get(box, "FileRootPath", 0)`: read the first effective UTF-16 value, including template/global fallback, instead of joining inherited roots into a path. Account ownership checks still require the managed absolute root and matching protected identity. Shared-path lists retain full enumeration; scalar root handling does not relax those checks.

普通启动与隔离启动共享同账号的日常完成摘要，以及按角色、服务器和周周期核验的地图服务器快照。摘要逐环节合并，跨日后不沿用完成状态；地图快照只复用匹配的周周期，旧数据不覆盖新数据。连接、锁、登录凭据和未确认操作回执不参与交换，另一实例的队列不能直接作为本实例的接续队列。界面同步在后台执行并限频，不扫描历史操作日志。

Weekly catalog updates preserve historical queue completion. Selected routes compare owned map coverage with individual server snapshots; only missing or unfinished cartridges need fresh reads. Exhausting absorption defers remaining drops, while available suppression continues across maps. No fallback enables walking collection when it is disabled.

### Concurrent queues / 集中并行队列

`DailyParallelSession` owns bounded dispatch and persists the host overview. `DailyParallelRuntime` launches account-bound Sandboxie workers; `DailyParallelWorker` reuses the normal coordinator and queue instead of duplicating daily logic. A per-account command lease stops orphan workers; process/start identity gates Show Game and optional end-of-queue cleanup. Host and worker control ownership prevents conflicting tool execution. Setup, failure handling and pending runtime acceptance are documented in [parallel execution](PARALLEL_EXECUTION.md).

Optional game exit applies when the queue reaches its end, including deferred/blocked stages; paused queues, pending commands and changed process/account identities retain the game. The completion window aggregates serial/parallel account results without blocking the next account. It omits routine successes while retaining draws, event battle fallback and unresolved tasks. Confirmed draw rewards are projected from server results (including duplicates before conversion), with names and rarity read from the installed client tables. Map labels travel with each stage as a small multilingual projection; displaying results does not rescan operation history.

勾选完成后退出游戏时，队列走完即可退出，部分环节暂缓不再静默阻止退出；暂停、未确认操作及身份变化仍保留现场。整批结果统一汇总，普通成功项目不重复展示，抽取结果、活动战斗回退和待处理事项保留。抽取明细来自已确认回执，地图名称随进度保存，查看时无需扫描历史日志。

Completion alerts use independently selectable Windows notifications, a modeless results window and sound. Serial and parallel runs notify once at the end of the batch; an alert failure cannot change the queue result or prevent another channel. Settings live in `desktop-behavior.json` (Windows and results on, sound off). The Windows channel is silent so it respects the separate sound choice and Windows quiet hours.

Independent isolated launch uses the selected account and the existing verified Sandboxie bootstrap. Its first-use explanation can be dismissed permanently. Operational log, IPC, queue and cache paths are sandbox-private; settings remain copy-on-write and verified same-account progress still syncs explicitly. An isolated window ignores the host's external cache-location preference and cleans only its own virtualized storage. New path isolation rules require the affected sandbox to be idle before configuration changes.

A Windows reinstall can invalidate DPAPI-encrypted observations and account slots. Unreadable observations are treated as absent age hints, never credentials. Saving a new authenticated login may replace an unreadable slot only through an explicit recovery plan with the same full account key from existing profile metadata. Readable slots belonging to another account remain protected; profile hints never authorize login or decryption. The normal encrypted atomic writer verifies the replacement before deleting its temporary backup.

Saffron farming gathers eligible trees and ore while a planted potato batch grows. A fully mature batch preempts travel only after native gathering and settlement finish; partial maturity does not trigger a return. After the selected potato rounds, a bounded pass finishes remaining resources before replanting and processing saffron. Already gathered targets are not revisited on regrowth, and repeated unreachable resources are reported and skipped without blocking later daily tasks. This runtime path reuses territory navigation, gathering settlement and surplus sales.

MANSION RUNAWAY and equipment open their actual bundled windows before connecting or preparing a module. The child inherits the desktop suite owner, data root and exact game process identity, but receives no usable tool fingerprint until preparation succeeds. A cancellable preparation banner holds the shared activity lease, locks only tool content, and leaves window controls available. Native preparation runs off the UI thread. Failures require an explicit retry; a restarted game requires reopening from the menu. Once ready, the normal hosted activity guard owns inventory reads and automation. Opening a window never starts gameplay or consumes equipment materials. Shared loading indicators animate only while visible and respect the system animation preference.
