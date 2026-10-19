# Fiend Hunter · Dustweave

[简体中文](README.md) · English · [Back to Dustweave](../../README.en.md)

Fiend Hunter is maintained in Dustweave. The archived standalone repository retains historical versions; use [Dustweave](https://github.com/MadestSamurai/Dustweave) for downloads and issues.

Open **Tools → Fiend Hunter** and wait for the shared connection. Choose Berserker or Fiery Tiger, then start a solo Very Hard round with Blade. An ongoing round with the same configuration can be resumed. The tool attacks, guards, dodges and heals, then stops after settlement. Pausing automation leaves the game running.

The separate window follows Dustweave's language, theme, loading feedback and shared control lock. Opening it never starts combat. No old component is polled during preparation; exiting the game or changing accounts stops input.

## Source and storage

- [desktop](desktop): monster selection, combat status and controls.
- [shared](shared) · [core](core): policy, control contract and desktop client.
- [compatibility](compatibility) · [hook](hook) · [ipc](ipc): version adaptation, in-game execution and shared transport.
- Compiled modules use the host's `suite-cache/modules/fiend-hunter` and follow the selected cache location.
- Preferences and diagnostics use `tools/fiend-hunter` in the host data directory. Logs participate in host export, accounting and retention. Known legacy logs are moved on opening the tool while retaining their age; account and communication state remain untouched.

Use `build.ps1`, `test.ps1` and `package.ps1` at the repository root. All three `Dustweave.FiendHunter.*` projects belong to the main solution; existing assembly and transport identities remain compatible with the host loader.

Original MadestSamurai attribution and the [MIT license](LICENSE) are retained. See [third-party notices](THIRD_PARTY_NOTICES.md).
