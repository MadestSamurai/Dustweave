# Signed OTA publishing channel

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
