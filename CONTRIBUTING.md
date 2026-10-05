# Contributing / 参与贡献

感谢帮助改进 Dustweave · 织尘。欢迎错误复现、翻译、文档、可访问性改进和代码修复。仓库目前仍私有，只有已获访问权限的协作者可以参与；开放时间单独决定。

Thank you for improving Dustweave. Reproducible reports, translations, documentation, accessibility improvements and code fixes are welcome. Access remains limited to invited collaborators while the repository is private.

## Issues

使用错误报告或功能建议模板。描述目标、具体步骤、期望与实际结果；一次讨论一个问题。截图与日志先去除私人内容。涉及凭据或安全缺陷时，按 [SECURITY.md](SECURITY.md) 处理。

Use the issue templates and keep each report focused on one problem. Provide steps, expected and actual behavior. Review attachments for personal data; use the security policy for sensitive reports.

## Development

开发环境为 Windows x64、`global.json` 固定的 .NET SDK 和 .NET 8 Desktop Runtime。打开 `Dustweave.slnx`；代码分区见[架构](docs/ARCHITECTURE.md)，构建与验证见[开发指南](docs/DEVELOPMENT.md)。

Use Windows x64, the SDK pinned in `global.json`, and .NET 8 Desktop Runtime. Open `Dustweave.slnx`; follow the architecture and development guides.

```powershell
.\build.ps1
.\test.ps1 -NoBuild
.\check-source.ps1 -AfterBuild
```

大范围架构或业务改变先讨论目标。新业务使用 .NET，测试使用合成输入；新增源码登记到 `source-manifest.json`。不要提交账号会话、真实库存/回放、游戏程序集或外部插件实现。现有第三方与独立工具许可证须保留。

Discuss broad architectural changes first. Implement new business logic in .NET, use synthetic test inputs and register new sources in the manifest. Never include account sessions, captured inventories/replays, game assemblies or external plugin implementations. Preserve existing third-party notices.

## Pull requests

说明解决了什么问题、行为如何变化、做过哪些验证。区分离线测试、包检查与实际游戏验证；未执行的检查直接写明。避免混入无关格式化、依赖升级或生成文件。

Explain the problem, resulting behavior and verification. Distinguish offline tests, package checks and live-game validation; name checks you did not run. Avoid unrelated formatting changes, dependency updates and generated output.

界面文字应同时考虑简体中文、繁體中文和英文。涉及界面时提供隔离演示截图，检查窄窗口、长文本和键盘操作。文档中英文同步更新。

Consider all three interface languages. UI changes should include isolated-demo screenshots and checks for narrow windows, long text and keyboard access. Keep Chinese and English documentation aligned.

## Collaboration

请尊重其他参与者，讨论行为和证据，不攻击个人，不发布他人的私人信息。维护者可能要求缩小改动或补充复现；报告和贡献不承诺固定响应时间。

Be respectful, discuss behavior and evidence, and do not publish another person's private information. Maintainers may request a smaller change or additional reproduction details. No fixed response time is promised.
