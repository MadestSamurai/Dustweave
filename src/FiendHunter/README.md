# 恶魔猎人：怒火边缘 · Dustweave

简体中文 · [English](README.en.md) · [返回 Dustweave](../../README.md)

恶魔猎人：怒火边缘现在由 Dustweave 统一维护。原独立仓库归档后仅保留历史版本；后续下载与反馈请使用 [Dustweave](https://github.com/MadestSamurai/Dustweave)。

在 **工具 → 恶魔猎人：怒火边缘** 打开窗口，等待共享连接就绪。选择布莱德、泰瑞丝或格兰希特，搭配巴萨卡或炎虎以及普通／困难／极难；可以接续相同配置的进行中对局。工具自动攻击、防御、闪避与治疗，在本局结算后停止。暂停会停止操作，游戏本身继续运行。

窗口沿用织尘的语言、深浅主题、连接加载动画与操作互斥。打开窗口不会自动开始战斗。连接准备完成前不读取旧组件；游戏退出或账号改变后停止发送操作。

策略以本局通关为目标，不安排小游戏内的额外任务。

## 源码与数据

- [desktop](desktop)：怪物选择、运行状态与操作窗口。
- [shared](shared) · [core](core)：策略、控制与本机客户端。
- [compatibility](compatibility) · [hook](hook) · [ipc](ipc)：跨版本适配、游戏侧执行与共享通信。
- 编译缓存复用主程序的 `suite-cache/modules/fiend-hunter`，跟随缓存位置设置。
- 偏好及诊断位于主程序数据目录的 `tools/fiend-hunter`；日志由织尘导出、统计和按期清理。打开工具时将旧目录中已知日志归入此处，保留原文件时间；不移动账号或通信状态。

使用仓库根目录的 `build.ps1`、`test.ps1`、`package.ps1`。三个 `Dustweave.FiendHunter.*` 工程已加入根解决方案，原程序集与通信标识保留，以兼容共享加载器。

保留原作者 MadestSamurai 署名及 [MIT 许可证](LICENSE)。依赖见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
