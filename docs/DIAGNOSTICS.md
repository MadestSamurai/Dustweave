# Diagnostics / 诊断与反馈

打开 **诊断 → 导出日志与联系开发者**，选择 ZIP 保存位置并导出。窗口提供微信 **SuJakads0133**、QQ **1104563414**。请说明出问题的环节、发生时间及游戏当时的画面，私下发送压缩包；不要发送密码或登录验证码。

Open **Diagnostics → Export logs and get help**, choose a ZIP location and export. Contact the developer via **WeChat: SuJakads0133** or **QQ: 1104563414**. Describe the affected step, time and game screen, then send the archive privately. Never send passwords or login codes.

## Reading the status / 怎样理解提示

The connection card describes the current game connection and the next step when it is unavailable. It does not imply that a previous task succeeded. Task results and recovery actions stay with the affected task in Today; Diagnostics provides status copying and report export.

连接卡片只表示当前连接状态，并在不可用时说明下一步。连接正常不代表此前的任务成功；具体任务结果和恢复入口留在今日任务的对应环节中。诊断页用于复制连接摘要、导出日志并联系开发者。

“本机登录信息不完整”与游戏未登录不同。连接当前游戏会核对实时身份，不要求可保存的自动登录凭据；保存账号和切换账号仍有独立检查。导出包含连接前、成功或失败时的简短身份诊断（`live/diagnostics/login-*.json`）：进程、观察时间、身份是否就绪，以及本机登录字段是否存在、自动登录是否启用；不含字段值或令牌。没有这些记录的旧版导出无法确定缺少哪一项。

Incomplete local sign-in data does not imply an in-game logout. Connecting verifies live identity; saving or switching accounts retains separate checks. Bounded `live/diagnostics/login-*.json` summaries distinguish local readiness from live observations without recording login values or tokens. Older exports without these summaries cannot identify the exact missing local field.

## Export boundary / 导出范围

- Last 7 days of allowlisted run, connection, task, diagnostic and owned isolated-account logs; known integrated-tool log directories are included when present.
- Maximum 64 MiB of uncompressed log contents, 8 MiB per file and 2,000 files. Recent files take precedence. Missing, busy, invalid or over-limit files are skipped; `manifest.json` lists omissions.
- Saved login/session stores, plugins, executable payloads and signing keys are excluded. Structured secret fields and common credential formats are redacted. Character names, task details and local paths may remain; the archive is for private support, not public posting.
- The export performs no game actions and uploads nothing. Closing the export dialog cancels an ongoing export. Existing ZIP files are preserved; select a different filename to export again.

最近 7 天、最多 64 MiB，优先保留新日志。文件被占用或未写完不会阻断其他日志导出；遗漏清单随包保存。账号登录存储、插件和密钥不纳入，常见敏感字段会过滤，但角色名与任务信息可能仍在，请勿公开上传。导出不操作游戏、不自动上传；已有 ZIP 不会被覆盖。

## Developer checks

Use `test.ps1 -Groups UnifiedSuite,DiagnosticExport` and the isolated desktop smoke scope `support`. The latter checks all three languages, both themes, the icon, download destinations and actual ZIP creation from synthetic data. These checks do not prove a particular user's game-side failure was reproduced.

## Log retention / 日志清理

在 **软件设置 → 更新与存储 → 日志与空间管理** 中设置保留时间、容量目标，或统计后手动清理。

