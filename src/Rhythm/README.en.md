# Rhythm · Dustweave

[简体中文](README.md) · English · [Back to Dustweave](../../README.en.md)

Rhythm is maintained inside Dustweave. The original repository is archived; use [Dustweave](https://github.com/MadestSamurai/Dustweave) for source, downloads and issues.

## Use

Open **Tools → Rhythm**. The window appears before the shared connection is prepared, with cancellation and retry. Connecting does not enable playback. Click **Enable auto-play**, then choose a song and difficulty in the game.

- Jitter defaults to **±50 ms**, range 0–200 ms. Set 0 to disable it.
- Calibration supports **−500–500 ms**: positive delays input, negative advances it. Changes apply to the next song.
- Supports taps, holds, slides, repeated notes and Fever. Pausing retains the timeline; enabling mid-song skips past notes.
- Stop or close the window to end input. Preparation and playback share the host control lock; idle windows do not reserve automation.
- Uses the host language and theme. Does not select songs, purchase attempts, restart rounds or modify scores, health or the clock. Perfect scores are not guaranteed.

## Storage and source

Preferences live at `tools/rhythm/settings.json` under the host data directory. Valid legacy timing preferences are imported once; old commands and identities are not imported. Known logs move to `tools/rhythm/diagnostics` with original timestamps, joining host export and retention. Caches use host storage and relocation controls.

- [desktop](desktop): window, playback status and settings.
- [core](core) · [shared](shared): control, chart plans and storage.
- [compatibility](compatibility) · [hook](hook) · [ipc](ipc): adaptation and transport.
- [localization](localization) · [vendor](vendor): translations and dependencies.

Use root `build.ps1`, `test.ps1` and `package.ps1`. Projects use `Dustweave.Rhythm.*` names, retaining assembly and transport identities. Checks: `Rhythm`, `UnifiedSuite`, `ToolMenu`, `ActivityLease`; UI: `tool-preparation` and `--tool rhythm --smoke`. These do not operate a real game.

Attribution to MadestSamurai and the [MIT license](LICENSE) are retained. See [third-party notices](THIRD_PARTY_NOTICES.md).
