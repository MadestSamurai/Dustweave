# MANSION RUNAWAY automation

## Scope

Internal Dustweave tool using the shared game connection and exclusive enabled-state control. Started 2026-10-09 01:50 HKT; user revised the deadline to 07:30 HKT. No public release is authorized for this task.

Normal mode has four stages. Challenge mode has no fixed final stage: the native initial timer is 180 seconds and each cleared stage adds 30 seconds. Its native `Clear=true` result can mean the timer expired while the player was alive. This must not be described as clearing every challenge stage.

## Implementation

- Resolve the installed client's semantic fields and method shapes at connection time. Do not distribute client assemblies or hard-code a release-time obfuscated symbol map.
- Read maze connections, remaining ordinary candy, exits, health, timer, enemies, targeting states and item inventory.
- Cover all remaining required candy with a complete route; improve complete tours in a background worker with a 30 ms budget. Use a bounded local planner to reject dangerous next steps.
- Predict pursuit, scatter destinations, stalking stops, native bait behavior and motion through intersections. Smooth the camera turn across approximately five frames and include the changing heading in prediction.
- Use native item actions. Magnet decisions compare nearby candy density against travel distance; consuming a magnet invalidates the route that described the old candy set. An optional item failure is reported without stopping movement.
- Reach the native exit center once required candy is gone. Retry defeats only when enabled; leave successful results visible.
- Prepare the component on demand from the tool menu. Opening a window does not reserve all automation; enabling it does. Stop, closing, lost heartbeat and component handoff revoke movement.
- Support simplified Chinese, traditional Chinese, English, and the host's light/dark appearance.

## Evidence boundaries

Game input and observation use the existing connector. No Computer Use. No changes to native speed, health, timers, scores, rewards or enemy rules.

Research logs and client inputs remain in the parent workspace's `artifacts/research/mansion-runaway-20261009`, outside the independent source repository. Client MVID: `132cda4f-d57f-4991-8b09-6310f09b7fe5`.

| Native trial | Outcome |
| --- | --- |
| 02:13 HKT, live02 | Normal mode completed all four stages; native reward result saved. |
| 02:29 HKT, live05 | Normal mode completed again, three lives remaining. |
| 03:34 HKT, live15 | Challenge reached stage 4; 846 points, chain 64, two lives remaining at timeout. |
| 03:42 HKT, live16 | Challenge reached stage 4; 810 points, chain 54, two lives remaining at timeout. |
| 04:14 HKT, live21 | Challenge reached stage 4 with 17 ordinary candy remaining; 818 points, chain 69, two lives remaining at timeout. |

The live19 defeat is not a clean planner comparison: an ambiguous item reflection lookup stopped movement. The enum overload is now selected explicitly; subsequent live21 trials used items without that failure. Optional item errors now degrade to movement-only assistance.

Current regression evidence: 61 Mansion checks, including connected full-route coverage across generated mazes, exit arrival, moving enemy edges, smooth turns, and invalidation of an in-flight route after magnet use. Shared tool-menu and ownership checks were run separately. These do not replace product-package and real-game validation.

## Controlled trials

Nine native Challenge runs completed with build24. Maps, spawn choices and item draws remain native/random; these are practical comparisons, not same-seed proofs.

| Collision horizon / route weight | Scores (three runs) | Mean | Native time-limit completions | Furthest stage |
| --- | --- | ---: | ---: | ---: |
| 3 seconds / 110 | 753, 865, 787 | 801.7 | 2/3 | 4 |
| 2 seconds / 110 | 639, 811, 447 | 632.3 | 2/3 | 4 |
| 3 seconds / 180 | 783, 518, 630 | 643.7 | 2/3 | 4 |

Build26 compares recomputation after evasive moves and skipping collected detours, against the same changes with optional 0.2–0.4 second yielding. The stationary interval is included in collision forecasting. It does not pause native game time, move enemies or alter player speed.

The native exit is chosen randomly from available item spawn points **after** required candy is collected. It cannot be treated as a known destination at the start of a stage. Current speed items last five seconds with a 25% boost; a long dedicated detour can cost more time than it saves.

## Remaining work within the deadline

- Finish controlled live comparisons of collision horizon and route preference; choose defaults from multiple results rather than a single high score.
- Repeat normal mode with the final settings, check stop/resume and shared handoff, and validate the finished product entry.
- Complete the local Portable/Lite package and record its verification separately. No GitHub publication.

## Simulator investigation

