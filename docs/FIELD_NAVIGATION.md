# Field navigation

The route graph distinguishes physical regions, ordinary gates, waypoint landings and native minimap quick-travel landings. A shortcut is a zero-walking-distance edge, with a nonzero transition-time cost. Short walks may beat a shortcut. A map ID alone is not enough to identify a position: an NPC landing must not inherit distances measured at a distant gate or overwrite a waypoint landing.

## Native quick travel

`QuickTravelNative` reads the current account's actual quick-list entries. Only NPC entries whose native `QuikAutoMove` rule selects teleportation are eligible. Walking-only list entries are excluded. The native destination resolver supplies the map and position; the native nearby check prevents no-op travel when already at the destination. Neither availability nor arbitrary landing coordinates are granted by the saved route atlas.

The executor uses the game's `MoveMapNpcPosition` path after clearing quest navigation and closing the quick list. Its completion callback distinguishes a completed same-map move from an unchanged old frame. Account/process checks remain in the shared command driver. An unavailable shortcut is removed from that origin's candidates and ordinary routing resumes; an uncertain submitted command is not blindly replayed. Optional shortcut discovery failures leave ordinary field observations usable.

Initial transition estimates are 3.5 seconds within a map and five seconds between maps, calibrated from movement-only live checks. After an actual arrival, the atlas learns the observed transition time and outgoing gate distances for that NPC landing. Same-map and cross-map timings are learned separately. Changed native destination coordinates invalidate the learned landing. Hunting grounds and caller-excluded maps remain excluded. Explicit walking-only callers do not use shortcuts.

Square merchant travel also considers the native shop shortcut, retaining square A* for nearby travel and the final approach if necessary. This changes travel only, not shopping or talent rules.

Weekly NPC board visits target the actual board's quick-list entry, including when the character is already on that map. They retry shortcut discovery after walking out of an interior. A board already within native interaction range opens directly, without starting and stopping navigation. Native quick travel and the short approach inside that board's native nearby area do not prepare stealth or speed talents. If the shortcut is unavailable or leaves the character outside that area, ordinary threatened walking still prepares protection. Another NPC's nearby flag cannot exempt the board walk. Quest selection and acceptance still go through the visible board and its confirmation.

## Planning cost and latency

The existing region/visited-map-state Dijkstra search already finds the least weighted route on its observed graph, including summon penalties and return destinations. Its major CPU cost was rebuilding JSON edges and reparsing region IDs for each visited-map subset. Each planning call now compiles adjacency, map IDs, numeric weights and destination bits once. This preserves ordering and route decisions without a heuristic that could worsen a route.

`GoTo` reuses the live origin observation made by `Plan`. Walking also avoids a second full map observation when preparation sent no input. Merchant approach polls only `trade.navigation`, instead of also requesting inventory, recipes and dispatch state. Command-time identity, field readiness, unexpected-popup and cancellation checks remain in place. This does not cache movement geometry across commands or remove actual scene-loading waits.

Plans record `planning_ms`, `states` and `expanded_regions` for diagnosis. Unknown regions still use estimates; the graph is updated and replanned after travel. Consequently, optimality is relative to the current graph and cost estimates, not a claim that every unvisited physical route is known.

Ordinary walking estimates use the native map speed (five units per second in the tested maps), with the previous estimate retained only when that observation is unavailable. Active MoveSpeedUp effects use their actual remaining duration and the game's read-only bonus getter. Walking time changes when the effect expires part-way through a route; gates and quick travel consume elapsed buff time too. Summoning penalties are planning preferences, not elapsed seconds. Buff state is never written into reusable route geometry, and a missing optional bonus reading falls back to ordinary walking estimates.

Walking starts as soon as the native field and input are ready, without an additional fixed one-second settle before departure or after arrival. Loading, hidden controls, unknown popups, identity checks, patrol protection and bounded interrupted-route recovery remain active. A speed effect already active on arrival is reused, rather than opening the talent menu again. Optional speed preparation reuses its fresh talent-selection observation; it still respects the native animation, cooldown and server response.

