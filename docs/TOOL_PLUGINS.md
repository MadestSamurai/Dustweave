# 多插件与原生工具协议

日常插件的选择记录使用 `Enabled` 指纹数组；旧 `Active` 单值会作为一项选择读取。启用新插件保留其他 ID 的选择，同 ID 升级替换自身版本。相同任务 capability 的两个日常插件不重复执行，启用前明确提示用户选择。独立停用、移除不影响其他选择。组合加载先进行离线子进程试加载，运行中的宿主和工作进程固定同一组指纹，重启后加载新选择。

工具插件使用独立注册位置 `tool-plugins/<id>/extensions`，不会覆盖日常插件。通用清单维持 API 4、bridge API 1。`kind: tool` 接受 `inventory_read` 与 `tool_operations` capability；后者必须有已校验 Hook。私有算法和实现不进入宿主源代码。

## 共享运行时工具 v2

从 1.6.2-local.3 开始，推荐 `toolApiVersion: 2`、`uiApiVersion: 1`、`runtime: host-net8.0-windows-x64`。清单声明 `toolAssembly: tool/*.dll` 与 `toolType`，所有依赖、规则和内核继续列入文件哈希。插件引用公开的 `Dustweave.ToolPluginApi`，实现 `IToolPluginV2`；不携带 .NET、WPF、宿主或 UI API 的副本。

宿主通过自己的 `Dustweave.exe --tool-plugin <package> <fingerprint> ...` 创建独立子进程。该入口在主程序初始化之前分流，不访问账号库、不触发更新恢复、不获取主窗口单实例锁，也不初始化游戏连接。子进程再验证包、最低宿主版本、工具和界面 API 后才加载代码。`AssemblyLoadContext` 仅解析清单内的插件程序集，UI API 与 WPF/.NET 使用宿主的类型身份。Portable 使用宿主内置运行时，Lite 使用宿主已经要求的桌面运行时。两种成品都必须验证。

`ToolPluginContext` 只提供包目录、外观状态、变化通知、宿主窗口外框和确认对话框。控件样式、色表（含高对比度）、字体、滚动条都来自现有宿主；没有复制 UI 或私有游戏逻辑。工具进程仍不是系统权限沙盒，只能安装可信插件。计算崩溃不在主窗口进程内执行；宿主可关闭自己创建的子进程并重新打开。

v1 独立 EXE 工具仍可加载（`standalone-windows-x64`、`toolExecutable`），没有把新工具降级为 v1 或依赖系统临时安装运行时的分支。最低宿主版本不满足时使用现有更新提示，不执行不兼容代码。

原生工具程序通过 `--dustweave-probe <result.json>` 完成无游戏操作的自检，返回 `status: passed`、`toolApiVersion: 1`。正式打开使用 `--dustweave-session <exchange>` 与 `--parent-pid <pid>`。原生窗口出现后写入 `ready.json`，字段为 `ready: true`、`processId`（自身进程）、`toolApiVersion: 1`。宿主只显示自己创建的工具窗口；协议不包含网页地址、浏览器或 HTTP 服务。

工具请求 `request.json` 使用唯一 N 格式 GUID 和 `operation: inventory`。宿主取得短期控制锁，连接已打开的当前游戏并核对身份后返回 `response.json`，字段为同一个 `id` 以及 `result` 或 `error`。目前只提供角色、觉醒、服装、装备四种库存，调用完清除交换内容；不通过协议提供登录凭证或任意游戏命令。该协议是接口边界，不是操作系统安全沙盒，用户仍只能安装信任的插件。

工具窗口计算在独立进程运行，库存读取结束即释放游戏控制；不会因为启用多个插件而并发操作游戏。同一工具会话的账号身份变更要求重新打开工具。未连接、短暂超时或取消可以再次刷新，不改变账号配置。`tool_operations` 通过已有通用 Hook 请求处理；库存、收藏、代码、预设读取及预览/保存由插件的允许操作列表约束。写入前显式确认，核对账号、槽位指纹及预览令牌，保留原内容并读回核验。

## 外观同步

启动传递 `--language` 与 `--appearance system|light|dark`，并保留 v1 的 `--dark`。每次启动有独立交换目录，其中 `appearance.json` 使用 `SchemaVersion: 1`、`Language`、`Appearance`。宿主切换时原子替换；工具子进程以 500ms 间隔读取至多 4KiB，保留最后一次有效状态。部分写入、未知版本和无效值均不影响计算或请求，也不覆盖宿主设置。系统主题和高对比度变化复用 `DailyTheme`。插件按变化通知重新翻译显示内容，不重建方案或计算状态。

v2 仍使用相同的请求/回执、父进程和就绪协议，只将就绪与试加载报告中的 `toolApiVersion` 改为 2。试加载不创建窗口、不访问游戏；打开窗口的成品验收另外执行。

离线验证：`PluginStore`、`ToolPlugins`、`LocalizationArchitecture`、`UserText`；界面使用 `plugins` 和 `localization` 范围。实机读取与应用的验证需独立记录。
# 工具计算诊断

工具可在本次交换目录保存 `tool-error.json`、`tool-input.json`、`tool-failed-input.json`、`tool-result.json`。异常应包含时间、操作阶段、完整异常和对应输入标识；只保留最新记录，单文件不超过 8 MiB。输入仅包含计算所需游戏数据，不包含登录信息、凭据或原始 IPC 响应。记录失败不得阻断工具功能。

宿主诊断导出只收录最近 16 个合法会话中的上述固定文件，执行常规脱敏和 ZIP 压缩。`request.json`、`response.json` 与任意额外文件不在导出范围。旧版 `adaptation-result.json` 作为已有结果证据保留支持。

