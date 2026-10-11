# Connection polling and development artifacts

## Measured connection reads

A normal Windows game process was already connected. The measurement ran 100 reads of each operation; it did not execute daily tasks.

| Operation | Previous mean | Bound-process mean |
| --- | ---: | ---: |
| Game snapshot | 8.33 ms | 0.239 ms |
| Runtime status | 7.47 ms | 0.223 ms |

Process discovery itself averaged 7.41 ms. The former combined callers discovered the same process repeatedly before reading the already-bound pipe. Snapshot allocations across 100 reads fell from approximately 10 MB to 1.09 MB; status reads fell from approximately 9 MB to 0.59 MB.

`IGameHost` now accepts the process already checked by the caller. Pipe responses must still match its PID and start time. The public convenience methods continue to discover a process when the caller has none. Hosted tool activity checks retain their account and process guards. Main-window connection refreshes run in the background with a single in-flight refresh; late results cannot update a closed or parallel workspace.

These numbers describe connection-read overhead, not whole-program or game speed. No battle calculation or account check was removed.

## Preventing synthetic fixture growth

Successful synthetic test runs now retain root reports and runner logs but retire generated fixture directories after the runner exits. `test.ps1 -KeepFixtures` preserves them for inspection. Failed runs and captured/live-game evidence remain untouched.

The compactor accepts only a direct repository `artifacts/tests-<date>-<time>-<id>` directory with a successful synthetic report. It rejects linked roots, embedded repositories and active helpers. Nested links are unlinked without traversing their targets. Checks cover successful compaction, preserved failure evidence, embedded repositories and an external junction target.

This avoids accumulating copied packages and thousands of generated files on each passing test. It does not clean user accounts, captures, application logs or private research.
