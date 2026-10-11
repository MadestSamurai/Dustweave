# MANSION RUNAWAY

[返回首页 / Home](../README.md)

## 开始使用

1. 连接游戏，在游戏中进入 MANSION RUNAWAY 入口。
2. 从织尘的「工具」页打开 MANSION RUNAWAY，选择普通或挑战模式。
3. 按需开启失败重试，点击开始。窗口显示当前关卡、糖果、生命、计时、连锁和追兵位置。
4. 点击停止即可交回游戏控制权；仅打开工具窗口不会占用其他自动化。

普通模式有四关。挑战模式按游戏计时连续推进，并非只有三关；仍存活时的超时结算不代表已经通过所有关卡。

### 80连锁目标

启用「刷到80连锁后停止」后，工具使用挑战模式，优先寻找能连续吃豆的路线，**不以通关或清空地图为目标**。使用游戏实际连锁倒计时、移动速度及普通／稀有糖果的位置评估能否续连；不再为了缩短整图路线牺牲连锁。对远期追兵预测更激进，仍避开即将发生的碰撞，不为道具高密度点绕路。普通模式保留综合路线与道具策略。

达到80后停止推关和使用道具，让本局自然结束，等待游戏结算确认后停止；失败结算同样可确认目标。不会直接退出丢失本局记录，不会累加不同局的连锁或把旧记录当成本局成绩。

未达标时会提前评估是否值得继续：计入后续连锁奖励后，当前连锁与剩余糖果仍不够80，或多次独立路线搜索持续找不到有希望的路线，会通过游戏的暂停、重试、确认流程重新抽取挑战地图。路线评估每两秒在后台进行一次，至少观察三次；接近80的路线获得更长观察时间。这是节约尝试时间的策略，并非证明该图绝对无解；不会把单次规划失败、旧盘面结果或普通模式的避敌停留直接判成重开。

2026-10-11实机验证：先验证68连断开、剩余41个糖果时的原生重开，约7秒回到新盘面。接入完整判断后，连续3张地图在约13–15秒时因路线不理想自动重开，第4局达到80连，并由游戏结果确认 `MaxChain=80` 后停止；本轮总计约128秒，未出现活动日程错误。一次成功样本不代表长期成功率。导出诊断保留重开的连锁、糖果、预计路线、原因和次数，不保存持续的完整盘面流。

普通局进行中需先完成该局，再切换挑战目标。停止和关闭窗口始终可以中止自动化。

2026-10-11：连锁优先策略通过105项针对性检查及当前客户端组件编译。8个历史盘面的离线路线对照中，原覆盖路线的预计最高连锁为51–62，新策略为74–80，其中4个盘面找到80连路线。此对照使用采集的糖果位置、速度和游戏1秒连锁时限，未将追兵干扰、后续道具和随机生成纳入路线结果；不代表实机达标率。全图路线在后台最多搜索30毫秒，前台短期碰撞决策保留约5毫秒预算。

### 重新开始与活动入口

自动续局等待游戏完成场景切换。若回到原卡带时，本局已确认的小游戏入口信息被清空，工具会重新核对游戏活动日程，并通过游戏正常入口恢复一次。不同活动、未知入口或当前不可用的活动不会被覆盖或强行继续。失败重试与成功退出后再开局都使用这一检查。

“入口信息已丢失”与“活动当前不可用”分别显示，并记录恢复前后的活动标识与场景阶段。无法恢复时，从小游戏大厅重新进入后再开始；不必因为这一提示重启游戏。

2026-10-11：续局边界通过合成回归及当前客户端组件编译；尚无本次用户故障现场，不能据此断言领地是唯一触发条件，实际从领地返回的连续续局仍需使用验证。

## 决策与限制

- 读取当前迷宫、糖果、出口、敌人及道具状态；规划完整收集路线，用短期预测检查下一步风险。
- 约五帧平滑转向，并将转向时间纳入移动预测。
- 比较道具收益与绕行成本。必要糖果收完后，根据游戏实际生成的出口继续移动。
- 组件按需准备，语言和外观跟随主程序；组件交接或连接过期会停止输入。
- 不修改游戏速度、生命、计时、分数、奖励或敌人规则。

普通模式已完成四关实测；挑战曾达到第4关和单局83连锁。地图与道具存在随机性，这些记录不保证每一局的结果；成就领取由游戏判定。80连锁目标的自动续局边界有回归覆盖，完整长期运行仍需实际使用反馈。

## Getting started

Connect to the game, open the native MANSION RUNAWAY entrance, then launch the matching tool from Dustweave. Choose Normal or Challenge mode and enable retries if desired. The live view shows stages, candy, lives, time, chains and pursuers. Stop returns control; an idle tool window does not reserve automation ownership.

Normal mode has four stages. Challenge continues under the native timer; surviving until timeout is not equivalent to clearing every stage.

### 80-chain goal

This goal prioritizes a continuous 80-chain in Challenge mode, rather than stage clears or complete maze coverage. It uses the live chain timer, movement speed, and ordinary and rare candy positions. Distant enemy predictions are treated less conservatively while imminent collisions are still avoided; it does not detour toward dense magnet targets. Ordinary play retains its balanced planner. Finish an ongoing Normal round before switching to the Challenge goal.

At 80, movement and item use stop so the round can end naturally. Only the game's confirmed result completes the goal, including a defeat with a sufficient maximum chain. Direct exit would discard the record. Chains from separate rounds are never added together. Before reaching 80, insufficient remaining candy (including future native chain rewards) or repeatedly unpromising route searches trigger a native pause/retry/confirm sequence. Independent background checks run every two seconds and require multiple samples; near misses receive a longer grace period. This is an efficiency heuristic, not a proof of impossibility. Normal play is unchanged.

On October 11, live testing confirmed the native retry path after a broken 68-chain with 41 candies remaining. The integrated policy then restarted three unpromising maps after roughly 13–15 seconds each. The fourth round reached 80 and stopped after the native result confirmed it, about 128 seconds after the first map began. No schedule error occurred. This single trial is not a long-term success-rate estimate.

The chain-first policy has 105 targeted checks and compiles against the installed client. An offline comparison of eight historical boards projected maximum chains of 51–62 for the coverage planner and 74–80 for this policy; four boards yielded an 80-chain route. This uses captured candy positions and speed with the native one-second chain window, excluding enemy interference, subsequent items and random spawns. It is not a live success-rate measurement. Background route search has a 30 ms budget; local collision decisions retain an approximately 5 ms budget.

The planner reads the live maze and predicts nearby enemy movement, smooths turns over roughly five frames, weighs item detours, and travels to the actual exit once required candy is collected. Language, appearance and connection ownership are shared with the host. It does not modify native speed, health, timers, scores, rewards or enemy rules.

Restarts wait for native scene transitions. If the previously verified minigame entry is cleared on return to the original cartridge, the tool rechecks the native event schedule and reopens that exact entry once. It never replaces another activity or bypasses availability checks. Missing entry information and an unavailable event have separate messages and diagnostic context. If recovery fails, reopen the game from the minigame hub. These guards have synthetic coverage and compile against the current client; the reported territory-return case has not yet been reproduced live.

Live trials completed Normal mode and reached Challenge stage 4, with one naturally recorded chain of 83. Random maps and item drops mean these are observations, not guaranteed outcomes. Goal continuation has synthetic regression coverage; extended live operation remains subject to user feedback.
