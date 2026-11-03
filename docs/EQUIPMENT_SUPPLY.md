# Equipment crafting supply

The equipment assistant can replenish crafting wood and gold using the game's
native fixed-count hunting dispatch. It never buys rice. This is an opt-in,
account-scoped powder-crafting setting; daily free-only hunting is unchanged.
Wood reserve defaults to zero. Rice reserve applies to free plus stored rice.

## Current reward interpretation

Checked against the locally installed client on 2026-11-03 (assembly MVID
`6ef9a350-0ae8-4c96-ad55-9ad3e2d26a9c`). Private decompilation and table exports
remain outside this repository.

The reward popup distinguishes weighted choices (`RewardGroupTable.DropType=0`)
from guaranteed bundles (`DropType=1`). Chapter 9's guaranteed parent contains
a wood child and an extra-reward child. The wood child has exactly one possible
outcome. Random extra rewards do **not** make wood random, and the parent's
90/10 ratio must not be used to discount guaranteed wood.

| Difficulty | Normal enemies | Boss | Wood per dispatch | Rice |
| --- | ---: | ---: | ---: | ---: |
| Normal | 5 × 40 | 45 | 245 | 6 |
| Hard | 5 × 60 | 67 | 367 | 6 |
| Very hard | 5 × 80 | 90 | 490 | 6 |

Gold also uses fixed single-outcome rewards, with the selected difficulty and
the native popup's daily bonus. Very-hard base rewards are 5 × 3,500 + 4,500 =
22,000 before the bonus. No reward-growth multiplier is applied twice.
Values are read from current game tables, not hardcoded in the planner.

`SupplyRewardMath` resolves only the requested resource. Random alternatives
that all contribute zero remain exactly zero. Differing target yields cannot be
silently converted into a fixed average. Such a future change affects supply
quotes, not ordinary crafting/refinement.

## Planning

Nonnegative integer variables represent recipe counts and sweep counts. The
model constrains usable materials, talent potions, crafting gold, total rice,
the requested crafting limit, and optional gold/potion budgets. Wood consumed
must leave the configured reserve. Existing wood below reserve does not prevent
ore-only crafting; using wood then requires covering that shortfall too.

Lexicographic priorities:

1. Maximize feasible crafting count (current N recipes have equal powder yield).
2. Minimize rice for that count, using existing resources first.
3. Minimize talent potions, then material scarcity.

Only unlocked, fixed-yield routes are included. An unavailable wood route does
not prevent gold replenishment or crafting from existing inventory. The UI
shows the selected routes, count, exact rice budget, free/stored split, and
reserves before confirmation. A changed account invalidates the plan.

## Execution and recovery

The normal dispatch UI selects the available difficulty. The native free-only
toggle is disabled, a fixed count is applied through its normal callback, and
the ordinary dispatch button submits each batch (at most 20). The original
signed plan and resource budget remain the approval boundary.

Accepted native receipts and fresh resource balances establish settlement.
Projected rewards never substitute for server results. Crafting is recalculated
from the actual resulting inventory within the approved count/gold/potion caps;
no extra rice is spent automatically. A shortfall is reported explicitly.

Stopping finishes observation of an already submitted batch and prevents the
next batch. Resume subtracts settled sweeps and crafts. Result-popup cleanup
is separate from resource settlement. A restart can reconcile a pending sweep
from same-account resource changes. If still unresolved, inventory refresh
offers an explicit restart from the observed balance; this archives the old
intent without deleting evidence or repeating the old sweep. A new plan needs
confirmation. Unrelated crafting/refinement does not require rice observations.

## Validation

`EquipmentSupply` includes an independent finite enumeration oracle, reserve
and budget boundaries, guaranteed wood versus random extras, unavailable routes,
free/stored rice settlement, stopping after a settled batch, resume without
duplicate consumption, account boundaries and explicit pending-state recovery.
Connection policy checks cover permitted destinations/counts and ownership.
Native `prepare` and `prepare-evidence` compile/read current game assemblies
offline. Desktop smoke checks retain normal crafting/refinement coverage.

Live acceptance on 2026-11-03 used the production equipment planner, native
quick-hunt popup, free-only toggle, count callback and dispatch button. No
alternative map entry or resource-purchase action was added.

- With zero free rice, 22 very-hard chapter-9 sweeps consumed 132 stored rice
  and returned exactly 10,780 wood (490 each).
- Stopping after the first 20 sweeps preserved the settled batch. A fresh
  inventory read offered the remaining 2 sweeps / 12 rice, then completed
  100 N-equipment crafts, +7 upgrades and disassembly: 29,000 gold and 300
  talent potions consumed, 1,000 powder received.
- A separate 20-sweep gold check consumed 120 stored rice and returned
  880,000 gold, matching the current 100% bonus quote of 44,000 per sweep.
  This account already had sufficient gold; automatic gold-shortfall planning
  is covered by the independent resource-model tests rather than an invented
  live shortage.
- Total rice consumption was 252, below the explicit 300-rice test cap.
  The completed-plan repeat spent nothing; existing equipment was unchanged,
  no pending supply remained, and the game returned to its main menu.

Private receipts and before/after inventories are retained outside this source
tree under `artifacts/research/equipment-supply-live-20261103`. These results
validate the observed game version and tested paths, not every network failure
or account condition.
