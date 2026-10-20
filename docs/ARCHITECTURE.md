# Repository architecture / 仓库架构

Dustweave 是一个日常应用。源码、账号、连接、配置与验证在同一仓库内维护。装备助手、使徒运气防守、无限抽抽乐、恶魔猎人、音游、SECRET VISION、连连看、MANSION RUNAWAY 与潘多拉是 `src/` 下的自有模块；自动钓鱼和领地自动化也分别位于 `src/Fishing`、`src/Territory`。支持可选插件。

Dustweave is one daily application. Its accounts, workflows, connection, configuration and verification are maintained together. Equipment Assistant, Apostle Defense, Infinite Gacha, MANSION RUNAWAY, Pandora, Fiend Hunter, Rhythm, SECRET VISION and Sichuan are owned modules under `src/`; Fishing and Territory are also owned modules under `src/Fishing` and `src/Territory`. Optional plugins are supported.

## Dependencies / 依赖方向

```text
Desktop ── Core ── Accounts
    │         └── Compatibility ── Runtime + Shared/Ipc (embedded source)
    ├── Connection ── Core + Shared/Ipc (bounded helper mode)
    ├── ToolHost ── owned Fishing / Sichuan
    ├── tools/TableExporter + tools/RuntimeCheck
    ├── Equipment/desktop ── Equipment/core ── Equipment/connection
    ├── FiendHunter/desktop ── FiendHunter/core ── FiendHunter/compatibility
    ├── ApostleDefense/desktop ── ApostleDefense/core ── ApostleDefense/compatibility
    ├── InfiniteGacha/desktop ── InfiniteGacha/core ── InfiniteGacha/compatibility
    ├── Rhythm/desktop ── Rhythm/core ── Rhythm/compatibility
    └── hosted tool windows

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
| [`src/Equipment`](../src/Equipment), [`src/ApostleDefense`](../src/ApostleDefense), [`src/InfiniteGacha`](../src/InfiniteGacha), [`src/Mansion`](../src/Mansion), [`src/Pandora`](../src/Pandora), [`src/FiendHunter`](../src/FiendHunter), [`src/Rhythm`](../src/Rhythm), [`src/SecretVision`](../src/SecretVision), [`src/Sichuan`](../src/Sichuan) | Owned tool logic and integration, shipped with Dustweave | Requiring archived standalone downloads or separate releases |
| `assets` | Flow rules and observation specifications | Captures, inventories or whole client assemblies |
| [`src/Fishing`](../src/Fishing), [`src/Territory`](../src/Territory) | Hosted fishing and territory windows, settings and diagnostics | Replaying or deleting pending planting/sale journals during migration |
| `tools` | Data generation and development/diagnostic entry points | Another user-facing daily application |

All integrated automation participates in the host's ownership and enabled-state rules. Directory isolation does not authorize concurrent gameplay automation. Main-window language remains the single language setting for hosted tools.

Hosted child windows reserve the same control lease before connection and start handlers run. Initial connection waits for the parent to finish recording the child, and asynchronous connection/start preparation retains ownership until completion. Occupancy is sampled again after background observations to avoid releasing a newly acquired lease from an older observation. Only conflicting command buttons are disabled; settings, pause and close remain available. The `tool-activity` desktop smoke scope checks connection contention, parent/child handoff, pending settlement, idle release and process exit using synthetic hosts for every catalog tool.

### Hosted tower defense / 内置塔防窗口

使徒运气防守通过 `src/Desktop/ApostleToolWindow` 展示独立、非模态的内置工具窗口，承载 `ApostleToolPanel`，复用原工具的控制器、退火决策器与只读盘面控件。窗口先完成首帧显示，再后台按需准备统一连接；显示加载动画和当前阶段，可随时取消。连接成功不启用自动化，仍由用户点击开始。界面使用主程序的语言、主题和控件，切换主程序页面不结束运行。

`ApostleToolSession` prepares the connection and runs the existing controller off the UI thread. `ApostleToolPort` selects only the host's exact process/start/account/player and checks the shared owner before enabled writes. Connection preparation, automation and settlement hold the shared lease, disabling account switches, duplicate connections and competing automation. Connected idle windows release it. Starting revalidates the connection in case daily tasks changed the active module or account. Scheduling, plugin changes, OTA, window close and app exit use the same asynchronous cancellation/stop path. An exited game is never rediscovered or reconnected automatically.

设置保存在当前主程序数据目录的 `tools/apostle-defense/settings.json`；首次读取可以沿用独立版的有效偏好，不导入账号或执行证据。决策与错误日志写入 `live/diagnostics/apostle-defense`，按主程序的导出与空间清理规则处理。模块源码统一维护于 `src/ApostleDefense`，原独立仓库已归档；作者、许可证和版本记录仍保留。加载动画与首帧后的启动时机复用其他内置工具的实现，连接准备先等待当前游戏的有效身份。

Targeted checks: `test.ps1 -Groups ApostleIntegrated,ToolMenu,ActivityLease,ApostleAnnealing,ApostlePerformance,ApostleTimeToClear`. Set `DUSTWEAVE_UI_SMOKE_SCOPE=apostle` when running the isolated desktop `--smoke` entry to verify all three languages, both themes, compact board layout, paint-before-connect, loading animation, connection locks, idle release, start/pause and close during setup/run. These checks do not connect to a game.

## Local storage / 本地空间

机器生成的操作记录使用紧凑 JSON，设置和用户导出继续保持易读格式。超过 24 小时、位于明确记录或可重建组件目录的文件，可在本地 NTFS 卷上做透明无损压缩。文件内容、路径与原始时间戳不变，现有恢复读取及诊断脱敏导出无需新格式；不支持的卷、加密／链接／占用文件跳过。每次最多处理 128 个文件或约 32 MiB，未完成时在下一次空闲机会继续，不在操作线程上全量压缩。后续批次不重复核对历史记录、清理日志或更新缓存；完整保留期维护仍保持独立的十二小时间隔。占用文件不会挡住后续批次，整轮没有进展时返回正常维护间隔。压缩及稀疏文件的空间统计使用存储大小；普通文件沿用文件长度口径，不计磁盘簇尾部和文件系统元数据。

Windows `CrashDumps` 只识别织尘及其更新器的精确文件名：最多保留两份近期转储，多出的文件至少保留 24 小时，所有超过用户日志保留期（最多七天）的转储可清理。沙盒必须通过现有空闲与归属核验；其他程序的转储、系统目录、注册表、游戏数据均不删除。插件已安装内容不按缓存淘汰，更新事务仍使用原有独立保护。

Machine-generated operational records use compact JSON. Older records and reproducible components can use bounded, byte-preserving [NTFS compression](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_set_compression); recovery and diagnostic readers keep their original paths and formats. Statistics use [stored sizes](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew) for compressed/sparse files. Busy, linked, encrypted and unsupported files are skipped. Maintenance preserves original age and yields after 128 files or approximately 32 MiB. Continuations do not repeat journal reconciliation or log/update retention, nor postpone the independent twelve-hour retention clock. A busy prefix cannot starve later files; an exhausted pass without progress returns to the ordinary interval. Owned crash dumps have both a two-file limit and the configured log age limit, without touching unrelated application or sandbox system files. Checks: `StorageOptimization`, existing storage/log/export/OTA groups and the `support` desktop smoke.

“日志与缓存”按实际文件大小统计主数据目录、迁移后的缓存、隔离空间及本工具的 .NET 解压缓存，重叠路径不重复计算。沙盒中的游戏文件也计入总量，但不纳入清理。`DailyCacheStorage` 管理连接组件、商品表、规则、预编译和绑定缓存：闲置超过 7 天即可淘汰，7 天内通常保留最近两代，多出的代际至少保留 24 小时。成功编译后立即删除临时构建副本，失败构建保留至少 24 小时。OTA 仍按 `UpdateCacheRetention` 的数量、时间及事务保护规则处理。

`DailyStorageMaintenance` 只清理注册信息、真实路径与驱动归属一致且没有运行进程的隔离空间，并逐个获取操作锁。运行中的旧版程序保护解压缓存，账号、游戏存档、未知文件及待恢复的更新始终保留。无界面的双开工作进程结束后也会压缩成功步骤的日志并维护本地缓存，最多使用 20 秒；失败不影响队列结果。主窗口自动维护有 30 秒预算，重试时保留已完成的清理。超过保留期的已结算旧回执去掉 UI 树，保留身份、指令与结果；未确认回执不按成功处理。

缓存迁移在主窗口空闲、工具和隔离窗口关闭时执行，与更新下载和自动化互斥。先复制并逐文件校验 SHA-256，再原子切换 `cache-location.json`，最后清除原缓存；失败或取消不切换。账号、登录、操作归属、未确认回执及 OTA 恢复路径保持原位置；禁止迁入沙盒或 .NET 解压目录。迁移后的规则使用相对路径，不触发重复导表。针对性检查：`StorageMaintenance`、`CacheStorage`、`RuleData`、`TradeData`、`LiveBindings`、`LogCleanup`、`LogEvidence`、`UpdateRetention`，以及 `support` 界面检查。

Storage totals include the host, relocated caches, complete sandbox containers and this application's .NET extraction trees, without counting overlapping paths twice. Game and account data count toward usage but are never cleanup targets. Unused cache generations expire after seven days; newer generations retain the latest two with a 24-hour grace period for extras. Successful compiler scratch is discarded immediately. OTA retention and pending-transaction protection remain unchanged. Sandbox cleanup requires verified ownership, an idle driver scope and its own operation lock; active legacy hosts protect extraction trees. Headless workers perform bounded local maintenance after the queue ends. Metadata scans avoid materializing old UI snapshots; deletion still revalidates paths, timestamps, ownership and open files. Cache migration copies and verifies files before atomically switching the location; accounts, IPC, journals and OTA recovery do not move.

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

Weekly catalog updates preserve historical queue completion. Selected routes compare owned map coverage with individual server snapshots; only missing or unfinished cartridges need fresh reads. Exhausting absorption pauses collection if any weekly drops remain; it must not turn into a daily suppression sweep. Remaining suppression may continue only after all collection maps have confirmed empty drop lists. Unknown cartridges can be inspected once, with suppression held until coverage is complete; already verified cartridges are not rechecked. NPC quests and theft remain independent. No fallback enables walking collection when it is disabled.

### Concurrent queues / 集中并行队列

`DailyParallelSession` owns bounded dispatch and persists the host overview. `DailyParallelRuntime` launches account-bound Sandboxie workers; `DailyParallelWorker` reuses the normal coordinator and queue instead of duplicating daily logic. A per-account command lease stops orphan workers; process/start identity gates Show Game and optional end-of-queue cleanup. Host and worker control ownership prevents conflicting tool execution. Setup, failure handling and pending runtime acceptance are documented in [parallel execution](PARALLEL_EXECUTION.md).

Optional game exit applies when the queue reaches its end, including deferred/blocked stages; paused queues, pending commands and changed process/account identities retain the game. The completion window aggregates serial/parallel account results without blocking the next account. It omits routine successes while retaining draws, event battle fallback and unresolved tasks. Confirmed draw rewards are projected from server results (including duplicates before conversion), with names and rarity read from the installed client tables. Map labels travel with each stage as a small multilingual projection; displaying results does not rescan operation history.

勾选完成后退出游戏时，队列走完即可退出，部分环节暂缓不再静默阻止退出；暂停、未确认操作及身份变化仍保留现场。整批结果统一汇总，普通成功项目不重复展示，抽取结果、活动战斗回退和待处理事项保留。抽取明细来自已确认回执，地图名称随进度保存，查看时无需扫描历史日志。

Completion alerts use independently selectable Windows notifications, a modeless results window and sound. Serial and parallel runs notify once at the end of the batch; an alert failure cannot change the queue result or prevent another channel. Settings live in `desktop-behavior.json` (Windows and results on, sound off). The Windows channel is silent so it respects the separate sound choice and Windows quiet hours.

Independent isolated launch uses the selected account and the existing verified Sandboxie bootstrap. Its first-use explanation can be dismissed permanently. Operational log, IPC, queue and cache paths are sandbox-private; settings remain copy-on-write and verified same-account progress still syncs explicitly. An isolated window ignores the host's external cache-location preference and cleans only its own virtualized storage. New path isolation rules require the affected sandbox to be idle before configuration changes.

A Windows reinstall can invalidate DPAPI-encrypted observations and account slots. Unreadable observations are treated as absent age hints, never credentials. Saving a new authenticated login may replace an unreadable slot only through an explicit recovery plan with the same full account key from existing profile metadata. Readable slots belonging to another account remain protected; profile hints never authorize login or decryption. The normal encrypted atomic writer verifies the replacement before deleting its temporary backup.

Saffron farming gathers eligible trees and ore while a planted potato batch grows. A fully mature batch preempts travel only after native gathering and settlement finish; partial maturity does not trigger a return. After the selected potato rounds, a bounded pass finishes remaining resources before replanting and processing saffron. Already gathered targets are not revisited on regrowth, and repeated unreachable resources are reported and skipped without blocking later daily tasks. This runtime path reuses territory navigation, gathering settlement and surplus sales.

MANSION RUNAWAY and equipment open their actual bundled windows before connecting or preparing a module. The child inherits the desktop suite owner, data root and exact game process identity, but receives no usable tool fingerprint until preparation succeeds. A cancellable preparation banner holds the shared activity lease, locks only tool content, and leaves window controls available. Native preparation runs off the UI thread. Failures require an explicit retry; a restarted game requires reopening from the menu. Once ready, the normal hosted activity guard owns inventory reads and automation. Opening a window never starts gameplay or consumes equipment materials. Shared loading indicators animate only while visible and respect the system animation preference.

### Hosted Infinite Gacha / 内置无限抽抽乐

无限抽抽乐直接维护于 `src/InfiniteGacha`。既有双列表和规则窗口先显示，再由 `HostedToolPreparation` 准备连接；取消、失败重试和关闭均走共用生命周期。准备完成前不读取旧组件，成功连接也不开始抽取。`HostedToolPresentation` 同时服务装备助手与无限抽抽乐，统一主题和弹窗；全软件语言由 `HostedToolLocaleBridge` 同步。

The hosted window retains account/pool preferences and existing IPC identities. Shared preparation pins the exact game process and validates its identity before activation. `HostedToolActivity` guards connection/start handlers and releases ownership only when idle. `tool-preparation` renders the actual window in three languages and two themes, checks cancellation/retry/close, verifies no premature transport reads or automatic rolls, and captures loaded results. The tool's own synthetic smoke retains target/filter/stop/notification regression coverage.

Fiend Hunter is owned in `src/FiendHunter`. Its window paints before shared connection preparation, uses the host theme and language, and holds the same activity lease during preparation or automation. Selecting a monster never starts play. Durable settings and bounded combat diagnostics live in `tools/fiend-hunter`; generated components use the host suite cache, including cache relocation. The established named-pipe identity is retained solely for transport, and known old diagnostic files are migrated without importing account or execution state.

### SECRET VISION and Sichuan

Owned modules under src/SecretVision and src/Sichuan retain their runtime pipe identities. Tool windows use shared preparation, theme and locale. Preferences live under tools/<id>; known legacy preferences are imported only when no owned file exists. Diagnostics and captured tile images live under tools/<id>/diagnostics and participate in host export and retention. Weekly minigames prepare the selected suite module directly in the daily process; control ownership and fresh readiness are still required.
