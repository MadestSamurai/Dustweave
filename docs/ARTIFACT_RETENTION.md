# Release artifact retention

Run cleanup after the GitHub release, OTA publication and public download verification have all passed. A successful build alone is insufficient. Cleanup affects this repository's local `artifacts/` only; it never removes server or GitHub assets.

```powershell
./scripts/cleanup-release-artifacts.ps1 -Version X.Y.Z
# Review the returned plan: paths, sizes, reasons and skipped entries.
./scripts/cleanup-release-artifacts.ps1 -Version X.Y.Z -Apply -Plan <absolute-plan-path>
```

If the published candidate was retained in a differently named folder under `artifacts/releases`, pass `-PackageDirectory <absolute-folder>` to both commands. The same signed-feed, archive and public-verification gates apply; the override does not expand cleanup targets. With separate OTA lanes, preserve the legacy lane's active baselines independently of the new feed.

The default invocation only creates a plan. Apply rechecks the signed release feed, verified publication receipts, exact path boundaries, tracked source, running processes and file metadata. Changed targets, links and unrecognized layouts are skipped. Every deletion is recorded under `artifacts/maintenance/`; retain that audit with the release receipt.

| Files | Retention |
| --- | --- |
| Original full ZIPs and differential ZIPs | Keep the current release and three preceding published releases, including the full packages needed by active delta baselines; older local versions may be retired after an audited preview |
| Hashes, signed feeds, release receipts and input manifests | Preserve the release identity and evidence, including when the local package is retired |
| Extracted application folders | Keep the current release and three preceding releases for rollback and delta tests |
| Duplicate static-site staging | Remove only after verifying the original archives against the signed feed |
| Packaging compilation/cache/table copies | Remove recognized generated subdirectories; preserve checks, logs and input inventories |
| Successful synthetic and OTA test binaries | Remove EXE/DLL/PDB/ZIP/NUPKG copies; preserve result files and text evidence |
| Failed or unfinished tests, unknown folders | Keep for diagnosis |
| Source, captures, account/session data, credentials and user installations | Outside the cleanup scope |

Packaging seeds its compiler cache with at most two generations per component, instead of copying every historical generation. The compiler still validates assembly identity, client inputs and hashes; an omitted entry is rebuilt normally. The shared cache is not deleted by this script.

The workspace cleanup script above removes reproducible copies; it does not automatically retire complete historical releases. Retirement is a separate, reviewed operation: preserve metadata outside the version folder, protect current packages and active delta baselines, verify paths, source tracking, links and running processes, then remove the exact retired version directory. Do not rewrite signed release history or delete remote downloads as part of local retirement. The next delta build uses the retained full packages; unavailable older local baselines are already skipped by `new-update-deltas.ps1`.

Do not run cleanup concurrently with packaging or updater acceptance. The process check is a guard against existing users of a path, not a filesystem transaction or a lock on future processes. The plan's byte total is logical file size; measure free disk space before and after to report actual reclaimed storage.

## Synthetic test fixtures

`test.ps1` now removes fixture subdirectories after a successful synthetic run, keeping its root reports and logs. Use `-KeepFixtures` when inspecting those fixtures. Failed runs remain intact. Compaction requires a passed result with `realGameTouched=false` and `capturedFixturesIncluded=false`, refuses repositories and linked roots, unlinks nested reparse points without following them, and defers cleanup while a helper from that run is still active. `compacted.json` records removed file counts and logical bytes. Cleanup failure is reported separately from the test result.

合成测试通过后默认清理该次运行的临时副本，保留报告和日志；调试时可使用 `-KeepFixtures`。失败测试保留完整现场。清理仅限已验证成功的本仓库测试目录，不涉及用户数据、游戏采集或发行包。

## 发布后的固定步骤

每次 GitHub 与 OTA 发布验收完成后，生成并核对清理清单，再执行清理并记录结果。按用户 2026-10-12 的新保留要求，不再永久保留全部历史发行包：Dustweave 保留当前及前三个已发布版本的完整包、差分包和解压目录，并核对当前差分基线。更老版本可在单独核对清单后整版退役，发布清单、哈希、签名更新清单和验收记录迁出留档。现有脚本仍只清理可重建副本，历史整版退役需单独计划，不能假定已自动执行。失败检查、原始采集、账号数据和签名凭据不参与清理。

2026-10-12 首轮清理保留了 1.2.0、1.1.10-beta、1.1.9-beta、1.1.8-beta。后续发布按「当前及前三个版本」滚动保留，并重新核对差分基线。线上 GitHub 与 OTA 文件保持原样，本地退役不会改变用户已有的下载和更新地址。

其他工具和研究项目应使用各自核实过的保留规则，不要直接扩大本脚本的路径范围。历史文档中的验证记录仍然有效，但清理后的测试目录不再保证保有当时所有可执行副本；需要复现时使用保留的正式包或对应源码重新构建。

1.2.1 发布验收后，本地滚动保留范围已更新为 1.2.1、1.2.0、1.1.10-beta、1.1.9-beta。1.1.8-beta 的签名清单、校验值和构建记录已单独归档，其本地发行包退役；线上文件不变。清理记录见 [1.2.1 发布记录](releases/1.2.1-deployment.md)。
