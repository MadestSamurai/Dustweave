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

## Extension boundary / 扩展边界

本体提供通用插件接口。插件清单声明入口程序集、接口版本、文件哈希和适用环节；本体在加载前校验，缺少插件时隐藏依赖它的选项。插件不能绕过账号身份、操作归属或任务恢复检查。

The host exposes a generic plugin interface. A manifest declares its entry assembly, interface version, file hashes and applicable stages. The host validates it before loading and hides unavailable options. Plugins use the same account identity, command ownership and recovery rules as built-in tasks.

源码清单仅记录本仓库内的路径和校验值。内置工具保留原作者、许可证与版本记录。
The source manifest records repository-local paths and checksums. Integrated tools retain their authorship, licenses and version records.
## Isolated instances / 隔离实例

Optional Sandboxie-Plus instances keep one existing Dustweave pipeline per sandbox. The ordinary window remains the authority for account editing, schedules and OTA. Process selection and child launching verify the driver-reported sandbox, not a command-line hint. Game account preferences, connection ownership and application history stay inside that instance. See [isolated instances](ISOLATED_INSTANCES.md) for tested scope and limitations.

可选的 Sandboxie-Plus 实例各自运行原有单游戏链路；普通窗口管理账号、计划和更新。进程查询及子进程启动均按驱动报告的沙箱归属检查。每个实例仍保持单一自动化所有者，不允许两个工具同时控制同一游戏。

### Concurrent queues / 集中并行队列

`DailyParallelSession` owns bounded dispatch and persists the host overview. `DailyParallelRuntime` launches account-bound Sandboxie workers; `DailyParallelWorker` reuses the normal coordinator and queue instead of duplicating daily logic. A per-account command lease stops orphan workers; process/start identity gates Show Game and optional successful-completion cleanup. Host and worker control ownership prevents conflicting tool execution. Setup, failure handling and pending runtime acceptance are documented in [parallel execution](PARALLEL_EXECUTION.md).
