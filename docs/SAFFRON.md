# Saffron daily stage / 藏红花环节

Enable **Daily settings → Saffron planting and harvest** for each desired account. It is off by default and is independent of ordinary territory automation.

在**日常设置 → 藏红花种植与收割**按账号启用，默认关闭，与普通领地挂机独立。

- Empty fields: plant saffron, then return to the daily queue. Growth takes 20 hours; the stage does not wait in the queue for maturation.
- Ripe saffron: confirm field data and maturity, move directly to the nearest harvest position, and check unlocks, available time, inventory and territory currency before using food. There is no preliminary tour of the fields; movement to subsequent crops is part of harvesting. Missing harvest prerequisites skip food use.
- Food confirmation also checks the three native yield effects. A missing optional callback triggers one read-only refresh; active effects allow harvesting to continue without consuming food again or walking the fields again.
- Buy missing crop-yield foods only after the harvest preflight. The current client has three matching dishes (3071, 3111, 3121), each adding one crop item for ten minutes. Existing duration buffs are reused rather than refreshed.
- Consume the missing buffs at a ready harvest position, then immediately harvest saffron. Other mature crops may be harvested normally; their presence does not block the stage. Trees, minerals, cooking and sales are excluded during this harvest phase.
- Choose **1–3 potato rounds** per account (default **2**). After saffron is harvested, complete exactly that many planting-and-harvest rounds regardless of remaining food effects. A round counts only after its planted potatoes are collected. Replant saffron immediately afterwards, without waiting for buffs to expire.
- Cook saffron soup and sell eligible dishes, retaining one for the next harvest. Locked inventory is respected. If potatoes or capacity prevent processing all saffron, keep the ingredients and report partial completion.

空田首次执行只种藏红花。藏红花未成熟时不使用料理；成熟后直接走到最近的收割位置，确认资源和时限，再补足缺少的三种料理、使用并立即开始收割，不预先逐块绕田探路。若料理回调未完成，会先同步一次状态；三种增产效果已生效即可继续，不重复吃料理或重新探路。不砍树、不采矿，其他成熟作物也可以正常收割。收完藏红花后按账号设置种收 **1–3 轮土豆，默认 2 轮**，不受加成剩余时间影响；每轮收完才计数，达到轮数后立即补种藏红花。制作爱欲浓汤后出售可出售的成品并保留一份。

If the controller disappears its three-second lease stops new actions. Pending purchases or food use are persisted before dispatch. A later run obtains a fresh native LifeInfo response and replans from actual crops and inventory, retaining the completed-round count and finishing any planted batch. Unknown purchases or food use are reconciled rather than replayed. A brief loss of field visibility does not restart the cycle; sustained unavailability or no progress defers this stage so other daily tasks can continue when the game connection is usable.

重跑会读取当前农田与库存，接着收完未完成的一轮，保留已完成轮数，不重复购买或吃料理。短暂的农田画面切换不会重置流程；持续不可用、资源不足或长时间无进展时只暂缓本环节。游戏连接可用时，其余日常继续。

## Validation boundary / 验证范围

Offline checks cover settings migration and account isolation, crop-only command scope, food preconditions, fixed-round counting and recovery, stage isolation, UI rendering and compilation against the installed client. **The revised fixed-round flow still requires a complete live harvest with naturally mature saffron.** Growth time is never changed to make a test pass.

本次固定轮数流程尚未完成成熟藏红花的完整实机验收。不会修改生长时间，也不会把离线测试表述为实机验证；实测需要已有成熟藏红花，或首次种植后自然等待成熟。
