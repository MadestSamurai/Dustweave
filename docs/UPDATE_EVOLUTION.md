**1.5.0 onward:** the separate layout-3 channel uses differential OTA only and a temporary copy of the main executable. The block-repair/native-updater rules below apply only to 1.4 and earlier. See [current distribution and acceptance](ALL_IN_ONE_DISTRIBUTION.md).

# 1.4.15 update and recovery protocol

Status: published and verified online; see [the 1.4.15 deployment receipt](releases/1.4.15-deployment.md). [Local preparation](releases/1.4.15-preparation.md) retains the original package and acceptance evidence. 1.4.14 remains an immutable published baseline.

## User flow

An existing installation approves one update. Its original updater installs `1.4.15-bridge.1`, which immediately continues to final `1.4.15`. The bridge never opens daily automation, connects to the game or runs a schedule. There is no second approval or download-size confirmation. Canceling or losing the network during preparation leaves a resumable bridge: reopen Dustweave to continue. Final startup failure restores the bridge without automatically retrying the failed installation in a loop.

Final 1.4.15 can replace its launcher, runtime and managed components. Small deltas remain preferred; a damaged base falls back to verified target blocks under the same approval. Healthy local blocks are reused. Updating all logical components does not imply downloading a complete ZIP. Future protocol-2 releases update directly, without another 1.4.15 bridge.

The root `Dustweave.exe` is native and independent of .NET. It offers repair if the managed application cannot start; `Dustweave.exe --repair` opens the same recovery path. Repair preserves accounts, settings, plugins and unowned files. The native entry pins its edition, version and repair-helper digest. Missing or damaged repair helpers can be restored from signed inventories. Repair never silently selects a newer version because local metadata is damaged. If the root executable itself is deleted, use `app/support/Dustweave.Updater.exe` or restore the official package. A deleted executable cannot repair itself.

Critical startup dependencies are checked before entering the CLR: an invalid entry assembly can terminate the process without returning an exception to the native caller. The native entry also uses the Common Controls v6 activation manifest needed by its independent repair progress dialog. These two failure paths are part of packaged repair acceptance.

## Compatibility and authority

- Original 1.4.7–1.4.14 updaters keep their schema-3 feed at `updates.json`. Its eventual current release is the compatible bridge, with byte-identical bootstrap/runtime files copied from verified original 1.4.14 archives.
- Bridge and final clients read `updates-v3.json`. Both indexes share the existing signing keys and immutable payload base, with separate publication state, receipts and current indexes.
- Signed feed schema 4 explicitly sets `InstallerProtocol: 2` on capable targets. Signed inventories describe bytes; the signed release feed separately authorizes source version, target version, edition and protocol. Repair signatures alone do not authorize unrelated upgrades.
- `dustweave-copy-v3` deltas can replace bootstrap/runtime files. Legacy v2 deltas retain their original constraints. Failed delta reconstruction falls through to signed inventory reconstruction, not unchecked copying.
- The bridge pins its final target to 1.4.15. A later release cannot change an already approved second-stage target. Keep bridge and final inventories available while legacy clients still need them.
- Policy/signatures remain at the origin. Immutable blocks and patches use Qicaiyun with verified origin fallback. No storage credentials ship to users.

## Transaction and recovery

Before replacement, verify signatures, all target bytes, installed source/edition, paths, free space including backup, idle automation and ownership locks. Copy the verified self-contained helper outside the installation and wait for its parent to exit. Both Lite and Portable use this independent helper.

All replacements use flushed temporary files and atomic file replacement with a durable backup/journal. Install the native root recovery entry first, before changing the CLR or managed launcher. Before that first replacement, the old entry/runtime remain intact and can dispatch the external helper. Restore the root entry last during rollback. Normal startup checks unfinished journals before loading managed code. A healthy main window explicitly acknowledges startup before the updater completes.

The bridge resumes before desktop initialization. Completed inventory transactions obey the same two-version/seven-day cache retention rules; unfinished recovery stays protected. Cached signed inventories and verified staged bytes permit offline continuation.

Process interruption and replacement faults can be tested locally; they are not a physical power-loss test or protection against hardware failure. A Lite installation still needs its external .NET Desktop Runtime. Repair restores program files, not Windows prerequisites.

## Acceptance

`scripts/test-bridge-update.ps1` extracts original published archives and runs their actual helpers for Lite and Portable of 1.4.7 and 1.4.14. It checks automatic second-stage installation, every final file, account/plugin sentinels, startup failure rollback and normal-entry recovery with a broken Portable CLR. Payloads are pre-staged in this process test. Transport corruption, resume, source fallback and signature rejection are separate synthetic gates.

`scripts/test-native-repair.ps1` verifies the native prompt and automatic repair with damaged startup files, helpers and descriptors, including a missing Portable host and foreign runtime files overlaid into Lite. `scripts/test-repair-packages.ps1` retains the independent repair engine's earlier fault matrix. `UpdateInventoryCases` exercises replacement boundaries and damaged-delta fallback. Publisher capability tests cover independent indexes, inventory prerequisites and immutable history. Protocol-2 publication additionally requires version-matched native repair results for both editions. Canonical packages retain source, localization, actual UI/tool and binary-boundary checks. Keep dated evidence and exact package hashes; local success is not a publication receipt.

## Publication order

1. Finish exact-package validation and prepare final/bridge archives, deltas, signed inventories and repair blocks.
2. Deploy the capability-aware v2 publisher and exact no-cache nginx rule for `updates-v3.json`, preserving current indexes and the legacy v1 channel.
3. Publish and verify repair blocks/inventories for final and bridge releases.
4. Publish final schema-4 index with the constrained `capable` commands. Check public signatures, editions, payloads and repair availability.
5. Publish the compatible bridge to schema-3 index last. The server rejects activation unless final 1.4.15 and its bridge identity are ready in the capable index. Old clients must not arrive at a dead end.
6. Publish the ordinary GitHub release as final 1.4.15. The bridge is delivery infrastructure, not a second manual application download.

Use `-Capable` with `publish-update-signed.ps1` and `publish-repair-signed.ps1` for the new index. Deployment/publication requires its own authorization. Acceptance scripts never upload test fixtures or modify public feeds.

Pass the canonical prepared `-RepairDirectory` to `publish-update-signed.ps1` when those inventories were uploaded in advance. Reuse their signed bytes: separately re-signing an identical payload can produce a different envelope, which immutable publication correctly rejects.

The initial 1.4.15 migration uses the reviewed local bridge/final bundle. From 1.4.16 onward, the update preparation workflow reads `updates-v3.json` and creates patches from at most ten recent capable releases. Older capable versions remain eligible for signed-block reconstruction; the internal bridge always finishes at 1.4.15 and does not need a public GitHub release or a new patch for every future release.
