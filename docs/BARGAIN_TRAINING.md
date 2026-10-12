# Bargaining training / 砍价技能训练

Open **Daily settings → Trading → Bargaining training** with the selected account logged in. The dialog reads its owned bargaining characters, current-tier XP, talent-pill balance and estimated cost. Starting travels to the plaza merchant and fills the selected tier. It never buys pills or uses talent books. If the balance is insufficient for the tier, the dialog shows the shortage before training can start. At the cap it stops and asks the user to upgrade with a talent book, then read the account again.

在已登录所选账号的情况下，进入**日常设置 → 跑商 → 砍价技能训练**。选择角色，检查预计耗药后开始。程序自动前往广场商人，练满本级后停止，并提示使用天赋书升级。天赋药不足时先显示缺口；本功能不自动补药。日常队列的保药设置不作用于此手动工具，训练消耗以弹窗显示的预算为上限。

The idle dialog holds no controller lease. Reading and training use the shared daily module and control lock. The native skill definition supplies XP and cost; estimates round up the final partial attempt. Every consuming attempt must agree with the response, character XP and resource deltas. Failed bargains that grant XP count normally. Zero XP, changed identity/tier/cost or an uncertain result stop the run. Stop/close cancels an uncommitted preview or waits for the submitted round to settle.

After a round the native discount cancellation closes the shop but can retain its cached discount flag. The training loop checks its own exit instead of opening and closing an ordinary shop to refresh that flag. It then returns directly to the merchant talent menu, waiting on native readiness instead of a fixed post-round delay. Purchase pricing continues to require a fresh shop response.

Validation: `test.ps1 -Groups BargainTraining,BusinessScope,BusinessJournal,TradeReplan`; desktop smoke scope `DUSTWEAVE_UI_SMOKE_SCOPE=bargain`. These use synthetic state and isolated UI data, and do not establish real-game throughput. Training errors are included under `tools/bargain-training/logs` in the diagnostic export; consuming attempts use the existing managed business journal.
