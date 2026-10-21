param(
 [Parameter(Mandatory)][string]$Version,
 [Parameter(Mandatory)][string]$PackageDirectory,
 [string[]]$BasePackageDirectory=@(),
 [Parameter(Mandatory)][string]$PreviousFeed,
 [string]$Tool,
 [switch]$VerifyBaselinesOnly
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$folder=[IO.Path]::GetFullPath($PackageDirectory)
if(!$Tool){$Tool=Join-Path $root 'src/Desktop/bin/Release/net8.0-windows/win-x64/Dustweave.exe'}
$Tool=[IO.Path]::GetFullPath($Tool)
$trust=Get-Content -LiteralPath (Join-Path $root 'assets/updates/trust.json') -Raw|ConvertFrom-Json
$envelope=Get-Content -LiteralPath $PreviousFeed -Raw|ConvertFrom-Json
$key=[Security.Cryptography.ECDsa]::Create()
try{
 $public=$trust.Keys.PSObject.Properties[$envelope.KeyId].Value
 if($envelope.Schema -ne 1 -or $envelope.Algorithm -ne 'ECDSA-P256-SHA256' -or !$public){throw 'Untrusted previous feed.'}
 $key.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($public),[ref]([int]0))
 $bytes=[Convert]::FromBase64String($envelope.Payload)
 if(!$key.VerifyData($bytes,[Convert]::FromBase64String($envelope.Signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)){throw 'Previous feed signature failed.'}
 $prior=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
}finally{$key.Dispose()}
if($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$' -or $prior.Product -ne 'Dustweave' -or $prior.Schema -notin @(3,4)){throw 'Invalid release identity.'}
$reports=@()
$automaticBaselines=!$BasePackageDirectory.Count
$baselineCache=Join-Path $root 'artifacts/update-baselines'
if($automaticBaselines){
 $BasePackageDirectory=@($baselineCache)+@(Get-ChildItem -LiteralPath (Join-Path $root 'artifacts/releases') -Directory -ErrorAction SilentlyContinue|ForEach-Object FullName)
}
$resolved=[Collections.Generic.List[object]]::new()
$ordered=@($prior.Releases|Sort-Object {[semver]$_.Version} -Descending)
$recent=@($ordered|Select-Object -First 10|ForEach-Object Version)
$supported=@($ordered|Where-Object {$_.Version -in $recent -or [semver]$_.Version -ge [semver]'1.4.7'})
if([semver]$Version -ge [semver]'1.4.15'){
 $minimum=if($Version -eq '1.4.15'){'1.4.15-bridge.1'}else{'1.4.15'}
 # Older capable clients reconstruct signed blocks without requiring every old
 # archive. The internal bridge always finishes at 1.4.15, never a later release.
 $supported=@($ordered|Where-Object {[semver]$_.Version -ge [semver]$minimum}|Select-Object -First 10)
}
# Resolve the whole supported window before generating any patch. Missing local
# archives must not silently turn a long-term baseline policy into three versions.
foreach($release in $supported){
 if([semver]$release.Version -ge [semver]$Version){throw 'Delta publication must advance the version.'}
 if($release.Version -eq '1.3.0'){
  $reports+=@{from=$release.Version;to=$Version;offered=$false;reason='withdrawn_layout';manualDownload=$true}
  continue
 }
 foreach($asset in @($release.Assets)){
  $expected="Dustweave-$($release.Version)-$($asset.Flavor)-win-x64.zip"
  if($asset.Flavor -notin @('Portable','Lite') -or $asset.FileName -ne $expected){throw 'Unexpected signed baseline asset.'}
  $available=@($BasePackageDirectory|ForEach-Object {Join-Path $_ $asset.FileName}|Where-Object {Test-Path -LiteralPath $_}|Select-Object -Unique)
  $old=@($available|Where-Object {(Get-Item -LiteralPath $_).Length -eq $asset.Bytes -and (Get-FileHash -LiteralPath $_).Hash -eq $asset.Sha256}|Select-Object -First 1)
  if(!$old.Count -and $automaticBaselines){
   [IO.Directory]::CreateDirectory($baselineCache)|Out-Null
   & gh release download ('v'+$release.Version) --repo MadestSamurai/Dustweave --pattern $asset.FileName --dir $baselineCache
   if($LASTEXITCODE -ne 0){throw "Cannot obtain supported OTA baseline $($release.Version) $($asset.Flavor)."}
   $old=@(Join-Path $baselineCache $asset.FileName)
  }
  if(!$old.Count){throw "Missing supported OTA baseline $($release.Version) $($asset.Flavor). Provide every baseline in the supported baseline set, or omit BasePackageDirectory for verified discovery/download."}
  if((Get-Item -LiteralPath $old[0]).Length -ne $asset.Bytes -or (Get-FileHash -LiteralPath $old[0]).Hash -ne $asset.Sha256){throw 'Baseline differs from signed release.'}
  $resolved.Add(@{version=$release.Version;flavor=$asset.Flavor;path=$old[0];bytes=$asset.Bytes;sha256=$asset.Sha256})
 }
}
$baselineReport=@{status='passed';legacyWindow=10;longTermBaseline='1.4.7';maxDeltaBytes=10MB;target=$Version;baselines=$resolved.ToArray();excluded=$reports}
$baselineReport|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $folder 'baseline-report.json') -Encoding utf8
if($VerifyBaselinesOnly){$baselineReport|ConvertTo-Json -Depth 6;return}
# Legacy clients retain their ten-release window; 1.4.7+ is size bounded.
foreach($release in $supported){
 if([semver]$release.Version -ge [semver]$Version){throw 'Delta publication must advance the version.'}
 if($release.Version -eq '1.3.0'){continue}
 foreach($flavor in @('Portable','Lite')){
  $asset=$release.Assets|Where-Object Flavor -eq $flavor
  if(!$asset){continue}
  $old=@($resolved|Where-Object {$_.version -eq $release.Version -and $_.flavor -eq $flavor}|ForEach-Object path)
  if($old.Count -ne 1){throw 'Resolved baseline is missing or duplicated.'}
  if((Get-Item -LiteralPath $old[0]).Length -ne $asset.Bytes -or (Get-FileHash -LiteralPath $old[0]).Hash -ne $asset.Sha256){throw 'Baseline differs from signed release.'}
  $next=Join-Path $folder "Dustweave-$Version-$flavor-win-x64.zip"
  function ReadPackage($path){
   $zip=[IO.Compression.ZipFile]::OpenRead($path)
   try{$reader=[IO.StreamReader]::new($zip.GetEntry('update-package.json').Open());try{return $reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}}finally{$zip.Dispose()}
  }
  $before=ReadPackage $old[0];$after=ReadPackage $next
  if($before.Layout -ne $after.Layout -or $after.InstallerProtocol -ne 2 -and ($before.RuntimeVersion -ne $after.RuntimeVersion -or (@($before.BootstrapFiles|Sort-Object) -join "`n") -cne (@($after.BootstrapFiles|Sort-Object) -join "`n"))){
   $reports+=@{from=$release.Version;to=$Version;flavor=$flavor;offered=$false;reason='manual_layout'}
   continue
  }
  $start=[Diagnostics.ProcessStartInfo]::new($Tool);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.WorkingDirectory=(Get-Location).Path
  foreach($arg in @('--create-update-delta',[IO.Path]::GetFullPath($old[0]),$next,$folder)){$start.ArgumentList.Add($arg)}
  $process=[Diagnostics.Process]::Start($start)
  if(!$process.WaitForExit(180000)){throw "Delta generator still running: $($process.Id)"}
  if($process.ExitCode -ne 0){throw "Delta generation failed; inspect $folder/delta-error.json"}
  $name="Dustweave-$Version-$flavor-from-$($release.Version)-win-x64.delta.zip"
  $delta=Get-Content -LiteralPath (Join-Path $folder ($name+'.json')) -Raw|ConvertFrom-Json
  $full=(Get-Item -LiteralPath $next).Length
  $capable=$after.InstallerProtocol -eq 2 -and $delta.Algorithm -eq 'dustweave-copy-v3'
  $offered=if($capable){$delta.Bytes -lt $full}else{$delta.Bytes -le $(if([semver]$release.Version -ge [semver]'1.4.7'){10MB}else{8MB}) -and ([semver]$release.Version -ge [semver]'1.4.7' -or $delta.Bytes -lt $full*0.8)}
  $reports+=@{from=$release.Version;to=$Version;flavor=$flavor;fullBytes=$full;deltaBytes=$delta.Bytes;savingsPercent=[Math]::Round(100*(1-$delta.Bytes/$full),2);offered=$offered}
 }
}
$reports|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $folder 'delta-report.json') -Encoding utf8
$reports|ForEach-Object {[pscustomobject]$_}|Format-Table from,to,flavor,fullBytes,deltaBytes,savingsPercent,offered
