# Isolated instances / 隔离实例

Status: experimental local candidate; no public release or OTA deployment.
状态：本地实验候选，尚未发布或上线 OTA。

## Central queue in 0.9.4 / 0.9.4 集中队列

The main window now supports opt-in concurrent queues. See [setup, controls and acceptance boundaries](PARALLEL_EXECUTION.md). This new scheduler is pending live acceptance; the 0.9.3 evidence below covers the earlier separate-window workflow.

主窗口已补齐可选的同时运行、自动补位和集中控制。新流程参见 [操作与验收说明](PARALLEL_EXECUTION.md)。以下实机记录属于 0.9.3 独立窗口流程；0.9.4 集中调度等待开服后验证。

## Separate-window workflow in 0.9.3 / 0.9.3 独立窗口流程

1. Install the official Sandboxie-Plus once. Dustweave does not install a driver silently.
2. Save each account through the normal Dustweave account page first.
3. Highlight an account and choose **Open isolated / 隔离启动**. Repeat for a different account.
4. Wait for the identity check in each new window, then run that window's chosen stages.
5. Stopping or closing one window only affects its instance. Close its game as well to release its resources.

首次安装官方 Sandboxie-Plus 后，无需自己配置沙箱。账号页选中一个已保存账号，点击「隔离启动」；再为另一个账号重复操作。每个窗口先检查实际登录身份，之后独立选择环节并执行。停止和关闭一个窗口不影响其他窗口。安装更新前需要关闭所有隔离工具窗口，游戏可以保持打开。账号管理、计划任务和 OTA 仍在普通主窗口完成；0.9.3 的计划任务按原顺序执行；0.9.4 可选择集中并行执行。

The installed game is shared read-only until the sandbox needs to write; game preferences, account registry values, WebView profile, IPC and application records are isolated. A full second game copy is not required. A copied folder alone does not isolate the game's global single-instance mutex.

正式入口复用当前安装的游戏，通过 Sandboxie 隔离需要写入的数据，不要求每个账号额外复制整套游戏。只复制目录无法解决游戏的全局单实例互斥。实验中的完整复制目录仅用于对照。

## Identity and recovery / 身份与恢复

- Each managed sandbox is registered against the account identity, independent of the displayed name or saved slot number.
- Process scope comes from Sandboxie's driver. Command-line account/PID strings are not trusted as proof of scope.
- Bootstrap validates the account and sandbox before writing the five existing sign-in fields. Actual in-game account identity is checked again by the normal queue pipeline.
- A different running account, changed process/start time, unexpected shared-resource settings or modified sandbox directory stops the operation.
- The original account vault and normal-host registry are preserved. Closed isolated instances keep their own settings and history for reopening.
- Expired game sign-in credentials still require a normal sign-in. Close the isolated game, update the saved sign-in in the main window, then reopen isolation. A temporary DPAPI-encrypted snapshot transfers the selected saved sign-in; no plaintext credentials are placed in requests or command lines. Isolation cannot bypass game authentication.

实例根据账号身份登记，改名或移动账号位置不会生成另一份实例。启动前校验实例和账号，进入游戏后再次核对实际身份。连接、停止、子组件和恢复沿用原有归属检查，只是检查范围限制在当前沙箱。过期凭据仍需在普通窗口重新登录并保存；关闭该账号的隔离游戏，再点击隔离启动，才会应用新的加密登录快照。不会在正在运行的游戏旁改写登录信息。

