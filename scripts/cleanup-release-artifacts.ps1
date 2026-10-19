param(
 [Parameter(Mandatory)][string]$Version,
 [switch]$Apply,
 [string]$Plan,
 [string]$PackageDirectory,
 # Extracted copies only. Keep full ZIPs for the legacy window and every 1.4.7+ baseline.
 [ValidateRange(4,11)][int]$KeepReleases=4
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts=Join-Path $root 'artifacts'
$prefix=$artifacts+[IO.Path]::DirectorySeparatorChar
if((Get-Item -LiteralPath $artifacts -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Artifacts root must not be a link.'}
if($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$'){throw 'Invalid release version.'}
$releaseRoot=Join-Path $artifacts ('releases/'+$Version)
if($PackageDirectory){
 $releaseRoot=[IO.Path]::GetFullPath($PackageDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
 $releasesPrefix=(Join-Path $artifacts 'releases')+[IO.Path]::DirectorySeparatorChar
 if(!$releaseRoot.StartsWith($releasesPrefix,[StringComparison]::OrdinalIgnoreCase) -or
    (Get-Item -LiteralPath $releaseRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Package directory must be an ordinary folder under artifacts/releases.'}
}
$delivery=Join-Path $artifacts ('deployments/'+$Version)
# Publication alone is insufficient: require the completed public download gate.
$public=Get-Content -LiteralPath (Join-Path $delivery 'public-verification.json') -Raw|ConvertFrom-Json
$github=Get-Content -LiteralPath (Join-Path $delivery 'github-release.json') -Raw|ConvertFrom-Json
if($public.status -ne 'passed' -or $public.version -ne $Version -or !$public.signatureVerified -or !$public.feedMatched -or
 $github.isDraft -ne $false -or $github.isPrerelease -ne $false -or $github.tagName -ne ('v'+$Version)){throw 'Complete GitHub and public OTA verification before cleanup.'}
$signed=Get-Content -LiteralPath (Join-Path $releaseRoot 'updates.json') -Raw|ConvertFrom-Json
$trust=Get-Content -LiteralPath (Join-Path $root 'assets/updates/trust.json') -Raw|ConvertFrom-Json
$key=[Security.Cryptography.ECDsa]::Create()
try {
 $key.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($trust.Keys.($signed.KeyId)),[ref]([int]0))
 $payload=[Convert]::FromBase64String($signed.Payload)
 if(!$key.VerifyData($payload,[Convert]::FromBase64String($signed.Signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)){throw 'Invalid release feed signature.'}
 $feed=[Text.Encoding]::UTF8.GetString($payload)|ConvertFrom-Json
}finally{$key.Dispose()}
if($feed.Product -ne 'Dustweave' -or $feed.Releases[0].Version -ne $Version){throw 'Release feed mismatch.'}
if((Get-FileHash (Join-Path $releaseRoot 'updates.json')).Hash -ne (Get-FileHash (Join-Path $delivery 'public-updates.json')).Hash){throw 'Verified public feed differs.'}
$keep=@($feed.Releases|Select-Object -First $KeepReleases|ForEach-Object Version)
$allPublished=@($feed.Releases|ForEach-Object Version)
$archiveSafe=@{}
foreach($release in $feed.Releases){
 $v=$release.Version;$archiveSafe[$v]=$true
 $extended=if($v -eq $Version){@($feed.BaselineDeltas)}else{@()}
 foreach($asset in @($release.Assets)+@($release.Deltas)+$extended){
  if(!$asset){continue}
  $path=if($v -eq $Version){Join-Path $releaseRoot $asset.FileName}else{Join-Path $artifacts ('releases/'+$v+'/'+$asset.FileName)}
  if(!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -ne $asset.Bytes -or (Get-FileHash -LiteralPath $path).Hash -ne $asset.Sha256){$archiveSafe[$v]=$false;break}
 }
}
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
public static class ReleaseCleanupTree {
 public static FileInfo[] Files(string path) {
  var files=new List<FileInfo>();var dirs=new Stack<DirectoryInfo>();dirs.Push(new DirectoryInfo(path));
  while(dirs.Count>0){var dir=dirs.Pop();if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked directory: "+dir.FullName);
   foreach(var item in dir.EnumerateFileSystemInfos()){
    if((item.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked entry: "+item.FullName);
    if(item is DirectoryInfo child)dirs.Push(child);else if(item is FileInfo file)files.Add(file);
   }
  }return files.ToArray();
 }
}
'@
$processes=@(Get-CimInstance Win32_Process|Where-Object ProcessId -ne $PID)
$tracked=@(git -C $root ls-files);if($LASTEXITCODE -ne 0){throw 'Cannot establish source boundary.'}
$tracked=@($tracked|Where-Object {$_ -like 'artifacts/*'})
$passedReports=@{}
function Safe([string]$path,[switch]$IgnoreProcess){
 $full=[IO.Path]::GetFullPath($path)
 if(!$full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw "Path escaped artifacts: $full"}
 for($p=$full;$p.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase);$p=[IO.Path]::GetDirectoryName($p)){
  if((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Linked path: $p"}
 }
 $relative=[IO.Path]::GetRelativePath($root,$full).Replace('\','/')
 if(@($tracked|Where-Object {$_ -eq $relative -or $_.StartsWith($relative+'/',[StringComparison]::OrdinalIgnoreCase)}).Count){throw "Tracked source under cleanup target: $full"}
 if(!$IgnoreProcess){foreach($process in $processes){
  if(($process.ExecutablePath -and ($process.ExecutablePath -eq $full -or $process.ExecutablePath.StartsWith($full+'\',[StringComparison]::OrdinalIgnoreCase))) -or
   ($process.CommandLine -and $process.CommandLine.IndexOf($full,[StringComparison]::OrdinalIgnoreCase) -ge 0)){throw "Target is used by process $($process.ProcessId): $full"}
 }}
 return $full
}
function Stamp($files){
 $hash=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
 [long]$bytes=0
 try{foreach($f in @($files|Sort-Object FullName)){$bytes+=$f.Length;$hash.AppendData([Text.Encoding]::UTF8.GetBytes($f.FullName+"`n"+$f.Length+"`n"+$f.LastWriteTimeUtc.Ticks+"`n"))};return @{bytes=$bytes;files=@($files).Count;stamp=[Convert]::ToHexString($hash.GetHashAndReset())}}finally{$hash.Dispose()}
}
function Allowed($row){
 $r=[IO.Path]::GetRelativePath($artifacts,$row.path).Replace('\','/')
 if($row.kind -eq 'directory'){
  if($r -match '^package-(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)-[a-f0-9]{32}/(cold-bootstrap|drop-tables|tables|locks|unified-suite-daily|evidence-configs-daily|live-client-daily|tool-equipment-equipment|(?:Portable|Lite)-live-self-test-daily)$'){return [semver]$Matches[1] -le [semver]$Version}
  if($r -match '^releases/([^/]+)/(Portable|Lite|site)$'){
   $v=$Matches[1];return $v -in $allPublished -and $archiveSafe[$v] -and ($Matches[2] -eq 'site' -or $v -notin $keep)
  }
  return $false
 }
 if($row.kind -eq 'file' -and $r -match '^(ota-[^/]+|tests-\d{8}-\d{6}-\d+)/.+\.(exe|dll|pdb|zip|nupkg)$'){
  $test=Join-Path $artifacts $Matches[1];$report=Join-Path $test 'results.json'
  if(!$passedReports.ContainsKey($report)){$passedReports[$report]=(Test-Path -LiteralPath $report) -and (Get-Content -LiteralPath $report -Raw|ConvertFrom-Json).status -eq 'passed'}
  return $passedReports[$report]
 }
 return $false
}
$warnings=[Collections.Generic.List[object]]::new()
if(!$Apply){
 if($Plan){throw 'Plan path is output-only when generating a new plan.'}
 $out=Join-Path $artifacts ('maintenance/release-'+$Version+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
 [IO.Directory]::CreateDirectory($out)|Out-Null
 $Plan=Join-Path $out 'plan.json'
 $candidates=[Collections.Generic.List[object]]::new()
 foreach($work in Get-ChildItem -LiteralPath $artifacts -Directory -Filter 'package-*'){
  if($work.Name -notmatch '^package-(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)-[a-f0-9]{32}$' -or [semver]$Matches[1] -gt [semver]$Version){continue}
  if(!(Test-Path -LiteralPath (Join-Path $work.FullName 'source-before.json'))){continue}
  foreach($child in Get-ChildItem -LiteralPath $work.FullName -Directory){
   $row=@{path=$child.FullName;kind='directory';reason='Reproducible packaging workspace; logs and checks remain'}
   if(Allowed $row){$candidates.Add($row)}
  }
 }
 foreach($v in $allPublished){foreach($name in @('Portable','Lite','site')){
  $row=@{path=Join-Path $artifacts ('releases/'+$v+'/'+$name);kind='directory';reason='Published package copy; original signed archives remain'}
  if((Test-Path -LiteralPath $row.path) -and (Allowed $row)){$candidates.Add($row)}
 }}
 foreach($dir in Get-ChildItem -LiteralPath $artifacts -Directory|Where-Object {$_.Name -match '^(ota-|tests-\d{8}-\d{6}-\d+)'} ){
  $report=Join-Path $dir.FullName 'results.json'
  if(!(Test-Path -LiteralPath $report) -or (Get-Content -LiteralPath $report -Raw|ConvertFrom-Json).status -ne 'passed'){continue}
  try{[void](Safe $dir.FullName);$files=[ReleaseCleanupTree]::Files($dir.FullName)}catch{$warnings.Add(@{path=$dir.FullName;reason=$_.Exception.Message});continue}
  foreach($file in $files|Where-Object Extension -in @('.exe','.dll','.pdb','.zip','.nupkg')){
   $candidates.Add(@{path=$file.FullName;kind='file';reason='Binary copy in completed synthetic/OTA check; text evidence remains'})
  }
 }
 $rows=[Collections.Generic.List[object]]::new()
 foreach($candidate in $candidates){
  try{
   [void](Safe $candidate.path)
   if(!(Allowed $candidate)){throw 'Target not allowlisted'}
   $files=if($candidate.kind -eq 'directory'){[ReleaseCleanupTree]::Files($candidate.path)}else{@(Get-Item -LiteralPath $candidate.path)}
   $stamp=Stamp $files
   if($stamp.files){$rows.Add(@{path=$candidate.path;kind=$candidate.kind;reason=$candidate.reason;bytes=$stamp.bytes;files=$stamp.files;stamp=$stamp.stamp})}
  }catch{$warnings.Add(@{path=$candidate.path;reason=$_.Exception.Message})}
 }
 $data=[ordered]@{schema=1;root=$root;version=$Version;keep=$keep;atUtc=[DateTime]::UtcNow.ToString('o');bytes=($rows|Measure-Object bytes -Sum).Sum;files=($rows|Measure-Object files -Sum).Sum;targets=$rows.ToArray();skipped=$warnings.ToArray()}
 $data|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $Plan -Encoding utf8
 @{plan=$Plan;targets=$rows.Count;bytes=$data.bytes;files=$data.files;skipped=$warnings.Count;keep=$keep}|ConvertTo-Json -Depth 4
 exit
}
if(!$Plan){throw 'Apply requires the previously reviewed plan path.'}
$Plan=[IO.Path]::GetFullPath($Plan)
if(!$Plan.StartsWith((Join-Path $artifacts 'maintenance')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Plan must be inside artifacts/maintenance.'}
[void](Safe $Plan -IgnoreProcess)
$planData=Get-Content -LiteralPath $Plan -Raw|ConvertFrom-Json
if($planData.schema -ne 1 -or $planData.root -ne $root -or $planData.version -ne $Version -or @((Compare-Object @($planData.keep) $keep)).Count){throw 'Plan does not match current retention boundary.'}
$result=Join-Path (Split-Path -Parent $Plan) 'result.json'
if(Test-Path -LiteralPath $result){throw 'Preserve previous cleanup evidence; create a new plan.'}
$audit=Join-Path (Split-Path -Parent $Plan) 'deleted.jsonl'
$writer=[IO.StreamWriter]::new($audit,$false,[Text.UTF8Encoding]::new($false))
[long]$deletedBytes=0;[long]$deletedFiles=0;$count=0
$lastProcessRefresh=[DateTime]::MinValue
try{foreach($row in $planData.targets){
 try{
  if(([DateTime]::UtcNow-$lastProcessRefresh).TotalSeconds -ge 10){$processes=@(Get-CimInstance Win32_Process|Where-Object ProcessId -ne $PID);$passedReports.Clear();$lastProcessRefresh=[DateTime]::UtcNow}
  if(!(Allowed $row)){throw 'Target no longer allowlisted'}
  $target=Safe $row.path
  $files=if($row.kind -eq 'directory'){[ReleaseCleanupTree]::Files($target)}else{@(Get-Item -LiteralPath $target)}
  $stamp=Stamp $files
  if($stamp.stamp -ne $row.stamp -or $stamp.bytes -ne $row.bytes -or $stamp.files -ne $row.files){throw 'Target changed after preview'}
  # Native PowerShell only; exact absolute root checked above, and every descendant was inspected for links.
  if($row.kind -eq 'directory'){Remove-Item -LiteralPath $target -Recurse -Force}else{Remove-Item -LiteralPath $target -Force}
  $deletedBytes+=$row.bytes;$deletedFiles+=$row.files;$count++
  $writer.WriteLine((@{path=$target;bytes=$row.bytes;files=$row.files;stamp=$row.stamp;atUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Compress));$writer.Flush()
 }catch{$warnings.Add(@{path=$row.path;reason=$_.Exception.Message})}
}}finally{$writer.Dispose()}
@{status=if($warnings.Count){'completed_with_skips'}else{'completed'};version=$Version;targets=$count;bytes=$deletedBytes;files=$deletedFiles;skipped=$warnings.ToArray();keep=$keep;atUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $result -Encoding utf8
Get-Content -LiteralPath $result
