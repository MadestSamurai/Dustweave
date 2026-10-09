# Trading and cooking / 跑商与料理

The planner maximizes combined profit from current stock and a shared 29-day supply forecast. It uses mixed-integer optimization, not separate per-ingredient retention premiums. Current purchases/cooking/sales and all recipe quantities are integral. Future supply is non-executable and never pays for today's orders.

Current inventory, unlocked recipes, actual shop offers, reserved resources, funding and confirmed sale quotes remain authoritative. Expected weekly drops are optional: only configured routes with matching account, server map observations and static-database fingerprints participate. A stale or unavailable forecast falls back to current-stock/shared-shop planning without stopping trade. No extra game connection or full-map inspection is started for forecasting.

Equivalent future raw resale is normalized into current pending sale; it is still executed only when a 120% quote is observed. Profitable current processing breaks equal-profit ties. Realized cash, current-stock gain and future estimates are distinct; a horizon optimum is not a guarantee of annual or stochastic optimality.

日常执行以实际库存、已解锁配方、报价、资金与保留设置为准。未来商店供货和已知周收集掉落参与共同规划，但只发出当前可执行订单。缺少匹配的掉落资料时继续按当前库存及商店供货计算，不为预测专门扫描地图。

优先比较总收益，再在同收益时完成当前可赚钱的加工；不为省材料而无限推迟，也不把同一份未来配套食材重复分配。金额按原料120%机会成本和实际药价核账；未来估值不等于到账金币。

参见 [365天对照与剩余局限](TRADING_ANNUAL_2026-10-10.md)。源码、离线模型与真实游戏执行验证分别记录。Test groups: `TradeOptimizer, TradeData, TradeReplan, TradeQuote, TradeResume`.