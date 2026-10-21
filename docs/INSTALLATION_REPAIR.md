# Installation repair

The **Version updates → Check and repair** section checks distributed program
files against the signed inventory for the running application version. It does
not infer the edition from an overwritten package descriptor; the running
runtime determines Portable versus Lite.

1. **Check installation** identifies missing or changed files, including changes
   that preserve the file size.
2. **Download repairs** reconstructs affected files in the update cache, reusing
   healthy local blocks. Cancelling preserves reusable downloads and does not
   change the installation.
3. **Restart and repair** is available once preparation succeeds. The user must
   stop automation and close tool windows; a due scheduled run takes priority.
   The independent updater backs up affected files, applies the transaction and
   waits for startup acknowledgement. A failed startup restores the previous
   files. The stable launcher recovers an interrupted replacement.

This is a repair of the selected version, not an implicit upgrade. After repair,
normal update checks can prepare a newer release. Accounts, preferences, plugins
and unrecognized files are outside the repair inventory and are not deleted.
If the installation changes after inspection, preparation and the independent
helper recheck it before modifying anything.

## When the main window cannot open

Open `app/support/Dustweave.Updater.exe` directly. Close all Dustweave windows
first. The stable launcher remains byte-identical to previous layout-2 releases
so existing updaters can continue to apply differential updates.
The helper shows the installation location, identifies affected files and asks
again before replacing them. It has a cancellable progress window and does not
load application DLLs to perform the repair.

If the package descriptor is missing, the helper asks which edition to repair.
If its version cannot be found in the signed feed, the preview names the current
published version before any download or replacement. A missing .NET installation
still prevents a Lite helper from running; use Portable in that case. If both the
launcher and independent helper are damaged, extract an official full package.

## Trust and storage

Repair inventories use the `Dustweave-Repair-v1` signature domain. Each inventory
is bound to a release already authenticated by the normal update feed. Payloads
use SHA-256 addresses with blocks of at most 2 MiB. A bad CDN response triggers a
bounded origin retry, and neither response is accepted without size/hash checks.
Manifest signatures, full-file hashes and allowed paths are validated separately.

Repair staging and blocks live under the existing version/edition update-cache
directory. Existing storage leases protect downloads from cleanup or migration;
transaction journals protect pending replacement and rollback. Logs use the
normal `updates` diagnostics location. Source/cloud credentials are never
distributed with the application.

## Validation

Targeted tests cover healthy installations, missing files, equal-size corruption,
healthy-block reuse, resumed downloads, corrupt CDN responses, origin fallback,
signature and path rejection, immutable publication, omitted-block verification,
replacement interruption, rollback and preservation of user-owned files.

The desktop uses the existing light/dark themes and all three UI languages.
The standalone progress dialog is implemented with Windows Task Dialog controls;
see the [Microsoft Task Dialog reference](https://learn.microsoft.com/en-us/windows/win32/controls/task-dialogs).

The live service and end-to-end acceptance record are documented in
[the OTA deployment record](OTA_PUBLISH_CHANNEL.md#repair-service-deployment--2026-10-21).
The desktop entry and independent repair helper are included in 1.4.14.
