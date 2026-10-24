# Dustweave · 织尘

把琐碎日常，织成有序时间线。

**Windows x64 · 简体中文 / 繁體中文 / English**

| 版本 | 如何选择 |
| --- | --- |
| Portable | 包含 .NET 桌面运行库，解压后直接运行 |
| Lite | 体积较小，需要已安装 .NET Desktop Runtime 8 x64 |

## 开始使用

完整解压到一个新目录，运行 `Dustweave.exe`。请保留旁边的 `app` 文件夹，其中有数据、说明与许可信息。不要只复制 EXE，也不要覆盖混用不同版本的文件。

升级前从托盘退出旧版织尘。已有账号、设置与历史记录沿用当前 Windows 用户的数据目录，无需复制游戏文件。内置工具跟随主界面的语言与主题。

## 从 1.4 升级到 1.5

从 1.4 及更早版本升级到 1.5，需要手动下载一次完整包并解压到新目录。账号和设置会继续沿用。

- [GitHub 最新发行版](https://github.com/MadestSamurai/Dustweave/releases/latest)
- [夸克网盘](https://pan.quark.cn/s/0ee553c60c0d?pwd=5AgC)，提取码：`5AgC`

完成迁移后，使用新目录的 `Dustweave.exe`；同时更新自己创建的快捷方式。首次启动后，若启用了开机启动或定时执行，请在软件中重新保存这些设置，使其使用新位置。

## 更新与修复

软件设置中的更新页面提供版本检查与手动下载入口。自动更新只下载变化内容，在空闲时征得确认后重启，不中断正在执行的队列；保留更新失败回退和中断恢复。一个本地测试包不代表对应版本已经上线。

1.5 差分不可用或程序文件损坏时，请从 GitHub／夸克下载完整包并解压到新目录，账号和设置会继续沿用。1.4.18 仍保留旧版独立修复入口。

## 账号与定时执行

多账号队列在账号页管理。隔离运行需要安装官方 Sandboxie-Plus，普通单账号使用不需要。游戏更新期间请先停止隔离实例，在主机完成更新后再从织尘启动。

定时执行需要电脑开机、保持唤醒并登录 Windows；默认关闭。

## 来源与许可

免费提供：B站 **MadSamurai** / GitHub **MadestSamurai**。源码使用 MIT 许可证：[Dustweave](https://github.com/MadestSamurai/Dustweave)。内置工具的许可证与来源见 `app/licenses`、`app/docs/tools` 和 `app/THIRD_PARTY_NOTICES.md`。