- 默认开启自动清理：程序启动后等待空闲，每 12 小时执行一次；未运行程序时不会清理。开始任务或关闭窗口会取消后台扫描／清理。
- 默认保留 7 天、目标 512 MB。可选保留 2／3／7 天；旧版超过 7 天的设置会收敛到 7 天。容量超标时先清理较早的成功步骤和普通日志，始终保留最近 48 小时。
- 异常、旧版、缺少结果或损坏的步骤诊断也会到期删除，不再永久保留。清理包括事件快照、任务查询及普通诊断 JSON；使用中、统计后发生变化的文件稍后再处理。
- 每个环节结束时只登记整理任务；空闲时后台处理，开始日常即可取消，重启后可继续。成功环节的界面步骤与只读查询保留摘要；失败、未知结果及前 3／后 2 步保留完整现场。不能把单纯“指令已发出”当作成功，必须有环节最终成功状态，且关联业务已经结束。
- 已结束业务保留原生数值、回执、身份及核账依据，移除重复 UI 树。超过 7 天后保留结果与归属摘要。未结算操作的必要恢复依据独立保留，避免清理后重复消费；过期步骤的必要回执先迁入恢复存储。
- 账号、设置、队列与补跑进度、插件、组件缓存及已导出的 ZIP 不清理。容量目标只针对诊断文件，不是整个数据目录的硬上限。
- 手动清理先显示可释放容量和文件数，不会直接清空目录。只删除明确允许的文件；跳过符号链接和硬链接，不跟随链接删除其他目录。隔离账号的运行空间由对应实例独立管理。

Open **App settings → Updates & storage → Logs and storage** to configure retention or preview and perform manual cleanup. Automatic cleanup is enabled by default and runs when idle, at most once every 12 hours while the app is open. Defaults are 7 days and a 512 MB target; the latest 48 hours always remain. Task startup cancels background maintenance.

Retention is capped at 7 days (options: 2, 3, 7). Expired failure, legacy and malformed diagnostics are removed too. Native event snapshots and query records are included. An unresolved operation's necessary receipt is preserved separately before its diagnostic files expire; account, task and transaction recovery state remain intact.

Completed stages enqueue durable compaction work without delaying execution. The next idle tick converts successful step and query snapshots to summaries. Failures keep full evidence plus three preceding and two following steps. Dispatch alone is not success; the enclosing workflow must confirm completion and any associated business operation must be resolved. Settled business records retain native values, ordering and accounting proof, without redundant UI trees; after 7 days only results and historical ownership remain. Pending recovery data is preserved separately. Maintenance yields to active work and resumes after restart.

Only allowlisted files are managed. Open, changed or linked files are skipped. Accounts, settings, queue/retry progress, plugins, caches and exported ZIPs are preserved. The storage target is soft and does not cover the entire data folder. Sandboxed instances manage their own stores. Manual cleanup previews reclaimable space; closing its dialog cancels the operation.

Focused verification: `test.ps1 -Groups LogCleanup,LogEvidence,DiagnosticExport,QueueEngine,TradeResume,FieldTalent`, plus the desktop `support` smoke scope. Tests delete only synthetic fixture files.

### Failure snapshots / 异常现场快照

输入或环节失败时，`live/diagnostics/failures/` 额外保存最近观察到的界面、错误、会话与步骤信息。输入失败同时区分“指令已提交”和“游戏可能已执行”；没有明确回执时不猜测结果。快照使用已经收到的数据，不增加游戏轮询。同一环节、同一场景的相同错误在 30 秒内只保存一份独立快照，普通观察时间线仍按原上限滚动。

异常快照参与现有最近 7 天导出与到期清理，导出优先级高于普通步骤。这样，操作准备前出现的错误提示不会仅因时间线滚动而丢失。导出仍会过滤凭据；完整现场可能包含角色名和界面文字，仍用于私下排查。

Failure snapshots preserve the latest already-observed frame when an input or stage fails, independently of rotating observation logs. Submission and possible execution are recorded separately. Identical failures within the same scope and scene are deduplicated for 30 seconds. Snapshots add no game queries and follow existing retention, redaction and export limits.

通行证读取异常额外记录该页编号、定义与错误，不影响其他页检查；`live/pass-claims-latest.json` 和 `live/mission-inspection-latest.json` 纳入导出，区分已处理任务与未能读取的通行证。偷窃入口不可用时保存原生天赋就绪条件、冷却与待办目标，避免只留下“未找到可用天赋”的泛化提示。
