# Single-file distribution and self-update · 1.5

Status: implementation and release validation in progress. This document supersedes the earlier 1.5.0-local.1 candidate, which still shipped an independent updater.

## Distribution

The standard .NET bundle contains the host, built-in tools and dependencies. Portable includes the pinned .NET Desktop Runtime; Lite uses the installed runtime. The adjacent app directory contains data, specifications, documentation and attribution. The package ships exactly one executable: Dustweave.exe. It does not contain a separate launcher or Updater.

Accounts, settings, plugins and game assemblies remain outside the distribution. Standard .NET native extraction is counted by maintenance; active generations are protected.

## Self-update

Layout 3 uses a signed schema-3 feed and full ZIPs. There are no differential prerequisites, baseline-distance limits, block inventories or intermediate capability upgrades. A runtime update is simply part of the next Portable archive. Lite retains its published runtime requirement.

1. Verify the signed feed and download the selected full archive with bounded resumable transfers.
2. Wait for the existing idle/restart confirmation. Preserve task, storage and single-instance ownership.
3. Copy the running main executable into an attempt directory and invoke its early --apply-update mode. No UI or game components start in that mode.
4. After the parent exits, verify the signed feed and complete archive again. Back up affected application files durably, persist the transaction and replace files atomically, executable last.
5. Start the target with a nonce. Only the actual target version's initialized window acknowledges and commits. Failed startup restores the previous installation and preserves the failure record.
6. On an interrupted replacement, the ordinary executable invokes the preserved self-copy before any account, UI or game initialization. The self-copy can also perform recovery if the root executable is damaged.

Integrity repair downloads and compares the same signed full archive for the running version. Confirmed repair uses the verified executable from that archive when the on-disk main executable has been overwritten. It changes only distributed application files and retains user data and plugins. If no valid executable remains available, download the official full package into a new directory. A missing or corrupted executable cannot promise to repair itself.

The transaction engine is shared with historical releases; the new lane does not enter their block reconstruction or bridge paths. Keeping signature, path, ownership and rollback checks is intentional.

## Separate release lanes

- 1.4.18 is built from the latest common source with all completed shared functional fixes. It retains layout 2, the native launcher and independent updater required by the existing 1.4 channel, and displays localized manual migration instructions.
- 1.5.0 starts a new layout-3 baseline at https://bd2.madsam.work/updates/dustweave/all-in-one/releases.json. Its signed index, publishing account/state, original archives and cloud object prefix are independent.
- New cloud objects use dustweave/ota/all-in-one; old v2 objects and both old indexes remain unchanged during new-lane provisioning.
- The old channel ends at 1.4.18. It never advertises 1.5 as an automatically installable target. Users download 1.5 into a new directory, retain account/settings storage, and save Windows startup/schedules again to point to the new location.
- Publish and verify 1.5 first. Keep GitHub latest on 1.5 when subsequently publishing 1.4.18. Both are normal releases.

## Release acceptance

Both editions must pass actual self-copy upgrade, bad-target startup rollback, interrupted replacement recovery through the host and preserved copy, and full-package repair. Verify exact installed bytes and preserved account/plugin sentinels before disposing exact test-owned processes. Include public HTTPS signature/hash/range checks separately from cached local tests. Never describe an injected transaction interruption as a physical power-cut test.

The first public baseline uses a separately built layout-3 predecessor fixture; this validates the self-update mechanism, not migration from 1.4. The old channel separately requires the original published 1.4.17 helper to install final 1.4.18, including its native repair acceptance.
