# Changelog / 更新记录

## 1.1.5-beta — 2026-10-10

- 跑商计划可直接准备连接并读取商店，不再需要先运行其他日常；修复尚未生成购买记录的商品被误判为数据缺失。
- 新增默认关闭、按账号保存的零收益采购选项：使用剩余预算购买折扣后买价等于120%卖价的额外商品，不分摊砍价费，实际药耗和垫资仍完整记录。
- 修复免费抽取预览的首次读取及未提交旧预览阻塞；恢复前重新核对免费次数。
- 周收集等待地图操作界面恢复后重新识别出口并接续，避免短暂切图状态导致持续卡住；补齐路线、周收集状态和独立跑商读取的诊断导出。
- Fixes direct trade capture initialization and uninitialized shop purchase records.
- Adds opt-in break-even resale purchases using spare funds, with the bargaining fee excluded from eligibility and strategy but retained in actual costs.
- Fixes first-read and unsubmitted-preview interruptions in free draws, and resumes weekly travel after the field interface recovers.
- Expands diagnostic exports for route recovery and standalone trade planning.
- [完整说明 / Full notes](docs/releases/1.1.5-beta.md)

## 1.1.3-beta — 2026-10-10

- 周收集自动覆盖固定的可收集卡带并按游戏进度接续，移除章节范围及角色／活动卡带设置；仅保留默认关闭的步行收集选项。
- 镜中免费战斗使用游戏原生连续次数：1倍40场一次启动，余票不足倍率时再处理尾批；整批核对及中断接续避免重复匹配。
- 修复当前游戏已登录、但本地启动凭据不完整时被提前拒绝连接的问题；补齐登录阶段诊断。
- 日志最多保留7天；成功步骤保留摘要，异常步骤保留前后证据，并保存恢复任务必需的状态。
- 今日任务的启动执行按钮移到顶部状态卡右侧；批量视图可单向返回主窗口的单账号任务列表，不再另开沙盒窗口。
- 批量完成情况按账号保留，已完成项默认不勾选；旧批量页在游戏每日刷新后关闭，不再跨天恢复。
- Mirror battles now use native continuous counts, verify the whole batch and resume observation without submitting it again.
- Weekly collection follows the maintained cartridge route and in-game progress. Range selectors are removed; walking collection remains optional and off by default.
- Fixes connecting to an already signed-in game when local launch credentials are incomplete, and adds bounded login diagnostics.
- Keeps logs for at most seven days, summarizes successful steps and retains failure evidence and required recovery state.
- Places launch-and-run on the right of the Today’s Tasks status card. The temporary batch view returns to account tasks in the same window.
- Preserves per-account completion without preselecting finished tasks, and expires batch overviews at the game’s daily reset.

## 1.1.2-beta — 2026-10-10

- 修复控制程序仍运行却被误判为退出导致的连接失败，区分权限不足与实际退出。
- 诊断导出补齐连接与控制权历史、队列初始化和轮转日志；关键证据优先，大日志保留有标记的末尾。
- Fixes live controllers being misreported as exited under restricted process access.
- Adds connection and ownership evidence, queue initialization and rotated logs to diagnostic exports, prioritizing critical evidence and marking retained tails of large logs.

## 1.1.1-beta — 2026-10-10

- 新增日志保留设置、清理预览和空闲自动清理，保护账号、任务进度与未结算操作。
- 跑商与料理共同规划当前和后续供给，纳入已知周收集掉落期望，修复重复估值、长期推迟加工和无效囤积。
- Adds guarded manual/automatic log maintenance and shared-resource trading/cooking optimization with expected weekly drops.
- [完整说明 / Full notes](docs/releases/1.1.1-beta.md)
## 1.1.0-beta — 2026-10-10

- 修复控制身份继承及登录阶段提前绑定，保留真实停止原因；新增错误分类、下一步指引和诊断 ZIP 导出。
- 新增软件图标、Release 与夸克入口、更高的默认窗口；补齐三语言与深浅主题。
- 今日任务支持按账号查看及保留勾选，批量后返回账号任务；包含插件准备和设置保存改进。
- Fixes session ownership and early login binding, adds actionable interruption messages and diagnostic ZIP export, and refreshes app identity and download access. Includes per-account task browsing and preparation improvements.
- [完整说明 / Full notes](docs/releases/1.1.0-beta.md)

## 1.0.1-beta — 2026-10-09

