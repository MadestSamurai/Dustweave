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

Live validation on 2026-10-14 completed one normal Stage 1 round, automatically returned through the arcade menu and started Stage 1 again. After stopping that repeat, selecting Stage 2 automatically entered the normal Stage 2 battle; automated input was then stopped. Challenge mode and prolonged farming were not tested.

The live check also exposed two native UI details: pages without animation controllers cannot use the generic animation getter, and an acknowledged upgrade may remain open when the game's first close attempt is blocked by another animation. Readiness now handles the former; the latter closes only after the native response or updated skill inventory confirms the upgrade. New selections on reused windows have their own observed revision and do not repeat an old upgrade request.

2026-10-14 实测完成普通第一关结算后自动续开第一关；停止后改选第二关，也能自动进入实际对局，确认后即停止。未进行挑战通关或长时间挂机测试。升级已生效但窗口未关闭的情况，会核对游戏回调或现有技能后补关窗，不重复提交升级。
