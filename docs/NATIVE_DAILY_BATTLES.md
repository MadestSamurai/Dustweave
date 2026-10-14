# Native daily battles

## Event battles without an extension

The host selects the first uncleared stage and uses the game's own consecutive-battle interface. It attempts at most five free battles per game day and event, finishing normal stages before optionally entering challenge stages. Combat decisions remain with the game. A defeat stops the native batch; the user can end this daily stage or sweep the highest fully cleared eligible stage. The default is to sweep. Only free event AP is enabled.

The native result page, ordered server responses and AP changes jointly confirm the outcome. An interrupted batch is never blindly replayed. If the same account is back in an idle lobby, current progress and resources are read again; the old uncertain request is superseded without claiming it completed. An uncertain batch retains its daily attempt budget. An active battle in another game process is not adopted.

Existing extension-driven battles retain their own execution path. The native defeat setting is shown only when that path is unavailable.

## Tower of Salvation

This independent daily stage is disabled by default. Daily-key reserve defaults to zero (allowed range: 0–999). The route enters Evil Castle and walks into the entrance trigger, then uses the ordinary Tower of Salvation lobby and quick-battle popup.

For the highest cleared difficulty, let `F` be daily keys, `K` the reserve, `C` keys per sweep, `S` current growth stones, `G` the sum of all still-unpurchased permanent growth levels, and `R` stones per sweep. The count is:

`min(floor(max(0, F − K) / C), ceil(max(0, G − S) / R))`

Locked or closed content, no cleared difficulty, an ongoing challenge, sufficient stones, or reserved keys produces an explained skip. The workflow does not buy attempts, use paid keys, end challenges, or spend growth stones. Before confirming it checks the selected difficulty, popup count, free-only toggle and current resources again. Settlement checks the server's score reward, exact daily-key debit and unchanged paid keys.

## Verification — 2026-10-14

- Native source compilation and response/read binding generation passed against the installed client. The new protocol rejects older bridge versions.
- Targeted synthetic tests cover budgets, settings migration, progression, defeat, settlement, account/process recovery and entrance reward presentation. These do not substitute for live consuming validation.
- Public settings were rendered in Simplified Chinese, Traditional Chinese and English, in light and dark themes.
- Live validation entered Evil Castle, dismissed an already-issued season reward, walked to the trigger, entered the tower lobby and exited with `salvation_no_cleared_level`. No keys were used.
- Live event validation traversed normal and challenge pages and identified challenge stage 15 as the highest sweep target. Free AP was zero; it exited normally without starting a battle or sweep.
- Still required on an eligible account: live native consecutive wins/defeat and post-defeat sweep settlement; live Tower of Salvation sweep debit/reward settlement. Paid attempts were neither purchased nor consumed for verification.

Private observations and screenshots are kept outside the source repository.
