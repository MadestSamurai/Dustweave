# Repository status / 仓库状态

## 1.1.0-beta published / 当前版本

2026-10-10：`1.1.0-beta` 已由 GitHub Actions 发布为普通 Release，并通过受限签名通道上线国内 OTA。新增图标、明确的错误分类与下一步、诊断 ZIP 导出及联系方式、下载入口和更高默认窗口；包含按账号返回任务选择及准备流程改进。最终 2,624 项检查（68 组）、双版本成品、扩展兼容、实际旧更新器升级／回退／恢复及公网差分重建均通过。本次未操作真实游戏，受影响用户的具体故障仍需新日志确认。详见 [发布验收记录](releases/1.1.0-beta-deployment.md)。以下为历史阶段记录。

The current release is **1.1.0-beta**, available as a normal GitHub Release and through domestic signed OTA. Package, extension, prior-updater recovery and public-download checks passed. See the linked receipt for validation boundaries; entries below describe earlier stages.

## Public-version OTA published / 公开版 OTA 已发布

2026-10-09：升级基线按用户要求从首个公开版 `1.0.0-beta` 开始，不再维护更早内部版本的升级兼容。`1.0.1-beta` 的签名清单、双版本完整包与直接差分已上线；Portable 差分 20,065,694 字节，Lite 差分 1,345,171 字节。真实 `1.0.0-beta` 成品更新器完成两版仅差分缓存升级、启动失败回退与中断恢复，账号及插件哨兵文件保持不变。证据：`artifacts/ota-1.0.1-from-1.0.0-delta/results.json`。

独立 .NET OTA 签名发布通道已安装，使用专用低权限账号及受限 SSH 命令。43 项检查在 Windows 和服务器 Linux 均通过。15:53（香港时间）通过新通道完成 `1.0.1-beta` 发布；两版由正式下载器完成公网差分下载和重建校验，缓存与 Range 检查通过。临时管理密钥已撤销并验证拒绝认证，长期受限通道仍正常。见 [发布记录](releases/1.0.1-beta-deployment.md) 与 [通道使用说明](OTA_PUBLISH_CHANNEL.md)。

The public-version update chain is live at 1.0.1-beta. The new constrained .NET channel passed 43 checks on Windows and Linux and performed the real deployment. Both public differential downloads and reconstructions passed using the production transport. The temporary administrator key was revoked; routine publishing retains only the constrained transport identity.

## 1.0.1-beta delivery / 反馈修复交付

2026-10-09：新增三语言、深浅主题的首次连接教程与账号页入口，说明正常退出后保存账号，以及后续从织尘启动的顺序。修复合并周路线任务优先导致未压制地图反复遇敌；先执行已启用且不影响任务后续击杀的收集，NPC 单独运行也准备压制／藏身，并在意外撤退后重新读取任务步骤。活动代币消费者增加任务领奖到邮箱领取的依赖，保留每页访问缓存。独立与内置装备助手同步修复无通信组件时提前读快照的超时分支，有限重试只读通信，不重发消费命令。

定向验证：392 项日常检查（6 组）通过；装备助手 283 项主用例及目录传输、连接恢复、结果接续等专项检查通过。Dustweave 与独立装备助手桌面构建通过。新增连接教程已通过三语言、深浅主题及账号页跳转的隔离界面检查。此轮没有连接游戏或消耗资源；完整打包的 2,506 项检查（64 组）通过，双版本已经由 GitHub Actions 发布为普通 Release；标签 `v1.0.1-beta` 对应 `b439e3d2bdf1bfbd395844eddfd117008ea40e22`。

Focused synthetic regressions, desktop builds and the three-language themed connection guide passed. Both flavors passed full packaging and GitHub Actions and were published as a normal Release; live validation remains separate. The reported equipment timeout has no user stack trace yet; the confirmed cold-connection defect is fixed, without claiming it explains every possible timeout.

## 1.0.0-beta publication / 首次公开版

2026-10-09：主体采用 MIT 许可证，公开版使用 `v1.0.0-beta` 标签和普通 GitHub Release。Portable、Lite 均完成完整打包及三语言、深浅主题、连接和内置工具边界检查。全量 2,492 项合成检查（64 组）通过；源码历史与既有发行包完成凭据及载荷边界复查。README 使用该版本真实界面的隔离演示截图。

