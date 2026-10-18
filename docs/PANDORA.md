# 潘多拉大逃脱 / Pandora Escape

[返回首页 / Home](../README.md)

## 使用方法

1. 在织尘连接游戏，进入游戏中的潘多拉大逃脱选关页，再从**工具**打开潘多拉大逃脱。
2. 选择模式、关卡及角色。角色默认为游戏当前选择，也可指定已解锁角色；更换角色在下一局生效。
3. 点击开始。停止按钮或关闭工具窗口会停止自动操作，当前对局仍留在游戏中。

| 模式 | 执行方式 |
| --- | --- |
| 自动刷游戏货币 | 选择已解锁的地图与难度，自动移动、拾取、选择升级，结算后循环同一关 |
| 自动刷挑战 | 尝试已解锁的挑战，自动配装与走位，失败后重试；读取本局最终结算，确认通关或生存满 20 分钟后停止并保留结算画面 |

挑战模式可关闭**自动选择装备**。遇到选装备界面时等待用户选择，完成后继续；不会把等待手动选择当作超时。角色养成、配装与随机局会影响结果，自动挑战不保证每局通关。

语言与外观跟随织尘。连接准备、自动运行和结算期间协调使用同一控制权；仅打开已经就绪的空闲窗口不会占用其他自动化。

## 挑战决策

- **配装**：优先鞭子、引雷及鞋子，再考虑刀刃旋风、弩箭、无人机与对应辅助道具。选择升级和换牌时同时评估剩余栏位、核心组合与至少五组配对的可行性。确认无法补齐时才重开；未知装备表不会直接触发循环重开。刷货币模式不套用这组挑战配装要求。
- **移动与 BOSS**：结合怪物速度、攻击、当前血量、实际输出和回血道具评估短时风险。按当前招式的预警处理冲刺、跳跃落点与退路，提前避免困在墙角；不能提前知道下一次随机攻击。
- **道具与技能**：优先考虑安全可达的炸弹，再利用磁铁收取掉落；缺血时提高回血权重，围堵时考虑眩晕与主动技能。是否使用技能取决于冷却和可命中目标，不能假定释放后一定清空怪群。
- **后期生存**：15 分钟后逐步转向坚持到时限。眩晕不会让怪群被视为安全区域；临近结束时更重视拖延时间。后期被困墙角而没有已确认安全的脱困路线时保持站定，不将尚未拾取的下一件眩晕道具视为已有保护。旧的“满装备后站定”开关已由这些场景判断替代。

## 等待与恢复

短暂读不到新状态时显示等待，恢复后继续，期间不继续发送操作。确认游戏退出、账号变化或连接被接管时停止。关卡未解锁、未知确认弹窗或页面长时间不变时，会显示具体处理建议。

重试先等待结算页真正关闭，再进入下一局；等待期间不会反复提交同一请求。若显示“进入关卡受阻”，先处理游戏提示或回到选关页再开始。不要在结果尚未明确时连续点击游戏的重试按钮。

遇到问题可使用织尘的**诊断 → 导出诊断日志**。记录包含关卡、角色、配装、运行状态与异常原因，不持续保存每帧的完整敌人和掉落物列表。

## 验证范围

- 普通模式已实测完成第一关后自动续开第一关；停止后改选第二关，可自动进入第二关。
- [1.4.1](releases/1.4.1.md) 接入的挑战策略在一次实机测试中坚持到 20 分钟。单次通过不代表整体胜率。
- [1.4.3](releases/1.4.3.md) 补齐最终结算与生存时间判定，确认完成后停止，避免通关后再次重开。这项结算修复已做离线回归和当前客户端兼容性验证，未另跑一次完整 20 分钟挑战。

## Getting started

1. Connect in Dustweave, enter Pandora's stage selection page in the game, then open **Tools → Pandora Escape**.
2. Choose a mode, stage and character. Keep the game's current character or select an unlocked one; character changes take effect next round.
3. Start automation. Stopping or closing the tool stops automated input while leaving the current round open.

| Mode | Behavior |
| --- | --- |
| Farm currency | Choose an unlocked map and difficulty; move, collect, select upgrades and repeat the same stage after settlement |
| Auto challenge | Attempt an unlocked challenge with equipment planning and movement; retry after defeat, then stop and retain the result screen when the current round's final result confirms completion or 20-minute survival |

In Challenge mode, disable **Automatically select equipment** to make those choices yourself. The tool waits at the selection screen and continues afterward without treating manual selection as a timeout. Character upgrades, equipment and random runs affect results; a clear is not guaranteed.

Language and appearance follow Dustweave. Connection setup, active play and settlement coordinate through the shared control lease; an already connected idle window does not reserve automation ownership.

## Challenge decisions

- **Equipment:** prioritize whip, lightning and shoes, followed by blade whirlwind, crossbow, drone and matching support items. Upgrade and reroll choices evaluate remaining slots, core combinations and the feasibility of at least five pairs. Restart only after the build is confirmed infeasible; an unknown equipment table does not trigger endless restarts. Currency farming keeps its separate policy.
- **Movement and bosses:** use enemy speed, attacks, health, observed damage output and healing pickups to assess short-term risk. React to dash warnings and jump landing areas while preserving escape space near walls. Future random attacks are not known in advance.
- **Pickups and skills:** favor accessible bombs before magnets, prioritize healing when needed, and consider stun pickups and active skills when surrounded. Skills require readiness and predicted target coverage; remaining enemies are not assumed dead after a cast.
- **Late-run survival:** shift toward reaching the time limit after 15 minutes. Stunned crowds do not become safe destinations. When cornered late in a run, hold position unless a verified escape route exists; uncollected stun items do not count as protection. These contextual decisions replace the old max-equipment standing toggle.

## Waiting and recovery

Temporary observation gaps display a waiting state and resume on fresh data without issuing commands in between. Confirmed game exit, account change or connection takeover stops execution. Locked stages, unknown confirmations and stalled transitions show recovery guidance.

Retries wait for the result screen to close before entering another round. Waiting does not repeatedly submit the same request. If entry is blocked, resolve the game's prompt or return to stage selection before restarting; avoid repeatedly clicking Retry while the previous outcome is unknown.

Use **Diagnostics → Export logs and get help** in Dustweave. Exports include stages, characters, build decisions, state and failure reasons without continuous full-frame enemy or pickup dumps.

## Validation

- Live testing completed Normal Stage 1, automatically started it again, and entered Stage 2 after changing the selected stage.
- The challenge strategy integrated in [1.4.1](releases/1.4.1.md) reached 20 minutes in one live trial. This is not an overall success-rate estimate.
- [1.4.3](releases/1.4.3.md) added final-result and survival-time completion detection to prevent restarting after a clear. That fix passed offline regression and installed-client compatibility checks; no additional full 20-minute live challenge was run for it.

## Implementation / 开发入口

The tool uses the unified connection and normal native UI actions. The planner combines observed game state with bounded forecasts, rather than simulating every future random outcome. Game assemblies are not distributed.

- [Challenge equipment planner / 挑战配装](../src/Pandora/ChallengeBuildPlanner.cs)
- [Combat forecast / 战斗预测](../src/Pandora/CombatSimulation.cs) · [Boss movement / BOSS 走位](../src/Pandora/BossKiting.cs)
- [Round and completion rules / 续局与通关判断](../src/Pandora/LoopRules.cs)
- Focused regressions: `Pandora`, `PandoraBuild`, `PandoraSimulation`, `PandoraKiting`, `PandoraTactics` in `test.ps1 -Groups ...`.
