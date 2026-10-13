# Sandbox association recovery

The original `Dustweave_<account-key-prefix>` directory remains the stable account location. The actual Sandboxie section name is stored in `instance.json`; renaming a box does not move any files. Parallel worker status and diagnostic discovery use the stable directory, while process checks and launch/export commands use the registered name.

Before importing or collecting credentials, recovery compares the configured file root, inner account binding, and decryptable saved account identity. A unique candidate is selected automatically. The Accounts repair dialog also lists nonmatching boxes and their reasons; only verified candidates may be selected manually. A second confirmation names the account and box. No registry edits, account resets or sandbox deletion are part of this repair.

All aliases pointing at the account root must have no running processes before a binding is changed. Shared/breakout settings are rejected. Each JSON replacement is atomic; if interrupted between the inner binding and host index writes, the next recovery validates the same root/account and completes the rename. Explicit ownership conflicts are preserved for diagnosis. Credential contents and original import timestamps are never rewritten by binding repair.

Enumeration uses Sandboxie's UTF-16 `SbieApi_EnumBoxesEx`, matching the upstream [SbieIni section enumeration](https://github.com/sandboxie-plus/Sandboxie/blob/master/Sandboxie/apps/ini/query.c), rather than relying on console code pages. Process ownership remains driver-reported.

1.3.2 verification includes isolated encrypted synthetic account fixtures, interruption recovery, ambiguous/manual selection, wrong-owner/root rejection, and three-language light/dark WPF checks. Native installed-box enumeration is read-only. No real game or user account was renamed during release validation.

The 1.3.1 window and executable both returned valid DW icons locally; the missing taskbar icon was not reproduced in that sample. The new window explicitly sets its shell identity and relaunch icon resource, and verification checks decoded pixels, native window icon and shell properties. Old pinned shortcuts may retain Windows cached metadata and can be repinned by the user; the application does not delete unrelated shell caches.
