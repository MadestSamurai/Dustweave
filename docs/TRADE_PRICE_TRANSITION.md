# Trading strategy: mature model plus the November 19 transition

Updated 2026-11-10. The price input is the user's supplied 25-dish announcement
image. It remains a forecast assumption, not an observed server quote. Captured
catalogs and static game tables are never overwritten with announced prices.

## Execution and forecasting

`DailyTradePriceChange` matches recipe ID, output item and the old peak price. Expected prices are `floor(base * 120 / 100)` per unit. Before November 19, these prices apply only to forecast sales on or after that date. On and after November 19, the captured game's prices take precedence, including unexpected later changes. A two-day maintenance grace retains affected dishes still quoting the old price; after that grace, the actual captured price remains authoritative rather than indefinitely assuming an unconfirmed update.

On November 10 the user supplied a postponed maintenance date, November 19.
Only `EffectiveDate` changes in production code. The 25 announced prices,
joint model, liquidity priorities, horizon, inventory constraints, execution
order and two-day grace remain unchanged. November 16–18 remain ordinary
transition days: abundant cash favors holding rising-price food; an early sale
can still be selected if its financing benefit exceeds the forgone price gain.
The existing grace therefore falls on November 19–20, with observed pricing
authoritative afterward. Peak days are not moved: a November 17 peak deferred
past maintenance becomes December 17, not November 19. Historical release
notes and the November 3 evidence below retain their original dates/results.

The 62-day calendar replacement has been removed. It changed established
processing priorities and allowed unchanged food to remain in end inventory
instead of being sold at an equal objective value. November 3 records contained
3,133 and 2,342 saleable fire-grilled fish sticks but emitted neither sale. The
primary solver's `optimal` flag did not prove the secondary liquidity objective.
That was a model/execution-priority regression, not evidence that the mature
recipe allocation needed replacing.

