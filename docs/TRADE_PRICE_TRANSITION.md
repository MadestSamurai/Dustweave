# Dated trading strategy

The next release adopts the November 16, 2026 price transition automatically for the full trading mode. The price input is the user's supplied 25-dish announcement image; it is a forecast assumption, not an observed server quote. This feature does not modify captured catalogs or static game tables.

## Execution and forecasting

`DailyTradePriceChange` matches recipe ID, output item and the old peak price. Expected prices are `floor(base * 120 / 100)` per unit. Before November 16, these prices apply only to forecast sales on or after that date. On and after November 16, the captured game's prices take precedence, including unexpected later changes. A two-day maintenance grace retains affected dishes still quoting the old price; after that grace, the actual captured price remains authoritative rather than indefinitely assuming an unconfirmed update.

`DailyTradeCalendar` uses 62 dated inventory and cash balances, unlocked recipes, visible daily supplies, premium days, pill costs, one bargaining activation per purchasing day, protection reservations and storage capacities. Current purchases, cooking and sales are integral. Future recipe quantities are a continuous forecast; they are not executable and optimality is not a long-run guarantee. Expected drops enter only future inventory; overflow may discard forecast drops, never real inventory.

The order is the production executor's existing sequence: buy pills, bargain, purchase, cook, sell. Each day's purchases must be funded before that day's receipts. Today also respects `reserve_gold` and `max_spend`; future estimates cannot spend today's reserved coins. End-of-window inventory and pills are valued separately from cash. The secondary objective retains liquidity across dates at unchanged primary value. Current cooking is deferred unless a verified sale is available today or full next-day supply could overfill an ingredient. Every run replans from current observations, not a fixed report allocation.

Only today's orders are emitted to the executor. Independent integer resource, purchase-limit and capacity audits run after optimization, and the existing transaction receipts/quote checks still apply. If no feasible solution is obtained, an explicit no-order warning retains stock instead of falling back to old-price liquidation. Cancellation propagates.

Full-auto preferences migrate once through `Trade.StrategyRevision = 1`: the temporary processing pause is lifted; trading enabled/disabled, favorites, custom recipes, reserve settings and spending caps are preserved. A later explicit processing opt-out persists. The in-app release notice and settings description explain the change without an opt-in dialog. There are no game actions or scheduled background transactions during migration.

## Validation

Targeted groups: `TradeCalendar`, `TradeOptimizer`, `TradeReplan`, plus trade execution/quote/potion regressions. Calendar cases cover future-price isolation, delayed cooking, retention of prepared food, actual-price boundaries, mismatching quotes, zero cash, spending caps, protected/full inventory, future drops and cancellation. The captured trade catalog is benchmarked offline with synthetic inventories and 1M/5M/20M gold; no live accounts or game actions are used.

The 62-day window, terminal valuation and relaxed future recipes differ from the finite-horizon research report. Do not present its projected profit as the report's guaranteed final cash, or imply the research allocation is copied unchanged into accounts.

Implementation validation (offline, 2026-10-31): the trade/quote/resume/potion/preference groups passed 318 checks; additional calendar and user-text checks passed 86 checks, with overlap in calendar cases. The existing optimizer group also passed after the sparse solver adapter change. A 19-day rolling replay from October 31 through November 18 used the captured 119-item catalog with synthetic 5M starting gold and no external cash. Each day's actual orders were independently applied to inventory, cash and pills; all balances and capacities passed, and closing cash stayed at least 785,368. Separate 1M/5M/20M scenarios passed current funding and exact pill-purchase checks. These are model replays, not live game or release acceptance. Evidence is retained under `artifacts/calendar-benchmark/` and the associated targeted test runs. The desktop build completed with zero errors; no package was published in this task.
