param(
 [Parameter(Mandatory)][string]$Version,
 [Parameter(Mandatory)][string]$PackageDirectory,
 [Parameter(Mandatory)][string[]]$BasePackageDirectory,
 [Parameter(Mandatory)][string]$PreviousFeed,
 [string]$Tool
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
if($Version -notmatch '^\d+\.\d+\.\d+$' -or $prior.Product -ne 'Dustweave' -or $prior.Schema -ne 2){throw 'Invalid release identity.'}
$reports=@()
# At most six baselines per flavor, bounded identically to client validation.
foreach($release in @($prior.Releases|Sort-Object {[version]$_.Version} -Descending|Select-Object -First 6)){
 if([version]$release.Version -ge [version]$Version){throw 'Delta publication must advance the version.'}
 foreach($flavor in @('Portable','Lite')){
  $asset=$release.Assets|Where-Object Flavor -eq $flavor
  if(!$asset){continue}
  $old=@($BasePackageDirectory|ForEach-Object {Join-Path $_ $asset.FileName}|Where-Object {Test-Path -LiteralPath $_}|Select-Object -First 1)
  if(!$old.Count){continue}
  if((Get-Item -LiteralPath $old[0]).Length -ne $asset.Bytes -or (Get-FileHash -LiteralPath $old[0]).Hash -ne $asset.Sha256){throw 'Baseline differs from signed release.'}
  $next=Join-Path $folder "Dustweave-$Version-$flavor-win-x64.zip"
  $start=[Diagnostics.ProcessStartInfo]::new($Tool);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.WorkingDirectory=(Get-Location).Path
  foreach($arg in @('--create-update-delta',[IO.Path]::GetFullPath($old[0]),$next,$folder)){$start.ArgumentList.Add($arg)}
  $process=[Diagnostics.Process]::Start($start)
  if(!$process.WaitForExit(180000)){throw "Delta generator still running: $($process.Id)"}
  if($process.ExitCode -ne 0){throw "Delta generation failed; inspect $folder/delta-error.json"}
  $name="Dustweave-$Version-$flavor-from-$($release.Version)-win-x64.delta.zip"
  $delta=Get-Content -LiteralPath (Join-Path $folder ($name+'.json')) -Raw|ConvertFrom-Json
  $full=(Get-Item -LiteralPath $next).Length
  $reports+=@{from=$release.Version;to=$Version;flavor=$flavor;fullBytes=$full;deltaBytes=$delta.Bytes;savingsPercent=[Math]::Round(100*(1-$delta.Bytes/$full),2);offered=($delta.Bytes -lt $full*0.8)}
 }
}
$reports|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $folder 'delta-report.json') -Encoding utf8
$reports|ForEach-Object {[pscustomobject]$_}|Format-Table from,to,flavor,fullBytes,deltaBytes,savingsPercent,offered
