# Dustweave signed full-package updates / 签名完整包更新

## Scope / 当前范围

The host uses its own small updater, without Velopack or binary deltas. The active source is
https://bd2.madsam.work/updates/dustweave/updates.json.
A public GitHub/Vultr update mirror is deferred; the existing private GitHub release archives the same packages. The transport compares multiple signed sources and can fail over; production currently configures only the domestic source. There is no empty international selector or repeated probe of a private repository.

目前只启用国内站点，下载完整包，允许跨过中间版本直接升级。定时执行、更新说明及空闲确认沿用主程序。
源码、账号、连接凭据与插件实现不进入静态更新目录。第一次必须手动安装带 OTA 的版本；更早版本不会凭空获得更新能力。

## Trust / 签名

assets/updates/trust.json contains only public ECDSA P-256 keys and HTTPS source addresses, embedded in Dustweave.Core.
The signed envelope contains Schema=1, KeyId, Algorithm=ECDSA-P256-SHA256, base64 Payload and base64 Signature.
The signature is the fixed 64-byte IEEE P1363 representation over exact UTF-8 payload bytes, using SHA-256.
Payload schema 2 contains Product, Channel and descending Releases, with Version/PreviousVersion/Layout/Notes/Assets.
Each asset has Flavor, FileName, Bytes and Sha256; file names are derived from version/flavor and cannot redirect to arbitrary URLs.

Both the downloader and independent installer verify the embedded trust root. Changing a ZIP and its digest in the manifest is insufficient without the signing key.
The signed manifest authenticates package bytes; this is **not Windows Authenticode**, and does not claim to remove SmartScreen prompts.

- scripts/new-update-signing-key.ps1 creates a current-user DPAPI-protected key outside the source repository, with a current-user directory ACL. It refuses to overwrite an existing key or trust configuration.
- Local signing uses %LOCALAPPDATA%/DustweavePublisher/update-signing.dpapi. Never copy this file into a package.
- CI can use the secret DUSTWEAVE_UPDATE_SIGNING_KEY (base64 PKCS#8); there is no automatic export of the local key and it must never be printed in logs.
- Back up the signing key securely before relying on OTA. A DPAPI file alone is not a portable backup across Windows accounts or machines. Signing-key loss requires an already trusted overlapping key or a manual reinstall.
- Rotation requires first releasing both old/new public keys, then switching the signer after users have received the overlap release. Do not replace the sole public key and expect old clients to accept it.

## Download / 下载

Small manifests are checked in parallel, with a 10-second source timeout. The highest valid version wins; conflicting hashes for one version are rejected.
A previously seen higher version cannot silently disappear. The last successful download route is remembered.
Only the selected package is downloaded. Interrupted bytes remain in a partial file, then resume with HTTP Range. A server ignoring Range restarts the output instead of appending. Bad hashes discard the partial file; the bounded retry can try another source.
Requests have header, inactivity and total timeouts. Package length, SHA-256, product/flavor and archive paths are checked.
Updates are independent from daily execution; failures become update status and diagnostics, never a queue failure.

## Replacement and recovery / 替换与恢复

1. Wait for idle confirmation. Stop accepting automation starts and close idle tool windows.
2. Copy the current single-file EXE to a per-attempt helper in the existing user data directory. Verify its job and signed manifest.
3. Wait for the original application to exit. Hold application and automation ownership locks while replacing files.
4. Extract only allowed application files. Back up **all** affected files durably before any replacement; persist backup hashes and a transaction journal.
5. Persist .dustweave-update.json in the application directory. Replace each file atomically on its own volume, with the executable last.
6. Start the new application with a per-attempt nonce. After normal window initialization it records a durable commit and acknowledges startup.
7. Failed startup attempts restore the prior files. On a later launch after interruption, the early bootstrap hands recovery to the preserved helper before normal UI or game components initialize. Repeated rollback is idempotent.

Account/settings directories and local plugins are outside the replacement allowlist. Files removed from an old application's signed package inventory can be retired, but arbitrary user files are never enumerated for deletion. Old packages with no inventory leave unknown files alone.
A backup failure prevents replacement. If recovery itself fails, preserve backups and report manual recovery instead of running a mixed installation.
This supports interrupted-process recovery; it cannot repair failing storage or guarantee durability against every device's hardware cache behavior.

## Publish / 发布步骤

1. Build/test both flavors with package.ps1; update-package.json lists exactly the packaged application files.
2. Run scripts/new-update-feed.ps1 -Version X.Y.Z -PackageDirectory <packages>, adding -PreviousFeed <downloaded-current-updates.json> after the first release. Previous manifests must verify with a trusted key.
3. Run scripts/prepare-update-site.ps1 -PackageDirectory <packages>. It verifies signatures/hashes and prepares only the public static payload.
4. Upload immutable vX.Y.Z/ files to the existing web server, verify remote lengths/hashes, then atomically rename the new updates.json **last**. Never overwrite a published version's files. Publish a new higher version to repair a release.
5. Download both flavors through the public HTTPS address and verify them; test upgrade from a prior packaged version before announcing availability.

The manually dispatched Prepare signed update feed workflow only creates a signed artifact from existing tested release packages. It creates no public repository/release and performs no deployment.
Runtime endpoints are independent from whether the source repository is public.

### Server configuration / 服务端配置

Use the website's existing HTTPS host. Map only /updates/dustweave/ to a dedicated static directory, not the source repository.
Disable directory listing. Serve updates.json with revalidation/no-cache and versioned packages with immutable caching; support HEAD and byte ranges.
A plain Nginx static location normally handles ranges; do not proxy archive bytes through the simulator API.
Set a suitable download bandwidth budget so updates do not crowd out the simulator. Keep older full packages as rollback/manual-download evidence until a separate retention decision.
No DNS, Nginx or production files are modified by the client build scripts.

## Acceptance / 验收边界

Synthetic coverage includes altered signatures, untrusted keys, stale/conflicting sources, retry/fallback, Range resume/ignore, hash failures, durable interrupted replacements, repeated rollback, retired files, and account/plugin preservation.
Packaged acceptance and public HTTPS deployment are separate results. An injected interruption is not a claim of a physical power-cut test.

### Verified locally · 2026-10-07 / 本机验证

Both Portable and Lite single-file candidates completed replacement and the real application's startup acknowledgement. A deliberately unlaunchable signed test package restored 0.8.17, and a persisted interruption recovered through the actual executable bootstrap. Test profiles had automatic checks disabled, no game connection and no real scheduled tasks. Backup and package checks preserved account/plugin sentinel files.

See `artifacts/ota-package-check-20261007-r3/results.json` and [repository status](STATUS.md) for the corresponding candidate and regression evidence. The 0.8.17 baseline needed an explicitly added descriptor in the isolated test because the installed older release has no OTA support; users still need one manual installation of the first OTA-enabled release.

The helper must close its application-mutex handle before launching the application, even after releasing ownership: earlier application versions use mutex creation as their single-instance signal. Upgrade, rollback and recovery all follow this rule.

The static payload preparation has also passed signature, size and digest checks. Public HTTPS range/cache behavior, operational key backup and an actual scheduled game queue remain deployment/runtime acceptance items. No production files or GitHub releases were changed by this validation.