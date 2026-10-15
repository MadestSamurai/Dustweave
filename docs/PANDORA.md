# 潘多拉大逃脱 / Pandora Escape

## 使用方法

在织尘连接游戏后，进入游戏中的潘多拉大逃脱选关页，再从「工具」打开潘多拉大逃脱。

- **自动刷游戏货币**：选择已解锁的地图与难度，点击开始。自动移动、拾取、选择升级，并在结算后循环同一关。沿用游戏当前选择的角色。
- **自动刷挑战**：自动尝试已解锁的挑战，失败后重试。**尚未加入装备数据，目前自动化无法通关挑战模式。**

停止按钮停止自动操作，当前对局仍留在游戏中。关闭窗口同样停止自动操作。工具运行时占用织尘的自动化控制；仅打开窗口不占用其他功能。

简体中文、繁体中文和 English 跟随织尘的语言设置，外观也跟随主程序。

## 等待与恢复

等待结算时不会重复提交请求。关卡未解锁、游戏连接丢失、未知确认弹窗或页面长时间没有变化时，会停止自动操作并显示处理建议。处理游戏中的提示后，可再次开始；不要在尚未明确结果时反复点击游戏的重试按钮。

诊断导出包含关卡、运行状态及异常原因，不持续保存每帧的敌人和掉落物列表。

## How to use

Connect the game in Dustweave, open Pandora's stage selection page in the game, then open Pandora Escape from **Tools**.

- **Farm currency**: choose an unlocked map and difficulty. The tool moves, collects items, selects upgrades and repeats that stage after settlement. It uses the character currently selected in the game.
- **Auto challenge**: attempt an unlocked challenge and retry after defeat. **Equipment data is not yet included; automation cannot currently clear challenge mode.**

Stopping or closing the tool stops automated input while leaving the current game round open. An active run holds Dustweave's automation control; an idle tool window does not. Language and appearance follow Dustweave.

Settlement requests are submitted once per transition. A locked stage, lost connection, unknown confirmation or stalled transition stops automation with a recovery message. Resolve the game's prompt before starting again. Diagnostic exports include stage and transition information without continuous frame dumps.

## Implementation and validation

The tool uses the unified Dustweave connection, bounded input leases and the game's normal UI actions. The compatibility component resolves table and UI contracts from the installed client; game assemblies are not distributed.

Movement predicts nearby enemies, projectiles and boss attack areas. Upgrade selection evaluates immediate benefit and weapon/passive combinations. Currency mode additionally values coins and currency bonuses. This is a heuristic planner, not a complete combat or equipment simulator.

### BOSS 走位 / Boss kiting

Boss encounters use a bounded 2.4-second forecast with at most 12 retained paths per layer. The planner compares retreats and turns, reserves usable escape directions before reaching a wall, and executes only the first direction before observing again. Current collisions and attack warnings take priority over damage and pickups. Normal loot collection keeps its existing planner.

Effective DPS comes from each boss's observed HP loss over active game time. Pauses, pooled spawns, health resets and long observation gaps do not carry old estimates forward. Measured distance bands retaining similar DPS favor the safer distance; short kill windows permit a smaller margin, while low health increases it. Sustained zero damage outside a vulnerable boss's range cautiously narrows the target distance. Weapon-table ranges are only initial estimates, not an equipment damage simulator.

Known wind-up, dash, jump and recovery animations constrain prediction. Dashes stop at the observed destination; beyond the current phase the forecast assumes pursuit with a growing margin. It does **not** claim to know the next randomly selected attack. Completed animations cannot recreate stale warnings, and a dash box's travel length is not counted again as corridor width. Transition diagnostics include a compact movement decision without creating per-frame disk logs.

遇到 BOSS 后，优先保留退路，比较提前转弯、沿边脱困和绕行输出；不再持续向远离 BOSS 的方向退。工具按实际掉血调整输出距离，暂停和重开不沿用旧 DPS。冲刺、跳跃等当前招式以游戏动画和攻击区域为依据；对下一招仍保留不确定性，不能保证提前知道随机招式。

