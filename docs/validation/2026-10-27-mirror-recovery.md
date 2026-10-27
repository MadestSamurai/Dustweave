# Mirror batch waiting and deferred recovery fixes

## Mirror

Diagnostic `Dustweave-diagnostics-20261027-144509.zip` contains two isolated queues
whose native 40-battle batches reached the fixed 1,800-second deadline at 36/40
and 33/40. The game was still advancing; the queue then attempted daily dispatch
inside the unfinished battle. Later resumes reconciled both batches with zero
free tickets remaining.

Fresh execution and recovery now extend a five-minute inactivity window only
when a new, accepted matching/settlement reply arrives for the original actor.
Repeated replies do not extend it. There is no fixed total batch-duration limit.
An inactive batch pauses its queue with the settled count, keeping its journal
for resume instead of submitting another batch or entering the next stage.
Other transaction timeout policies and paid-ticket restrictions are unchanged.

## Redirected storage

User-selected/default data and cache roots resolve Windows directory redirects
before acquiring the existing storage file lock or writing updater journals.
Aliases share one physical lock and location; existing account/settings files
are not migrated. Installation replacement, cache descendants and destructive
operations still reject embedded links. Broken redirects include the failing
path in the error. Logical account/IPC identities are unchanged.

Synthetic checks create real Windows junctions in temporary test directories:
parent/nested aliases, shared storage/control locking, signed manifest and
download reuse, counts, cleanup, external descendants, broken targets, overlap
rejection and migration via a redirected destination parent.

## Saved-login recovery

The new Accounts action opens ordinary manual authentication independently of
isolated credential export. It backs up a complete current local session before
clearing it, allocates no new account slot, and preserves sandbox contents.
The recovery intent stores the target identity and an authentication digest,
never plaintext credentials. Saving requires a fresh session for that identity
after normal game exit. Unchanged credentials or another account cannot overwrite
the target. Ordinary retry bypasses the failing export path only for this explicit
verified recovery and retains live identity checks; isolated retry uses the
existing strict sandbox handoff.

The prompts say to keep automatic sign-in enabled and close the game without
logging out. Missing fields, disabled automatic login and collection failures
have separate messages. This repairs the confirmed circular recovery flow;
the initial cause of the reported isolated authentication loss remains unknown.

## Validation

- Mirror/queue: `artifacts/tests-20261027-073206-7632/results.json` — 274 checks,
  including fresh and resumed 40-battle batches exceeding two simulated hours,
  no repeated submission, no paid-ticket use, and a stalled queue staying paused.
- Storage, sandbox credentials and update boundaries:
  `artifacts/tests-20261027-072902-8014/results.json` — 398 checks.
- Final alias migration, control-lock and recovered-launch tests:
  `artifacts/tests-20261027-073835-1598/results.json` — 185 checks.
- Final sandbox/recovery and legacy inventory-download boundary checks:
  `artifacts/tests-20261027-074344-9694/results.json` — 284 checks. In particular,
  inventory delta downloads also normalize their network-journal root.
- Existing log/cache groups passed 55 checks in
  `artifacts/tests-20261027-072744-5331/results.json`; its new storage fixture
  initially used files older than the retention period. Correcting fixture ages
  produced the successful later runs; no production retention rule was relaxed.
- Accounts UI: `artifacts/smoke-20261027-072804-5959/accounts-presentation.json`
  — 69 checks across simplified Chinese, traditional Chinese and English,
  including wrong-account and game-still-running states; screenshots inspected.
  The final recovered-launch implementation was rebuilt and the same UI scope
  passed again in `artifacts/smoke-login-final-20261027-074149/`.

These are source, synthetic filesystem/session/network and isolated UI checks.
No real game authentication, sandbox launch, gameplay, packaging or publication
was performed for this change.
