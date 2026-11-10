# 1.6 retirement and 1.7 release plan

Decision recorded on 2026-11-10 (Asia/Hong_Kong).

## Final direction

- The owner withdrew the full 1.6.10 GitHub release and explicitly released its
  sole manual downloader from the compatibility requirement. Preserve the old
  local artifacts and receipts as withdrawn evidence, never as proof for the new
  retirement build. Do not publish 1.6.11.
- The old 1.6 channel ends with **1.6.10, a manual-upgrade notice**. It explains
  CVE-2026-111497 and links to the complete 1.7.0 download. Its update must be
  small; it must not download the replacement runtime or continue into a large
  second automatic update. Account/settings directories are preserved.
- **1.7.0 is the full application**, with all verified changes from the withdrawn
  candidate and Portable runtime 8.0.33. Lite keeps its established .NET 8 startup
  compatibility and recommends updating the external Desktop Runtime separately.
- Full application downloads remain on GitHub/Quark, not the object-storage
  bucket. Differential OTA and plugin packages remain on object storage.
- Keep direct deltas from retained 1.7 baselines without a ten-version cutoff,
  retaining only the current and two rollback target sets online. Each patch is
  at most 32 MiB, and each release set at most 512 MiB. A missing, oversized or
  damaged baseline has an honest manual-download route. If the complete set
  cannot fit, stop publication for a reviewed maintenance transition; do not
  silently discard required baselines or enlarge downloads.
- Total bucket usage must stay below **10,000,000,000 bytes**, including unrelated
  retained prefixes. Upload preparation inventories the entire bucket, reserves
  room for the whole batch and rejects over-budget publication before its first
  upload. Cleanup must identify live, pinned, rollback and in-flight dependencies;
  age alone never authorizes deleting an active file. Versioned/noncurrent objects
  and unfinished multipart uploads must be accounted for or reported as unknown.
  Uploads use an 8 GB working ceiling and fail closed on unknown accounting.
  `publish-update-mirror.ps1` automatically prepares and applies the audited
  retention plan before uploading. Plugin publication also uses the same
  whole-bucket budget. Object ETag and length must still match; timestamps are
  compared at HTTP Last-Modified precision because LIST retains subseconds.

## Current state, before implementation

- Main source: `71e1682407d8e9928f3db36ba0068656c936df22`.
- GitHub release v1.6.10 was deleted by its owner; its tag still exists.
- The public current OTA is still 1.6.9, signed feed SHA-256
  `08538644c83cf33c4c9dc9ec12b773b54da7fa53f6026c6e08235955610322d2`.
- The superseded large-patch upload was interrupted before creating a server
  publication operation. It did not switch the public feed. Its uploaded objects
  are candidates for exact-key cleanup after checking all live manifests.
- Read-only bucket listing: 1,462 current objects, 3,382,694,395 bytes. This is a
  current-object inventory, not yet a full versioning/multipart accounting.

## Release order and acceptance

1. Preserve the withdrawn source/tag identity, package hashes and failed/aborted
   delivery evidence. Build the retirement notice separately from the full host.
2. Produce and verify the two 1.7.0 complete packages. Reuse verified unchanged
   game/tool evidence; run affected source checks, actual packaged startup,
   updater recovery and edition/runtime checks. Do not operate the game.
3. Publish 1.7.0 full packages and verify their public bytes before advertising
   any retirement notice. GitHub latest must point to 1.7.0.
4. Prepare the new current channel and its constrained maintenance/publishing
   entry. Preserve the old 1.5 bridge and pinned 1.6.0 dependencies. Install only
   after local install/rollback and channel-isolation checks are concrete.
5. Build the 1.6.10 retirement notice, measure every supported 1.6.0–1.6.9 delta,
   and exercise actual old helpers, startup-failure rollback and interruption
   recovery. Rendering the notice acknowledges healthy startup; it never starts
   the game or a queue. Closing/reopening remains usable and preserves data.
6. Publish the notice with `latest=false`, then atomically update the old 1.6 feed.
   Validate localized text, download links and the absence of a large automatic
   follow-up. Do not advertise the withdrawn full 1.6.10 bytes.
7. Review and apply object cleanup, then retain inventory, deleted-key receipts,
   protected references and post-cleanup measurements. Apply normal local release
   cleanup separately; local cleanup is not evidence of cloud cleanup.

## Version invariants

| Installation | Expected result |
| --- | --- |
| 1.6.0–1.6.9, automatic update enabled | Small retirement update, explicit manual 1.7.0 download |
| 1.6.0–1.6.9, automatic update disabled | Availability/notes; no silent download or forced restart |
| Retirement 1.6.10 | Manual download notice; no game launch, no large automatic migration |
| 1.7.0 | Current channel only; never installs the retirement build |
| Lite | No silent conversion to Portable; external runtime remains user-managed |
| Stale/offline feed | No downgrade; bounded retry, preserved installed application/data |
| Interrupted retirement installation | Existing helper restores/recovers; notice is not an acknowledgement shortcut |
| Existing 1.5.17 bridge | Its exact pinned 1.6.0 target stays available until an explicitly verified replacement policy |

The discarded proposals—special handling of the withdrawn 1.6.10, a 1.6.11
intermediate, and large per-baseline runtime patches—must not reappear implicitly.

Security source: [Microsoft .NET advisory](https://github.com/dotnet/runtime/security/advisories/GHSA-p3jp-9v83-76w9).
The retirement build is a migration aid, not the security-fixed full application.