2026-10-15 development validation: 148 scoped synthetic checks passed, and the movement component compiled against the installed client without connecting to the game. A paired experiment against the 1.3.13 planner covered 96 synthetic arenas with chasing/dashing bosses, varied speeds/ranges and perturbed pursuit speed. Contact events fell from 384 to 0, corner samples from 117 to 0, and simulated kills within 30 seconds rose from 31 to 78. These are simplified model outcomes, **not actual game completion rates**. Single-boss planning averaged approximately 0.2 ms in .NET; the separate 104-threat case remained under 4 ms on the development machine. Unity observation and native calls require live profiling. A real-game BOSS survival test has not been performed for this change.

Reproduction/evidence: `tests/Dustweave.Tests/Cases/PandoraKitingCases.cs`, local `artifacts/research/pandora-boss-kiting-20261015`, and `artifacts/pandora-kiting-client-final`. Run the `Pandora` and `PandoraKiting` groups for scoped regressions.

### 道具与主动技能 / Pickups and active skills

道具决策使用当前血量、怪物分布、攻击预警与通行情况。安全可达的近处炸弹优先于磁铁；观察到炸弹实际生效后，给新掉落物一次短暂更新窗口，再收磁铁。炸弹过远、不可达或消失不会让磁铁一直等待。回血优先用于明显缺血，眩晕用于普通怪物围堵，安全时尽量绕开保留；保命路线仍高于道具节约。BOSS 期间不再完全忽略有用道具，但不会为了拾取而穿越预测碰撞或攻击预警。

主动技能检查实际冷却和可命中目标，子弹不计作技能目标。定向扇形按技能表的发射数量与角度估算覆盖，径向技能保留游戏固定方向；释放前调用原生转向，移动不等待技能结算。附近可安全拾取眩晕且尚未危急时，先拾取眩晕，再接技能。眩晕只以原生效果处理回调确认，技能只以冷却实际变化确认，不能把点击返回值或道具消失当成成功。短暂利用清场方向后，沿剩余怪群外侧绕行；每步仍避让当前活怪，不能假定技能必然击杀。

The planner reserves heals and EMP pickups until useful, prefers accessible nearby bombs before magnets, and preserves valuable pickups in a separate bounded observation budget. Items already travelling to the player are excluded. Tactical paths never outrank collisions or attack warnings. EMP is crowd control for ordinary monsters, **not** ultimate charge or boss control.

Active skills require native cooldown readiness and predicted enemy coverage. Directional fan rays use the current skill table; radial rays retain their native world orientation. The native facing is set before clicking the skill, with independent movement continuing immediately. Applied-item callbacks and actual cooldown changes confirm effects. Expired pickups, rejected casts and restarted rounds cannot retain an unbounded combo wait. Confirmed casts only bias an escape direction: remaining live enemies continue to block it.

2026-10-15 development validation: 217 focused checks across `Pandora`, `PandoraKiting` and `PandoraTactics` passed. The installed-client compatibility compilation passed without connecting to the game. A combined 185-threat / 288-drop .NET benchmark averaged 4.52 ms per decision over 100 iterations on this machine; this excludes Unity observation/native-call costs. Evidence: `artifacts/tests-20261015-064630-9647/pandora-tactics.json` and `artifacts/pandora-tactics-client-final`. These changes are included in 1.3.14. Live gameplay has not been validated; no claim of improved real-game win rate is made.

Live validation on 2026-10-14 completed one normal Stage 1 round, automatically returned through the arcade menu and started Stage 1 again. After stopping that repeat, selecting Stage 2 automatically entered the normal Stage 2 battle; automated input was then stopped. Challenge mode and prolonged farming were not tested.

The live check also exposed two native UI details: pages without animation controllers cannot use the generic animation getter, and an acknowledged upgrade may remain open when the game's first close attempt is blocked by another animation. Readiness now handles the former; the latter closes only after the native response or updated skill inventory confirms the upgrade. New selections on reused windows have their own observed revision and do not repeat an old upgrade request.

2026-10-14 实测完成普通第一关结算后自动续开第一关；停止后改选第二关，也能自动进入实际对局，确认后即停止。未进行挑战通关或长时间挂机测试。升级已生效但窗口未关闭的情况，会核对游戏回调或现有技能后补关窗，不重复提交升级。
