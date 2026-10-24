# Development rules

Dustweave is maintained directly in this repository. Use `src/` for the host, `assets/` for flow inputs, and `tests/Dustweave.Tests` for synthetic regressions. `Dustweave.slnx`, `build.ps1`, `test.ps1`, `check-source.ps1` and `package.ps1` are the canonical entry points. See `docs/ARCHITECTURE.md` and `docs/DEVELOPMENT.md`.

- Respect the calling task's explicitly required working directory. Root scripts locate their own sources without changing that directory.
- Use .NET and PowerShell; do not add Python runtime or required Python build/verification steps.
- Preserve account/session identity, user-data paths, the host control lock and shared IPC ownership across integrated tools.
- Keep external plugin implementations, account exports, credentials, captures and client assemblies outside this repository. Only the generic extension contract belongs here.
- Unknown consuming commands, confirmation dialogs, unrelated battles or changed account/process/cycle identities require evidence and guarded recovery; do not blindly repeat them.
- All integrated tools are owned modules under `src/`. Preserve their attribution and licenses, stable IPC identities and pending transaction journals. Import only validated preferences and explicitly recognized logs; never copy account credentials.
- Keep source, package and real-game validation distinct. Do not start the game, inject/reload components, run automation, publish or deploy unless the user has authorized that action in the current task.
- When opening a real host window from a development execution environment, use the existing `--launch-desktop` entry (or the validated `DesktopGameLaunch` relay for explicit account-check arguments). Do not directly start the normal host with `Start-Process`: the development environment can expose a different HKCU store despite the same Windows SID and registry path. Synthetic smoke windows keep their isolated test context. See `docs/DEVELOPMENT.md`, Desktop startup.
- Do not overwrite this tree by exporting the former research-project layout. New source inputs must be registered in `source-manifest.json`; new projects must also appear in `Dustweave.slnx`.
- During iteration, run affected test groups with `test.ps1 -Groups ...`. Do not repeat the entire suite for each component edit. Packaging defaults to scope selection; retain source/binary integrity checks and record any reused validation baseline. Expand checks only for changed shared boundaries, new failures, or explicit release requirements.
- Run release acceptance through reproducible scripts. Do not use interactive computer-use clicks to rescue test windows. Separate update/startup assertions from fixture teardown; dispose only exact test-owned processes after recording their results, and preserve failed evidence.
- After each successfully verified release, follow `docs/ARTIFACT_RETENTION.md`: preview and review the local cleanup plan, apply it, and retain its audit. Preserve original published archives, current plus three prior extracted releases, and validation evidence. Never expand cleanup to accounts, captures, credentials or research data based only on age or size.
