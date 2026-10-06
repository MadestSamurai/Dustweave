# Changelog / 更新记录

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

## Unreleased · interface polish / 界面打磨

- Added coherent vector task/status symbols and directly selectable system/light/dark appearance segments.
- Reworked diagnostics around connection status, practical next steps, activity and feedback; guild checks are now an advanced disclosure. Diagnostic refresh does not send game commands, and copied summaries exclude account identifiers and sign-in data.
- 统一时间线图标，外观改为系统／浅色／深色三段切换；诊断页优先说明当前问题和下一步，并提供状态摘要与日志入口。细化滚动条和时间线连接线，86项界面检查通过。
- Clarified plan/history selection and removed repeated pending descriptions from the task plan.
- Unified input, button and disclosure styling across themes; added visible dropdown focus and stable text-field geometry.
- Improved compact account/settings layouts, long-value tooltips and sidebar navigation; idle stop actions no longer consume space.
- 统一任务、设置、账号与工具页面的间距和控件状态；修复搜索框内边距重复与紧凑窗口导航被挤压的问题。三语双主题界面检查与现有回归通过，未改变游戏业务流程。

## Unreleased · repository layout / 仓库结构

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
