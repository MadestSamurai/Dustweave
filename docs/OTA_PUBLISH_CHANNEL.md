**1.5.0 onward:** the separate layout-3 channel uses full archives and a temporary copy of the main executable. The block-repair/native-updater rules below apply only to 1.4 and earlier. See [current distribution and acceptance](ALL_IN_ONE_DISTRIBUTION.md).

# Signed OTA publishing channel

## Protocol-2 publication (1.4.15+)

The capability-aware v2 publisher maintains `updates-v3.json` and a separate
`capable/` operation state. Use `-Capable` for its signed publication and repair
commands. Existing clients retain `updates.json`; its migration bridge must be
activated only after the exact final target and signed inventories are available.
The initial administrator upgrade uses `scripts/upgrade-update-capabilities.sh`;
it replaces the publisher and adds the no-cache manifest route without changing
existing feeds. See [update evolution](UPDATE_EVOLUTION.md) for the full sequence.

Protocol-2 publication requires version-matched native repair acceptance for both
editions, in addition to real baseline-helper upgrade, rollback and recovery.
Reuse prepared signed inventory envelopes with `-RepairDirectory`; immutable
published manifests must not be replaced by separately re-signed equivalents.
Capable clients can rebuild from signed blocks when a delta is unavailable or its
base is damaged. Legacy patch size limits still apply only to the legacy index.

This is a separate channel from the simulator deployment service. It publishes only Dustweave static update files and never executes uploaded scripts, starts game components, changes website configuration or deploys simulator code.

The channel consists of the repository-owned .NET tool in `tools/OtaChannel`, an administrator-only installation script, and `scripts/publish-update-signed.ps1`. It does not require Python or a server-side .NET installation: publish the server tool as a self-contained Linux x64 executable.

## Authorization and trust

- A dedicated `dustweave-ota` account owns only the update directory and its operation state. The executable, configuration, account home and SSH authorization are administrator-owned.
- A separate Ed25519 transport key is restricted to `dustweave-ota submit` and `dustweave-ota status [nonce]`. Interactive shells, arbitrary commands, file transfers, PTYs and forwarding are unavailable through this key.
- The existing DPAPI-protected OTA ECDSA key signs publication requests with the distinct `Dustweave-OTA-Publish-v1` domain. The server stores only public keys. A transport key alone cannot authorize publication.
- Each request binds the product, target version, expected current feed digest, exact signed feed and packaged acceptance digests, every archive digest/length, a random nonce and a maximum 15-minute acceptance window.
- The feed itself is verified separately. Supported release history starts at `1.0.0-beta`; already published public-release metadata is immutable. The expected current digest is checked before upload and again before the atomic feed switch.

This is a trusted publisher capability, not a sandbox for an untrusted signing-key holder. Keep both the transport key and signing key outside Git, packages and diagnostic exports. Key rotation and channel maintenance require administrator access; ordinary releases do not.

## Prepare and install once

1. Complete the normal package, differential generation and actual prior-version updater acceptance. See [OTA release procedure](OTA_RELEASE.md).
2. Build and check the channel:

```powershell
dotnet run --project tools/OtaChannel/Dustweave.OtaChannel.csproj -c Release -- self-test
dotnet publish tools/OtaChannel/Dustweave.OtaChannel.csproj -c Release -r linux-x64 --self-contained true -o <isolated-output>
```

3. Prepare a configuration with `State`, `Public`, and `Keys` (the public keys from `assets/updates/trust.json`), plus a freshly generated SSH `transport.pub`. The production layout is `/var/lib/dustweave-ota-channel` and `/home/nginx/dustweave-updates`. These must share a filesystem to support an atomic directory rename.
4. With one-time administrator access, upload the verified executable/configuration/public key and run `scripts/install-update-channel.sh <payload-directory>`. Inspect the new account, ownership, restricted command, denied arbitrary command and status response. No private key is sent to the server.
5. Revoke the temporary administrator key after deployment and recheck that the constrained publisher still works.

The local client configuration contains `Host`, `User` (`dustweave-ota`) and the absolute `IdentityFile` path. Keep it and the private transport key in a current-user-only directory outside source control. This installation is separate from running the application; end users need no SSH setup.

## Subsequent publication

Run from the task's required working directory, using appropriate absolute paths when outside this repository:

