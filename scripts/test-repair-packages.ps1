param(
 [Parameter(Mandatory)][string]$Packages,
 [Parameter(Mandatory)][string]$RepairDirectory,
 [Parameter(Mandatory)][string]$Helper,
 [Parameter(Mandatory)][string]$CoreAssembly,
 [Parameter(Mandatory)][string]$Output,
 [ValidateSet('Lite','Portable','startup-failure','interrupted-repair')][string[]]$Cases=@('Lite','Portable','startup-failure','interrupted-repair')
)
$ErrorActionPreference='Stop'
$outputRoot=[IO.Path]::GetFullPath($Output)
if($outputRoot -notmatch '[\\/]artifacts[\\/]' -or (Test-Path -LiteralPath $outputRoot)){throw 'Choose a new isolated artifacts directory.'}
[IO.Directory]::CreateDirectory($outputRoot)|Out-Null
Add-Type -Path ([IO.Path]::GetFullPath($CoreAssembly))
$feedBytes=[IO.File]::ReadAllBytes((Join-Path $RepairDirectory 'updates.json'))
$envelope=[Text.Encoding]::UTF8.GetString($feedBytes)|ConvertFrom-Json
$feed=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.Payload))|ConvertFrom-Json
$release=$feed.Releases[0]
$reports=[Collections.Generic.List[object]]::new()
function WriteJson($path,$value){[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 60),[Text.UTF8Encoding]::new($false))}
function Typed($value,$type){return [Text.Json.JsonSerializer]::Deserialize(($value|ConvertTo-Json -Depth 60),$type)}
function Sign($value,[string]$domain){
 $signer=[Security.Cryptography.ECDsa]::Create()
 try{
  $raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
  try{$signer.ImportPkcs8PrivateKey($raw,[ref]([int]0))}finally{[Array]::Clear($raw,0,$raw.Length)}
  $payload=[Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 60 -Compress))
  $signature=$signer.SignData(([Text.Encoding]::UTF8.GetBytes($domain)+$payload),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
  return @{Schema=1;KeyId=$envelope.KeyId;Algorithm=$envelope.Algorithm;Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($signature)}
 }finally{$signer.Dispose()}
}
function StartIsolated($exe,$arguments,$data){
 $info=[Diagnostics.ProcessStartInfo]::new($exe);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden'
 $info.Environment['BD2_DAILY_DATA_ROOT']=$data;$info.Environment['DUSTWEAVE_PLUGIN']='none'
 foreach($argument in $arguments){$info.ArgumentList.Add($argument)}
 return [Diagnostics.Process]::Start($info)
}
function CloseInstalled($target){
 foreach($process in @(Get-Process Dustweave -ErrorAction SilentlyContinue|Where-Object {[string]::Equals($_.Path,$target,[StringComparison]::OrdinalIgnoreCase)})){
  $deadline=[DateTime]::UtcNow.AddSeconds(15)
  while(!$process.HasExited -and $process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200;$process.Refresh()}
  if($process.HasExited){continue}
  [void]$process.CloseMainWindow();if(!$process.WaitForExit(15000)){throw "Isolated window did not exit: $($process.Id)"}
 }
}
function Setup($name,$flavor){
 $folder=Join-Path $outputRoot $name;$install=Join-Path $folder 'install';$data=Join-Path $folder 'data'
 [IO.Directory]::CreateDirectory($install)|Out-Null
 foreach($file in Get-ChildItem -LiteralPath (Join-Path $Packages $flavor)){Copy-Item -LiteralPath $file.FullName -Destination $install -Recurse}
 $nonce=[Guid]::NewGuid().ToString('N');$cache=Join-Path $data "updates/$($release.Version)-$flavor";$attempt=Join-Path $cache "attempts/$nonce"
 [IO.Directory]::CreateDirectory($attempt)|Out-Null
 Copy-Item -LiteralPath (Join-Path $RepairDirectory "repair/v$($release.Version)/Dustweave-$($release.Version)-$flavor-repair.json") -Destination (Join-Path $cache 'repair-manifest.json')
 [IO.File]::WriteAllBytes((Join-Path $cache 'signed-feed.json'),$feedBytes)
 $target=Join-Path $install 'Dustweave.exe';$helperPath=Join-Path $attempt 'Dustweave.Updater.exe'
 Copy-Item -LiteralPath $Helper -Destination $helperPath
 $job=[ordered]@{Directory=$cache;Target=$target;ParentPid=[int]::MaxValue;ParentStartTicks=0;Release=$release;Asset=($release.Assets|Where-Object Flavor -eq $flavor);Nonce=$nonce;OriginalSha256=(Get-FileHash -LiteralPath $target).Hash;Repair=$true}
 $jobPath=Join-Path $attempt 'job.json';WriteJson $jobPath $job
 WriteJson (Join-Path $data 'updates-preference.json') @{Automatic=$false}
 WriteJson (Join-Path $data 'seen-release.json') @{Version=$release.Version;SeenVersions=@($release.Version)}
 WriteJson (Join-Path $data 'schedule.json') @{Enabled=$false}
 WriteJson (Join-Path $data 'desktop-behavior.json') @{CloseAction='exit';RunOnOpen=$false}
 WriteJson (Join-Path $data 'first-run.json') @{Revision=1;State='completed';TourIndex=0}
 [IO.Directory]::CreateDirectory((Join-Path $install 'plugins'))|Out-Null
 [IO.File]::WriteAllText((Join-Path $install 'plugins/acceptance.keep'),'private plugin sentinel')
 [IO.File]::WriteAllText((Join-Path $data 'account-preservation.keep'),'account sentinel')
 return @{folder=$folder;install=$install;target=$target;data=$data;cache=$cache;attempt=$attempt;helper=$helperPath;job=$job;jobPath=$jobPath;flavor=$flavor}
}
function VerifyInstall($test){
 $original=Join-Path $Packages $test.flavor
 $descriptor=Get-Content -LiteralPath (Join-Path $original 'update-package.json') -Raw|ConvertFrom-Json
 foreach($name in $descriptor.Files){if((Get-FileHash -LiteralPath (Join-Path $test.install $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $original $name)).Hash){throw "File not restored: $name"}}
 if((Get-Content -LiteralPath (Join-Path $test.install 'plugins/acceptance.keep') -Raw) -ne 'private plugin sentinel' -or (Get-Content -LiteralPath (Join-Path $test.data 'account-preservation.keep') -Raw) -ne 'account sentinel'){throw 'User files changed'}
 if(Test-Path -LiteralPath (Join-Path $test.install '.dustweave-update.json')){throw 'Recovery marker remains'}
}
function Apply($test,$expected){
 $process=StartIsolated $test.helper @('--apply-update',$test.jobPath) $test.data
 if(!$process.WaitForExit(150000)){throw "Helper still active: $($process.Id)"}
 $result=Get-Content -LiteralPath (Join-Path $test.data 'update-result.json') -Raw|ConvertFrom-Json
 if($result.State -ne $expected){throw "Unexpected repair result: $($test.folder), $($result.State)"}
}
foreach($flavor in @('Lite','Portable')|Where-Object {$_ -in $Cases}){
 $test=Setup "repair-$flavor" $flavor
 $changed=@('app/Dustweave.Core.dll','app/Dustweave.Compatibility.dll','app/support/Dustweave.Updater.exe','update-package.json')
 foreach($name in $changed){
  $incoming=Join-Path $test.cache "repair-staging/$name";[IO.Directory]::CreateDirectory((Split-Path -Parent $incoming))|Out-Null
  Copy-Item -LiteralPath (Join-Path $Packages "$flavor/$name") -Destination $incoming
 }
 $core=Join-Path $test.install 'app/Dustweave.Core.dll';$bytes=[IO.File]::ReadAllBytes($core);$bytes[33]=$bytes[33] -bxor 255;[IO.File]::WriteAllBytes($core,$bytes)
 Remove-Item -LiteralPath (Join-Path $test.install 'app/Dustweave.Compatibility.dll'),(Join-Path $test.install 'app/support/Dustweave.Updater.exe')
 [IO.File]::WriteAllText((Join-Path $test.install 'update-package.json'),'{broken')
 Apply $test 'completed';VerifyInstall $test;CloseInstalled $test.target
 $reports.Add(@{case="repair-$flavor";status='passed';sameVersion=$true;missingHelper=$true;corruptDescriptor=$true;equalSizeCorruption=$true;startupAcknowledged=$true;userFilesPreserved=$true})
 Write-Host "Passed repair-$flavor"
}
function FaultSetup($name){
 $test=Setup $name 'Lite'
 $repairEnvelope=Get-Content -LiteralPath (Join-Path $test.cache 'repair-manifest.json') -Raw|ConvertFrom-Json
 $manifest=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($repairEnvelope.Payload))|ConvertFrom-Json
 $fault=[Text.Encoding]::UTF8.GetBytes('Deliberately invalid application assembly.')
 $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($fault)).ToLowerInvariant()
 $entry=$manifest.Files|Where-Object Name -eq 'app/Dustweave.dll'
 $entry.Bytes=$fault.Length;$entry.Sha256=$digest;$entry.Blocks=@(@{Bytes=$fault.Length;Sha256=$digest})
 $fork=$feed|ConvertTo-Json -Depth 60|ConvertFrom-Json
 $asset=$fork.Releases[0].Assets|Where-Object Flavor -eq 'Lite';$asset.Sha256=$digest;$manifest.ArchiveSha256=$digest
 WriteJson (Join-Path $test.cache 'signed-feed.json') (Sign $fork '')
 WriteJson (Join-Path $test.cache 'repair-manifest.json') (Sign $manifest "Dustweave-Repair-v1`n")
 $test.job.Release=$fork.Releases[0];$test.job.Asset=$asset;WriteJson $test.jobPath $test.job
 $stage=Join-Path $test.cache 'repair-staging';[IO.Directory]::CreateDirectory((Join-Path $stage 'app'))|Out-Null
 [IO.File]::WriteAllBytes((Join-Path $stage 'app/Dustweave.dll'),$fault)
 $test.manifest=$manifest;$test.stage=$stage
 return $test
}
if('startup-failure' -in $Cases){
 $test=FaultSetup 'startup-failure'
 Apply $test 'rolled_back';VerifyInstall $test;CloseInstalled $test.target
 $reports.Add(@{case='startup-failure';status='passed';workingFilesRestored=$true})
 Write-Host 'Passed startup-failure'
}
if('interrupted-repair' -in $Cases){
$test=FaultSetup 'interrupted-repair'
$manifest=Typed $test.manifest ([type][Dustweave.DailyRepairManifest])
$retained=[Collections.Generic.Dictionary[string,string]]::new()
$names=[Dustweave.DailyUpdateRepair]::VerifyStaging($manifest,$test.install,$test.stage,[ref]$retained)
$journal=[Dustweave.DailyUpdateTransaction]::Prepare($test.stage,$test.install,(Join-Path $test.attempt 'backup'),$names,$retained,$false)
WriteJson (Join-Path $test.install '.dustweave-update.json') @{JobPath=$test.jobPath;Nonce=$test.job.Nonce;HelperSha256=(Get-FileHash -LiteralPath $test.helper).Hash}
try{[void][Dustweave.DailyUpdateTransaction]::Apply($journal,[Action[int]]{param($count) throw 'Injected interruption'})}catch{if($_.Exception.ToString() -notmatch 'Injected interruption'){throw}}
$process=StartIsolated $test.target @('--desktop-session') $test.data
if(!$process.WaitForExit(30000)){throw 'Bootstrap did not exit'}
$deadline=[DateTime]::UtcNow.AddSeconds(45);$resultPath=Join-Path $test.data 'update-result.json'
while(!(Test-Path -LiteralPath $resultPath) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 250}
$result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
if($result.State -ne 'rolled_back'){throw 'Interrupted repair did not roll back'}
VerifyInstall $test;CloseInstalled $test.target
$reports.Add(@{case='interrupted-repair';status='passed';bootstrapRecovery=$true;physicalPowerCut=$false})
}
WriteJson (Join-Path $outputRoot 'results.json') @{status='passed';version=$release.Version;cases=$reports.ToArray();realGameTouched=$false;published=$false}
Write-Host "Repair package acceptance passed: $outputRoot"
