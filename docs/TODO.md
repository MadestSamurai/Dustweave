# Follow-up fixes

The two deferred items below were implemented on 2026-10-27 alongside the Mirror
batch-wait fix. Source and synthetic validation only; no game operation, package
or release was performed. See `docs/validation/2026-10-27-mirror-recovery.md`.

## Data-directory links block connection preparation (2026-10-27)

- [x] Resolve redirected data/cache roots while retaining strict descendant checks.
- Diagnostic: `Dustweave-diagnostics-20261027-110305.zip`, application 1.5.2.
- Before this fix, `DailyCacheStorage.Lease` used `DailyUpdateHost.AcquireStorage`, which invoked
  `DailyUpdates.EnsureNoLinks` on `cache-storage.lock` and every ancestor.
  The same `updates.linked_folder` error blocks game connection, hosted fishing
  preparation and update-network journal writes. The update-specific manual
  download message is misleading for ordinary connection preparation.
- Reproduced with the actual 1.5.4 Core assembly: direct physical paths work;
  access to the same data through a parent directory junction fails in both
  storage leasing and update journal writing. The diagnostic does not identify
  the user's exact reparse point or establish why it exists.
- Resolve supported directory redirections to stable physical locations without
  changing shared lock ownership or abandoning existing account/settings data.
  Distinguish normal data access from installation replacement and destructive
  cleanup. Do not remove link protection globally.
- Include the offending path and actionable context in diagnostics and messages.
  Cover shared lock behavior through aliases, nested junctions, unsupported or
  broken redirections, and links encountered inside an update/cleanup target.
- Implemented `DailyStoragePath.ResolveRoot` at data/cache boundaries. Storage
  leases, update network/download/repair state, cache migration and local cleanup
  use the resolved location. Installation replacement and descendant links remain
  guarded; broken redirections report the specific path.

## Recover saved login without a circular sandbox dependency (2026-10-27)

- [x] Add an independent manual sign-in and verified save/retry flow.
- Evidence: the English-speaking user's new screenshot reports
  "Automatic sign-in in this isolated account is unavailable or disabled" after
  pressing ordinary **Play**. The earlier diagnostic is
  `Dustweave-diagnostics-20261026-224627.zip` (1.5.4); do not assume its final
  agreement-dialog state describes the newer screenshot.
- Confirmed original code path: `AccountSessions.ActivateAndLaunch` calls
  `DailySandboxSessions.CollectForHost` before ordinary launch. An incomplete
  isolated login without a provably fresh replacement can therefore block Play.
  The message tells the user to sign in in a normal window without providing a
  recovery entry that can actually open that window independently of this check.
- Provide a clear in-app **Sign in again** action which opens the ordinary game
  for manual authentication without requiring successful sandbox session return.
  Preserve saved accounts, sandbox contents, identity checks and active-game locks;
  never silently reactivate disabled credentials or substitute another account.
- Separate ordinary-launch recovery from isolated-launch credential import.
  Continue collecting valid, newer credentials when available; an unusable
  isolated session must have a recoverable outcome instead of blocking every
  route to authentication. Keep strict checks when writing credentials into a box.
- After manual authentication, verify the intended account, save its refreshed
  session, and offer retrying the original launch. Handle a different account,
  cancelled login and unchanged credentials explicitly without overwriting data.
- Explain whether fields are missing, automatic login is disabled, collection
  failed, or the game actually rejected authentication. Local completeness checks
  alone do not prove token expiry. Keep UI language separate from these causes.
- Cover ordinary Play, Open isolated, single-account daily launch and batch launch;
  verify missing/disabled isolated login, fresh replacement, newer valid sandbox
  credentials, wrong-account login and running-game conflicts. Recovery must not
  require deleting an account, recreating a sandbox or reinstalling the software.
- The external-launcher instructions are a temporary workaround. The initial
  cause of this user's isolated login loss remains unconfirmed; this TODO fixes
  the confirmed recovery dead end, not an assumed authentication failure.
- Implemented **Sign in again** and an inline recovery card in Accounts. Ordinary
  recovery retries launch the freshly authenticated account directly and still
  verify its live identity; they do not call the failed sandbox exporter again.
  Isolated retries retain the existing strict import/ownership checks. Prompts
  explain normal game exit without logging out and distinguish disabled login,
  incomplete data and failed collection. The intent survives restarting Dustweave.