The minigame trial was temporarily paused for the Fallen Star attack-range investigation. The current game intentionally mirrors range 24 to 23 for the red team; both simulator engines match. The user authorized resuming this task afterwards. Details are in the parent workspace's `docs/combat/ATTACK_RANGE_ORIENTATION_2026-10-09.md`.

## Additional experiments

Build26 tested short stationary yields using the same collision predictor. With a 3-second horizon, its two runs ended in defeat (780 and 610 points); the 2-second variant returned 797 at timeout and 289 in defeat. The continuous-movement control returned 847 and 791 at timeout. Yielding remains disabled by default.

The 847-point run naturally recorded a maximum chain of 83. This is a native battle result; achievement claiming has not been verified.

A separate offline comparison replayed recorded player trajectories against enemy forecasts. Simulating the 0.33-second pursuit-target refresh explicitly only slightly reduced roughly two-second forecast error: Predict 1.07→1.05 world units, Realtime 1.85→1.84, Stalking 1.01→1.00. That extra state was not imported into the product.

Build27 tests nearby item collection with a conservative round-trip travel charge. Speed duration and effect are read from the installed client; its runtime rule has already converted milliseconds to seconds. Full inventory, an already-held copy, sparse magnet coverage and the last few required candy suppress detours. Native collection and item use remain unchanged.

The current targeted boundary run passed 160 checks across Mansion, tool-menu and shared-suite groups. This is distinct from the pending complete package gate.

## Product entry corrections and chosen defaults

The product entry exposed two lifecycle problems that the research controller did not exercise:

- Windows reports `ERROR_SEM_TIMEOUT` when a named pipe exists but is busy. The one-millisecond availability probe previously returned a missing identity in that case. Busy/timeout now proceeds to the existing authenticated, bounded read; absent and inaccessible pipes remain unavailable. A real single-instance busy-pipe test covers this boundary.
- A dynamically loaded Unity MonoBehaviour could fail to instantiate on reopening the same component. Mansion now uses the shared main-thread canvas callback with a frame-count guard, removes it on handoff, and publishes an initial observation before reporting ready. No restart or weakened account guard is needed.
- Closing an unconnected window now defers the second Close call until the first Closing handler returns.

Defaults: continuous movement, a three-second collision horizon, route preference 110, conservative nearby-item collection enabled. Two item-enabled Challenge trials scored 612 and 802, both reaching timeout alive with three lives; controls scored 576 (defeat) and 678 (timeout). These small randomized samples support a provisional choice, not a guaranteed win rate.

The final product trial uses the actual WPF tool host, identity guard, shared lease and MansionClient. The 06:27 HKT run confirmed idle windows do not hold control, Stop releases it, and Start resumes the current native round. Completion and package reports are stored separately in the parent workspace.


## 80-chain goal (0.9.15)

Enable “刷到80连锁后停止” to use Challenge mode and repeat rounds until the current round's acknowledged native result contains MaxChain >= 80. Goal mode owns retry behavior: a successful result below 80 exits through the native result screen and starts another Challenge round; defeat uses Retry when available. It never adds chains across rounds or trusts an old result. Reaching 80 during play continues the current round to its normal network-confirmed result, then stops and releases shared control. The goal selection is saved; manual Pause remains available. A Normal round already in progress must finish before selecting this Challenge goal.

Added ten goal-boundary regressions. Full package validation is separate from real-game evidence; this feature has not been run in the live game as part of the code-change request.


## Goal-specific profile (0.9.16)

The 80-chain goal selects Build26's recorded 83-chain configuration: collision horizon 3 seconds, guide weight 110, no stationary yielding and no later nearby-item pickup detours. Ordinary mode keeps the current balanced profile (nearby pickup evaluation enabled). Existing held-item logic is shared, including magnet positioning. Engine lifecycle and connection fixes are retained.

Verified against the embedded sources of `harness-build26/Dustweave.Compatibility.dll`: normalized Motion and RouteGuide are identical; Planning only differs by defaults and added PickupPolicy. Runtime movement and held-item decisions match; later changes are lifecycle, diagnostics and optional ChoosePickup. No duplicated legacy engine is needed.

Profile changes invalidate the current movement target, item goal and planner before the next movement decision, without clearing native round progress. Routine heartbeats do not invalidate the route. The window permits changing the goal while stopped. `PlannerProfile` and `PlannerSettings` report the applied profile. Goal continuation and native-result confirmation are unchanged.