The restored model jointly allocates current stock and 29 days of known future
supplies. All recipes compete for the same materials. Original bounded
end-stock dual values, recipe integrality and current profitable processing
priority remain. The ordered objectives are joint gain, then current gain;
during the transition, current cash is minimized only after fixing both gains.
This last step removes zero-gain purchased cooking without postponing profitable
processing of owned materials. Full equations and proof boundaries are in
[ARCHITECTURE.md](ARCHITECTURE.md#trade-allocation-and-the-price-change-transition).

Each rising-price output has two competing allocations: old-price sale, or
holding until its next peak date on/after November 19. Funds are not unlimited:
old stock may be sold earlier when that creates enough additional profit to
outweigh the price increase. Only original stock assigned to sale can finance
future purchases, and only **after** its receipt date. Purchase phases respect
their remaining daily supply; no retroactive procurement or credit from
hypothetical production/drops is allowed. Today's purchases and potion refill
still need real cash above the user's reserve and spending cap. Unaffected
finished food without a recipe use has no deferred-stock escape from its sale
balance. Future allocations are not executable orders.

Execution remains buy pills → bargain → purchase → cook → sell. Live quotes,
material balances, protected items, individual sale stacks and transaction
receipts still govern execution. A solver timeout retains a feasible incumbent
or a validated no-purchase allocation, including unchanged finished-food sales;
it does not return an empty plan that strands all inventory. Secondary proof
flags are separate, and incomplete secondary solves are not cached as final.
Cancellation propagates. The no-purchase allocation is a fallback, not a warm
start that perturbs the historical search.

## Backpack audit

Stack size is not an item-wide stock limit. The model adds every partial stack's
headroom and allocates remaining **shared** empty slots across all items. Slots
occupied by other item types remain occupied. Future sales cannot release space
for earlier purchases. When all independent purchase/output upper bounds fit,
the capacity constraints are provably redundant and omitted from the solver;
the final allocation is still audited.

Cooking capacity is currently a conservative pre-sale peak bound, not an exact
optimal schedule for a nearly full bag. This can leave feasible cooking unused.
The reviewed native handler adds intermediate packet outputs before removing
the batch's material list on its final callback. It is incorrect to grant all
that space at the start, or to check only final net stock. Current-client live
packet/slot behavior has not been revalidated in this offline task. Further
tightening must establish that behavior and the execution order first. This
limitation is explicit; it is not a claim that every old capacity check was
correct or that every feasible inventory schedule has been searched.

Full-auto preferences migrate once through `Trade.StrategyRevision = 1`: the temporary processing pause is lifted; trading enabled/disabled, favorites, custom recipes, reserve settings and spending caps are preserved. A later explicit processing opt-out persists. The in-app release notice and settings description explain the change without an opt-in dialog. There are no game actions or scheduled background transactions during migration.

## Validation

On 2026-11-10, the November 19 parameter change passed 817 checks in
`TradeCalendar`, `TradeTransition`, `TradeOptimizer`, `TradeData`, `TradeReplan`,
`TradeQuote`, and `TradeResume`. New date-boundary cases cover November 16–21,
including the unchanged grace, ample-funds holding, unaffected fish-stick sales,
and a missed pre-maintenance peak correctly rolling to December. Existing
low-capital early-sale cases and the 105 independent finite allocation cases
remain unchanged. The independent wallet now runs October 31–November 22 with
0, 45 and 1,000 initial gold (69 account-days), observing the price change on
November 19. The optimizer, forecast, transition-funding and execution sources
are unchanged. Evidence: `artifacts/tests-20261110-015546-6258/`.
This is offline source validation; no game action, package or publication was
performed for this date adjustment.

The following November 3 results describe the original November 16 assumption:

On 2026-11-03, 2,820 checks passed across `TradeOptimizer`, `TradeCalendar`,
`TradeTransition`, `TradeData`, `TradeReplan`, `TradeQuote`, `TradeResume`,
`TalentPotion`, `BargainTraining`, `UserText`, and `LocalizationArchitecture`.
The historical `TradeCalendar` group name is retained, but its expectations now
test the restored model rather than endorse calendar-only delayed cooking.

Independent oracles enumerate 105 finite inventory/capital/early-sale scenarios
and 27 unit-by-unit shared-slot packing scenarios. They do not invoke the
production optimizer to obtain expected answers. Other cases cover final-day
receipts, unavailable merchants, zero cash, reserves, quote/date boundaries,
predicted supplies, processing opt-out, cancellation, tiny time budgets, actual
protected sale stacks, and zero-gain cooking that should not consume cash.
An independent wallet also rolls executable orders from October 31 through
November 20 with 0, 45 and 1,000 initial gold (63 account-days). It credits only
actual peak-day sales, changes the captured price on November 16, verifies
nonnegative resources/daily supply, and confirms unchanged food sells and held
food is released after the observed price change.

An immutable historical optimizer **and forecast** from `2042e8b^` were replayed
against the two November 3 records. With the price transition inactive, each
account was tested at 0, 100K, 1M, 10M and its captured gold balance: purchases,
cooking, joint gain and current gain all matched. Both objective proof flags
were true in all ten comparisons. These were read-only copies under an isolated
artifact data root, not game actions.

Final transition replay:

| Account fixture | Purchase cost (same as historical model) | Fish sticks sold | Olive pasta sold | Solver time | Joint / current / cash proof |
|---|---:|---:|---:|---:|---|
| Main | 1,032,092 | 3,133 | 574 | 3.876 s | all true |
| Small | 979,160 | 2,342 | 513 | 7.278 s | all true |

Changed cooking, relative to the mature old-price model:

| Dish | Main additional quantity | Small additional quantity | New gain per owned-stock batch, after old raw resale value and 45-gold pills |
|---|---:|---:|---:|
| Chocolate bread | 7,145 | 6,871 | 3 |
| Potato soup | 31,908 | 31,886 | 3 |
| Fruit slush | 37,132 | 400 | 3 |
| Black beer stew | 6,468 | 6,605 | 5 |
| Saffron tea | 1,077 | 1,006 | 3 |
| Fried mushrooms | 0 | 1,411 | 3 |

All six newly selected dishes lose value under old prices and gain value under
the announced prices. In the main fixture, 27 sandwich bentos move to 27 street
chicken skewers, sharing the same scarce pepper. At old raw prices, the bento's
gain is 1,020 versus the skewer's 1,014. The bento also uses 20 toast, now able to
earn another 60 through chocolate bread. Releasing that toast adds 54 per
substitution (1,458 for 27), explaining the changed choice without rewriting
the recipe-selection principle. The remaining unchanged recipes are preserved.
A provisional extra 400-potato order proved to be a zero-gain cooking tie; the
transition cash objective removes it while keeping both gains unchanged.

`optimal` refers only to this finite, explicitly assumed model. Future purchases
are a forecast relaxation, drop supply is expected rather than guaranteed,
future inventory scheduling is not fully modeled, and ordinary forecast
bargaining retains the historical assumption. Do not present forecast profit as
guaranteed final cash or claim a perfect global policy. Orders replan from fresh
inventory and quotes. No game operation, package or release was performed in
this task.

Local evidence: `artifacts/trade-model-audit-20261103/comparison-stable/`,
`replay-final/`, and `artifacts/tests-20261103-114330-3454/`. The superseded
October 31 calendar benchmark remains historical evidence only and must not be
reused to certify this model.
