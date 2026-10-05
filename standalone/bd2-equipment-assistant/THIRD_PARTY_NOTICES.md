# Third-party notices

BD2 Equipment Assistant includes these libraries under their respective licenses:

| Component | Version / origin | License |
| --- | --- | --- |
| HiGHS | 1.15.1, high-performance optimization solver | MIT, `licenses/HiGHS-MIT.txt`; source/build provenance in `vendor/highs/README.md` |
| SharpMonoInjector | Biney, vendored source | MIT, `licenses/SharpMonoInjector-MIT.txt` |
| Harmony / Lib.Harmony | 2.4.2, Andreas Pardeike | MIT, `licenses/Harmony-MIT.txt` |
| Mono.Cecil | 0.11.6, Jb Evain et al. | MIT, `licenses/Mono.Cecil-MIT.txt` |
| Roslyn | Microsoft.CodeAnalysis 4.14.0, .NET Foundation | MIT, `licenses/Roslyn-MIT.txt`; additional notices in `licenses/Roslyn-ThirdPartyNotices.rtf` |
| .NET runtime | Self-contained runtime selected by NuGet resolution | MIT and third-party notices in `licenses/dotnet-MIT.txt` and `licenses/dotnet-ThirdPartyNotices.txt` |

Managed dependency versions are recorded in committed `packages.lock.json` files. The HiGHS DLL is embedded in both editions and extracted to local application data. Portable also includes the .NET runtime; Lite requires the installed desktop runtime.

Reduced static game metadata is included for equipment names, options and resource costs. No game assemblies, images or account captures are distributed. Game content belongs to its respective rights holders and is not relicensed by this project's MIT license.
