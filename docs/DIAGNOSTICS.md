# Diagnostics / 诊断与反馈

打开 **诊断 → 导出日志与联系开发者**，选择 ZIP 保存位置并导出。窗口提供微信 **SuJakads0133**、QQ **1104563414**。请说明出问题的环节、发生时间及游戏当时的画面，私下发送压缩包；不要发送密码或登录验证码。

Open **Diagnostics → Export logs and get help**, choose a ZIP location and export. Contact the developer via **WeChat: SuJakads0133** or **QQ: 1104563414**. Describe the affected step, time and game screen, then send the archive privately. Never send passwords or login codes.

## Reading the status / 怎样理解提示

The connection card describes the current game connection. The operation section describes an earlier action; a healthy connection does not mean that action succeeded. Each recognized issue explains a cause category and next step. Unconfirmed results must be checked before retrying; the app does not replay them blindly.

连接卡片表示当前连接；最近一次操作表示之前执行的动作。连接正常不代表上次动作已完成。已识别的问题会给出类别和下一步；结果待核对时先核对游戏，不会自动重复消费或提交。

## Export boundary / 导出范围

- Last 7 days of allowlisted run, connection, task, diagnostic and owned isolated-account logs; known integrated-tool log directories are included when present.
- Maximum 64 MiB of uncompressed log contents, 8 MiB per file and 2,000 files. Recent files take precedence. Missing, busy, invalid or over-limit files are skipped; `manifest.json` lists omissions.
- Saved login/session stores, plugins, executable payloads and signing keys are excluded. Structured secret fields and common credential formats are redacted. Character names, task details and local paths may remain; the archive is for private support, not public posting.
- The export performs no game actions and uploads nothing. Closing the export dialog cancels an ongoing export. Existing ZIP files are preserved; select a different filename to export again.

最近 7 天、最多 64 MiB，优先保留新日志。文件被占用或未写完不会阻断其他日志导出；遗漏清单随包保存。账号登录存储、插件和密钥不纳入，常见敏感字段会过滤，但角色名与任务信息可能仍在，请勿公开上传。导出不操作游戏、不自动上传；已有 ZIP 不会被覆盖。

## Developer checks

Use `test.ps1 -Groups UnifiedSuite,DiagnosticExport` and the isolated desktop smoke scope `support`. The latter checks all three languages, both themes, the icon, download destinations and actual ZIP creation from synthetic data. These checks do not prove a particular user's game-side failure was reproduced.

## Log retention / 日志清理

在 **诊断 → 日志与空间管理** 中设置保留时间、容量目标，或统计后手动清理。

- 默认开启自动清理：程序启动后等待空闲，每 12 小时执行一次；未运行程序时不会清理。开始任务或关闭窗口会取消后台扫描／清理。
- 默认保留 7 天、目标 512 MB。容量超标时先清理较早的可清理记录，始终保留最近 48 小时。可选保留 2／7／14／30／90 天，容量目标 128／512／1024／4096 MB。
- 清理当前运行空间中的普通日志、内置工具日志，以及已确认完成的界面步骤快照。涉及交易的步骤还需确认所属交易已结束；待核对、读取失败、占用、统计后发生变化的文件会保留。
- 账号、设置、队列与补跑进度、业务核对记录、插件、缓存、已导出的 ZIP 都不清理。容量目标只针对可管理诊断文件，不是整个数据目录的硬上限；受保护数据可能使占用超出目标。
- 手动清理先显示可释放容量和文件数，不会直接清空目录。只删除明确允许的文件；跳过符号链接和硬链接，不跟随链接删除其他目录。隔离账号的运行空间由对应实例独立管理。

Open **Diagnostics → Logs and storage** to configure retention or preview and perform manual cleanup. Automatic cleanup is enabled by default and runs when idle, at most once every 12 hours while the app is open. Defaults are 7 days and a 512 MB target; the latest 48 hours always remain. Task startup cancels background maintenance.

Only allowlisted logs and confirmed completed-step snapshots in this instance are eligible. Transaction-dependent steps require a terminal business record. Accounts, settings, queue/retry progress, transaction journals, plugins, caches and exported ZIPs are preserved. Busy, changed, malformed and unresolved records are skipped. The storage target is soft and applies to managed diagnostic files, not the whole data folder. Sandboxed account instances manage their own stores. Manual cleanup previews reclaimable space before deletion; closing its dialog cancels the operation.

Focused verification: `test.ps1 -Groups LogCleanup,DiagnosticExport`, plus the desktop `support` smoke scope for manual cleanup, idle automatic cleanup, three languages and both themes. Tests delete only synthetic fixture files.
