# Single-file distribution and self-update · 1.5 / 1.6

The 1.5 distribution contains one executable, `Dustweave.exe`, bundling the host, built-in tools and dependencies. Portable includes the pinned .NET Desktop Runtime; Lite uses the installed runtime. Keep the adjacent `app` directory for data, specifications, documentation and licenses. No independent launcher or updater is distributed.

Runtime security servicing: `Directory.Build.props` is the authoritative Portable runtime pin. Publish, apphost templates and package descriptors read that pin. Lite retains its established .NET 8.0.0 startup floor so automatic updates keep starting on existing .NET 8 installations; recommend upgrading its separately installed Desktop Runtime to 8.0.33 or a later 8.0 patch for security fixes. A packaged `--package-runtime-check` probe reports CLR, core library, Brotli and WPF versions before account/game initialization. Official packaging verifies Portable against the exact pin and Lite in a patched validation environment; additionally verify the actual Lite package startup and upgrade on an older installed .NET 8 runtime. Update SDK/compiler/linker locks together and run locked restore for the complete graph. Historical 1.4 bootstrap bytes and fixtures remain immutable. Do not present Lite as self-contained or claim that it patches the external runtime.

Accounts, settings, plugins and game assemblies remain outside the distribution. Maintenance protects the active native extraction generation.

## Download policy

- First installation, manual upgrades and recovery from damaged files use full packages from [GitHub](https://github.com/MadestSamurai/Dustweave/releases/latest) or [Quark](https://pan.quark.cn/s/0ee553c60c0d?pwd=5AgC), code `5AgC`.
- Qicaiyun hosts **differential OTA files only** for the 1.5 lane. The origin provides the signed release index and redirect control, never public patch bodies. Neither service distributes full 1.5 archives or repair blocks. Origin copies retained by the publisher are verification/rollback inputs, not a download fallback.
- Verified immutable per-file routes remain active through transient health-probe failures. Probes report degraded reachability without diverting downloads. Old clients' `?origin=1` retry also redirects to object storage. An unverified path returns a small, uncached 503 with Retry-After; it cannot expose local files. Clients retain bounded retries and can use manual GitHub/Quark downloads if delivery is unavailable.
- Before advertising a new 1.5 release, the server verifies all cloud patch bodies against the signed index. Failed preparation preserves the previous index and its download routes. Progress is resumable; a later publication attempt reuses only matching signed checkpoints.
- For 1.5.0–1.5.16, an exact baseline and edition, unchanged runtime, valid local files and a direct patch of at most 10 MiB are required. These already released clients receive the small 1.5.17 transition before 1.6.0.
- From 1.6.0, retain the existing signed `dustweave-copy-v2` differential format and self-copy transaction. The 10 MiB policy and ten-release window do not apply to layout 3. Generate direct deltas from every retained 1.6 baseline; runtime bytes may change inside the bundle. Archive bounds, exact baseline hashes, signatures and edition checks remain mandatory. Missing or damaged source files require manual download; full archives are never fetched automatically.
- The initial 1.5.0 index contains package hashes but no patch payloads. Full packages stay on GitHub; the hashes bind future reconstructed targets. Publisher attestation binds the initial package acceptance to this index.

## Self-update transaction

1. Verify the signed schema-3 index and download the matching delta. Reconstruct and hash-check the target in staging before offering a restart; do not alter the installation.
2. Wait for idle/restart confirmation while preserving task, storage and single-instance ownership.
3. Copy the running executable into the update attempt and invoke its early `--apply-update` mode. It starts no UI or game components.
4. After the parent exits, verify the signature, baseline and reconstructed files again. Persist a backup and transaction, then replace application files, executable last.
5. Start the target with a nonce. Only the actual target version's initialized window acknowledges completion. Failed startup restores the previous files and retains diagnostics.
6. After an interrupted replacement, the ordinary executable invokes the preserved copy before account, UI or game initialization. That copy can also recover if the root executable was interrupted during replacement.

A damaged installation that has no recoverable update transaction requires manual re-download. The application does not promise self-repair when its executable cannot run. Restore into a new directory; accounts and settings continue to be used. Historical block-repair paths do not run in layout 3.

## 1.5.17 transition to 1.6.0

The transition is a small bundled update application, not the full daily host. Its Lite build keeps the same .NET 8.0 baseline as the existing Lite host; only Portable pins and includes runtime 8.0.30. A visible progress window acknowledges the first transaction, waits for its completion, then downloads and installs the exact signed 1.6.0 target through the existing self-copy helper. Cancel or network failure preserves account data and permits retry by reopening the application. Target startup failure restores the bridge and shows retry guidance rather than looping immediately.

The old `releases.json` advertises 1.5.17 only. The bridge uses immutable `releases-1.6.0.json`; 1.6.0 and later use `releases-v2.json`. Publish and verify the final target before advertising the bridge. The publisher checks both final deltas and identical signed bridge metadata. Future releases must retain the pinned target, original baseline archives and transition routes.

The unpublished full 1.5.17 candidate occupied staging cloud names. Final transition objects therefore use `v1.5.17-transition`; user-facing original URLs remain unchanged. Never overwrite those earlier immutable objects. This is a publication route distinction, not a different client delta format.

## Separate release lanes

- 1.4.18 contains the completed common functional fixes, retains layout 2 and its historical updater/repair behavior, and presents manual migration instructions.
- 1.5.0 starts layout 3 at `https://bd2.madsam.work/updates/dustweave/all-in-one/releases.json`. The publishing account, signed index, state and patch directory are separate from both older lanes.
- The cloud prefix is `dustweave/ota/all-in-one`. The new uploader and origin reject complete archives and repair inventories for this lane.
- The old lane ends at 1.4.18 and never advertises 1.5 as an automatic target. Download 1.5 into a new directory and re-save Windows startup/schedules if enabled.
- Publish 1.5 first, then 1.4.18 as a normal release without changing GitHub latest away from 1.5.

## Acceptance

Both editions require actual self-copy delta upgrade, bad-target startup rollback, interrupted replacement recovery via both the host and preserved copy, and damaged-base refusal without installation writes. Verify exact files and account/plugin sentinels before disposing exact test-owned processes. Network checks separately enforce delta-only requests, size limits and rejection of stale full-package caches. An injected interruption is not a physical power-cut test.

The first baseline uses a separately built layout-3 predecessor fixture; this tests the mechanism, not automatic migration from 1.4. The old lane requires the original released 1.4.17 helper to install 1.4.18, with its historical native repair acceptance.

The earlier full-package 1.5.0 release was withdrawn before domestic activation when the delivery policy changed. Preserve its evidence as superseded; do not reuse it as proof of differential behavior.