- 新增首次连接教程与账号页常驻入口，支持三语言和深浅主题；明确退出游戏后保存账号，并从织尘启动的流程。
- Added a three-language, themed first-use connection guide and an Accounts entry point; explains saving after normal game exit and future launches through Dustweave.

- Weekly quests clear enabled collection work before interactions on shared maps. NPC-only routes also suppress ordinary monsters when safe, preserve later kill targets, and recover incidental encounters without starting unrelated battles.
- Token activities collect mailbox rewards after mission claims before revisiting puzzles, dice, roulette and exchange pages; the mailbox is checked once per reward phase.
- Equipment Assistant handles a missing first-connection broker, retries only transient reads, and preserves pending resource transactions without resubmission. The same fix is included in the embedded tool.
- 周路线优先完成当前地图已启用的收集，再做任务；保护任务后续击杀目标，补齐单跑任务的压制与误触战斗恢复。
- 活动任务领奖后先收邮箱，再处理拼图等代币活动，避免道具仍在邮件中导致漏做。
- 同步装备助手首次连接及短暂读取超时修复；未知消费结果保留待核对，不自动重发。

## 1.0.0-beta · 2026-10-09

- First public MIT release; refreshed Chinese/English documentation and real application screenshots in both themes. Normal GitHub Release with Portable and Lite packages.
- Includes MANSION RUNAWAY, smooth turns, normal/challenge progression and the 80-chain goal.
- Preserves game-written agreement records for matching isolated accounts/clients and pauses login timing while awaiting confirmation.
- Adds semantic release-version ordering to updates and plugin checks. This release is installed manually; the legacy OTA feed is preserved.
- 首次公开 MIT 发布，汇总 0.9.13 后的小游戏及隔离登录改进，更新双语文档与深浅主界面截图。完整说明见 [1.0.0-beta](docs/releases/1.0.0-beta.md)。

## 0.9.13 · 2026-10-09

- Added local plugin management, optional Sandboxie account instances and central concurrent queues; corrected credential handoff and registration recovery.
- Updated client compatibility, pass reading, acknowledgement-only background notifications, puzzle events and hunting priorities.
- Daily fixed-time schedules, reorderable account cards, themed dialogs and signed differential OTA are included from the intervening local versions.
- 新增插件管理、可选隔离多账号与集中队列，修复凭据交接和登记恢复；适配更新后客户端、通行证、提示、拼图活动及狩猎。
- 汇总此前本地版本的每日定时、账号卡片排序、主题弹窗及签名差分更新。双版本与实机验证范围见 [完整发行说明](docs/releases/0.9.13.md)。

## 0.9.0 · 2026-10-07

- Moved multi-account execution to the Accounts page as its primary action, and added opt-in Windows scheduled queues with fixed account identities and duplicate-run protection.
- Added one-time, three-language release notes and an OTA client: background downloads, verified official packages, idle restart confirmation and rollback. Signed manifests, resumable downloads and durable rollback protect the update path. Domestic hosting is configured under bd2.madsam.work; the manual workflow prepares artifacts only, with deployment and real-game scheduled execution verified separately.
- 多账号执行移至账号页主按钮；新增定时页面，支持固定账号名单、按星期执行、关闭软件后启动，以及避免重复执行。
- 新增每版本一次的三语言更新说明和 OTA 链路：后台下载、签名校验、断点续传、空闲确认重启和中断恢复；国内线路位于 bd2.madsam.work，发布脚本生成签名清单与完整包，升级失败时恢复旧版。

- Added Simplified Chinese, Traditional Chinese and English coverage for connection, account, queue, map, weekly NPC, trade and settings notices. Registered dynamic formats keep names, paths and diagnostics intact while existing messages update when the language changes.
- 补齐连接、账号切换、队列、跑图、周NPC、跑商和设置说明的简中／繁中／英文提示；动态格式保留账号名、路径和原始诊断，已显示的提示随语言切换更新。

- Reduced duplicate field-settle waits during route planning while retaining movement, arrival and teleport readiness checks. Travel talents filter unresolved history before loading capture details.
- Talent menus now keep one observation scope through readiness, selection and casting, with a mandatory fresh observation after opening.
- 减少跑图决策阶段的重复稳定等待，保留移动、到达和传送前的就绪检查；飞奔、藏身先筛选未完成记录，再读取相关详情。
- 天赋菜单、技能选择与施放沿用同一组观察请求，打开菜单后仍必须获取新数据；保留动画、冷却、身份和消耗回执检查。

