# Dependency inventory / 依赖清单

This inventory was checked against the restored project assets and NuGet package metadata on 2026-10-05. The lockfiles remain the exact dependency record. Native/client adaptation is generated on the user's machine; game assemblies are not copied into this source tree.

| Component | Version | Notice/source retained |
| --- | --- | --- |
| Lib.Harmony | 2.4.2 | ../licenses/Harmony-MIT.txt |
| Mono.Cecil | 0.11.6 | ../licenses/Mono.Cecil-MIT.txt |
| Microsoft.CodeAnalysis.Common / CSharp | 4.14.0 | Roslyn-MIT.txt and Roslyn-ThirdPartyNotices.rtf in the same licenses directory |
| Microsoft.CodeAnalysis.Analyzers | 3.11.0 | MIT; Roslyn notices |
| System.Collections.Immutable / Reflection.Metadata | 9.0.0 | MIT; .NET notices |
| Microsoft.NET.ILLink.Tasks | 8.0.22 and 8.0.30 | MIT; build-time .NET component, separate module targets retain their required version |
| .NET 8 desktop runtime | Portable flavor only | dotnet-MIT.txt and dotnet-ThirdPartyNotices.txt |
| SharpMonoInjector | vendored source | LICENSE.txt retained under each source vendor directory |
| HiGHS | 1.15.1 | ../third-party/highs/LICENSE.txt and provenance.json; Windows shared library with statically linked MSVC runtime |
| Nine integrated tools | each source declares its own version | standalone/bd2-*/LICENSE and THIRD_PARTY_NOTICES.md |

所有已恢复NuGet依赖的许可证元数据已核对为MIT，Harmony以包内LICENSE文件声明。现有许可证原文仍保留，不能用本表代替。九个工具的源码版本不表示对应改动已发布到其独立GitHub仓库。主体采用 MIT 许可证，见根目录 LICENSE。

The exported repository does not contain code from ok-bd2. Standalone modules retain their own layout and attribution; the daily host follows the layout documented in ARCHITECTURE.md. Public-release review must also inspect future source additions, package contents, generated assets and history; this inventory describes the current inputs only.