```powershell
./scripts/publish-update-signed.ps1 -Action status -Config <private-client.json>
./scripts/publish-update-signed.ps1 -Action submit -Config <private-client.json> `
  -PackageDirectory <tested-package-directory> -Evidence <packaged-acceptance-results.json> `
  -ExpectedCurrent <observed-current-version> -Operation <new-operation.json> -Wait
```

Both Portable and Lite must pass actual baseline-helper upgrade, startup-failure rollback and interrupted-transaction recovery. For a supported delta baseline, use `scripts/test-update-packages.ps1 -UseDelta -UseBaselineHelper`. The server binds the submitted evidence to the signed request; this is publisher attestation, not server-side execution of Windows acceptance tests.

The server streams and hashes each archive, enforces file names and size limits, rejects extra bytes, and preserves the nonce even after failure. Once accepted, a separate worker survives client disconnection. It checks hashes again, preserves the previous feed, makes the immutable version directory available and atomically switches `updates.json` last. It does not delete historical releases.

If upload or acknowledgement is uncertain, query the **same** saved operation:

```powershell
./scripts/publish-update-signed.ps1 -Action status -Config <private-client.json> -Operation <operation.json> -Wait
```

`completed` confirms server-side publication. Public HTTPS download/signature/hash, Range/cache behavior and client-version acceptance remain separate gates. A process interrupted after the feed switch can reconcile its receipt against the actual feed and archives. A stale operation without a confirmed switch reports `needs_attention`, rather than polling forever or guessing whether to resubmit. Preserve the receipt and investigate before preparing another nonce.

## Validation status

Local checks cover real ECDSA signatures, context separation, expiry, frame limits, replay, immutable public history, stale-feed conflicts, interrupted uploads, serialized publication, archive revalidation and receipt recovery. They run without connecting to the game. Server installation and a real constrained-channel deployment require their own dated deployment receipt; a local successful build is not proof that the channel is live.

## Production deployment · 2026-10-09

The channel is installed and has published 1.0.1-beta. Production command/signature rejection and both public differential downloads passed; the one-time administrator key is revoked. See [the deployment receipt](releases/1.0.1-beta-deployment.md). The local protected client configuration is kept in the parent workspace under artifacts/deployments/dustweave-ota-channel/client.json; it is not distributed to end users.

Routine publication of **1.0.4-beta** succeeded through the same constrained channel, without another administrator key. See [the 1.0.4-beta receipt](releases/1.0.4-beta-deployment.md) for package, differential upgrade and public download verification.

**1.0.6-beta** was subsequently published through the same channel. Both actual 1.0.4-beta helpers passed differential upgrades, and the public downloader verified both patches. See [the 1.0.6-beta receipt](releases/1.0.6-beta-deployment.md).

## Separate layout-2 channel (1.3.0+)

The new channel uses OS account `dustweave-ota-v2`, executable `/usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel`, configuration `/etc/dustweave-ota-channel-v2/config.json`, state `/var/lib/dustweave-ota-channel-v2`, and public files `/home/nginx/dustweave-updates-v2`. The OS account fixes the configuration path, including detached workers; an SSH request cannot select another lane. Each lane has a separate transport key. Neither publisher can write the other lane's files or state.

`scripts/install-update-channel-v2.sh` initializes the first signed 1.3.0 baseline without modifying the legacy installation. Map `/updates/dustweave/v2/` to the new static directory through an independent, longer-prefix web alias. The original `/updates/dustweave/updates.json` remains unchanged. Never initialize the new lane with a synthetic acceptance feed.

The private local configuration is in the parent workspace at `artifacts/deployments/dustweave-ota-channel-v2/client.json`. Use it with `publish-update-signed.ps1` for future releases. The feed layout and endpoint account must agree. Layout 2 uploads contain deltas only; full downloads remain GitHub release assets. Initial setup requires administrator access; later signed publications use only the constrained account. Deployment verification must compare the legacy feed, configuration and executable hashes before and after, test both channels and confirm cross-lane write denial.

## Legacy retirement notice · 2026-10-15

The separate `maintenance/1.2-migration` branch published **1.2.12** through the original `dustweave-ota` account. This is a migration reminder, not a layout-2 update. The latest six legacy baselines receive direct patches in both editions; older supported public versions use full-package fallback. Prior legacy release metadata is unchanged, v2 remains byte-identical at 1.3.16, and GitHub latest still points to v1.3.16. See [the delivery record](releases/1.2.12-deployment.md). Do not merge the retired application implementation back into main.