## 0.8.17 · 2026-10-07

- Increased the account-list checkbox inset and aligned the select-all header with each row. Game-entry actions now use a wider column, an accent outline and stronger text in both themes.
- 增加账号列表勾选框的左侧留白并对齐全选表头；加宽进入游戏操作列，使用强调色描边和加粗文字，在深浅主题下都更容易辨认。

## 0.8.16 · 2026-10-06

- Passive mission notices, character notices and HUD updates no longer invalidate shared input tokens or navigation stability. Applied the same passive-surface policy to field travel, startup recovery and event/dispatch gates.
- Added the observed return route from the Monster Hunt lobby. Locally rejected, unsubmitted previews can cancel their unchanged owned dialog so subsequent stages remain usable; uncertain or replaced dialogs remain guarded.
- 任务完成提示、角色提示和资源栏刷新不再干扰日常各环节的操作校验与导航等待；同步跑图、启动、派遣和活动入口的背景提示判定。
- 补齐魔兽页面返回路径；对确认未提交且内容未变的本次预览执行取消收尾，避免挡住后续任务。真实确认变化、身份变化和结果不明仍保留检查。

## 0.8.15 · 2026-10-06

- Unified owned project, assembly, host namespace and executable names under Dustweave. The application now starts from `Dustweave.exe`; both archive flavors retain their existing naming format.
- Updated desktop relay, elevated connection helpers, utility entry points, embedded resources, dependency locks and build/package checks together. Existing accounts, encryption, settings, history and shared connection ownership keep their storage/protocol identities.
- Managed extension API 4 requires rebuilding against the renamed host; incompatible packages are detected before loading. Independent tools retain their own names and versions.
- 统一本体工程、程序集、命名空间与程序文件名，入口改为 `Dustweave.exe`；同步构建、打包、辅助进程和管理员连接入口。账号、加密、设置、历史及连接归属保持兼容，独立工具名称不变。

## 0.8.14 · 2026-10-06

- Account management now separates the current sign-in card from the saved-account list. Each row opens its own account; daily-queue checkboxes and secondary batch sign-in verification have distinct scopes and explicit confirmations.
- The list shares its card surface with quiet headings, rounded row selection/hover and lighter game-entry actions. Filtered select-all preserves hidden selections, and unavailable session actions explain their requirements.
- Restored settings editing when a temporarily empty account catalog returns without losing unsaved choices. Added three-language account-state, keyboard-focus and 100-account scrolling checks.
- 账号页拆分为顶部本机登录卡片和已保存账号列表；每行直接进入对应游戏账号，勾选用于日常队列，批量登录验证明确列出操作范围。
- 表头、列表与卡片统一背景，选中和悬停使用整行圆角高亮；完善空列表、筛选全选及按钮不可用提示，并修复账号目录恢复后设置仍变灰的问题。支持简中、繁中、英文及深浅主题。
## 0.8.13 · 2026-10-06

- History metadata now includes the Windows file identity, so a rapid atomic replacement cannot reuse stale state when size and timestamps match. The check requests metadata access only and preserves shared readers/writers.
- 修复快速更新记录时大小、时间戳相同导致缓存未刷新的边界情况；新增固定复现回归。保留历史查询提速、任务详情和跳过说明。

## 0.8.12 · 2026-10-06

- Shared history queries now filter compact routing, account and state metadata before opening capture details. Event recovery skips history reads on unrelated screens; free-draw, Mirror and management queries benefit from the same index.
- 日常各环节复用增量历史索引，先筛选账号、操作和状态再读取详情；活动恢复先判断当前页面。保留冲突记录检查、未知操作拦截和停止后的只读诊断。

## 0.8.11 · 2026-10-06

- Pending-operation checks reuse compact record metadata instead of reopening every historical capture before each restaurant bubble click. New and changed files, unresolved claims, account isolation and conflicting copies remain checked.
- Restaurant entry prepares the check before selecting short-lived bubbles. This updates the host only; game-component versions remain unchanged.
- 修复餐厅气泡已出现却延迟点击的效率问题：保留历史记录，只增量检查操作状态，避免每次领取都读取大量旧采集。未知结果和冲突仍会拦截重复领取；实际领取时延待后续实机确认。
## 0.8.10 · 2026-10-06

