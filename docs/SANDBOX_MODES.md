# Account sandboxes

Sandbox management belongs to the selected saved account. Creating a sandbox prepares its configuration but does not start the game. File mode is a global host preference in Software settings, shared by manual isolated launch, parallel queues and scheduled queues across all accounts.

## File modes

- **Synchronized (default):** known shipped executables, assemblies and Unity program inputs are read from the host installation. Runtime caches remain writable through ordinary private copy-on-write.
- **Compatible:** ordinary Sandboxie file isolation. Before starting, retire stale overlays of known program inputs so host launcher updates remain visible. Preserve the replaced overlays outside the game search path. This does not reset resource caches, login data or game preferences.

Both modes use the host launcher to update the game while the isolated games are closed. Neither mode grants direct host write access. The global choice applies on the next isolated start, including scheduled execution. An explicit compatibility choice from an earlier per-account local build migrates once; after that, the global preference always wins.

## Management responsiveness

Open the management window before reading installation, binding or process state. Do not prepare the shared connection or rebuild all host panels for this read-only modal. Use Sandboxie's native process enumeration rather than starting a helper executable. Display a bounded wait and allow immediate close while reading; only a mutation needs safe cancellation. A refresh reuses a pending probe instead of accumulating threads.

Binding repair expands inside management. Present a unique verified match first, with Not this one revealing the manual selector and explicit binding action. Ownership and idle-process checks still run before committing. No additional binding dialog is used.

## Migration

The 1.4.17 `BrownDust II_Data\*` read-only rule incorrectly included the writable `Addressable` resource cache. Remove the obsolete rule from existing owned sandboxes, not just new ones. Only change rules with an idle box. Bound the wait for completed launcher/export helpers; retain the actual blocking process list for diagnostics.

Research capture transactions are not a production startup strategy. If an earlier transaction remains pending, complete its existing restoration before applying production rules. Normal launches create no capture transaction and do not move the entire game data directory.

## Removal

Require explicit confirmation identifying the account. Verify the exact account marker, configured root and lack of additional aliases. Reject active processes and custom deletion hooks. Collect the latest isolated login state and merge daily progress before removing the sandbox. Use Sandboxie's supported deletion operation for its private files and registry, then remove its configuration. Keep the host's saved account, preferences and merged daily records.

Persist removal intent so an interrupted deletion cannot silently launch a partially removed box. A retry continues removal; it does not overwrite an unrelated configuration or forget preserved recovery data.

Successful removal presents an inline Create a new sandbox action and explains that the next isolated launch can also create it. Do not recreate immediately or show another confirmation dialog; recreation requires an explicit action or a later isolated launch.

## Acceptance

- Migrate legacy rules, switch modes repeatedly, preserve unrelated rules and fail on lost writes.
- Keep `Addressable`, login state and channel settings writable and private.
- Update stale program overlays after a host update, including interrupted operations.
- Verify create/remove ownership and process-list parsing; surface concrete next actions.
- Inspect management UI in all supported languages and both themes, including empty, active and interrupted states.
- Validate both production launch modes through the normal entry, without collector dependence, and record game scope separately from synthetic checks.