## Publisher policy upgrade · 1.4.7

The v2 publisher accepts the signed feed-level BaselineDeltas index for 1.4.7+ and verifies its full upload inventory and 10 MiB limit. Update the administrator-owned executable with scripts/upgrade-update-channel-v2.sh after self-tests, then verify constrained status, denied shell commands and unchanged legacy hashes. The existing signing and transport keys remain unchanged. Ordinary releases use the same constrained publisher. Deployment completion is recorded in the release receipt.

## Optional Qicaiyun file delivery

The origin continues to serve the signed `updates.json`, enforce update policy and
own the original file URLs. Verified layout-2 differential files can be delivered
by Qicaiyun through temporary, non-cacheable redirects. This requires no feed
schema or signature change and preserves existing client URLs. The retired
layout-1 channel is not modified.

The local uploader is `tools/OtaMirror`; its AWS SDK dependency belongs only to
the publishing tool, not the application. Store its private JSON configuration
outside the repository, packages and diagnostic folders:

- `Endpoint`: `https://s3.7caiyun.com`
- `Region`: `us-west`
- `Bucket`: the real bucket identifier
- `PublicBaseUrl`: `https://<bucket>.cdn.7caiyun.com`
- `AccessKey`, `SecretKey`: credentials limited to the required bucket

No storage credentials are deployed to the origin. It stores only the public
`MirrorBaseUrl`, ending in `/dustweave/ota/v2`. Objects have immutable names
`dustweave/ota/v2/v<version>/<signed-delta-filename>`.

```powershell
./scripts/publish-update-mirror.ps1 -Config <private-storage.json> `
  -PackageDirectory <original-tested-release> -Receipt <upload-receipt.json>