## Additional movement verification, 2026-10-26

- Movement-only live checks covered the square, chapters 1, 3, 6 and 18, short and long gate walks, NPC shortcuts, representative reachable points, disconnected regions and patrol protection. Chapter 3 required leaving one town region through the tavern and returning to another region; routing followed that topology successfully.
- Eight repeated chapter-1 round trips completed with MoveSpeedUp enabled. A roughly 95-unit walk took about 24.3 seconds without an effect and 17.4 seconds with an already active 75% effect in the initial comparison. A fresh cast plus the same walk took about 19 seconds. These are local observations, including transition/observation overhead, not guaranteed game performance.
- Live testing exposed an execution allowlist mismatch: opening MoveSpeedUp was allowed but executing it was rejected before the click. Preflight and execution now share the same field-talent allowlist. Recovery recognizes only that exact historical pre-click rejection; it does not replay an ambiguous consuming action.
- Synthetic checks cover partial effect expiry, lower bonus values, missing optional readings, preserved ordinary geometry, immediate movement from a ready field, real loading waits and scene/cancellation races. Native compilation and all 174 configured read paths passed; runtime reads are also checked in the live evidence.
- An additional 128 deterministic random four-map itineraries, with multiple goals, a return destination, expiring 25%/75% bonuses and summon penalties, matched an independent exhaustive path search.
- In the final continuous-map run, the first observed controllable field frame to the next ordinary walking command was approximately 0.11–0.55 seconds across nine measured transitions. This interval excludes actual scene loading and cases needing a talent cast. The test harness also saves observations between segments.
- Final repeated speed activation measured about 2.9 seconds after journal initialization. The first cast in a newly connected test process took about 5.5 seconds; roughly 2.9 seconds in the comparable run belonged to first-time discovery of approximately 4,300 existing journal records. Reusing an active effect and short walks sent no extra cast. The test does not establish that every first cast can finish in three seconds.

The additional raw evidence and movement harness remain outside the public repository under `artifacts/research/navigation-deep-live-20261026`.

## Verification, 2026-10-25

- Targeted groups: `QuickTravel`, `WeeklyNpcBoard` (including field arrival/recovery), `CookingTravel`, `TradeEntry`, `TravelTransition`.
- Board shortcut regressions cover same-map and cross-map arrival, an already reachable board, the short final approach, leaving an interior, and unavailable, mismatched or failed shortcuts falling back to ordinary walking protection.
- Compared the prior committed planner against the optimized planner on 200 deterministic random directed graphs with waypoints, summon costs, multiple goals and return-map requirements. Exact actions and weighted costs agreed.
- A 12-map synthetic graph, five measured runs after warmup: prior median about 967 ms, optimized about 13 ms; allocation per plan fell from about 523 MB to 4.1 MB. This measures planning CPU, not end-to-end daily runtime.
- Current-client compilation and 173 native read paths passed. Movement-only live checks passed for square shop quick travel (about 2.6 s), chapter 1 town-to-shop quick travel (about 4.1 s), and onward walking from the shop to town (about 6.5 s). No purchases, reward claims or talent casts were requested.
- The production merchant-approach method was checked from the same square anchor: the previous eight-second shortcut estimate chose walking and took about 9.5 s; calibrated shortcut selection plus scoped navigation reads reached the native interaction range in about 5.5 s. These are individual local observations, not a guaranteed duration. The final position was checked through the native merchant `Near` state, not just the teleport completion callback.

Local raw evidence and comparison harnesses are retained outside the public repository under `artifacts/research/quick-travel-20261025`; account data and decompiled client sources are not public build inputs.

## Fast travel preference in 1.5.2

Fast travel is no longer experimental. New accounts default to enabled; schema-18 preferences migrate to schema 19 with this setting enabled once, preserving potion reserves and unrelated settings. Saving an explicit opt-out in schema 19 retains it on subsequent reads. The first-open release notice explains the change and where to disable it. Preference reads remain side-effect free.

