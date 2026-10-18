# 使徒运气防守 · Dustweave

简体中文 · [English](README.en.md) · [返回 Dustweave](../../README.md)

使徒运气防守已成为 Dustweave 的内置模块。原独立仓库停止维护并归档，后续代码、下载和问题反馈统一使用 [Dustweave](https://github.com/MadestSamurai/Dustweave)，无需另装旧版工具。

## 使用

在织尘的**工具 → 使徒运气防守**打开窗口。窗口先显示，再后台准备连接；加载时显示当前阶段和动画，可以取消或关闭。连接成功不会自动开始对局，确认目标后点击开始。连接失败可在同一窗口重新连接。

- **决策**：采用离线退火优化的策略，安排召唤、合成、属性培养与站位，并使用阶段重开规则。随机结果和角色条件仍会影响通关，不保证每局通关。
- **目标**：可选择 50 波与稀有角色目标，设置自动续局或本局结束后停止。打开窗口、切换语言或外观不会启用自动化。
- **控制权**：准备连接、自动运行及等待结算期间，与日常和其他工具互斥；连接完成但尚未运行时释放控制权。暂停会先停止后续决策，等待已发出的操作结算。

工具使用织尘的语言与深浅主题。设置位于当前织尘数据目录的 `tools/apostle-defense/settings.json`；首次使用可读取独立版已有的有效偏好。日志写入 `live/diagnostics/apostle-defense`，由织尘统一导出和清理。

## 源码结构

| 目录 | 职责 |
| --- | --- |
| [planner](planner) | 退火决策、阶段重开、培养与布局 |
| [core](core) | 控制器、连接客户端与恢复 |
| [shared](shared) | 数据结构、局内流程和结算规则 |
| [compatibility](compatibility) · [hook](hook) · [ipc](ipc) | 当前客户端适配、游戏侧操作与共享通信 |
| [desktop](desktop) | 盘面可视化及保留的兼容工具入口 |
| [localization](localization) · [vendor](vendor) | 本地化资源和保留许可证的依赖 |

织尘实际使用的工具窗口、加载与会话管理位于 [`src/Desktop`](../Desktop)：`ApostleToolWindow`、`ApostleToolPanel`、`ApostleToolSession`。这些组件复用本模块的控制器和盘面控件。

三个 `Dustweave.ApostleDefense.*.csproj` 工程加入根目录 `Dustweave.slnx`。程序集名、命名空间、连接标识与设置兼容路径保留原值，避免破坏宿主加载与历史设置；用户看到的版本跟随织尘。

从仓库根目录使用 `build.ps1`、`test.ps1`、`package.ps1`。针对性检查：`ApostleIntegrated`、`ToolMenu`、`ActivityLease`、`ApostleAnnealing`、`ApostlePerformance`、`ApostleTimeToClear`；界面检查使用 `apostle` 与 `tool-preparation` 范围。详见[架构](../../docs/ARCHITECTURE.md)与[开发指南](../../docs/DEVELOPMENT.md)。

保留原作者 MadestSamurai 署名与 [MIT 许可证](LICENSE)。依赖见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
