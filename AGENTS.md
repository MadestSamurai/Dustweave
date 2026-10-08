# Development rules

Dustweave is maintained directly in this repository. Use `src/` for the host, `assets/` for flow inputs, and `tests/Dustweave.Tests` for synthetic regressions. `Dustweave.slnx`, `build.ps1`, `test.ps1`, `check-source.ps1` and `package.ps1` are the canonical entry points. See `docs/ARCHITECTURE.md` and `docs/DEVELOPMENT.md`.

- Respect the calling task's explicitly required working directory. Root scripts locate their own sources without changing that directory.
- Use .NET and PowerShell; do not add Python runtime or required Python build/verification steps.
- Preserve account/session identity, user-data paths, the host control lock and shared IPC ownership across integrated tools.
- Keep external plugin implementations, account exports, credentials, captures and client assemblies outside this repository. Only the generic extension contract belongs here.
- Unknown consuming commands, confirmation dialogs, unrelated battles or changed account/process/cycle identities require evidence and guarded recovery; do not blindly repeat them.
- Independent tools under `standalone/` retain their attribution, licenses and release versions. Import reviewed changes; do not copy their user data.
- Keep source, package and real-game validation distinct. Do not start the game, inject/reload components, run automation, publish or deploy unless the user has authorized that action in the current task.
- Do not overwrite this tree by exporting the former research-project layout. New source inputs must be registered in `source-manifest.json`; new projects must also appear in `Dustweave.slnx`.
- During iteration, run affected test groups with `test.ps1 -Groups ...`. Do not repeat the entire suite for each component edit. Packaging defaults to scope selection; retain source/binary integrity checks and record any reused validation baseline. Expand checks only for changed shared boundaries, new failures, or explicit release requirements.