The first public MIT release uses a normal GitHub Release despite its beta label. Both flavors passed complete package gates, with 2,492 synthetic checks across 64 groups. Optional extension implementations, account captures and game assemblies remain excluded. This delivery did not operate the live game; historical live evidence and remaining limitations are recorded separately.

当前说明与安装方式见 [1.0.0-beta](releases/1.0.0-beta.md)。该版本首次发布时采用手动安装并保留旧数字版本 OTA 清单；后续公开版升级链路见本文顶部状态。国内服务器继续仅用于 OTA。以下为历史发布和开发记录，不代表当前仓库可见性。

## 0.9.13 publication / 0.9.13 发布

2026-10-09：实现已提交到私有 main（`280bf834275dcdf5dbe1b495c9470e0ceee157e1`）。[GitHub Actions](https://github.com/MadestSamurai/Dustweave/actions/runs/37806015969) 的构建、2,371 项合成检查（63 组）及来源边界检查全部通过。Portable／Lite 沿用已验证成品；1,092 项构建输入中仅根目录 README 和 CHANGELOG 在发布整理时改变，不影响包内文件或程序。

两个成品均已通过使用真实 0.9.0 更新器的完整升级，以及新版更新器的差分安装、启动失败回退和中断恢复。源码与包均不包含外部插件实现。国内 OTA 与 GitHub 正式发布结果另见 [发布记录](releases/0.9.13-deployment.md)。

下面各节记录对应开发阶段的历史状态；当时的“尚未发布”不表示当前发行状态。实机验收限制仍以各节记录为准。

## Local plugin lifecycle / 本地插件管理

2026-10-08：新增三语言、深浅主题的插件管理页，支持 ZIP 导入、兼容性检查、独立进程试加载／连接组件编译、启停及版本回退。插件独立存储，运行队列固定原版本；变更在重启 Dustweave 后生效，无需重启游戏。主程序更新前核对插件版本范围。341 项相关检查、后续 79 项插件专项及隔离界面检查通过；没有发布或执行真实游戏任务。详见 [插件说明](PLUGINS.md)。当前每次启用一个本地插件包，签名与在线插件更新尚未接入。

## 0.9.8 registration recovery / 登记恢复候选

修复普通桌面看不到外层登记时误判账号冲突的问题。以配置目录、内部绑定和可解密账号记录自动恢复缺失登记，真正冲突仍拒绝接管。本机三份缺失登记已经恢复，实际桌面凭据交接通过；48 项针对性检查通过。完整包验证及真实登录仍按各自证据记录，不以元数据恢复代替登录成功。
## 0.9.12 puzzle events and hunting / 拼图与狩猎

每日活动新增可独立关闭的拼图板选项，按现有活动代币及当前剩余格数调用游戏批量翻开。使用服务器盘面进度核对翻格与扣款，整板由游戏自动刷新，有余币继续下一板；不触发手动刷新，也不购买代币。新活动按游戏当前目录、周期和规则读取，不固定本期活动编号。旧设置自动迁移，保留各账号已关闭的活动总开关。

免费白饭直接按照金币／史莱姆加成日与章节狩猎优先级分配，移除每日首场普通狩猎及其任务进度前置门槛。圣石逻辑保持原有免费次数与预算。

已通过 285 项相关合成检查、581 项原生命令边界检查，更新前后两套客户端连接组件编译及新版 11 套证据配置生成。实际拼图批量、自动整板刷新及狩猎执行仍待使用验证；未接管或中断正在运行的游戏队列。版本为本地交付，不代表 GitHub／OTA 发布。

## 0.9.7 sign-in handoff / 登录交接候选

普通与隔离窗口已补齐双向凭据交接、采集时刻保留及防止旧状态回写。2,263 项合成检查通过；真实 Sandboxie 导出传输已核对。此前 0.9.6 的标题页连接测试未覆盖登录认证，后续发现 SDK 返回无效令牌；旧验收不代表登录通过。新版本真实账号重新登录及切回普通窗口仍待验证。详见 [隔离窗口说明](ISOLATED_INSTANCES.md)。本地候选不代表已发布。

## 0.9.6 updated client / 更新后兼容候选

2026-10-08：本地安装的新客户端 MVID `132cda4f-d57f-4991-8b09-6310f09b7fe5` 已核对。日常执行及证据读取统一适配，407 类型／824 成员、完整日常 164 项读取规则通过；12 个按需模块均可针对新版编译，初始连接仍不预载小游戏。2,250 项合成回归（62 组）通过。

普通实例已到 TOUCH TO START；两份 Sandboxie 实例同时启动并独立连接，均停在游戏协议弹窗，未进入游戏、未发送日常操作。主机 14 个账号槽位文件不变。集中队列调度、登录身份与实际日常结果仍待后续使用验证。新版移除了魔兽页快速战斗入口，该环节会说明原因并标记待处理，不替代为实际战斗，也不阻止其他环节。见 [更新适配清单](CLIENT_UPDATES.md)。本地候选不代表 GitHub／OTA 已发布。

Dustweave 1.0.0-beta is prepared for public distribution under MIT. The .NET application, integrated tools, synthetic tests and packaging scripts are maintained here. Third-party licenses are preserved. Earlier private-release records below are historical.

Dustweave 1.0.0-beta 按 MIT 许可证整理公开分发；本体、内置工具、合成测试与打包脚本统一维护。以下旧版私有发布记录仅描述当时状态。

## Interface / 界面

The desktop supports three languages, light/dark/system appearance, account-scoped task settings and a continuous execution timeline. Diagnostics explains connection status and next steps. Optional plugins use the generic extension interface; unavailable options are hidden. README shows the light screenshot by default, with a separate dark preview.

桌面提供三种语言、浅色／深色／跟随系统外观、分账号设置与连续任务时间线。诊断页提供连接说明和下一步提示。可选插件通过通用接口接入，不可用的选项不展示。README 默认展示浅色图，深色图单独展开。

## Verification / 验证

Source builds and synthetic tests do not require the game or connect to it. Isolated WPF checks cover both themes and all three languages. Actual game compatibility and packaged-binary verification are separate checks; UI screenshots and synthetic tests do not substitute for live-game validation.

源码构建、合成回归和隔离 WPF 检查均不会连接游戏。界面验证覆盖双主题及三种语言；成品验证和实机兼容验证分别进行，不以演示截图或合成测试替代实机结论。0.9.0 已通过两种成品的隔离升级验收；正式发行包由 package.ps1 生成。

See [architecture](ARCHITECTURE.md), [development](DEVELOPMENT.md) and [design rules](DESIGN.md).
## 0.9.0 / 2026-10-07

Account queues now start from the Accounts page. An opt-in scheduler can launch the app through the current user's Windows Task Scheduler. Bundled release notes appear once per version; the OTA client downloads official packages in the background and asks before restarting while idle. See [scheduling and updates](SCHEDULING_AND_UPDATES.md).

多账号队列的主入口已移至账号页。定时页面通过当前用户的 Windows 计划任务启动软件；版本说明每个版本只显示一次；OTA 后台下载官方包，空闲时确认重启。已通过 2,104 项合成检查（56 组）、隔离 WPF 界面验收和真实单文件成品的升级／回退测试。0.9.0 正式双版本及国内 OTA 已发布；未登记真实定时计划或执行游戏队列。完整签名与发布规则见 [OTA 发布流程](OTA_RELEASE.md)。

### OTA acceptance · 2026-10-07

- Synthetic regression: 2,104 checks in 56 groups; `artifacts/tests-20261007-090345-6564/results.json`.
- Packaged application checks: Lite and Portable upgrade from an isolated 0.8.17 copy, failed new-application startup restores the previous executable, and an injected interrupted transaction recovers on the next real executable launch. Account and plugin sentinel files remain unchanged. Evidence: `artifacts/ota-package-check-20261007-r3/results.json`.
- Actual 0.9.0 single-file acceptance candidates: `artifacts/ota-acceptance-20261007-r4/`. These reuse 0.8.17's already packaged static data for updater acceptance; they are not a new game-compatibility release.
- Three-language WPF checks pass, including 15 schedule/update checks with zero game commands and zero registered Windows tasks; `artifacts/ota-ui-check-20261007-final/`.
- Signature/hash-verified domestic-site payload is prepared at `artifacts/ota-acceptance-20261007-r4/site/`. It has **not** been uploaded. GitHub publishing remains deferred.
- The independent installer now disposes its application mutex before restarting either version. Atomic replacements tolerate short file locks and skip identical content; failures retain exact-file diagnostics.

以上是本机隔离成品测试，不代表生产 HTTPS 下载、真实断电或游戏日常队列已经验证。用户的实际 0.8.17 程序、账号和游戏未被升级测试修改。
## Published 0.9.0 / 已发布 0.9.0

2026-10-07：正式包已发布到私有 GitHub Releases，签名清单和国内 OTA 已上线。首次安装及手动下载使用 GitHub Releases；国内只作为软件内 OTA 更新入口。最终 2,107 项检查及真实成品升级／回退通过，公网使用实际下载器完整验证两种包。详见 [发布验收记录](releases/0.9.0-deployment.md)；前述 acceptance 目录保留为发布前实验记录。

## 0.9.1 packaged / 已打包

2026-10-08：完成启动后遮罩版本说明、深浅主题与应用弹窗标题统一、签名差分 OTA，以及定时执行账号卡片的添加、移除、搜索和拖动排序。插入提示支持跨行，Ctrl＋左右键支持键盘排序；保存与重载保留顺序。代码 e80161d 已推送私有 main，GitHub Build 全部通过。

最终 Portable／Lite 双版本位于 `artifacts/releases/0.9.1/`，均已包含账号卡片与拖动排序。2,130 项回归、每种成品 77 项定时／更新界面检查、差分升级、旧版完整升级、启动失败回退和模拟中断恢复均通过。差分下载量减少 Portable 82.44%、Lite 95.54%；0.9.0 仍需完整更新一次。详见 [0.9.1 验收](releases/0.9.1-validation.md)。

Final packages include themed dialogs, signed differential updates and ordered account cards. Source is pushed and local acceptance is complete. No 0.9.1 GitHub Release or domestic OTA deployment has been performed; production remains 0.9.0. No game operation or real scheduled task was started.

## Next changes / 后续修改 · 2026-10-08

定时执行简化为每天固定时间，移除星期勾选。新建或保存的 Windows 任务使用每日触发器。首次读到旧版指定星期计划时，先持久化停用，再更新 Windows 登记，成功后恢复原启用状态；保留时间、账号顺序，失败则保持停用并提示重新保存。转换不会补跑此前错过的时间，也不自动启用关闭的计划。

用户告知 2026-10-09 游戏大更新。按 [游戏更新检查清单](CLIENT_UPDATES.md) 核对新程序集、规则表和实际流程；尚未取得新版运行证据，不把 0.9.1 的旧版通过记录作为新版兼容证明。以上修改尚未打包或发布。

验证：2,154 项合成检查（57 组）通过，证据 artifacts/tests-20261007-175236-5522；完整 WPF 检查通过，定时／更新相关 82 项，证据 artifacts/smoke-20261007-175446-6671。已检查深浅主题和窄屏英文。未连接游戏，未登记真实 Windows 计划。

## 0.9.3 local candidate / 本地候选

2026-10-08：新增 Sandboxie-Plus 实验性隔离启动。已经实测两个不同账号同时在线、独立连接、各自完成经营领取和邮箱检查；关闭其中一个账号的游戏及工具后重新打开，另一个保持在线。运行中的队列能独立暂停，另一窗口继续执行。普通窗口拒绝重复启动已在隔离空间运行的同一账号。没有发布到 GitHub Releases 或 OTA。

账号页的入口支持简中、繁中和英文及深浅主题。主窗口保存的新登录信息通过临时 DPAPI 加密快照送入实例；仅在该账号游戏关闭后更新。安全暂停的队列允许同游戏进程内重建观察连接后接续，账号／角色／进程／周期变化或未确认操作仍受限制。定时仍按顺序执行，未实现集中并行计划。详情见 [隔离实例](ISOLATED_INSTANCES.md)。最终成品与实机验收记录随本地候选保存在 `artifacts/releases/0.9.3/` 和本次研究证据目录中。

This is a local candidate, not a published release. Two-account core operation is verified; all daily stages, every integrated tool, larger instance counts and compatibility with the announced game update remain outside the completed live coverage.

0.9.2 保留为首轮成品实测候选；0.9.3 追加普通窗口与安装器双层更新保护，隔离工具窗口未关闭时不改动安装文件。游戏可以保持运行。

## 0.9.4 software candidate / 软件候选

2026-10-08：主窗口新增可选的集中并行队列，默认同时 2 个账号，支持自动补位、账号级暂停／继续／停止、圆环进度、结束时间和步骤详情。定时计划沿用执行模式。主窗口与各隔离执行器分别持有操作锁；独立心跳在主窗口失联后停止任务，不自动重放未确认操作。成功后仅可关闭本次启动创建的游戏。顺序模式继续保留。

已完成 2,218 项合成回归（60 组），其中新增真实双测试进程的控制通道、独立暂停和失联停止验证；它们不连接游戏。WPF 覆盖三语言、双主题和窄窗口，进一步成品结果见候选包报告。此次游戏维护，没有启动游戏、连接 Hook、执行日常、提交或发布。集中调度真实队列仍为 `source_implemented_pending_runtime`。操作步骤与开服后检查清单见 [集中并行执行](PARALLEL_EXECUTION.md)。

## 0.9.5 client update preparation / 游戏更新准备

2026-10-08：组件缓存改用全部 Managed DLL 内容指纹；编译期间输入变化与已知旧游戏进程会拦截连接。隔离启动在账号写入和游戏启动之前比对主机与沙箱程序内容。保留三语言提示、账号数据与按需准备。2,234 项合成回归（61 组）通过，其中新增 16 项更新／旧覆盖文件检查。旧客户端输入基线保留在私有研究目录，不进入源码或分发包。新客户端连接与多开仍为 source_implemented_pending_runtime；本轮没有启动游戏或发布。见 [游戏更新清单](CLIENT_UPDATES.md)。

### 0.9.9 — login refresh and scoped delivery (2026-10-08)

The desktop client log contains an initial InvalidAccessTokenException followed by sign-in. Read-only live observation later confirms an identified player outside the title screen, with complete local credentials and both automatic-sign-in flags enabled. This establishes recovery, but not why the server rejected that earlier token.

A transient incomplete registry snapshot no longer aborts an active login check. Input permission remains revoked while waiting; restoration resumes the same check, actual identity mismatch still stops, and persistent incompleteness uses the existing login deadline. Relevant synthetic regressions pass; no automated sign-in or daily actions were performed for this validation.

The canonical test runner accepts named groups. Packaging selects a narrowly verified account scope against an unchanged full baseline instead of rechecking every independent tool. Source and package validation remain distinct from live evidence.

### 0.9.10 — background payment-region timeout acknowledgement (2026-10-08)

Observed the ErrorMessagePopupUI titled payment failed, carrying the MyCard nation-check request timeout. Both decision callbacks were null; the close wrapper's optional onClose was also null. The current client's OnClickUI and close-wrapper IL confirm acknowledgement only. A single native-handler call closed the observed popup; no payment or retry was dispatched and the daily queue was retained.

The runtime now classifies this precise diagnostic only after checking its callbacks, including the no-op wrapper's IL rather than an obfuscated method name. The managed navigation uses a dedicated action; native dispatch checks the current context and text again. Unknown confirmations and active callbacks remain blocked. Existing navigation progress detection prevents repeating a click on an unchanged popup.

The account verification timestamp request is deferred at the user's request. No account-timestamp changes were made in this iteration.

### 0.9.11 — stable pass observation after client updates (2026-10-08)

The current client has two valid, distinct passes. The old observer serialized the list's backing array with the new obfuscated item field names while the managed consumer expected the old names. Missing IDs became zero and were incorrectly diagnosed as duplicate active passes; both reward claims and equipment planning were affected.

The pass list now uses the bounded, explicit collection projection with stable protocol column names and the existing per-client member mapping. The consumer requires valid counts and positive identifiers; missing fields are incompatible evidence, never an empty task set or a zero ID. Genuine duplicates and incomplete lists still fail validation.

Update preparation also validates serialized output shapes: raw client objects exposing obfuscated fields must use explicit projections or stable native DTOs. This includes nested collections and inherited public data. The former pass specification is rejected by the new check; the corrected daily observation specifications pass. A private replay of the captured failure recognizes both passes and the selected pass's six missions (three claimable, three pending). No claims or daily actions were sent during this repair; real post-update pass collection remains pending use.
