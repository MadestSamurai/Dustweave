# Third-party notices

Dustweave includes the components listed below. Their existing licenses and attribution remain in force independently of the host project's publication status.

| Component | Version / origin | License |
| --- | --- | --- |
| Shared compatibility engine | MadestSamurai/bd2-fishing, own project | MIT, `licenses/bd2-fishing-MIT.txt` |
| Weekly Sichuan solver and runtime | MadestSamurai/bd2-sichuan, own project | MIT, `licenses/bd2-sichuan-MIT.txt` |
| SharpMonoInjector | Biney, vendored source | MIT, `licenses/SharpMonoInjector-MIT.txt` |
| Harmony / Lib.Harmony | 2.4.2, Andreas Pardeike | MIT, `licenses/Harmony-MIT.txt` |
| Mono.Cecil | 0.11.6 | MIT, `licenses/Mono.Cecil-MIT.txt` |
| Roslyn | Microsoft.CodeAnalysis 4.14.0 | MIT plus third-party notices in `licenses/` |
| System.Collections.Immutable / System.Reflection.Metadata | 9.0.0 | MIT, .NET notices in `licenses/` |
| .NET linker tasks | 8.0.22 / 8.0.30, build-time dependency | MIT, .NET notices in licenses/ |
| .NET runtime | Portable package only | MIT plus third-party notices in `licenses/` |
| HiGHS | 1.15.1, Windows x64 shared library with statically linked MSVC runtime | MIT, `licenses/HiGHS.txt` in packages; source/API provenance in `third-party/highs/` in the source repository |

Dependency versions are recorded in `packages.lock.json`. No game assemblies, account sessions, captured inventories or replays are bundled. No source from ok-bd2 is included.

No Python runtime, NumPy, SciPy or PyInstaller component is distributed.

## Integrated open-source tools

The tool menu embeds the original .NET applications from MadestSamurai's
`bd2-fishing`, `bd2-sichuan`, `bd2-rhythm`, `bd2-territory`,
`bd2-equipment-assistant`, `bd2-apostle-defense`, `bd2-infinite-gacha`,
`bd2-secret-vision`, and `bd2-fiend-hunter` repositories. Their MIT license texts
are distributed as `licenses/<repository>-MIT.txt`; their bilingual manuals and
source/risk notices are in `docs/tools/<repository>`. All original author/source
notices in those applications remain intact. Version metadata is in `tools.json`.

The equipment tool also embeds its MIT-licensed HiGHS binary; its source project's
license notice is retained. The applications share the packaged .NET and Roslyn
dependencies rather than carrying separate copies of their runtime environments.
