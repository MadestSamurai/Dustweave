# Equipment Assistant dependency notices

This module is maintained as part of Dustweave and retains its original MIT attribution. See [LICENSE](LICENSE) and the host's [third-party notices](../../THIRD_PARTY_NOTICES.md).

| Component | License and source |
| --- | --- |
| HiGHS 1.15.1 | [MIT](vendor/highs/LICENSE.txt), [binary provenance](vendor/highs/provenance.json) |
| SharpMonoInjector | [MIT](../../licenses/SharpMonoInjector-MIT.txt), source in `vendor/SharpMonoInjector` |
| Harmony / Lib.Harmony 2.4.2 | [MIT](../../licenses/Harmony-MIT.txt) |
| Mono.Cecil 0.11.6 | [MIT](../../licenses/Mono.Cecil-MIT.txt) |
| Roslyn 4.14.0 | [MIT](../../licenses/Roslyn-MIT.txt), [third-party notices](../../licenses/Roslyn-ThirdPartyNotices.rtf) |
| .NET runtime | [MIT](../../licenses/dotnet-MIT.txt), [third-party notices](../../licenses/dotnet-ThirdPartyNotices.txt) |

Dependency locks remain in each project. Dustweave packaging shares the runtime and eligible resources across modules. Release packages retain the original equipment license filename `licenses/bd2-equipment-assistant-MIT.txt` and guide directory `docs/tools/bd2-equipment-assistant` for continuity; source ownership is `src/Equipment`.

Reduced static equipment metadata is included. No game assemblies, images or account captures are distributed. Game content remains the property of its respective rights holders.
