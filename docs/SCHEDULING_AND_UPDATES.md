# Scheduled runs and updates / 定时执行与版本更新

## Account queues / 多账号队列

账号页中的“执行勾选账号”是多账号队列的主要入口。按列表顺序执行，各账号沿用自己保存的任务配置。今日任务页继续用于查看进度、停止、接续与补跑。

The primary multi-account action lives on Accounts. Accounts run in list order using their own saved settings; Today remains the progress, stop, resume and retry surface.

## Scheduling / 定时执行

- 默认关闭；在定时页面选择每天的固定时间和账号后保存才启用。
- 按 Windows 本机时区执行，账号名单不随账号页临时勾选改变。
- 当前 Windows 用户的 Task Scheduler 启动 `Dustweave.exe --scheduled`。使用 InteractiveToken / LeastPrivilege，不保存密码，不要求管理员，不唤醒关机电脑。电脑需已登录且保持唤醒。
- 软件已开时由同一应用轮询处理；第二个启动进程静默退出，不弹“已打开”窗口。
- 忙碌或晚启动最多延后 15 分钟。过期不补跑。已开始的计划在连接前落盘领取记录，崩溃或重启不会重放同次计划；需要在今日任务中手动接续。
- 所有目标身份必须仍有效才开始；从不回退到当前登录账号。沿用现有账号切换校验和工具控制锁。
- 计划、上次结果和领取记录位于既有用户数据目录；不是程序安装目录。迁移 EXE 到不同目录后须重新保存定时计划。

Schedules run every day when enabled, use local wall-clock time and persist fixed account identities. They use the existing queue and control lease. A per-occurrence claim prevents repeat execution after a crash. Missed runs have a 15-minute grace period; there is no multi-day catch-up. Windows runs the task only in the signed-in user's session, with no elevation or stored password. Moving the executable requires saving the schedule again.

### Earlier schedules / 旧计划迁移

New and saved schedules use Windows' daily trigger with an interval of one day. Earlier weekday-only plans are converted once before scheduling: persist disabled, update Windows registration, then restore the previous enabled state. Time and fixed account order are retained. The conversion time becomes the cutoff for missed runs, so migration cannot catch up an earlier occurrence. On registration failure, the plan remains disabled and the user is asked to save again; it is not retried on every poll. Previously disabled drafts stay disabled and do not register a task. Earlier plans already containing all seven days are equivalent to daily schedules and remain valid.

新版只提供每天固定时间。旧版指定星期计划会自动转换，保留时间、启用状态与账号顺序；登记失败则保持停用并提示重新保存。原本关闭的计划不会被自动开启。迁移不补跑之前错过的时间。Windows 每天启动软件，与软件已打开时的检查使用相同的每天执行规则。

## Release notes / 版本说明

`src/Desktop/Localization/releases.json` contains bundled Simplified Chinese, Traditional Chinese and English notes. Each product version is shown once per local user data directory. Version history remains accessible through the version button in the sidebar. Scheduled startup defers the notice until the queue returns.

版本弹窗与游戏连接无关，不读游戏，不触发自动化。已读版本集合会保留，因此回退版本也不会重复弹已读说明。

## OTA / 版本更新

The domestic static source is https://bd2.madsam.work/updates/dustweave/. Full and differential packages are authenticated with an embedded ECDSA public key; interrupted downloads resume, and file replacement uses durable backups and a recovery journal. The public GitHub update mirror is deferred; GitHub Releases provide first-install and manual-download packages. Domestic hosting is reserved for in-app OTA and is not promoted as a manual download site. Details and release steps are in [OTA_RELEASE.md](OTA_RELEASE.md).

国内更新源使用现有网站。0.9.1 起优先差分更新，不适用时自动回退完整包；0.9.0 需完整更新一次才能获得差分能力。下载与安装均验证签名及文件哈希；中断下载可接续，失败或中断替换可从备份恢复。默认后台下载，空闲时确认重启；游戏不重启，账号、设置和插件保留。Portable 与 Lite 不互换。首次需从 GitHub Releases 手动安装支持 OTA 的版本；国内线路只作为软件内 OTA 更新入口，不提供手动下载入口。

Source tests, packaged upgrade acceptance and production HTTPS deployment are tracked separately. Building does not publish the feed, register a real scheduled task or connect the game.

## Scheduled account cards / 定时执行账号卡片

The scheduled account set is displayed as ordered, removable cards. A dashed Add accounts card opens a searchable multi-select dialog containing only valid accounts that have not been added. Confirm appends to the current order; cancelling leaves the draft unchanged. Refreshing the account catalog never restores a removed selection, clears an empty draft or reorders existing cards. A missing identity stays visible as unavailable and prevents enabling the schedule until corrected. Saving a disabled schedule retains the edited account set.

执行账号采用可移除的顺序卡片，虚线加号卡片用于搜索和批量添加。修改后仍需保存定时计划，不影响账号页的勾选或当前游戏身份。添加窗口沿用统一遮罩、主题和键盘操作，列表短时自动压缩窗口高度。

### Drag ordering / 拖动排序 · 2026-10-08

Drag an account card to insert it before or after another card, including across wrapped rows. A theme-aware insertion marker previews the position. Dropping over the Add accounts tile places it last. Escape or dropping outside cancels. Removal controls do not start a drag; focused cards also support Ctrl + Left / Right. The selector scrolls near the visible edges when needed. Changes remain a draft until Save schedule.

The native drag payload is restricted to this selector and its unchanged identity set. Account catalog refresh is deferred visually while dragging, then reconciled without resetting the chosen order. Scheduled execution continues to use the persisted account-key sequence.

Full isolated WPF smoke and 77 scheduling/update checks pass at `artifacts/smoke-20261007-160909-9182/`. Coverage includes wrapped hit testing, foreign/cancelled payload rejection, remove-button separation, refresh during drag, forward/backward insertion and saved-order reload. Light/dark insertion renders were inspected. No game commands or Windows tasks were issued; physical mouse/assistive-technology testing and a new package remain separate.
