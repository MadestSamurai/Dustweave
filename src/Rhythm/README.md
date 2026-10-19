# 音游 · Dustweave

简体中文 · [English](README.en.md) · [返回 Dustweave](../../README.md)

音游已迁入 Dustweave。原独立仓库停止维护并归档，后续代码、下载和反馈统一使用 [Dustweave](https://github.com/MadestSamurai/Dustweave)。

## 使用

在织尘的 **工具 → 音游** 打开窗口。窗口先显示，再准备共享连接；加载时可取消或关闭，失败后可重试。连接不会开始演奏，点击 **开启自动演奏** 后再在游戏中选曲、选择难度并开始。

- 随机偏移默认 **±50 ms**，范围 0–200 ms；0 关闭随机偏移。
- 整体校准范围 **−500–500 ms**，正数延后、负数提前。修改在下一曲生效。
- 支持普通、长按、滑动、连打和 Fever；暂停保留时间轴，中途开启跳过已过音符。
- 停止或关闭窗口会结束输入。准备和运行期间与其他自动化互斥，连接后待机不占用其他功能。
- 跟随织尘的语言和主题。不负责选曲、购买次数或自动续局，不修改分数、血量或歌曲时钟，不保证全连、满分。

## 数据与源码

设置保存在织尘数据目录的 `tools/rhythm/settings.json`，首次使用可沿用旧版有效偏移设置，不导入旧指令或身份。日志归入 `tools/rhythm/diagnostics`，统一导出及清理；已知旧日志迁移时保留时间戳。组件缓存使用织尘的统一存储和迁移设置。

- [desktop](desktop)：窗口、状态与设置。
- [core](core) · [shared](shared)：控制、谱面计划及存储。
- [compatibility](compatibility) · [hook](hook) · [ipc](ipc)：适配及通信。
- [localization](localization) · [vendor](vendor)：翻译与依赖。

使用根目录的 `build.ps1`、`test.ps1`、`package.ps1`。工程名为 `Dustweave.Rhythm.*`，保留原程序集和通信标识。检查范围：`Rhythm`、`UnifiedSuite`、`ToolMenu`、`ActivityLease`；界面检查：`tool-preparation` 和 `--tool rhythm --smoke`，均不操作真实游戏。

保留 MadestSamurai 署名与 [MIT 许可证](LICENSE)。依赖见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
