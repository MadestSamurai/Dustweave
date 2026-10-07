param(
 [Parameter(Mandatory)][string]$Packages,
 [Parameter(Mandatory)][string]$Baseline,
 [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$outputRoot=[IO.Path]::GetFullPath($Output)
if(!$outputRoot.StartsWith((Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Use an isolated artifacts directory.'}
if(Test-Path $outputRoot){throw 'Preserve existing acceptance evidence.'}
[IO.Directory]::CreateDirectory($outputRoot)|Out-Null
Add-Type -Path (Join-Path $repo 'src/Desktop/bin/Release/net8.0-windows/win-x64/Dustweave.Core.dll')
$signed=Get-Content -LiteralPath (Join-Path $Packages 'updates.json') -Raw|ConvertFrom-Json
$feed=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($signed.Payload))|ConvertFrom-Json
$release=$feed.Releases[0]
$reports=[Collections.Generic.List[object]]::new()
function WriteJson($path,$value){[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))}
function StartIsolated($exe,$arguments,$data){
 $info=[Diagnostics.ProcessStartInfo]::new($exe);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden';$info.WorkingDirectory=(Get-Location).Path
 $info.Environment['BD2_DAILY_DATA_ROOT']=$data;$info.Environment['DUSTWEAVE_PLUGIN']='none'
 foreach($arg in $arguments){$info.ArgumentList.Add($arg)}
 return [Diagnostics.Process]::Start($info)
}
function WaitProcess($process,$seconds=150){if(!$process.WaitForExit($seconds*1000)){throw "Acceptance process is still active: $($process.Id). Inspect without touching unrelated processes."}}
function CloseInstalled($target){
 $matched=@(Get-Process Dustweave -ErrorAction SilentlyContinue|Where-Object {[string]::Equals($_.Path,$target,[StringComparison]::OrdinalIgnoreCase)})
 foreach($process in $matched){[void]$process.CloseMainWindow();if(!$process.WaitForExit(15000)){throw "Test window did not close: $($process.Id)"}}
}
function Setup($name,$flavor,$baselineFolder){
 $folder=Join-Path $outputRoot $name;$targetFolder=Join-Path $folder 'install';$data=Join-Path $folder 'data'
 [IO.Directory]::CreateDirectory($targetFolder)|Out-Null
 foreach($file in Get-ChildItem -LiteralPath $baselineFolder){Copy-Item -LiteralPath $file.FullName -Destination $targetFolder -Recurse}
 $nonce=[Guid]::NewGuid().ToString('N')
 $cache=Join-Path $data ("updates/"+$release.Version+"-"+$flavor)
 $attempt=Join-Path $cache ("attempts/"+$nonce)
 [IO.Directory]::CreateDirectory($attempt)|Out-Null
 $asset=$release.Assets|Where-Object Flavor -eq $flavor
 Copy-Item -LiteralPath (Join-Path $Packages $asset.FileName) -Destination (Join-Path $cache 'package.zip')
 Copy-Item -LiteralPath (Join-Path $Packages 'updates.json') -Destination (Join-Path $cache 'signed-feed.json')
 $helper=Join-Path $attempt 'Dustweave.Updater.exe'
 Copy-Item -LiteralPath (Join-Path $Packages "$flavor/Dustweave.exe") -Destination $helper
 $target=Join-Path $targetFolder 'Dustweave.exe'
 $job=[ordered]@{Directory=$cache;Target=$target;ParentPid=[int]::MaxValue;ParentStartTicks=0;Release=$release;Asset=$asset;Nonce=$nonce;OriginalSha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash}
 $jobPath=Join-Path $attempt 'job.json';WriteJson $jobPath $job
 WriteJson (Join-Path $data 'updates-preference.json') @{Automatic=$false}
 WriteJson (Join-Path $data 'seen-release.json') @{Version=$release.Version;SeenVersions=@($release.Version)}
 WriteJson (Join-Path $data 'schedule.json') @{Enabled=$false}
 [IO.Directory]::CreateDirectory((Join-Path $targetFolder 'plugins'))|Out-Null
 [IO.File]::WriteAllText((Join-Path $targetFolder 'plugins/acceptance.keep'),'private plugin sentinel')
 [IO.File]::WriteAllText((Join-Path $data 'account-preservation.keep'),'account sentinel')
 return @{folder=$folder;targetFolder=$targetFolder;target=$target;data=$data;cache=$cache;attempt=$attempt;helper=$helper;job=$job;jobPath=$jobPath;asset=$asset}
}
foreach($flavor in @('Lite','Portable')){
 $test=Setup ("upgrade-"+$flavor) $flavor (Join-Path $Baseline $flavor)
 # A prior release without an OTA descriptor is represented explicitly in this isolated bootstrap test.
 WriteJson (Join-Path $test.targetFolder 'update-package.json') @{Version='0.8.17';Flavor=$flavor}
 $process=StartIsolated $test.helper @('--apply-update',$test.jobPath) $test.data
 WaitProcess $process
 $result=Get-Content -LiteralPath (Join-Path $test.data 'update-result.json') -Raw|ConvertFrom-Json
 if($process.ExitCode -ne 0 -or $result.State -ne 'completed'){throw "Packaged upgrade failed: $($test.folder)"}
 if((Get-FileHash -LiteralPath $test.target).Hash -ne (Get-FileHash -LiteralPath (Join-Path $Packages "$flavor/Dustweave.exe")).Hash){throw 'Executable differs after upgrade'}
 if((Get-Content -LiteralPath (Join-Path $test.targetFolder 'plugins/acceptance.keep') -Raw) -ne 'private plugin sentinel'){throw 'Plugin sentinel changed'}
 if((Get-Content -LiteralPath (Join-Path $test.data 'account-preservation.keep') -Raw) -ne 'account sentinel'){throw 'Account sentinel changed'}
 if(Test-Path (Join-Path $test.targetFolder '.dustweave-update.json')){throw 'Completed transaction left a recovery marker'}
 CloseInstalled $test.target
 $reports.Add(@{case="upgrade-$flavor";status='passed';from='0.8.17';to=$release.Version;startupAcknowledged=$true})
}
# A signed but unlaunchable new executable must restore the actual prior package.
$test=Setup 'startup-failure' 'Lite' (Join-Path $Baseline 'Lite')
WriteJson (Join-Path $test.targetFolder 'update-package.json') @{Version='0.8.17';Flavor='Lite'}
$faultZip=Join-Path $test.folder 'fault.zip'
$archive=[IO.Compression.ZipFile]::Open($faultZip,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($member in @(@{name='Dustweave.exe';text='This is deliberately not an executable.'},@{name='update-package.json';text=(@{Version=$release.Version;Flavor='Lite';Files=@('Dustweave.exe','update-package.json')}|ConvertTo-Json)})){
  $writer=[IO.StreamWriter]::new($archive.CreateEntry($member.name).Open())
  try{$writer.Write($member.text)}finally{$writer.Dispose()}
 }
}finally{$archive.Dispose()}
$faultFeed=$feed|ConvertTo-Json -Depth 30|ConvertFrom-Json
$faultAsset=$faultFeed.Releases[0].Assets|Where-Object Flavor -eq 'Lite'
$faultAsset.Bytes=(Get-Item -LiteralPath $faultZip).Length
$faultAsset.Sha256=(Get-FileHash -LiteralPath $faultZip).Hash
$signer=[Security.Cryptography.ECDsa]::Create()
try{
 $raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
 try{$signer.ImportPkcs8PrivateKey($raw,[ref]([int]0))}finally{[Array]::Clear($raw,0,$raw.Length)}
 $payload=[Text.Encoding]::UTF8.GetBytes(($faultFeed|ConvertTo-Json -Depth 30 -Compress))
 $faultSigned=@{Schema=1;KeyId=$signed.KeyId;Algorithm=$signed.Algorithm;Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($signer.SignData($payload,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation))}
}finally{$signer.Dispose()}
Copy-Item -LiteralPath $faultZip -Destination (Join-Path $test.cache 'package.zip') -Force
WriteJson (Join-Path $test.cache 'signed-feed.json') $faultSigned
$test.job.Release=$faultFeed.Releases[0];$test.job.Asset=$faultAsset
WriteJson $test.jobPath $test.job
$process=StartIsolated $test.helper @('--apply-update',$test.jobPath) $test.data
WaitProcess $process
$result=Get-Content -LiteralPath (Join-Path $test.data 'update-result.json') -Raw|ConvertFrom-Json
if($result.State -ne 'rolled_back'){throw "Startup failure was not recovered: $($test.folder)"}
if((Get-FileHash -LiteralPath $test.target).Hash -ne $test.job.OriginalSha256){throw 'Rollback executable differs from baseline'}
Start-Sleep -Seconds 3
CloseInstalled $test.target
$reports.Add(@{case='startup-failure';status='passed';previousExecutableRestored=$true})
# An interrupted transaction is injected without terminating processes, then the actual EXE performs recovery on startup.
$test=Setup 'bootstrap-recovery' 'Lite' (Join-Path $Packages 'Lite')
$typedRelease=[Text.Json.JsonSerializer]::Deserialize(($test.job.Release|ConvertTo-Json -Depth 20),[type][Dustweave.DailyUpdateRelease])
$typedAsset=[Text.Json.JsonSerializer]::Deserialize(($test.asset|ConvertTo-Json -Depth 20),[type][Dustweave.DailyUpdateAsset])
$stage=Join-Path $test.attempt 'staging';$backup=Join-Path $test.attempt 'backup'
$names=[Dustweave.DailyUpdates]::Extract((Join-Path $test.cache 'package.zip'),$stage,$typedRelease,$typedAsset)
$journal=[Dustweave.DailyUpdateTransaction]::Prepare($stage,$test.targetFolder,$backup,$names)
WriteJson (Join-Path $test.targetFolder '.dustweave-update.json') @{JobPath=$test.jobPath;Nonce=$test.job.Nonce;HelperSha256=(Get-FileHash -LiteralPath $test.helper).Hash}
try{[void][Dustweave.DailyUpdateTransaction]::Apply($journal,[Action[int]]{param($count) if($count -eq 2){throw 'Injected interruption'}})}catch{if($_.Exception.ToString() -notmatch 'Injected interruption'){throw}}
$process=StartIsolated $test.target @('--desktop-session') $test.data
WaitProcess $process 30
$deadline=[DateTime]::UtcNow.AddSeconds(45)
$resultPath=Join-Path $test.data 'update-result.json'
while(!(Test-Path $resultPath) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 300}
if(!(Test-Path $resultPath)){throw 'Recovery result was not written'}
$result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
if($result.State -ne 'rolled_back'){throw "Bootstrap recovery failed: $($test.folder)"}
Start-Sleep -Seconds 3
CloseInstalled $test.target
if(Test-Path (Join-Path $test.targetFolder '.dustweave-update.json')){throw 'Recovery marker remains'}
$reports.Add(@{case='bootstrap-recovery';status='passed';realProcessRestart=$true;physicalPowerCut=$false})
WriteJson (Join-Path $outputRoot 'results.json') @{status='passed';cases=$reports.ToArray();realGameTouched=$false;registeredTasks=$false;published=$false}
Write-Host "Packaged OTA acceptance passed: $outputRoot"
