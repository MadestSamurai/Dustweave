# 1.3 distribution and updates

Status: implemented and locally validated on 2026-10-13. No deployment is implied by a local build.

## Package layout

Both flavors are complete directories. `Dustweave.exe` is a stable launcher; `Dustweave.dll` contains the WPF application. Tool assemblies and dependencies remain separate. Public names, account storage, process identity, control locks and IPC ownership are unchanged. The `support` directory holds an independent updater. Large resources live in `resources/shared/<SHA256>.bin`; owning assemblies embed small indexes. Identical Harmony and HiGHS payloads have one shared copy. Runtime-compiled game components still receive their original embedded dependencies.

Portable pins its app-local .NET desktop runtime to 8.0.30. Its independent updater carries a fixed runtime so it can restore a damaged installation. Lite uses an installed .NET 8 desktop runtime and has a small framework-dependent updater; its minimum framework is 8.0.0 with normal patch roll-forward. The package compatibility identity remains 8.0.30 for both flavors. Pinning makes builds reproducible; it is not a promise to ignore future runtime security fixes.

The launcher declares the same System.Collections.Immutable and System.Reflection.Metadata versions used by the application compiler. This prevents WPF from loading older framework copies before the application's dependency resolver is installed. Its generated WPF component URIs explicitly name the application assembly.

Builds disable the automatically appended Git revision and normalize source paths. Product version changes do not relabel independently imported tools. No trimming or obfuscation is applied to reflection-heavy components. Packaging is centralized in `scripts/publish-layout.ps1`; normal development builds retain embedded resources.

## Update policy

Layout 2 uses signed feed schema 3 and `dustweave-copy-v2` direct deltas. At most ten preceding release entries are eligible. A direct delta must match the exact installed version/flavor, remain at or below 8 MiB, and be smaller than 80% of the full archive. A layout/runtime change, unavailable baseline or oversized patch requires a manual complete download. There is no automatic full-package fallback in layout 2.

The new feed is `/updates/dustweave/v2/updates.json`. The old feed is not a migration bridge. Users on 1.2.x or older manually extract 1.3.0 into a new directory; saved accounts/settings stay in their established user-data directory. Domestic staging contains only the small signed feed and deltas. Full packages are first-install/manual-recovery downloads.

Delta generation compares extended exact matches, content-defined chunks and compressed literals, keeping the smallest representation. The DDW1 decoder retains 32 KiB command limits and reconstructs SHA-256-authorized output. Download preflight and installation independently check the exact baseline. Unchanged files are verified in place; only changed files are staged and backed up. Files removed from the old package inventory are retired; unrelated user files and plugins are preserved.

## Recovery

Before installation, the helper is copied into an isolated attempt directory. Its process does not load application assemblies. Backups and transaction state are flushed before destination writes. A launched application acknowledges healthy startup. A failed start restores the prior package.

The stable launcher inspects unfinished update markers before loading the business assembly. It checks the target, attempt ownership, nonce, paths and copied helper hash, then invokes independent recovery. Power-loss testing injects a durable interrupted transaction and corrupts a business DLL; it does not claim a physical power-cut test.

Do not change the launcher's runtime dependencies or bootstrap format within layout 2. Such a change needs a new layout/manual migration. Keep the launcher and updater versions independent from the product label.

## Validation

`UpdateLayout`, `UpdateDelta`, `UpdateSafety` and `SchedulingUpdates` cover parser, signature, direct-version policy, sparse updates and rollback. Full packaging separately checks real executables, three-language UI, each integrated tool, and offline component preparation against installed game assemblies without connecting to the game. Acceptance uses an isolated 1.3.0 acceptance baseline, the actual copied baseline helper, successful upgrades of both flavors, startup failure and interrupted-start recovery. Synthetic acceptance labels must never be published as ordinary release history.

The signed publishing service must be configured for the new feed lane before public deployment. Local construction does not modify the server, existing OTA feeds or user installations.

## Measured build and acceptance

The final local artifacts are in `artifacts/releases/1.3.0-ready2`. Lite is 20,981,318 bytes (20.01 MiB); Portable is 121,906,416 bytes (116.26 MiB). Lite was 31,915,962 bytes in 1.2.11, so the complete download is about 34% smaller. Portable's complete download is larger than the old 95,780,150-byte archive because its independent, self-contained recovery program adds about 30 MB compressed. This is an intentional first-install cost: unchanged recovery/runtime bytes are omitted from routine patches. Shared resources remove 29,361,664 bytes of duplicated uncompressed payload across tools.

Validation evidence:

- Full synthetic baseline: 3,626 checks passed; final affected groups: 287 checks passed.
- Both flavors: executable identity, normal UI, three languages and tool ownership/session checks passed. The full baseline additionally prepared all game components offline through the Portable package. A final launcher-only correction reused these checks after verifying every other package file was byte-identical.
- Publishing channel: 51 tests passed, including patch-only inventory, legacy-lane isolation, runtime mismatch and patch-size rejection.
- Real packaged update acceptance: Lite upgrade, Portable upgrade, failed business-assembly startup rollback, and interrupted startup recovery for both flavors passed. Every inventoried file was compared; account and plugin sentinels survived.
- A synthetic `1.3.0-acceptance` baseline produced 44,847-byte Lite and 71,163-byte Portable direct patches, changing five and six files respectively. This measures the working installation pipeline and version-only changes; it is **not** a promise that arbitrary future code changes have the same size. Earlier historical-change experiments are recorded separately under `artifacts/research/ota-layout-1.3.0`.
- The first real upgrade trial exposed a directory trailing-separator mismatch in launcher recovery ownership checking. After normalization, all five actual updater scenarios passed. The failed trial and final repair evidence are retained.

The final launcher repair rebuilt only `Dustweave.exe` and `Dustweave.Launcher.dll`; `launcher-repair2.json` records the comparison with the full validated package. `ota-acceptance.json` records the successful isolated updater cases. No live game, private plugin, registered scheduler task, public release or production OTA feed was touched.

Before the first public 1.3 deployment, initialize the separate `v2` lane with the signed 1.3.0 feed and configure the publishing service with `Layout: 2`, its own public directory and state directory. Keep the old lane unchanged. Subsequent signed publications carry deltas only; complete ZIPs remain manual-download assets. Synthetic acceptance feeds and baselines must never initialize a public lane.
