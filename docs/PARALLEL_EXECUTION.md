# Concurrent account queues / 多账号同时运行

0.9.4 local candidate · 2026-10-08. Source and packaged offline acceptance are separate from live-game acceptance. This update was developed during game maintenance; no game was launched or connected.

0.9.4 本地候选。开发期间游戏维护，本次没有启动游戏、连接组件或消耗游戏资源。0.9.3 的双账号实测结论不代表新集中调度已完成实机验收。

## 操作步骤

1. 首次安装官方 Sandboxie-Plus。已有安装无需重复安装，不需要手动建沙箱或复制游戏目录。
2. 在「账号」页保存账号，分别配置各账号的任务；勾选本次要执行的账号。
3. 点击底部「依次运行」打开执行方式，启用「同时运行」。默认上限 2，可选 1–4；3–4 个尚未实机验证。
4. 点击「运行勾选账号」。主窗口展示每个账号的圆环百分比、当前环节、结束时间与详细步骤，不再要求逐个打开 Dustweave 窗口。
5. 可以显示某个游戏、单独暂停／继续／停止，也可以全部暂停／继续／停止。暂停保留该账号的名额；结束后才给下一个排队账号补位。
6. 默认只在成功结束后关闭**本次启动创建的游戏进程**。此前已经打开的游戏、失败账号的游戏及其他账号均保留。可在执行方式里关闭此选项。

定时计划使用相同执行方式、上限和完成后关闭设置；账号顺序仍以计划自身保存的顺序为准。不开启「同时运行」时沿用原有顺序执行。

主窗口退出会通知各执行器停止，游戏不会被强制结束。主窗口意外退出时，执行器在心跳过期后请求停止当前队列；已经发给游戏的操作不会被强制撤销。再次打开只恢复显示记录，**不会自动补发未确认操作**。同一次运行的安全暂停可直接继续；进程或账号改变、跨刷新周期、结果不确定时，沿用原队列的核对规则。

启动失败、登录过期、账号不符或某环节失败只影响对应账号。凭据过期时请正常登录并重新保存；不能自动完成首次登录或绕过游戏认证。不要同时在普通游戏和隔离实例登录同一个账号。

## English quick start

Install official Sandboxie-Plus once. Save accounts and configure their tasks in Accounts. Select accounts, open the execution-mode button, enable concurrent execution (default 2), then run the selected accounts. The main window shows all progress and controls. Each finished slot starts the next queued account; paused workers retain their slot. A scheduled run uses the same global execution mode and capacity, with its own saved account order.

The optional close-after-completion setting only closes the exact game process created for that run after success. Existing games and failed runs are preserved. Closing the host requests worker stops; losing the parent heartbeat stops an orphan worker. Reopening shows the previous results without replaying uncertain operations. Review interrupted accounts before running them again. First sign-in and expired credentials still need normal user authentication.

## Implementation boundaries

- The host schedules only. A hidden worker uses the existing account coordinator, shared connection ownership, packaged stage executor and durable queue within the account's managed sandbox.
- Account preferences are snapshotted per run. Jobs and status contain no sign-in secrets; the existing temporary DPAPI snapshot remains the credential handoff.
- Driver-reported sandbox identity, account key, process ID and start time establish ownership. The host control lease prevents concurrent integrated automation; each worker holds its own sandbox-local lease.
- Per-job command/status files live in the sandbox data directory. Commands have monotonically increasing sequence numbers. Repeated heartbeats cannot undo a worker-initiated pause or reverse a stop.
- Startup runs off the UI thread. Sandbox configuration is serialized while independent worker heartbeats continue. A stale but living worker retains capacity until it exits or reports a terminal state, preventing duplicate execution.
- `parallel-queue.json` retains the host overview. Each sandbox preserves its ordinary queue evidence. The main overview is not an alternate record of game progress and cannot authorize replay.
- UI supports Simplified Chinese, Traditional Chinese, English and both themes. The circular indicator counts completed/skipped task stages; it is not an estimated remaining time.

## 开服后验收 / After maintenance

- [ ] 两个账号从新版主窗口启动、身份检查通过，均完成真实队列。
- [ ] 三个已保存账号、上限 2：前一账号结束后第三个自动补位。
- [ ] 暂停／继续一个账号，另一个不中断；全部暂停后仅继续指定账号。
- [ ] 单账号登录或任务失败不阻塞其他账号；能从界面看到原因。
- [ ] 正常关闭主窗口，执行器停止；只关闭成功且由本次创建的游戏。
- [ ] 新游戏版本重新解析、完整日常环节及长时间运行。
- [ ] 3–4 个账号容量、真实定时触发、首次登录与失效登录。

以上保持 `source_implemented_pending_runtime`。离线回归和截图仅验证软件逻辑与显示，不替代这些项目。

0.9.5 补充：游戏更新前关闭所有实例，普通启动器更新一次。隔离启动会在导入登录状态前核对程序内容，避免旧沙箱文件遮住新客户端。完整验证顺序见 [游戏更新清单](CLIENT_UPDATES.md)。该保护不替代新客户端双账号实测。
