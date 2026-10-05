# Independent synthetic regressions

This runner owns the synthetic cases under Cases/ in this repository, with a small set of explicit links to production shared sources. The cases originated in the parent research project. It executes the production .NET core against deterministic in-memory mailboxes, generated evidence and isolated temporary files. It does not connect to the game or contain account exports, captured inventories, replays or external plugin implementations.

The original complete parent test project remains intact. Its captured-screen oracle, user-report reproduction and original-client-data migration suites are not copied here. A passing synthetic run is not a claim that those separate suites or real gameplay passed. The source project list and results name every included test group.

Tests cover account slot/identity preservation, launch readiness, current connection and leases, queue/cycle recovery, tasks and settings, native cancellation, helper lifecycle, rule provenance, page navigation, dialogue/weekly-board flows, free draws, management, dispatch, mirror entry, trade calculation/execution boundaries, plugin contract and tool control. Plugin contract tests create harmless text fixtures; they do not include the private plugin.

The assembly keeps the existing BD2Daily.Tests internal-access identity so no production API is made public merely to test it. Cases and the runner now live together and build in one test directory. Source asset reads resolve DUSTWEAVE_TEST_SOURCE_ROOT for this independent repository; the caller's working directory is unchanged. Without that environment value, run from this repository root.

Run the independent repository's test.ps1. Evidence goes to a new artifacts/tests-* directory. CI uploads only the result summary and runner log. Test fixtures, build outputs and generated accounts are not source assets.

Weekly mission tests have two explicit inputs: the parent Run entry validates its current game table, while this runner calls RunSynthetic with generated semantic definitions. The latter checks progress and recovery behavior, not the contents of a shipped game table. A failed group writes results.json with its completed assertion count before exiting.
