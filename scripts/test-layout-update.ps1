param(
 [Parameter(Mandatory)][string]$Packages,
 [Parameter(Mandatory)][string]$Baseline,
 [Parameter(Mandatory)][string]$Output,
 [Parameter(Mandatory)][string]$CoreAssembly
)
$ErrorActionPreference='Stop'
$outputRoot=[IO.Path]::GetFullPath($Output)
if($outputRoot -notmatch '[\\/]artifacts[\\/]' -or (Test-Path -LiteralPath $outputRoot)){throw 'Use a new isolated artifacts directory.'}
[IO.Directory]::CreateDirectory($outputRoot)|Out-Null
Add-Type -Path $CoreAssembly
$signed=Get-Content -LiteralPath (Join-Path $Packages 'updates.json') -Raw|ConvertFrom-Json
$feed=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($signed.Payload))|ConvertFrom-Json
if($feed.Schema -notin @(3,4) -or $feed.Releases[0].Layout -ne 2){throw 'Expected layout 2 packages.'}
$capable=$feed.Releases[0].InstallerProtocol -eq 2
if($capable -and $feed.Schema -ne 4){throw 'Protocol 2 acceptance requires the capable feed.'}
$reports=[Collections.Generic.List[object]]::new()
function WriteJson($path,$value){[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 40),[Text.UTF8Encoding]::new($false))}
function Sign($value){
 $signer=[Security.Cryptography.ECDsa]::Create()
 try{
  $raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
  try{$signer.ImportPkcs8PrivateKey($raw,[ref]([int]0))}finally{[Array]::Clear($raw,0,$raw.Length)}
  $payload=[Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 40 -Compress))
  return @{Schema=1;KeyId=$signed.KeyId;Algorithm=$signed.Algorithm;Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($signer.SignData($payload,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation))}
 }finally{$signer.Dispose()}
}
function Typed($value,$type){return [Text.Json.JsonSerializer]::Deserialize(($value|ConvertTo-Json -Depth 40),$type)}
function StartIsolated($exe,$arguments,$data){
 $info=[Diagnostics.ProcessStartInfo]::new($exe);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden';$info.WorkingDirectory=(Get-Location).Path
 $info.Environment['BD2_DAILY_DATA_ROOT']=$data;$info.Environment['DUSTWEAVE_PLUGIN']='none'
 foreach($arg in $arguments){$info.ArgumentList.Add($arg)}
 return [Diagnostics.Process]::Start($info)
}
function WaitProcess($process,$seconds=150){if(!$process.WaitForExit($seconds*1000)){throw "Acceptance process still active: $($process.Id)."}}
function CloseInstalled($target){
 foreach($process in @(Get-Process Dustweave -ErrorAction SilentlyContinue|Where-Object {[string]::Equals($_.Path,$target,[StringComparison]::OrdinalIgnoreCase)})){
  [void]$process.CloseMainWindow();if(!$process.WaitForExit(15000)){throw "Test window did not close: $($process.Id)"}
 }
}
function VerifyInstall($folder,$expected){
 $descriptor=Get-Content -LiteralPath (Join-Path $expected 'update-package.json') -Raw|ConvertFrom-Json
 foreach($name in $descriptor.Files){if((Get-FileHash -LiteralPath (Join-Path $folder $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $expected $name)).Hash){throw "Installed file differs: $name"}}
}
function CheckPreserved($test){
 if((Get-Content -LiteralPath (Join-Path $test.targetFolder 'plugins/acceptance.keep') -Raw) -ne 'private plugin sentinel'){throw 'Plugin sentinel changed'}
 if((Get-Content -LiteralPath (Join-Path $test.data 'account-preservation.keep') -Raw) -ne 'account sentinel'){throw 'Account sentinel changed'}
 if(Test-Path -LiteralPath (Join-Path $test.targetFolder '.dustweave-update.json')){throw 'Recovery marker remains'}
}
$release=$feed.Releases[0]
$baselineVersion=(Get-Content -LiteralPath (Join-Path $Baseline 'Lite/update-package.json') -Raw|ConvertFrom-Json).Version
$oldRelease=$release|ConvertTo-Json -Depth 40|ConvertFrom-Json
$oldRelease.Version=$baselineVersion;$oldRelease.PreviousVersion=$null;$oldRelease.Deltas=@()
$oldRelease.Assets=@(foreach($flavor in @('Lite','Portable')){
 $name="Dustweave-$baselineVersion-$flavor-win-x64.zip";$path=Join-Path $Baseline $name
 @{Flavor=$flavor;FileName=$name;Bytes=(Get-Item -LiteralPath $path).Length;Sha256=(Get-FileHash -LiteralPath $path).Hash}
})
$patchFolder=Join-Path $outputRoot 'patches';[IO.Directory]::CreateDirectory($patchFolder)|Out-Null
$release.PreviousVersion=$baselineVersion
$release.Deltas=@(foreach($flavor in @('Lite','Portable')){
 [Dustweave.DailyUpdateDeltaPackage]::Create((Join-Path $Baseline "Dustweave-$baselineVersion-$flavor-win-x64.zip"),(Join-Path $Packages "Dustweave-$($release.Version)-$flavor-win-x64.zip"),$patchFolder)
})
$feed.Releases=@($release,$oldRelease)
$acceptanceSigned=Sign $feed
WriteJson (Join-Path $outputRoot 'acceptance-feed.json') $acceptanceSigned
function Setup($name,$flavor){
 $folder=Join-Path $outputRoot $name;$targetFolder=Join-Path $folder 'install';$data=Join-Path $folder 'data'
 $baselineFolder=Join-Path $Baseline $flavor
 [IO.Directory]::CreateDirectory($targetFolder)|Out-Null
 foreach($file in Get-ChildItem -LiteralPath $baselineFolder){Copy-Item -LiteralPath $file.FullName -Destination $targetFolder -Recurse}
 $nonce=[Guid]::NewGuid().ToString('N');$cache=Join-Path $data ("updates/"+$release.Version+"-"+$flavor);$attempt=Join-Path $cache ("attempts/"+$nonce)
 [IO.Directory]::CreateDirectory($attempt)|Out-Null
 $asset=$release.Assets|Where-Object Flavor -eq $flavor;$delta=$release.Deltas|Where-Object Flavor -eq $flavor
 Copy-Item -LiteralPath (Join-Path $patchFolder $delta.FileName) -Destination $cache
 WriteJson (Join-Path $cache 'signed-feed.json') $acceptanceSigned
 $helper=Join-Path $attempt 'Dustweave.Updater.exe'
 Copy-Item -LiteralPath (Join-Path $baselineFolder 'app/support/Dustweave.Updater.exe') -Destination $helper
 $target=Join-Path $targetFolder 'Dustweave.exe'
 $job=[ordered]@{Directory=$cache;Target=$target;ParentPid=[int]::MaxValue;ParentStartTicks=0;Release=$release;Asset=$asset;Nonce=$nonce;OriginalSha256=(Get-FileHash -LiteralPath $target).Hash;DeltaFileName=$delta.FileName}
 $jobPath=Join-Path $attempt 'job.json';WriteJson $jobPath $job
 WriteJson (Join-Path $data 'updates-preference.json') @{Automatic=$false}
 WriteJson (Join-Path $data 'seen-release.json') @{Version=$release.Version;SeenVersions=@($release.Version,$baselineVersion)}
 WriteJson (Join-Path $data 'schedule.json') @{Enabled=$false}
 WriteJson (Join-Path $data 'desktop-behavior.json') @{CloseAction='exit';RunOnOpen=$false}
 WriteJson (Join-Path $data 'first-run.json') @{Revision=1;State='completed';TourIndex=0}
 [IO.Directory]::CreateDirectory((Join-Path $targetFolder 'plugins'))|Out-Null
 [IO.File]::WriteAllText((Join-Path $targetFolder 'plugins/acceptance.keep'),'private plugin sentinel')
 [IO.File]::WriteAllText((Join-Path $data 'account-preservation.keep'),'account sentinel')
 return @{folder=$folder;targetFolder=$targetFolder;target=$target;data=$data;cache=$cache;attempt=$attempt;helper=$helper;job=$job;jobPath=$jobPath;asset=$asset;delta=$delta}
}
foreach($flavor in @('Lite','Portable')){
 $test=Setup ("upgrade-"+$flavor) $flavor
 $process=StartIsolated $test.helper @('--apply-update',$test.jobPath) $test.data;WaitProcess $process
 $result=Get-Content -LiteralPath (Join-Path $test.data 'update-result.json') -Raw|ConvertFrom-Json
 if($process.ExitCode -ne 0 -or $result.State -ne 'completed'){throw "Packaged upgrade failed: $($test.folder)"}
 VerifyInstall $test.targetFolder (Join-Path $Packages $flavor);CheckPreserved $test
 $journal=Get-Content -LiteralPath (Join-Path $test.attempt 'backup/transaction.json') -Raw|ConvertFrom-Json
 if(!$capable -and ($journal.Files.Name -contains 'Dustweave.exe' -or $journal.Files.Name -contains 'app/coreclr.dll')){throw 'Stable bootstrap/runtime was replaced'}
 if($capable -and $journal.Files.Name -contains 'Dustweave.exe' -and $journal.Files[0].Name -ne 'Dustweave.exe'){throw 'Recovery entry must be installed before managed components'}
 CloseInstalled $test.target
 $reports.Add(@{case="upgrade-$flavor";status='passed';delta=$true;baselineHelper=$true;to=$release.Version;startupAcknowledged=$true;changedFiles=$journal.Files.Count;deltaBytes=$test.delta.Bytes;allFilesVerified=$true})
 Write-Host "Passed upgrade-$flavor"
}
# Make valid signed patches whose business DLL cannot start, then verify both editions restore every baseline file.
foreach($flavor in @('Lite','Portable')){
$test=Setup ("startup-failure-"+$flavor) $flavor
$faultZip=Join-Path $test.folder 'fault.zip'
Copy-Item -LiteralPath (Join-Path $Packages $test.asset.FileName) -Destination $faultZip
$archive=[IO.Compression.ZipFile]::Open($faultZip,[IO.Compression.ZipArchiveMode]::Update)
try{
 $archive.GetEntry('app/Dustweave.dll').Delete()
 $writer=[IO.StreamWriter]::new($archive.CreateEntry('app/Dustweave.dll').Open());try{$writer.Write('Deliberately invalid business assembly.')}finally{$writer.Dispose()}
}finally{$archive.Dispose()}
$faultFolder=Join-Path $test.folder 'patch';[IO.Directory]::CreateDirectory($faultFolder)|Out-Null
$faultDelta=[Dustweave.DailyUpdateDeltaPackage]::Create((Join-Path $Baseline "Dustweave-$baselineVersion-$flavor-win-x64.zip"),$faultZip,$faultFolder)
$faultFeed=$feed|ConvertTo-Json -Depth 40|ConvertFrom-Json
$faultAsset=$faultFeed.Releases[0].Assets|Where-Object Flavor -eq $flavor
$faultAsset.Bytes=(Get-Item -LiteralPath $faultZip).Length;$faultAsset.Sha256=(Get-FileHash -LiteralPath $faultZip).Hash
$faultFeed.Releases[0].Deltas=@($faultFeed.Releases[0].Deltas|Where-Object Flavor -ne $flavor)+@($faultDelta)
WriteJson (Join-Path $test.cache 'signed-feed.json') (Sign $faultFeed)
Copy-Item -LiteralPath (Join-Path $faultFolder $faultDelta.FileName) -Destination $test.cache -Force
$test.job.Release=$faultFeed.Releases[0];$test.job.Asset=$faultAsset;WriteJson $test.jobPath $test.job
$process=StartIsolated $test.helper @('--apply-update',$test.jobPath) $test.data;WaitProcess $process
$result=Get-Content -LiteralPath (Join-Path $test.data 'update-result.json') -Raw|ConvertFrom-Json
if($result.State -ne 'rolled_back'){throw "Startup failure was not recovered: $($test.folder)"}
Start-Sleep -Seconds 3;VerifyInstall $test.targetFolder (Join-Path $Baseline $flavor);CheckPreserved $test;CloseInstalled $test.target
$reports.Add(@{case=$(if($flavor -eq 'Lite'){'startup-failure'}else{'startup-failure-Portable'});flavor=$flavor;status='passed';previousPackageRestored=$true;brokenBusinessAssembly=$true})
Write-Host "Passed startup-failure-$flavor"
}
# Interrupt a replacement, then corrupt a backed-up business DLL. Normal entry must recover before loading it.
foreach($flavor in @('Lite','Portable')){
 $test=Setup ('bootstrap-recovery-'+$flavor) $flavor
 $typedRelease=Typed $release ([type][Dustweave.DailyUpdateRelease]);$typedAsset=Typed $test.asset ([type][Dustweave.DailyUpdateAsset])
 $stage=Join-Path $test.attempt 'staging';$backup=Join-Path $test.attempt 'backup'
 $names=[Dustweave.DailyUpdateDeltaPackage]::Extract((Join-Path $test.cache $test.delta.FileName),$test.targetFolder,$stage,$typedRelease,$typedAsset,$test.delta,[Threading.CancellationToken]::None,$true)
 $journal=[Dustweave.DailyUpdateTransaction]::Prepare($stage,$test.targetFolder,$backup,$names,$null,$true,$capable,$null)
 if($journal.Files.Name -notcontains 'app/Dustweave.dll'){throw 'Business DLL is not covered by recovery backup'}
 WriteJson (Join-Path $test.targetFolder '.dustweave-update.json') @{JobPath=$test.jobPath;Nonce=$test.job.Nonce;HelperSha256=(Get-FileHash -LiteralPath $test.helper).Hash}
 try{[void][Dustweave.DailyUpdateTransaction]::Apply($journal,[Action[int]]{param($count) if($count -eq 1){throw 'Injected interruption'}})}catch{if($_.Exception.ToString() -notmatch 'Injected interruption'){throw}}
 [IO.File]::WriteAllText((Join-Path $test.targetFolder 'app/Dustweave.dll'),'Interrupted business file')
 $process=StartIsolated $test.target @('--desktop-session') $test.data;WaitProcess $process 30
 $deadline=[DateTime]::UtcNow.AddSeconds(45);$resultPath=Join-Path $test.data 'update-result.json'
 while(!(Test-Path -LiteralPath $resultPath) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 300}
 if(!(Test-Path -LiteralPath $resultPath)){throw 'Recovery result was not written'}
 $result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
 if($result.State -ne 'rolled_back'){throw "Bootstrap recovery failed: $($test.folder)"}
 Start-Sleep -Seconds 3;VerifyInstall $test.targetFolder (Join-Path $Baseline $flavor);CheckPreserved $test;CloseInstalled $test.target
 $reports.Add(@{case=if($flavor -eq 'Lite'){'bootstrap-recovery'}else{'bootstrap-recovery-Portable'};status='passed';realProcessRestart=$true;physicalPowerCut=$false;allFilesVerified=$true})
 Write-Host "Passed bootstrap-recovery-$flavor"
}
WriteJson (Join-Path $outputRoot 'results.json') @{status='passed';baseline=$baselineVersion;target=$release.Version;purpose='synthetic version baseline, real packaged updater';cases=$reports.ToArray();realGameTouched=$false;registeredTasks=$false;published=$false}
Write-Host "Packaged layout-2 OTA acceptance passed: $outputRoot"
