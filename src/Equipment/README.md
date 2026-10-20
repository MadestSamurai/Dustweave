# 装备助手 · Dustweave

简体中文 · [English](README.en.md) · [返回 Dustweave](../../README.md)

装备助手是 Dustweave 的内置模块，负责 N 装搓粉规划和批量精炼。原独立仓库已归档；更新、下载和问题反馈统一使用 [Dustweave 仓库](https://github.com/MadestSamurai/Dustweave)，不再单独发布装备助手。

## 使用

在织尘连接游戏后，从**工具 → 装备助手**打开窗口。窗口先显示加载进度，再准备连接并读取库存；语言、外观和自动化控制权由织尘统一管理。

- **搓粉**：设置强化等级、数量和预算，按需允许 5 药 N 装。主操作按钮先计算计划，再进入执行确认；核对金币、材料、天赋神药和预计产粉后执行。规划以实际库存为限，不自动购买天赋神药。
- **批量精炼**：搜索、筛选并勾选具体装备实例，设置目标等级与资源预算。默认到目标即停，也可选择游戏内目标为 24、整批结束后检查是否达到用户目标的模式。精炼有随机性，预算不保证达标。
- **停止**：停止后续批次，已提交的批次先等待结果并核账。切换账号、库存变化或成本不符时需重新读取和规划；结果不明的消费不会自动重发。

操作与错误日志统一写入织尘诊断目录，通过**诊断 → 导出诊断日志**提供反馈，由主程序的空间管理统一清理。已有装备助手设置、连接标识与历史数据路径保持兼容。

## 源码结构

| 目录 | 职责 |
| --- | --- |
| [desktop](desktop) | WPF 工具窗口、库存筛选与操作入口 |
| [core](core) | 搓粉规划、精炼执行、成本与回执核对 |
| [connection](connection) · [compatibility](compatibility) | 当前客户端适配、连接与数据读取 |
| [hook](hook) · [ipc](ipc) · [shared](shared) | 游戏侧操作、共享通信和数据结构 |
| [localization](localization) · [vendor](vendor) | 本地化资源与保留许可证的依赖 |

三个工程使用 `Dustweave.Equipment.*.csproj`，加入根目录 `Dustweave.slnx`。为兼容现有宿主加载、WPF 资源和依赖锁，程序集名及连接协议保留原有标识；这不表示模块继续独立发布。

从 Dustweave 仓库根目录使用 `build.ps1`、`test.ps1` 与 `package.ps1`，不要按旧独立版说明在本目录构建或下载另一个 EXE。开发要求见[开发指南](../../docs/DEVELOPMENT.md)，宿主接入见[架构说明](../../docs/ARCHITECTURE.md)。

代码采用 [MIT](LICENSE)，保留 MadestSamurai 的原始署名；依赖见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
