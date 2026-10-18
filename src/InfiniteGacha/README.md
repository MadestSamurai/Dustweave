# 无限抽抽乐 · Dustweave

简体中文 · [English](README.en.md) · [返回 Dustweave](../../README.md)

无限抽抽乐已成为 Dustweave 的内置模块。原独立仓库停止维护并归档，后续代码、下载和反馈统一使用 [Dustweave](https://github.com/MadestSamurai/Dustweave)，无需另装旧版工具。

## 使用

在织尘的**工具 → 无限抽抽乐**打开窗口。窗口立即显示，再准备共享连接；加载期间显示当前阶段，可取消、关闭或在失败后重试。连接成功后选择 A／B 目标与停止条件，点击“开始刷新”。打开窗口本身不会开始抽取。

- **目标配置**：保留 A／B 双列表、搜索、破度筛选、Ctrl／Shift 多选、双击互移，以及按账号和卡池保存的设置。
- **停止条件**：优先判断 A，再判断启用的 A／B 组合；可按重复张数计数，并排除补到 +5 后的溢出。命中后停止刷新，结果仍由用户在游戏中确认保留。
- **通知**：Windows 通知、弹窗与声音可分别启用。测试提醒不会开始抽取。
- **连接与外观**：使用织尘的游戏身份、控制权、语言和主题。准备连接、运行及等待结算期间，与日常和其他工具互斥；待机时释放控制权。

历史偏好继续沿用 `%LOCALAPPDATA%/BD2InfiniteGacha`，不更改账号和卡池键；本次源码迁移不移动用户数据。连接准备完成前不轮询旧组件，也不会自行寻找另一个游戏进程。

## 源码结构

| 目录 | 职责 |
| --- | --- |
| [desktop](desktop) | A／B 配置窗口、结果与通知 |
| [core](core) · [shared](shared) | 客户端控制器、规则、设置与状态机 |
| [compatibility](compatibility) · [hook](hook) · [ipc](ipc) | 客户端适配、游戏侧操作与共享通信 |
| [localization](localization) · [vendor](vendor) | 翻译资源与依赖 |

三个 `Dustweave.InfiniteGacha.*.csproj` 工程已加入根目录 `Dustweave.slnx`，保留原程序集名、命名空间、通信和依赖标识。实际启动、加载动画、互斥与主题复用 [`src/Desktop`](../Desktop) 的工具宿主。

从仓库根目录使用 `build.ps1`、`test.ps1`、`package.ps1`。针对性回归：`UnifiedSuite`、`ToolMenu`、`ActivityLease`；界面检查使用 `tool-preparation` 范围及 `--tool infinite-gacha --smoke <隔离输出目录>`。这些检查只使用合成数据。

保留原作者 MadestSamurai 署名与 [MIT 许可证](LICENSE)。依赖见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
