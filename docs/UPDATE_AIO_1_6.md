# 1.5.17 transition and 1.6.0 maintenance

1.5.17 is an update-only bridge to the pinned 1.6.0 target. It contains no game
startup or daily entry. Original layout-3 clients install it through their
existing signed v2 delta and temporary self-copy. Its initialized progress
window acknowledges that first transaction before it prepares the second.
Cancel/network loss leaves a resumable bridge; a failed final startup returns to
the bridge with an explicit retry notice instead of an automatic failure loop.

The bridge and final keep the existing binary delta decoder, signature checks,
durable file transaction, executable-last replacement and startup acknowledgement.
The new content-block experiment is deferred and is not part of these releases.
No complete ZIP, repair block, external updater or origin payload fallback is added.

The old `releases.json` stops at 1.5.17, whose deltas remain below the old 10 MiB
cap. `releases-v2.json` serves 1.6.0 and later. The immutable
`releases-1.6.0.json` allows delayed/interrupted bridges to finish at exactly the
approved target even after later releases are published. Install the final and
verify its delivery before activating the bridge. The publisher enforces this.

For 1.6.0 onward, every retained released baseline is eligible for a direct
differential update, without a ten-release window or the ten-MiB eligibility cap.
Runtime bytes travel inside Portable's executable; a runtime version difference
alone no longer rejects the delta. Lite retains its external Windows Desktop
Runtime prerequisite. Package integrity/format/resource checks remain enforced.
Damaged installed files lead to manual GitHub/Quark downloads; full archives are
never automatically fetched. A future change of the differential encoding needs
its own complete acceptance before replacing this established decoder.

Release evidence must include actual original-client-to-bridge and bridge-to-final
execution in both editions, final startup rollback, interrupted transaction
recovery, cancellation/resume, preserved user sentinels, exact installed hashes,
and public signed-feed/CDN checks. Reuse the completed full feature regression
when its application inputs are unchanged; rerun affected update tests and gates.
