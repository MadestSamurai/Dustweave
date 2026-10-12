# Saffron daily stage / 藏红花环节

Enable **Daily settings → Saffron planting and harvest** for each desired account. It is off by default and is independent of ordinary territory automation.

在**日常设置 → 藏红花种植与收割**按账号启用，默认关闭，与普通领地挂机独立。

- Empty fields: plant saffron, then return to the daily queue. Growth takes 20 hours; the stage does not wait in the queue for maturation.
- Ripe saffron: confirm all fields and their native maturity, survey every harvest position, and check unlocks, available time, inventory and territory currency before using food. Other crops or missing prerequisites skip the step without consuming food.
- Buy missing crop-yield foods only after the harvest preflight. The current client has three matching dishes (3071, 3111, 3121), each adding one crop item for ten minutes. Existing duration buffs are reused rather than refreshed.
- Consume the missing buffs at a ready harvest position, then harvest saffron only. Trees, minerals, cooking and sales are excluded during this phase.
- After all saffron is collected, use the remaining buff duration for potato rounds. Finish a planting round before harvesting it; do not start a round that cannot fit its growth and harvest. Replant saffron only after the buff expires and any potatoes are collected.
- Cook saffron soup and sell eligible dishes, retaining one for the next harvest. Locked inventory is respected. If potatoes or capacity prevent processing all saffron, keep the ingredients and report partial completion.

空田首次执行只种藏红花。未成熟时明确跳过；成熟后先巡查可收割位置，确认资源和时限，再购买缺少的三种料理、在收割位置使用并立即开始。此时只收藏红花，不砍树、不采矿。收完后利用剩余增产时间循环种收土豆，效果消失再补种藏红花；制作爱欲浓汤后出售可出售的成品并保留一份。

If the controller disappears its three-second lease stops new actions. Pending purchases or food use are persisted before dispatch. A later run obtains a fresh native LifeInfo response and replans from its actual inventory and duration buffs, preserving the unknown operation instead of replaying it. Incomplete or mixed crops are left untouched rather than guessed to belong to this run.

## Validation boundary / 验证范围

The initial local build validates settings migration, crop-only command scope, food preconditions, potato timing, component coexistence and compilation against the installed client. These are offline checks. **A complete live harvest requires naturally mature saffron and is not yet accepted.** Growth time is never changed to make a test pass. Start with an authorized live inspection; if no mature field exists, verify initial planting and return after natural maturation.

首版本尚未通过成熟藏红花的完整实机验收。不会修改生长时间，也不会把离线测试表述为已完成实机验证。实测需要已有成熟藏红花，或首次种植后自然等待成熟。
