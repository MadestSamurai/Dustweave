# 1.1.0-beta delivery plan

## Outcome
Make interruptions understandable and diagnosable, preserve account/control safety, and provide consistent app identity and update access.

## Stages
1. Reproduce session ownership/identity transitions; distinguish host exit, identity unavailable, confirmed account change and control handoff. Fix demonstrated causes without replaying uncertain game operations.
2. Add structured user-facing issue categories and next actions. Preserve raw errors for support and record transition evidence without credentials.
3. Add a themed diagnostic export dialog with destination selection and developer contact details. Export a bounded allowlist of logs, redact secrets, skip unsafe links and report omitted/unreadable entries. Never include saved sessions, plugins, binaries or publisher keys.
4. Embed the supplied icon, add latest Release and fixed mirror links above update history, and increase default height while respecting the display work area. Verify all three languages and both themes.
5. Run focused regressions while editing, then build both release packages once. Verify private plugin compatibility outside the public tree and real prior-version OTA upgrade/rollback/recovery. Commit and push reviewed changes, publish a normal GitHub Release through Actions, then publish signed OTA and verify public bytes.

## Acceptance
- A newly launched host owns its own session; integrated child tools keep the parent owner.
- Login preparation cannot bind an empty account as an executable module session.
- Actual identity changes still stop all actors; pending operations are drained without replay.
- Every issue includes a category and specific next action; an old operation failure is distinguished from current connection health.
- ZIP export does not freeze the UI or upload automatically. Missing/busy files are recorded; account vaults and credentials are excluded.
- Source, packaged and real-game validation remain separate. User game/account state is not changed during offline verification.
- Release version: 1.1.0-beta, normal GitHub Release. Private extensions are never bundled.

## Investigation baseline
The prior generic session notice combined owner-process failure with both account and player mismatch. The desktop configured owner variables only when absent, allowing a new host to inherit a previous process owner. The operation preflight also activated a daily module before requiring an identified account. These are source-confirmed paths; the supplied screenshot alone cannot identify which occurred on that user machine.

## Implementation and verification
- Stages 1–4 implemented. A paused module awaiting a receipt now retains the initial failure reason and retires after the receipt drains.
- Focused regression: 73 checks in UnifiedSuite and DiagnosticExport passed. Coverage includes independent host ownership, child inheritance, identity changes, pending receipts, bounded export, redaction, busy files, incomplete JSON and multiple sources.
- Focused support UI: 25 checks passed across Chinese, Traditional Chinese and English, light/dark themes and a compact window. The real dialog produced a ZIP from isolated synthetic logs; no game was touched.
- Canonical Portable/Lite packaging, plugin compatibility, prior-helper OTA acceptance and publication are the remaining release gates. Their final evidence will be recorded in the dated deployment receipt.