- Reward checks with unfinished tasks now expose a Details button with the task count. The read-only window groups daily, weekly, pass and event tasks with their saved progress, remaining requirements or unclaimed reward status.
- The view preserves distinct tasks across categories and does not start a game action. Missing details are reported explicitly; all controls support Simplified Chinese, Traditional Chinese and English.
- 奖励检查中仍有未完成任务时，可点击「详细信息」查看任务名称、分类、进度与未完成原因；显示记录时间，不重新操作游戏。支持长列表滚动、三语与深浅外观。

## 0.8.9 · 2026-10-06

- Timeline rows now show skip reasons directly. Completed tasks retain explicit result summaries; records without a skip reason say so instead of inventing a cause.
- Final result details and reasons take precedence over stale progress, including records with empty or null optional fields. Running tasks continue showing current progress.
- 时间线直接显示跳过原因，已完成项也保留具体结果说明；缺少原因时明确提示。修复空字段或过期进度遮住最终说明的问题，三语同步。

## 0.8.8 · 2026-10-06

- Original-queue resume is now the primary footer action after interruption. Selected retry remains beside it as a secondary button, with guidance explaining that checkboxes only affect retries.
- 中断后将「接续原队列」提升为底部主按钮，「补跑已选环节」并列为次要操作。明确接续不受勾选影响，清空勾选仍可接续；保留账号、会话及每日重置校验。

## 0.8.7 · 2026-10-06

- Removed the duplicate task-settings shortcut from Today; daily settings remain in the sidebar. Select unfinished and Clear selection now use standard bordered buttons aligned with the view switch.
- 今日任务页移除重复的环节设置入口；勾选未完成、清空勾选改为标准按钮，与当前计划／上次记录切换器居中对齐。保留底部补跑主按钮与独立的重新选择任务入口。

## 0.8.6 · 2026-10-06

- Interrupted runs now use the fixed footer primary action to retry selected unfinished tasks. Choosing other tasks is a separate secondary action; original-queue resume lives under More actions.
- Added contextual footer guidance and stable retry labels in Simplified Chinese, Traditional Chinese and English. Empty, expired, other-account and busy selections remain non-actionable.
- 中断后的补跑按钮统一放到底部主操作区，并明确显示勾选数量；「重新选择任务」独立为次要操作，「按原队列接续」收进更多操作。三语、深浅外观及窄窗口检查通过，补跑不会误启动新计划。

## 0.8.5 · 2026-10-06

- Merchant navigation now walks to a close, collision-clear stand before interaction, even if the character starts within the native interaction radius. NPC bodies cannot become standing ground.
- 跑商改为先靠近商人，再打开商店；不再刚进入交互范围就停止。近距离站位按实体碰撞筛选，保留大体积障碍与商人方向判定。

## 0.8.4 · 2026-10-06

- Unified the desktop and injected daily bridge version contract, fixing the component mismatch exposed by live verification.
- 统一主程序和游戏内日常组件的协议版本来源，修复实机验证发现的版本不匹配；新增只移动及开关商店、不买卖的实机诊断入口。

## 0.8.3 · 2026-10-06

- Fixed plaza merchant approach points: replans keep the original target instead of moving the anchor to the previous waypoint. Interaction stands use the game's sector, trigger or distance predicate independently of physical movement collision.
- Preserved approach candidates around the target and allowed the native proximity state to refresh before replanning. Added separate interaction-domain and physical-contact diagnostics.
- 修复跑商走到商人附近后反复绕行的问题：重规划不再移动商人目标，站位按原生交互区域筛选，实体碰撞单独处理。到达后等待一次原生交互状态刷新，避免连续重算耗尽预算。
- 23 项新增合成检查覆盖目标漂移、交互方向、实体阻挡及远侧候选；1,719 项回归通过，当前客户端组件离线编译通过。真实游戏跑商路径仍待验证。

## 0.8.2 · 2026-10-06

- Separated native readiness waits from UI-result observation deadlines. Weekly collection now checks a fresh frame after delayed readiness or a resumed desktop instead of treating elapsed time alone as an unknown action result.
- Management entry reselects a currently observed rotating banner after a proven non-dispatch. Zero-cost settlements may retry up to three times using fresh game eligibility and cooldown state. Recognized management reward windows can resume without a historical receipt; unrelated consuming operations retain their existing guards.
- Added native readiness timing and wait-reason diagnostics. 45 additional synthetic checks cover delayed menus, time jumps, rotating targets, cancellation and identity changes; the complete suite passes 1,696 checks across 50 groups.
- 修复周收集内层天赋等待与外层界面超时相互冲突的问题；恢复后先读取当前状态。经营入口轮播变化时重新选择有效按钮，不把未执行的点击直接当作环节失败。
- 经营零成本结算失败后最多重试3次，每次重新读取可领取／冷却状态；可识别并收尾缺少历史回执的经营奖励窗口。保留账号、场景、用户停止及无关操作保护。源码与离线回归已验证；用户笔记本熄屏场景尚待实机验证。

