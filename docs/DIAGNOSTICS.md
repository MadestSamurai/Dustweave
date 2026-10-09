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
