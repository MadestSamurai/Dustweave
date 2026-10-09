# MANSION RUNAWAY

[返回首页 / Home](../README.md)

## 开始使用

1. 连接游戏，在游戏中进入 MANSION RUNAWAY 入口。
2. 从织尘的「工具」页打开 MANSION RUNAWAY，选择普通或挑战模式。
3. 按需开启失败重试，点击开始。窗口显示当前关卡、糖果、生命、计时、连锁和追兵位置。
4. 点击停止即可交回游戏控制权；仅打开工具窗口不会占用其他自动化。

普通模式有四关。挑战模式按游戏计时连续推进，并非只有三关；仍存活时的超时结算不代表已经通过所有关卡。

### 80连锁目标

启用「刷到80连锁后停止」后，工具使用挑战模式并自动续局，直到**同一局**的游戏结算确认最高连锁达到80。达到目标后仍完成当前局的正常结算，再停止；不会累加不同局的连锁或把旧记录当成本局成绩。目标模式使用曾自然达到83连锁的配置，普通模式保留综合路线与道具策略。

普通局进行中需先完成该局，再切换挑战目标。停止和关闭窗口始终可以中止自动化。

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

This goal repeats Challenge rounds until the game's confirmed result reports a maximum chain of at least 80 **within one round**. It finishes that round before stopping and never adds chains across rounds. The goal uses the profile that previously reached 83; ordinary play retains balanced route and nearby-item decisions. Finish an ongoing Normal round before switching to the Challenge goal.

The planner reads the live maze and predicts nearby enemy movement, smooths turns over roughly five frames, weighs item detours, and travels to the actual exit once required candy is collected. Language, appearance and connection ownership are shared with the host. It does not modify native speed, health, timers, scores, rewards or enemy rules.

Live trials completed Normal mode and reached Challenge stage 4, with one naturally recorded chain of 83. Random maps and item drops mean these are observations, not guaranteed outcomes. Goal continuation has synthetic regression coverage; extended live operation remains subject to user feedback.
