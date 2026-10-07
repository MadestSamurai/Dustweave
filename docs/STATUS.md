# Repository status / 仓库状态

Dustweave is a private preview being prepared for open source. The .NET application, integrated tools, synthetic tests and packaging scripts are maintained in this repository. The main project license and public release are not finalized; integrated tools and third-party components retain their own licenses.

Dustweave 目前为私有预览，正在整理开源。日常本体、内置工具、合成测试与打包脚本统一维护。主体许可证与公开发布尚未确定，内置工具和第三方组件保留各自许可。

## Interface / 界面

The desktop supports three languages, light/dark/system appearance, account-scoped task settings and a continuous execution timeline. Diagnostics explains connection status and next steps. Optional plugins use the generic extension interface; unavailable options are hidden. README shows the light screenshot by default, with a separate dark preview.

桌面提供三种语言、浅色／深色／跟随系统外观、分账号设置与连续任务时间线。诊断页提供连接说明和下一步提示。可选插件通过通用接口接入，不可用的选项不展示。README 默认展示浅色图，深色图单独展开。

## Verification / 验证

Source builds and synthetic tests do not require the game or connect to it. Isolated WPF checks cover both themes and all three languages. Actual game compatibility and packaged-binary verification are separate checks; UI screenshots and synthetic tests do not substitute for live-game validation.

源码构建、合成回归和隔离 WPF 检查均不会连接游戏。界面验证覆盖双主题及三种语言；成品验证和实机兼容验证分别进行，不以演示截图或合成测试替代实机结论。0.9.0 已通过两种成品的隔离升级验收；正式发行包由 package.ps1 生成。

See [architecture](ARCHITECTURE.md), [development](DEVELOPMENT.md) and [design rules](DESIGN.md).
## 0.9.0 / 2026-10-07

Account queues now start from the Accounts page. An opt-in scheduler can launch the app through the current user's Windows Task Scheduler. Bundled release notes appear once per version; the OTA client downloads official packages in the background and asks before restarting while idle. See [scheduling and updates](SCHEDULING_AND_UPDATES.md).

多账号队列的主入口已移至账号页。定时页面通过当前用户的 Windows 计划任务启动软件；版本说明每个版本只显示一次；OTA 后台下载官方包，空闲时确认重启。已通过 2,104 项合成检查（56 组）、隔离 WPF 界面验收和真实单文件成品的升级／回退测试。未发布新包、登记真实定时计划或执行游戏队列。完整签名与发布规则见 [OTA 发布流程](OTA_RELEASE.md)。

### OTA acceptance · 2026-10-07

- Synthetic regression: 2,104 checks in 56 groups; `artifacts/tests-20261007-090345-6564/results.json`.
- Packaged application checks: Lite and Portable upgrade from an isolated 0.8.17 copy, failed new-application startup restores the previous executable, and an injected interrupted transaction recovers on the next real executable launch. Account and plugin sentinel files remain unchanged. Evidence: `artifacts/ota-package-check-20261007-r3/results.json`.
- Actual 0.9.0 single-file acceptance candidates: `artifacts/ota-acceptance-20261007-r4/`. These reuse 0.8.17's already packaged static data for updater acceptance; they are not a new game-compatibility release.
- Three-language WPF checks pass, including 15 schedule/update checks with zero game commands and zero registered Windows tasks; `artifacts/ota-ui-check-20261007-final/`.
- Signature/hash-verified domestic-site payload is prepared at `artifacts/ota-acceptance-20261007-r4/site/`. It has **not** been uploaded. GitHub publishing remains deferred.
- The independent installer now disposes its application mutex before restarting either version. Atomic replacements tolerate short file locks and skip identical content; failures retain exact-file diagnostics.

以上是本机隔离成品测试，不代表生产 HTTPS 下载、真实断电或游戏日常队列已经验证。用户的实际 0.8.17 程序、账号和游戏未被升级测试修改。