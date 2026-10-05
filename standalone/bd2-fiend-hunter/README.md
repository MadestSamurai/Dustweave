# BD2 Fiend Hunter · 恶魔猎人助手

> **免费开源：** 作者发布版免费提供，GitHub **MadestSamurai** · B站 **MadSamurai**。[官方下载](https://github.com/MadestSamurai/bd2-fiend-hunter/releases) · [来源与风险说明](DISTRIBUTION.md)。第三方收费不代表作者参与、背书或提供服务。
>
> **风险提示：** 本工具与游戏官方无关联。使用可能导致账号处罚、封禁、游戏异常或数据损失，请遵守游戏规则并自行承担使用风险。MIT 许可证保持不变。

[English](README.en.md) · 简体中文

[下载最新版本](https://github.com/MadestSamurai/bd2-fiend-hunter/releases/latest) · [问题反馈](https://github.com/MadestSamurai/bd2-fiend-hunter/issues)

适用于 BrownDust II Windows 客户端的独立恶魔猎人助手，使用布莱德自动挑战单人超困难巴萨卡。

## 下载

当前版本 **0.1.2**。两版功能相同，均内置简体中文／English。

| 版本 | 运行环境 | 建议 |
| --- | --- | --- |
| **Portable** | 内置 .NET 运行时 | 首次使用推荐，下载即用 |
| **Lite** | 需安装 [.NET Desktop Runtime 8 x64](https://dotnet.microsoft.com/download/dotnet/8.0) | 已安装运行时，下载更小 |

只需下载一种版本：EXE可独立运行，ZIP附中英文说明和许可证。无需Python、开发SDK或其他BD2工具。Lite需要 **Desktop Runtime**，基础.NET Runtime或ASP.NET Runtime不够。使用 `SHA256SUMS.txt` 校验下载。

## 快速开始

1. 打开官方Windows客户端，进入恶魔猎人大厅。
2. 打开助手，点击「开始一局」。首次连接会准备本机组件，请稍候。
3. 工具自动选择布莱德／单人超困难巴萨卡，完成战斗、退出结算后停止。已有同配置的单人对局也可以接续。
4. 随时点击「暂停」停止后续操作。助手不会暂停游戏计时。

更新前关闭旧工具；本工具新版之间支持同进程更新与控制交接，通常无需重启游戏。已经加载不支持交接的旧工具时，需先正常重启游戏一次。

## 功能与设置

- 自动接近目标、衔接普攻、释放特殊技能并使用治疗。
- 按攻击时机选择格挡反击或闪避，保留游戏本身的体力和技能条件。
- 实时显示双方生命；明确区分通关与失败，本局结算完成后停止。
- 当前支持 **布莱德／单人超困难巴萨卡**，不操作多人对局或其他配置。

自动策略会受到游戏更新、帧率和延迟影响，不保证每一局都通关。

## 界面语言

在右上角选择简体中文或English。首次启动跟随系统语言，之后记住手动选择；切换语言不重启任务。游戏原始提示和诊断详情保留原文。

维护翻译见 [翻译说明](docs/LOCALIZATION.md)。

## 兼容与限制

支持官方Windows x64客户端，一次连接一个游戏进程；工具与游戏需使用相同权限。不支持手机或Android模拟器。

连接时读取本机游戏接口，自动处理可识别的接口重命名，不锁定发行时的客户端版本。无法可靠匹配时停止连接并保留诊断；不保证所有未来更新都无需维护。发行包不包含游戏DLL、资源或账号数据。

## 诊断与反馈

点击「诊断」打开 `%LOCALAPPDATA%\BD2FiendHunter`。

| 文件 | 用途 |
| --- | --- |
| `events.jsonl` / `events-previous.jsonl` | 最近操作、结果和后台恢复记录 |
| `compatibility.json` | 本机接口适配结果 |
| `desktop-error.txt` | 最近一次操作错误 |
| `preferences.json` | 语言偏好 |

反馈时提供版本、提示和相关日志片段；先去除账号、个人路径等信息。不要上传游戏DLL、完整库存或连接凭据。

## 开发与贡献

需要Windows x64、PowerShell和.NET 8 SDK。常规构建和回归无需安装游戏，不会连接游戏。

```powershell
.\build.ps1 -Locked
.\package.ps1 -Locked
```

产物位于 `dist/v版本号/`。标签触发GitHub Actions构建、验证并发布双版本。

[开发与发布流程](docs/DEVELOPMENT.md) · [文档与发布格式](docs/PUBLICATION_STYLE.md) · [当前版本说明](docs/RELEASE_NOTES.md)

## 许可

项目代码采用 [MIT](LICENSE)。依赖保留各自许可，详见 [第三方许可说明](THIRD_PARTY_NOTICES.md)。本项目与游戏开发商或发行商无隶属关系。