## 1.0.1-beta — 2026-10-09

- 新增首次连接教程与账号页常驻入口，支持三语言和深浅主题；明确退出游戏后保存账号，并从织尘启动的流程。
- Added a three-language, themed first-use connection guide and an Accounts entry point; explains saving after normal game exit and future launches through Dustweave. · interface polish / 界面打磨

- Added coherent vector task/status symbols and directly selectable system/light/dark appearance segments.
- Reworked diagnostics around connection status, practical next steps, activity and feedback; guild checks are now an advanced disclosure. Diagnostic refresh does not send game commands, and copied summaries exclude account identifiers and sign-in data.
- 统一时间线图标，外观改为系统／浅色／深色三段切换；诊断页优先说明当前问题和下一步，并提供状态摘要与日志入口。细化滚动条和时间线连接线，86项界面检查通过。
- Clarified plan/history selection and removed repeated pending descriptions from the task plan.
- Unified input, button and disclosure styling across themes; added visible dropdown focus and stable text-field geometry.
- Improved compact account/settings layouts, long-value tooltips and sidebar navigation; idle stop actions no longer consume space.
- 统一任务、设置、账号与工具页面的间距和控件状态；修复搜索框内边距重复与紧凑窗口导航被挤压的问题。三语双主题界面检查与现有回归通过，未改变游戏业务流程。

## 1.0.1-beta — 2026-10-09

- 新增首次连接教程与账号页常驻入口，支持三语言和深浅主题；明确退出游戏后保存账号，并从织尘启动的流程。
- Added a three-language, themed first-use connection guide and an Accounts entry point; explains saving after normal game exit and future launches through Dustweave. · repository layout / 仓库结构

- Reworked the Chinese and English homepages around the Dustweave · 织尘 identity, real demo screenshots and user-oriented setup instructions; added contribution and reporting templates.
- 统一中英文首页、完整分发包说明、入门与协作规范；主体许可证及公开发布仍待确定。本次文档调整不改变程序行为。
- Consolidated the daily application, accounts and connection sources under `src`, with one `Dustweave.slnx` entry.
- Collected synthetic tests under `tests/Dustweave.Tests` and separated flow/specification assets from connection code.
- Updated project references, build/test/package scripts and CI; independent tool sources retain their layout and versions.
- Added repository layout validation and registered repository-local source inputs.
- 日常本体直接在本仓库维护；不再用旧研究工程导出覆盖。程序集身份、用户数据与任务行为保持。

## 0.8.0-preview.4 · 2026-10-06

- Fixed daily stages waiting indefinitely when the native home menu remains on the normal `Empty` scene.
- Applied the same correction to cartridge navigation while keeping actual map arrival, loading overlays, unknown-dialog and command-ownership checks.
- Added 35 scene regressions. The navigation suite passes 439 checks, and the independent synthetic suite passes 1,634 checks across 50 groups. Live-game verification remains pending.
- Portable includes the .NET 8 desktop runtime. Lite requires .NET Desktop Runtime 8 x64. Extract the entire folder and start `Dustweave.exe`.

- 修复主菜单正常停留在 `Empty` 场景时，日常环节一直等待加载、最终超时的问题。
- 同步修复卡带导航；保留实际地图到达、加载遮罩、未知弹窗与操作归属检查。
- 新增35项场景回归；导航专项439项、独立50组／1634条合成回归通过。实机验证待完成。
- Portable自带.NET 8桌面运行时；Lite需要.NET Desktop Runtime 8 x64。完整解压文件夹后启动 `Dustweave.exe`。

| Package / 包 | Runtime / 运行环境 |
| --- | --- |
| Portable | Included / 自带运行时 |
| Lite | Install .NET Desktop Runtime 8 x64 / 需另行安装 |

This is a private preview. It retains the existing account/settings location. The navigation fix changes the .NET host only; restarting the game is not required.

本版是私有预览，沿用原有账号与设置目录。此次导航修复仅修改.NET本体，无需重启游戏；使用新版前请关闭旧日常窗口。
