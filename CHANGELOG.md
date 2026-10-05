# Changelog / 更新记录

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
- Portable includes the .NET 8 desktop runtime. Lite requires .NET Desktop Runtime 8 x64. Extract the entire folder and start `BD2DailyAssistant.exe`.

- 修复主菜单正常停留在 `Empty` 场景时，日常环节一直等待加载、最终超时的问题。
- 同步修复卡带导航；保留实际地图到达、加载遮罩、未知弹窗与操作归属检查。
- 新增35项场景回归；导航专项439项、独立50组／1634条合成回归通过。实机验证待完成。
- Portable自带.NET 8桌面运行时；Lite需要.NET Desktop Runtime 8 x64。完整解压文件夹后启动 `BD2DailyAssistant.exe`。

| Package / 包 | Runtime / 运行环境 |
| --- | --- |
| Portable | Included / 自带运行时 |
| Lite | Install .NET Desktop Runtime 8 x64 / 需另行安装 |

This is a private preview. It retains the existing account/settings location. The navigation fix changes the .NET host only; restarting the game is not required.

本版是私有预览，沿用原有账号与设置目录。此次导航修复仅修改.NET本体，无需重启游戏；使用新版前请关闭旧日常窗口。
