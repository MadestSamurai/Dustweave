# Development / 开发指南

## Entry points / 入口

从本仓库根目录运行根目录脚本。若调用方必须保持另一个工作目录，也可用脚本完整路径调用；脚本通过 `$PSScriptRoot` 定位源码，不改变调用方的工作目录。开发所需 SDK 由 `global.json` 指定；从其他目录调用时也须使用同一 SDK。

Run root scripts from this repository, or invoke them by full path while retaining your required working directory. Source paths resolve from the script location. Use the SDK pinned by `global.json` in either case.

| Command | Purpose |
| --- | --- |
| `build.ps1` | Locked restore and build of the host and all required utilities/tests |
| `build.ps1 -RefreshDependencyLocks` | Deliberate dependency-lock refresh; inspect the diff afterward |
| `test.ps1` / `test.ps1 -NoBuild` | Build/run or run the synthetic suite in isolated directories |
| `scripts/check-layout.ps1` | Validate solution coverage and project/source/resource paths |
| `check-source.ps1 -AfterBuild` | Validate allowed source inputs and restored/compiled boundaries |
| `build.ps1 -Mode Smoke` | UI checks using isolated demonstration data |
| `package.ps1` | Complete private release package; requires locally installed client data |

`Dustweave.slnx` lists every project for IDE navigation. RID-specific command-line builds use project entry points because MSBuild does not accept `-r` at solution scope. Production namespaces/assembly names retain their existing identities.

## Normal change / 常规修改

1. 在 `src` 或 `assets` 修改对应模块；合成用例在 `tests/Dustweave.Tests/Cases`。
2. 新增源码、配置、依赖锁或项目时登记 `source-manifest.json`；新增项目也加入 `Dustweave.slnx`。清单只记录本仓库内的源码输入，不添加外部工作目录。
3. 运行构建、对应回归与来源边界检查。目录/资源移动时也验证独立打包和嵌入组件。
4. 源码验证、成品验证与实机验证分别记录。普通构建和合成测试不连接游戏。

Maintain host changes directly in this repository. Register new inputs in the source manifest and add new projects to the solution. Keep the manifest limited to repository-local inputs. Keep source, package and live-game verification distinct.

## Independent tools / 独立工具

`standalone/` 中的九个目录是经过边界检查的源码输入，不依赖父研究仓库，也不是构建时在线下载。它们保留自己的版本、依赖锁、说明和许可证。此处的主程序接入层在 `src/Desktop`、`src/Core`、`src/ToolHost`。

更新独立工具时先在其独立项目验证，按明确版本导入需要的源码差异，再登记新文件、检查源码边界、构建并运行主程序的语言/控制锁/工具宿主回归。不要覆盖账号、连接所有权或把独立工具用户数据一起导入。不要为了统一目录而改写其 GitHub 发行版本。

The nine source inputs retain their versions, locks and notices. Import reviewed upstream changes, then verify source boundaries and host language/ownership integration. Do not import user data or alter upstream release identities merely to align directories.

## Packaging / 打包

`package.ps1` 从 `assets/flows`、`assets/specs`、本机客户端规则和许可目录组装完整包。Portable/Lite 共享同一应用入口。产品版本号与 0.7.27 连接基线分离；打包使用隔离依赖锁，验证不会倒写旧候选。

旧版 `source-staging` 是初次建立仓库的过程，不再是日常开发入口。现有发行包留在原交付位置；源码搬迁本身不需要用户重启游戏或迁移账号数据。
## 版本策略 / Version policy

默认使用正式 X.Y.Z 版本号，不使用 preview 后缀；版本号与仓库公开权限独立。Dustweave 保持私有，包不包含外部插件实现。

Use stable X.Y.Z version numbers by default. Version labels do not change repository visibility; Dustweave remains private and excludes external plugin implementations.
