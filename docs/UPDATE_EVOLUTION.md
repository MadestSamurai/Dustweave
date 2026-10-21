# Update evolution after 1.4.14

Status: design direction, not a claim that bridge updates already ship.

The requirement is to preserve new capabilities through an automatic bridge
upgrade, not permanently freeze components for compatibility. One user approval
covers the complete upgrade path. A bridge is a technical intermediate step,
not a second manual download or a second confirmation.

## Compatibility boundary today

Layout-2 differential updates require the same runtime version, bootstrap file
list and byte-identical bootstrap files. Published old helpers enforce that
boundary themselves. Changing only the feed or delta generator cannot safely
teach those helpers a new installation protocol.

1.4.14 therefore retains the published launcher and runtime. Its new desktop
repair entry and independent `app/support/Dustweave.Updater.exe` repair the
selected release using signed file inventories and content-addressed blocks.
Same-version repair is not permission to silently upgrade the runtime or convert
editions. No launcher auto-prompt or `Dustweave.exe --repair` is shipped.

## Target behavior

1. Prefer a direct delta when the installed inventory matches a supported base.
2. For changed or missing files, offer repair followed by the pending update;
   later allow reconstruction of the target release directly from verified
   healthy local blocks and downloaded blocks.
3. Permit a complete target installation, including launcher and runtime, through
   an independent transactional helper. A full logical replacement does not
   require downloading a full ZIP if most blocks already exist locally.
4. When the installed helper lacks a required capability, first install a
   compatible bridge that adds it, then automatically continue to the requested
   target. Show both steps and resume the remaining step after a restart. Do not
   force all users through every intervening release.
5. Restore the launcher capability to offer independent repair when the main
   window cannot load through that bridge path. Compatibility is a delivery
   constraint, not a reason to drop the feature.

## Delivery and capability design

- Legacy clients cannot discover a bridge just by adding an unknown field to a
  feed whose newest release they cannot install. Keep their existing endpoint
  serving a compatible, signed bridge as its eligible current release. The
  bridge switches to a capability-specific signed feed for the final target.
  Both feeds remain on the origin and use the existing trust roots; historical
  release metadata stays immutable. Do not rely on server-side version guessing
  when old requests do not send their installed version.
- Record the original update approval and intended channel in the bridge
  handoff. The second stage revalidates the signed target and required capability
  before installation, then continues without another approval dialog. An
  unavailable target leaves the bridge usable and the update pending; it must
  not start game automation between stages or silently claim completion.
- Extend signed metadata with minimum helper capability, package/layout/runtime
  compatibility and an optional explicit bridge target. Unknown capabilities
  must remain ineligible for legacy clients; keep their signed feed compatible.
- Preserve the existing direct-delta path for 1.4.7+ while patches remain within
  its size limit. A new protocol must have its own eligibility path, not weaken
  old clients' checks or rewrite immutable release metadata.
- Legacy clients retain their existing eligibility rules. Capable clients select
  the verified direct or bridge path under the same update approval. Download
  bytes belong in ordinary progress reporting; there is no additional size-based
  confirmation or second-stage confirmation in this design.
- Bind target inventory, edition, helper hash, protocol and source constraints
  into the signed update authorization. Do not use a repair signature alone as
  authorization for an unannounced version change.
- Keep policy/signatures at the origin and immutable payload blocks on Qicaiyun,
  with verified origin fallback. Never ship storage or publication credentials.
- Prepare a supported independent helper outside the installation tree before
  any application exit. Updating the helper inside `app/support` is insufficient
  if it will be locked by the repair process; the transaction runs from a copied,
  verified attempt directory.
- A full bootstrap/runtime replacement must not depend on that same runtime for
  recovery. Use a self-contained external helper for both editions; account for
  its separate download/storage cost and reuse by hash.

## Installation and recovery gates

Before replacement, verify all target bytes, required free space including
rollback, path ownership, no reparse redirects, idle automation, closed tools,
the main instance and isolated workers. A queued scheduled run must not race the
installation or start game automation during bridge handoff.

Keep a durable journal and a verified previous installation until the new
application explicitly acknowledges a healthy start. Handle new/removed files
as well as replacements. Account data, credentials, preferences, private plugins
and other unowned files remain outside the transaction.

Current in-place bootstrap recovery is insufficient if power is lost while the
launcher or its runtime is being replaced. Before enabling these replacements,
select and validate either versioned installation directories with an atomic
activation pointer, or an external recovery entry that survives damage to the
active bootstrap. Preserve the normal user-facing entry point. Do not describe
ordinary per-file rollback alone as complete power-loss protection.

Only advance the signed feed after the intended published old client has passed
the complete path. Keep the bridge release and recovery payload available while
clients can still require them; cancelation after the first step leaves a usable
bridge version with a visible pending second step.

## Required acceptance matrix

- Real 1.4.7, current previous release and bridge-client installations; Lite and
  Portable. Validate eligibility as well as actual file replacement.
- Clean installation, user-overwritten files, missing descriptor, missing
  launcher, same-size corruption, extra user files, changed runtime and edition
  mismatch. Never infer authorization from an untrusted local descriptor alone.
- Interrupted download, offline CDN and origin fallback, invalid signatures,
  low disk space, antivirus/file locks and parent process failing to exit.
- Terminate at every durable transaction boundary, particularly between
  bootstrap/runtime file replacements and between bridge stages. Restart using
  the same entry point a user will use, not only an already-running test helper.
- Startup failure, lost acknowledgement, successful startup followed by helper
  loss, rollback interruption, canceled second-stage update and scheduled-run
  contention. Preserve account/plugin sentinels throughout.

## Phased rollout

**1.4.14:** direct deltas with the stable bootstrap; signed same-version repair
and independent recovery UI. This is the published compatibility baseline.

**Next implementation:** deliver bridge selection/continuation and an independent
helper capable of the target replacement through the current compatible path.
1.4.14 is the existing baseline; it must not be described as already containing
this bridge capability. Include restart continuation without starting a due game
queue between the two update stages.

**Target release:** change the launcher and restore its startup-failure repair
prompt. The bridge automatically installs it under the original update approval.
Validate the recovery architecture above before enabling bootstrap or runtime
replacement. Runtime changes follow the same supported path when needed.

**Payload optimization:** target-release block reconstruction and reuse of healthy
local blocks reduce transfers independently of the bridge mechanism. An upgrade
covering all components does not imply downloading the entire ZIP, and payload
optimization must not delay delivery of the bridge capability itself.
