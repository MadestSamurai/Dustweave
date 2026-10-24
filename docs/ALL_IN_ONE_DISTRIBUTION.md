# Single-file distribution and self-update · 1.5

The 1.5 distribution contains one executable, `Dustweave.exe`, bundling the host, built-in tools and dependencies. Portable includes the pinned .NET Desktop Runtime; Lite uses the installed runtime. Keep the adjacent `app` directory for data, specifications, documentation and licenses. No independent launcher or updater is distributed.

Accounts, settings, plugins and game assemblies remain outside the distribution. Maintenance protects the active native extraction generation.

## Download policy

- First installation, manual upgrades and recovery from damaged files use full packages from [GitHub](https://github.com/MadestSamurai/Dustweave/releases/latest) or [Quark](https://pan.quark.cn/s/0ee553c60c0d?pwd=5AgC), code `5AgC`.
- Qicaiyun hosts **differential OTA files only** for the 1.5 lane. The origin provides the signed release index and control logic, plus verified patch fallback. Neither service hosts full 1.5 archives or repair blocks.
- An exact baseline and edition, unchanged runtime, valid local files and a direct patch of at most 10 MiB are required. Version count alone does not end support. Missing, oversized or inapplicable patches lead to manual download instructions; no automatic full download is attempted.
- The initial 1.5.0 index contains package hashes but no patch payloads. Full packages stay on GitHub; the hashes bind future reconstructed targets. Publisher attestation binds the initial package acceptance to this index.

## Self-update transaction

1. Verify the signed schema-3 index and download the matching delta. Reconstruct and hash-check the target in staging before offering a restart; do not alter the installation.
2. Wait for idle/restart confirmation while preserving task, storage and single-instance ownership.
3. Copy the running executable into the update attempt and invoke its early `--apply-update` mode. It starts no UI or game components.
4. After the parent exits, verify the signature, baseline and reconstructed files again. Persist a backup and transaction, then replace application files, executable last.
5. Start the target with a nonce. Only the actual target version's initialized window acknowledges completion. Failed startup restores the previous files and retains diagnostics.
6. After an interrupted replacement, the ordinary executable invokes the preserved copy before account, UI or game initialization. That copy can also recover if the root executable was interrupted during replacement.

A damaged installation that has no recoverable update transaction requires manual re-download. The application does not promise self-repair when its executable cannot run. Restore into a new directory; accounts and settings continue to be used. Historical block-repair and bridge paths do not run in layout 3.

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
