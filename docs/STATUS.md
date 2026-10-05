# Repository status / 仓库状态

Dustweave is a private preview being prepared for open source. The .NET application, integrated tools, synthetic tests and packaging scripts are maintained in this repository. The main project license and public release are not finalized; integrated tools and third-party components retain their own licenses.

Dustweave 目前为私有预览，正在整理开源。日常本体、内置工具、合成测试与打包脚本统一维护。主体许可证与公开发布尚未确定，内置工具和第三方组件保留各自许可。

## Interface / 界面

The desktop supports three languages, light/dark/system appearance, account-scoped task settings and a continuous execution timeline. Diagnostics explains connection status and next steps. Optional plugins use the generic extension interface; unavailable options are hidden. README shows the light screenshot by default, with a separate dark preview.

桌面提供三种语言、浅色／深色／跟随系统外观、分账号设置与连续任务时间线。诊断页提供连接说明和下一步提示。可选插件通过通用接口接入，不可用的选项不展示。README 默认展示浅色图，深色图单独展开。

## Verification / 验证

Source builds and synthetic tests do not require the game or connect to it. Isolated WPF checks cover both themes and all three languages. Actual game compatibility and packaged-binary verification are separate checks; UI screenshots and synthetic tests do not substitute for live-game validation.

源码构建、合成回归和隔离 WPF 检查均不会连接游戏。界面验证覆盖双主题及三种语言；成品验证和实机兼容验证分别进行，不以演示截图或合成测试替代实机结论。当前源码更新尚未生成新的分发包。

See [architecture](ARCHITECTURE.md), [development](DEVELOPMENT.md) and [design rules](DESIGN.md).