```

The uploader verifies the signed feed and every local patch first, refuses to
overwrite a different object, and verifies each public download's complete length
and SHA-256. It uses single-object uploads, not multipart completion. The current
automatic-update policy caps each patch at 10 MiB. This operation neither uploads
full installation archives nor changes the live version.

For subsequent releases, add `MirrorConfig` to the protected local publishing
client configuration, or pass `-MirrorConfig` to `publish-update-signed.ps1`.
Cloud verification completes before the publication request's 15-minute validity
window starts. The normal signed submission still sends and verifies the same
files at the origin. The origin retains them as fallback; CDN migration is not
permission to remove rollback files.

### One-time origin installation

Build and test the self-contained Linux publisher, then review and run
`scripts/install-update-mirror.sh <executable> <sha256> <public-mirror-base>`
with temporary administrator access. The restricted release channel cannot
change nginx, its executable or system services. The installer:

1. Saves the previous executable, protected configuration and nginx configuration.
2. Prepares routes only after every cloud body matches the signed live feed.
3. Changes only the v2 file location; the manifest stays at the origin.
4. Installs a bounded availability check once per minute.
5. Verifies that the current feed and all legacy-channel hashes are unchanged.
6. Restores the old executable/configuration and nginx on installation failure.

Inspect the live web mount and run nginx's configuration test before reload.
Revoke temporary administrator credentials after deployment verification.
Per-file route markers and the health flag live under `.mirror`, which is denied
over HTTP. A file redirects only when both checks exist. Failed health checks
remove the flag, making the unchanged original URLs serve their local files.
`?origin=1` explicitly bypasses cloud delivery. Redirects carry `no-store` so a
previously cached redirect cannot prevent an origin rollback.

Old clients follow HTTP redirects using their existing downloader. A cloud
outage can still affect them until the next health check, and a network failure
specific to one user is not detectable by the origin. The updated transport uses
its existing second-attempt allowance to request `?origin=1` when configured with
`OriginFallback`; it still requires the original signature, size and hash.
This client improvement takes effect only after a new application build.

Hosting-only migrations reuse the original signed feed and package acceptance.
Verify public redirect, origin fallback, Range/resume, and real published client
download/reconstruction separately; do not claim a new application version or
re-run game automation for a routing-only change. The desktop repair feature is
separate from this routing migration; already published applications retain their
existing behavior until they install a version containing the repair UI/helper.

### Migration validation · 2026-10-21

The initial cloud copy contains the 20 original, signed 1.4.13 patches for
1.4.3–1.4.12 in both editions: 40,072,636 bytes (38.22 MiB). Every public object
was downloaded and verified against the unchanged signed manifest.

The original 1.4.7 and 1.4.12 `Dustweave.Core` binaries each downloaded both
editions from the cloud with a 65,536-byte partial file, received HTTP 206, and
reconstructed all 221 Lite / 677 Portable files with matching hashes. The
installation directories were isolated test copies; the application and game
were not started.

Additional evidence includes 87 publishing-channel checks on Windows and Linux,
10 real nginx routing tests, and the affected update safety/delta/layout checks.
Evidence is retained outside the public repository in the parent workspace under
`artifacts/research/dustweave-qicaiyun-20261020/`.

**OTA delivery switched and verified on 2026-10-21.** The live v2 feed remains 1.4.13,
SHA-256 `6170e8ada51c5fe61eb5a3ec0d7fedd8155464aeb945213bbf402edcd59741c8`.
Twenty-four live checks verified the source redirects, origin byte ranges and
hidden route-state directory. The same published-client checks also passed through
the actual source URLs. The original publishing lane and feed are unchanged.

### Installation repair

The repair endpoint is separate from immutable delta directories:

- `/repair/vVERSION/Dustweave-VERSION-FLAVOR-repair.json` stays on the origin.
- `/repair/objects/SHA256.bin` redirects to the verified cloud object. The original
  bytes remain available with `?origin=1`.
- Every repair manifest has a domain-separated P-256 signature and binds its
  version, edition, runtime and full-archive digest to the signed release feed.
- Files are divided into at most 2 MiB blocks, deduplicated across both editions.
  Clients reuse healthy local blocks and validate the complete reconstructed file.

`prepare-repair-package.ps1` uses the original signed release archives and the
existing protected publisher key. `publish-repair-signed.ps1` verifies/uploads the
cloud objects, submits a short-lived signed request through the v2 account and
records its nonce. `dustweave-ota repair-index` exposes only immutable object names
and sizes; omitted origin objects are rehashed by the receiver. The publisher
checks both editions, stores all origin objects, verifies cloud bodies and exposes
the manifests last. It never changes `updates.json`.

Normal `publish-update-signed.ps1` publication with `MirrorConfig` now prepares
repair data before switching the release feed. A repair failure stops that switch;
query the saved repair nonce with the existing status command before retrying.
No shell, arbitrary path or arbitrary file-upload command is granted to the
publishing account.

See [INSTALLATION_REPAIR.md](INSTALLATION_REPAIR.md) for client behavior and recovery.

### Repair service deployment · 2026-10-21

The signed inventories for the original 1.4.13 Lite and Portable archives are
available. All 741 shared content blocks (278,241,699 bytes) passed both local
public-download verification and an independent origin-side download/hash check.
All 741 routes use cloud delivery with an origin fallback. No application
release or feed metadata was changed.

The final v2 publisher SHA-256 is
`632394d9529e67320cbeed91c0d062fd5a36653deba9420497972f9dc4a57799`.
Its previous executable and web configuration are retained by the scoped upgrade
procedure. The final Windows and Linux channel suite passed 108 checks; real
nginx validation passed 12 checks, and all 24 original live routing checks passed
again after deployment. The temporary administrator key was revoked and a fresh
connection with that key was denied. The restricted publication/status channel
remains operational and continues to reject arbitrary commands.

Actual 1.4.13 installation copies were damaged and repaired through the live
origin URLs using the new independent helper:

| Edition | Verified files | Damaged files | Downloaded bytes | Result |
| --- | ---: | ---: | ---: | --- |
| Lite | 221 | 3 | 2,711,809 | Repaired and startup acknowledged |
| Portable | 677 | 3 | 2,742,743 | Repaired and startup acknowledged |

Both cases resumed a partial block. The Lite case additionally injected a CDN
failure and verified recovery through the real origin. Account and plugin
sentinels survived. Separate packaged tests passed startup-failure rollback and
interrupted-transaction recovery; no game was connected or operated.

The client implementation also passed 241 targeted update, repair, scheduling
and user-text checks, plus light/dark UI checks in all three languages. It is
implemented in source but **not included in the already published 1.4.13
application**. A subsequent client release must publish its own signed repair
inventories before switching the feed; the normal publishing script now performs
that step. Do not distribute a modified client under the existing 1.4.13 version
or overwrite its immutable archives.
