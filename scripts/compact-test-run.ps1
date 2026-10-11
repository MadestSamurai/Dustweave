param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$area=Join-Path $root 'artifacts'
$target=[IO.Path]::GetFullPath($Output)
if((Split-Path $target -Parent) -ne $area -or (Split-Path $target -Leaf) -notmatch '^tests-\d{8}-\d{6}-\d+$'){
 throw 'Only a repository-local synthetic test run may be compacted.'
}
for($parent=$target;$parent;$parent=[IO.Path]::GetDirectoryName($parent)){
 if((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked test root.'}
}
$report=Get-Content -LiteralPath (Join-Path $target 'results.json') -Raw|ConvertFrom-Json
if($report.status -ne 'passed' -or $report.realGameTouched -ne $false -or $report.capturedFixturesIncluded -ne $false){
 throw 'Unsuccessful or non-synthetic test evidence must be preserved.'
}
# The runner has exited before calling this script. Keep all root reports and logs;
# only fixture directories from this exact successful run may be removed.
$directories=@(Get-ChildItem -LiteralPath $target -Directory -Force)
$pending=[Collections.Generic.Stack[string]]::new()
foreach($directory in $directories){$pending.Push($directory.FullName)}
$links=[Collections.Generic.List[string]]::new()
$files=0L;$bytes=0L
while($pending.Count){
 $path=$pending.Pop();$entry=Get-Item -LiteralPath $path -Force
 if($entry.Name -eq '.git'){throw 'Repository found in test output; preserve it for inspection.'}
 if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){$links.Add($path);continue}
 foreach($child in Get-ChildItem -LiteralPath $path -Force){
  if($child.Name -eq '.git'){throw 'Repository found in test output; preserve it for inspection.'}
  if($child.Attributes -band [IO.FileAttributes]::ReparsePoint){$links.Add($child.FullName)}
  elseif($child.PSIsContainer){$pending.Push($child.FullName)}
  else{$files++;$bytes+=$child.Length}
 }
}
foreach($process in Get-CimInstance Win32_Process){
 if($process.ExecutablePath -and $process.ExecutablePath.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){
  throw 'Test helper is still running; keep its fixtures.'
 }
}
# Unlink reparse points explicitly; never traverse into their targets.
foreach($link in $links){Remove-Item -LiteralPath $link -Force}
foreach($directory in $directories){
 if((Test-Path -LiteralPath $directory.FullName)){
  if((Split-Path $directory.FullName -Parent) -ne $target){throw 'Unexpected fixture path.'}
  Remove-Item -LiteralPath $directory.FullName -Recurse -Force
 }
}
@{status='passed-fixtures-retired';atUtc=[DateTime]::UtcNow.ToString('o');removedFiles=$files;removedBytes=$bytes;unlinked=$links.Count}|
 ConvertTo-Json|Set-Content -LiteralPath (Join-Path $target 'compacted.json') -Encoding utf8