Managed data: `%LOCALAPPDATA%\Dustweave\instances\`. Do not manually share the account registry, named pipes or application data directory between instances. Do not delete a running instance's files.

## Compatibility / 兼容性

MacType can inject its own child-process hook after startup. On this test machine, a first-chance access violation showed its CreateProcess interception jumping through an invalid trampoline; the later CLR error concealed that first fault. Managed instances therefore exclude `MacType.dll` and `MacType64.dll` **only for the Dustweave executable inside that sandbox**. Global MacType settings and other applications are untouched. Compilation remains in the established pipeline; no separate compiler workaround is required.

本机已确认 MacType 与沙箱的进程启动拦截冲突。Dustweave 只在自己管理的隔离空间里排除自身进程的 MacType 注入，不修改系统字体设置。游戏和其他应用不在此排除规则内。

Existing optional tools retain their own modules and licenses. Sandbox visibility hides unrelated non-system processes from legacy process-name discovery; the shared connection descriptor still checks process identity. An installed game update should be completed with instances closed, then reconnect against the updated client. This candidate does not promise compatibility with a future client update without verification.

## Live acceptance / 实机验收

2026-10-08, Windows x64, Sandboxie-Plus 1.18.5:

- Two distinct accounts concurrently signed in, each with its own game process, observer, execution module and task history.
- Both completed the real management collection stage. Each independently checked mail and returned `mailbox_empty`.
- One account's tool and game were normally closed and reopened; the second account stayed online. The reopened account passed identity verification again.
- Stopping one running queue left it paused while the other queue continued to completion. Clean paused queues can renew the observer connection within the same live game before resuming; changed game/account/cycle and uncertain operations remain guarded.
- No administrator prompt was needed after the one-time Sandboxie installation in these launches.

以上证明双账号核心链路可行，不等于所有环节、所有独立小游戏、更多账号规模或跨游戏更新的长时间运行都已实测。最终包与停止恢复检查结果记录在本次验收文档；不能用离线测试替代实机结论。

Official references: [Sandboxie release](https://github.com/sandboxie-plus/Sandboxie/releases/tag/v1.18.5), [Start command line](https://sandboxie-plus.github.io/sandboxie-docs/Content/StartCommandLine/), [resource access](https://github.com/sandboxie-plus/sandboxie-docs/blob/main/docs/Content/ResourceAccessSettings.md).

## Game updates / 游戏更新

0.9.5 compares host and isolated game code before importing sign-in state or launching a game. Stale overlays are reported without deleting accounts or clearing the sandbox. Update the shared installation once through the ordinary official launcher with all instances closed. See [client update checklist](CLIENT_UPDATES.md).

0.9.5 在导入登录状态与启动游戏前核对普通目录和隔离目录所见的程序内容。发现旧覆盖文件时停止并说明原因，不清空账号数据。更新前关闭所有实例，使用普通官方启动器更新共享安装目录一次。新客户端仍需按清单实测。

## 0.9.7 session handoff / 登录交接修复

2026-10-08：普通窗口与隔离窗口切换前，先读取并保存原环境刷新后的凭据，再把较新的凭据交给目标环境。沙箱回传使用同一 Windows 用户的 DPAPI 加密短期文件，不在命令行或诊断中记录令牌。账号身份、原槽位及名称保留；运行中的同账号游戏仍不能同时在另一环境启动。

凭据的采集时刻不因复制、重命名或导入而变更。每个环境保存单独的加密观察基线，避免收回新令牌后，又把未变化的旧注册表当成一次新采集写回来。相同采集时刻但内容冲突时保留原数据并说明，不能凭文件修改时间猜测。

验证：真实 DPAPI 临时库的双向刷新、往返、工具重启、旧副本回退、改名、跨账号保护等回归已通过；实际 Sandboxie 中短期导出进程可执行，已关闭自动登录的状态不会回传覆盖主机。更新后真实账号的成功登录与再次切换仍待验证，不能把标题页连接成功当作登录成功。

Sign-in handoff now captures renewed credentials before changing contexts and returns sandbox renewals through an encrypted, short-lived transfer. Copies and aliases retain the original capture age. A separate per-context observation prevents an unchanged stale registry value from overwriting a returned renewal. Offline round-trip regressions and Sandboxie transfer transport passed; real account authentication and a subsequent switch remain pending.

## 0.9.8 registration recovery / 登记恢复

A missing host `instance.json` is distinguished from an explicit conflicting or unreadable record. Recovery requires the configured sandbox root, inner account binding and decryptable account snapshot to agree. The host marker is created atomically without replacing an existing record. Account credentials, game registry and game progress are untouched. Empty, unused directories do not imply an ownership conflict. Genuine conflicts remain visible on the affected account; queued peers can continue.

缺失外层登记不再等同于账号冲突。仅当 Sandboxie 实际目录、内部绑定和可解密账号记录三者一致时，自动补建外层登记；存在但不可读、属于其他账号或目录变化时保留现场并说明。不会修改登录令牌、游戏注册表或进度。2026-10-08 已在实际桌面上下文恢复本机三份缺失登记，并用现有 0.9.7 成品验证回收流程不再被此错误阻断。完整登录与日常执行不在本次验证内。
