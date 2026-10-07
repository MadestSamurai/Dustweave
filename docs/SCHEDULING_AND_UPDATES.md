# Scheduled runs and updates / 定时执行与版本更新

## Account queues / 多账号队列

账号页中的“执行勾选账号”是多账号队列的主要入口。按列表顺序执行，各账号沿用自己保存的任务配置。今日任务页继续用于查看进度、停止、接续与补跑。

The primary multi-account action lives on Accounts. Accounts run in list order using their own saved settings; Today remains the progress, stop, resume and retry surface.

## Scheduling / 定时执行

- 默认关闭；在定时页面选择时间、星期和固定账号后保存才启用。
- 按 Windows 本机时区执行，账号名单不随账号页临时勾选改变。
- 当前 Windows 用户的 Task Scheduler 启动 `Dustweave.exe --scheduled`。使用 InteractiveToken / LeastPrivilege，不保存密码，不要求管理员，不唤醒关机电脑。电脑需已登录且保持唤醒。
- 软件已开时由同一应用轮询处理；第二个启动进程静默退出，不弹“已打开”窗口。
- 忙碌或晚启动最多延后 15 分钟。过期不补跑。已开始的计划在连接前落盘领取记录，崩溃或重启不会重放同次计划；需要在今日任务中手动接续。
- 所有目标身份必须仍有效才开始；从不回退到当前登录账号。沿用现有账号切换校验和工具控制锁。
- 计划、上次结果和领取记录位于既有用户数据目录；不是程序安装目录。迁移 EXE 到不同目录后须重新保存定时计划。

Schedules are opt-in, use local wall-clock time and persist fixed account identities. They use the existing queue and control lease. A per-occurrence claim prevents repeat execution after a crash. Missed runs have a 15-minute grace period; there is no multi-day catch-up. Windows runs the task only in the signed-in user's session, with no elevation or stored password. Moving the executable requires saving the schedule again.

## Release notes / 版本说明

`src/Desktop/Localization/releases.json` contains bundled Simplified Chinese, Traditional Chinese and English notes. Each product version is shown once per local user data directory. Version history remains accessible through the version button in the sidebar. Scheduled startup defers the notice until the queue returns.

版本弹窗与游戏连接无关，不读游戏，不触发自动化。已读版本集合会保留，因此回退版本也不会重复弹已读说明。

## OTA / 版本更新

The domestic static source is https://bd2.madsam.work/updates/dustweave/. Full packages are authenticated with an embedded ECDSA public key; interrupted downloads resume, and file replacement uses durable backups and a recovery journal. The public GitHub update mirror is deferred; existing private Releases may archive packages. Details and release steps are in [OTA_RELEASE.md](OTA_RELEASE.md).

国内更新源使用现有网站。下载与安装均验证签名及文件哈希；中断下载可接续，失败或中断替换可从备份恢复。默认后台下载，空闲时确认重启；游戏不重启，账号、设置和插件保留。Portable 与 Lite 不互换。首次需手动安装支持 OTA 的版本。

Source tests, packaged upgrade acceptance and production HTTPS deployment are tracked separately. Building does not publish the feed, register a real scheduled task or connect the game.
