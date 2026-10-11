# Release artifact retention

Run cleanup after the GitHub release, OTA publication and public download verification have all passed. A successful build alone is insufficient. Cleanup affects this repository's local `artifacts/` only; it never removes server or GitHub assets.

```powershell
./scripts/cleanup-release-artifacts.ps1 -Version X.Y.Z
# Review the returned plan: paths, sizes, reasons and skipped entries.
./scripts/cleanup-release-artifacts.ps1 -Version X.Y.Z -Apply -Plan <absolute-plan-path>
```

The default invocation only creates a plan. Apply rechecks the signed release feed, verified publication receipts, exact path boundaries, tracked source, running processes and file metadata. Changed targets, links and unrecognized layouts are skipped. Every deletion is recorded under `artifacts/maintenance/`; retain that audit with the release receipt.

| Files | Retention |
| --- | --- |
| Original full ZIPs, differential ZIPs, hashes and signed feeds | Preserve all published versions locally |
| Extracted application folders | Keep the current release and three preceding releases for rollback and delta tests |
| Duplicate static-site staging | Remove only after verifying the original archives against the signed feed |
| Packaging compilation/cache/table copies | Remove recognized generated subdirectories; preserve checks, logs and input inventories |
| Successful synthetic and OTA test binaries | Remove EXE/DLL/PDB/ZIP/NUPKG copies; preserve result files and text evidence |
| Failed or unfinished tests, unknown folders | Keep for diagnosis |
| Source, captures, account/session data, credentials and user installations | Outside the cleanup scope |

Packaging seeds its compiler cache with at most two generations per component, instead of copying every historical generation. The compiler still validates assembly identity, client inputs and hashes; an omitted entry is rebuilt normally. The shared cache is not deleted by this script.

Do not run cleanup concurrently with packaging or updater acceptance. The process check is a guard against existing users of a path, not a filesystem transaction or a lock on future processes. The plan's byte total is logical file size; measure free disk space before and after to report actual reclaimed storage.

## 发布后的固定步骤

每次 GitHub 与 OTA 发布验收完成后，生成并核对清理清单，再执行清理并记录结果。保留全部正式压缩包、最近四版解压目录、测试结果和日志；仅删除已识别且可重建的副本。失败检查、原始采集、账号数据和签名凭据不参与清理。

其他工具和研究项目应使用各自核实过的保留规则，不要直接扩大本脚本的路径范围。历史文档中的验证记录仍然有效，但清理后的测试目录不再保证保有当时所有可执行副本；需要复现时使用保留的正式包或对应源码重新构建。
