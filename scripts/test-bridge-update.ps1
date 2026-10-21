param(
 [Parameter(Mandatory)][string]$Packages,
 [Parameter(Mandatory)][string]$Bridge,
 [Parameter(Mandatory)][string]$Output,
 [string[]]$Baselines=@('artifacts/releases/1.4.14','artifacts/releases/1.4.7'),
 [ValidateSet('upgrade','startup-failure','interrupted-runtime','interrupted-before-entry')][string[]]$Cases=@('upgrade','startup-failure','interrupted-runtime','interrupted-before-entry')
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$out=[IO.Path]::GetFullPath($Output);$Packages=[IO.Path]::GetFullPath($Packages);$Bridge=[IO.Path]::GetFullPath($Bridge)
if(!$out.StartsWith((Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $out)){throw 'Choose a new isolated artifacts directory.'}
[IO.Directory]::CreateDirectory($out)|Out-Null
Add-Type -Path (Join-Path $Packages 'Lite/app/Dustweave.Core.dll')
$trust=Get-Content -LiteralPath (Join-Path $repo 'assets/updates/trust.json') -Raw|ConvertFrom-Json
function WriteJson($path,$value){[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 70),[Text.UTF8Encoding]::new($false))}
function Typed($value,$type){return [Text.Json.JsonSerializer]::Deserialize(($value|ConvertTo-Json -Depth 70),$type)}
function Sign($value,[string]$domain=''){
 $key=[Security.Cryptography.ECDsa]::Create()
 try{
  $raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
  try{$key.ImportPkcs8PrivateKey($raw,[ref]([int]0))}finally{[Array]::Clear($raw,0,$raw.Length)}
  $public=[Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo());$id=@($trust.Keys.PSObject.Properties|Where-Object Value -eq $public|ForEach-Object Name)
  if($id.Count -ne 1){throw 'Acceptance signing key differs from shipped trust.'}
  $payload=[Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 70 -Compress));$data=[Text.Encoding]::UTF8.GetBytes($domain)+$payload
  return @{Schema=1;KeyId=$id[0];Algorithm='ECDSA-P256-SHA256';Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($key.SignData($data,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation))}
 }finally{$key.Dispose()}
}
function Release($folder){
 $p=Get-Content -LiteralPath (Join-Path $folder 'Lite/update-package.json') -Raw|ConvertFrom-Json
 return [ordered]@{Version=$p.Version;PreviousVersion=$null;Layout=2;RuntimeVersion=$p.RuntimeVersion;InstallerProtocol=$(if($p.InstallerProtocol){$p.InstallerProtocol}else{1});Notes=@{'zh-CN'=@('隔离更新验收');'zh-TW'=@('隔離更新驗收');'en-US'=@('Isolated update acceptance')};Deltas=@();Assets=@(foreach($f in @('Lite','Portable')){$name="Dustweave-$($p.Version)-$f-win-x64.zip";$path=Join-Path $folder $name;@{Flavor=$f;FileName=$name;Bytes=(Get-Item -LiteralPath $path).Length;Sha256=(Get-FileHash -LiteralPath $path).Hash}})}
}
function Feed($releases,$schema){return @{Schema=$schema;Product='Dustweave';Channel='stable';Releases=@($releases)}}
$final=Release $Packages;$middle=Release $Bridge;$final.PreviousVersion=$middle.Version
$capable=Feed @($final,$middle) 4
WriteJson (Join-Path $Packages 'updates.json') (Sign $capable)
WriteJson (Join-Path $Bridge 'updates.json') (Sign (Feed @($middle) 3))
& (Join-Path $PSScriptRoot 'prepare-repair-package.ps1') -PackageDirectory $Packages -OutputDirectory (Join-Path $out 'final-repair')
& (Join-Path $PSScriptRoot 'prepare-repair-package.ps1') -PackageDirectory $Bridge -OutputDirectory (Join-Path $out 'bridge-repair')
$finalSigned=Sign $capable;$reports=[Collections.Generic.List[object]]::new()
function CopyTree($source,$destination){[IO.Directory]::CreateDirectory($destination)|Out-Null;foreach($entry in Get-ChildItem -LiteralPath $source){Copy-Item -LiteralPath $entry.FullName -Destination $destination -Recurse}}
function StartIsolated($exe,$arguments,$data){
 $i=[Diagnostics.ProcessStartInfo]::new($exe);$i.UseShellExecute=$false;$i.CreateNoWindow=$true;$i.WindowStyle='Hidden';$i.WorkingDirectory=$out
 $i.Environment['BD2_DAILY_DATA_ROOT']=$data;$i.Environment['DUSTWEAVE_PLUGIN']='none'
 foreach($a in $arguments){$i.ArgumentList.Add($a)}
 return [Diagnostics.Process]::Start($i)
}
function CloseTestProcesses($t){
 foreach($p in @(Get-Process Dustweave,Dustweave.Updater -ErrorAction SilentlyContinue)){
  try{$path=$p.Path}catch{continue}
  if(!$path -or !$path.StartsWith($t.folder+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){continue}
  [void]$p.CloseMainWindow();if(!$p.WaitForExit(15000)){throw "Isolated acceptance window did not close: $($p.Id)"}
 }
}
function Setup($name,$baseline,$flavor){
 $folder=Join-Path $out $name;$install=Join-Path $folder 'install';$data=Join-Path $folder 'data';CopyTree (Join-Path $baseline $flavor) $install
 foreach($record in @(@('updates-preference.json',@{Automatic=$false}),@('seen-release.json',@{Version=$final.Version;SeenVersions=@($final.Version,$middle.Version)}),@('schedule.json',@{Enabled=$false}),@('desktop-behavior.json',@{CloseAction='exit';RunOnOpen=$false}),@('first-run.json',@{Revision=1;State='completed';TourIndex=0}))){WriteJson (Join-Path $data $record[0]) $record[1]}
 [IO.Directory]::CreateDirectory((Join-Path $install 'plugins'))|Out-Null
 [IO.File]::WriteAllText((Join-Path $install 'plugins/acceptance.keep'),'private plugin sentinel');[IO.File]::WriteAllText((Join-Path $data 'account-preservation.keep'),'account sentinel')
 $cache=Join-Path $data "updates/$($final.Version)-$flavor";$sourceCache=Join-Path $data "updates/$($middle.Version)-$flavor"
 [IO.Directory]::CreateDirectory($cache)|Out-Null;[IO.Directory]::CreateDirectory($sourceCache)|Out-Null
 Copy-Item -LiteralPath (Join-Path $out "final-repair/repair/v$($final.Version)/Dustweave-$($final.Version)-$flavor-repair.json") -Destination (Join-Path $cache 'repair-manifest.json')
 Copy-Item -LiteralPath (Join-Path $out "bridge-repair/repair/v$($middle.Version)/Dustweave-$($middle.Version)-$flavor-repair.json") -Destination (Join-Path $sourceCache 'repair-manifest.json')
 Copy-Item -LiteralPath (Join-Path $sourceCache 'repair-manifest.json') -Destination (Join-Path $cache 'source-repair-manifest.json')
 WriteJson (Join-Path $cache 'signed-feed.json') $finalSigned
 CopyTree (Join-Path $Packages $flavor) (Join-Path $cache 'repair-staging')
 return @{folder=$folder;install=$install;target=(Join-Path $install 'Dustweave.exe');data=$data;cache=$cache;sourceCache=$sourceCache;flavor=$flavor}
}
function VerifyInstall($t,$expected){
 $p=Get-Content -LiteralPath (Join-Path $expected 'update-package.json') -Raw|ConvertFrom-Json
 foreach($name in $p.Files){if((Get-FileHash -LiteralPath (Join-Path $t.install $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $expected $name)).Hash){throw "Installed file differs: $name"}}
 if((Get-Content -LiteralPath (Join-Path $t.install 'plugins/acceptance.keep') -Raw) -ne 'private plugin sentinel' -or (Get-Content -LiteralPath (Join-Path $t.data 'account-preservation.keep') -Raw) -ne 'account sentinel'){throw 'User files changed'}
 if(Test-Path -LiteralPath (Join-Path $t.install '.dustweave-update.json')){throw 'Update marker remains'}
 if(Test-Path -LiteralPath (Join-Path $t.data 'run.json')){throw 'Automation ran during acceptance'}
}
function WaitResult($t,$state,$seconds=180){
 $deadline=[DateTime]::UtcNow.AddSeconds($seconds);$path=Join-Path $t.data 'update-result.json'
 while([DateTime]::UtcNow -lt $deadline){
  if(Test-Path -LiteralPath $path){$r=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json;if($r.Version -eq $final.Version -and $r.State -eq $state){return}}
  Start-Sleep -Milliseconds 300
 }
 throw "Final update did not reach $state; inspect $($t.folder)"
}
if('upgrade' -in $Cases){foreach($baseline in $Baselines){
 $baseline=[IO.Path]::GetFullPath($baseline)
 # Original release archives are immutable; extraction lives only under this acceptance.
 $version=Split-Path -Leaf $baseline;$original=Join-Path $out "baselines/$version"
 foreach($f in @('Lite','Portable')){[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $baseline "Dustweave-$version-$f-win-x64.zip"),(Join-Path $original $f));Copy-Item -LiteralPath (Join-Path $baseline "Dustweave-$version-$f-win-x64.zip") -Destination $original}
 $old=Release $original;$bridgeRelease=$middle|ConvertTo-Json -Depth 70|ConvertFrom-Json;$bridgeRelease.PreviousVersion=$old.Version
 $patches=Join-Path $out "patches/$version";[IO.Directory]::CreateDirectory($patches)|Out-Null
 $bridgeRelease.Deltas=@(foreach($f in @('Lite','Portable')){[Dustweave.DailyUpdateDeltaPackage]::Create((Join-Path $original "Dustweave-$version-$f-win-x64.zip"),(Join-Path $Bridge "Dustweave-$($middle.Version)-$f-win-x64.zip"),$patches)})
 $legacy=Feed @($bridgeRelease,$old) 3;$legacySigned=Sign $legacy
 foreach($f in @('Lite','Portable')){
  $t=Setup "upgrade-$version-$f" $original $f;$nonce=[Guid]::NewGuid().ToString('N');$attempt=Join-Path $t.sourceCache "attempts/$nonce";[IO.Directory]::CreateDirectory($attempt)|Out-Null
  $delta=$bridgeRelease.Deltas|Where-Object Flavor -eq $f
  if($delta.Bytes -gt 10MB){throw "Legacy bridge delta exceeds old automatic limit: $version $f"}
  Copy-Item -LiteralPath (Join-Path $patches $delta.FileName) -Destination $t.sourceCache
  WriteJson (Join-Path $t.sourceCache 'signed-feed.json') $legacySigned
  $helper=Join-Path $attempt 'Dustweave.Updater.exe';Copy-Item -LiteralPath (Join-Path $original "$f/app/support/Dustweave.Updater.exe") -Destination $helper
  $job=@{Directory=$t.sourceCache;Target=$t.target;ParentPid=[int]::MaxValue;ParentStartTicks=0;Release=$bridgeRelease;Asset=($bridgeRelease.Assets|Where-Object Flavor -eq $f);Nonce=$nonce;OriginalSha256=(Get-FileHash -LiteralPath $t.target).Hash;DeltaFileName=$delta.FileName}
  $jobPath=Join-Path $attempt 'job.json';WriteJson $jobPath $job
  $p=StartIsolated $helper @('--apply-update',$jobPath) $t.data
  WaitResult $t 'completed'
  if(!$p.WaitForExit(15000) -or $p.ExitCode -ne 0){throw 'Original updater did not complete'}
  VerifyInstall $t (Join-Path $Packages $f);CloseTestProcesses $t
  $reports.Add(@{case="upgrade-$version-$f";status='passed';originalHelper=$true;automaticSecondStage=$true;deltaBytes=$delta.Bytes;target=$final.Version;filesVerified=$true;automationStarted=$false})
  if($version -eq '1.4.14'){$reports.Add(@{case="upgrade-$f";status='passed';baselineHelper=$true;to=$final.Version;automaticSecondStage=$true;source=$version})}
  WriteJson (Join-Path $out 'results.json') @{status='running';cases=$reports.ToArray()};Write-Host "Passed upgrade-$version-$f"
 }
}}
function FinalJob($t){
 $nonce=[Guid]::NewGuid().ToString('N');$attempt=Join-Path $t.cache "attempts/$nonce";[IO.Directory]::CreateDirectory($attempt)|Out-Null
 $helper=Join-Path $attempt 'Dustweave.Updater.exe';Copy-Item -LiteralPath (Join-Path $Packages "$($t.flavor)/app/support/Dustweave.Updater.exe") -Destination $helper
 $job=@{Directory=$t.cache;Target=$t.target;ParentPid=[int]::MaxValue;ParentStartTicks=0;Release=$final;Asset=($final.Assets|Where-Object Flavor -eq $t.flavor);Nonce=$nonce;OriginalSha256=(Get-FileHash -LiteralPath $t.target).Hash;InventoryUpgrade=$true;FromVersion=$middle.Version}
 $t.attempt=$attempt;$t.helper=$helper;$t.job=$job;$t.jobPath=Join-Path $attempt 'job.json';WriteJson $t.jobPath $job
}
function DamageTarget($t,$name){
 $envlp=Get-Content -LiteralPath (Join-Path $t.cache 'repair-manifest.json') -Raw|ConvertFrom-Json;$manifest=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envlp.Payload))|ConvertFrom-Json
 $bytes=[Text.Encoding]::UTF8.GetBytes('Deliberately invalid update acceptance runtime.');$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
 $entry=$manifest.Files|Where-Object Name -eq $name;$entry.Bytes=$bytes.Length;$entry.Sha256=$hash;$entry.Blocks=@(@{Bytes=$bytes.Length;Sha256=$hash})
 $feed=$capable|ConvertTo-Json -Depth 70|ConvertFrom-Json;$asset=$feed.Releases[0].Assets|Where-Object Flavor -eq $t.flavor;$asset.Sha256=$hash;$manifest.ArchiveSha256=$hash
 WriteJson (Join-Path $t.cache 'signed-feed.json') (Sign $feed);WriteJson (Join-Path $t.cache 'repair-manifest.json') (Sign $manifest "Dustweave-Repair-v1`n")
 [IO.File]::WriteAllBytes((Join-Path $t.cache "repair-staging/$name"),$bytes)
 $t.job.Release=$feed.Releases[0];$t.job.Asset=$asset;WriteJson $t.jobPath $t.job;$t.manifest=$manifest
}
if('startup-failure' -in $Cases){foreach($f in @('Lite','Portable')){
 $t=Setup "rollback-$f" $Bridge $f;FinalJob $t;DamageTarget $t 'app/Dustweave.dll'
 $p=StartIsolated $t.helper @('--apply-update',$t.jobPath) $t.data;WaitResult $t 'rolled_back'
 if(!$p.WaitForExit(15000)){throw 'Rollback helper still running'}
 VerifyInstall $t (Join-Path $Bridge $f);Start-Sleep -Seconds 2;CloseTestProcesses $t
 $reports.Add(@{case="rollback-$f";status='passed';bootstrapRestored=$true;userFilesPreserved=$true;noRetryLoop=$true});Write-Host "Passed rollback-$f"
}}
if('interrupted-runtime' -in $Cases){
 $t=Setup 'interrupted-runtime' $Bridge 'Portable';FinalJob $t;DamageTarget $t 'app/coreclr.dll'
 $manifest=Typed $t.manifest ([type][Dustweave.DailyRepairManifest]);$retained=[Collections.Generic.Dictionary[string,string]]::new();$stage=Join-Path $t.cache 'repair-staging'
 $names=[Dustweave.DailyUpdateRepair]::VerifyStaging($manifest,$t.install,$stage,[ref]$retained)
 $source=Get-Content -LiteralPath (Join-Path $Bridge 'Portable/update-package.json') -Raw|ConvertFrom-Json
 $journal=[Dustweave.DailyUpdateTransaction]::Prepare($stage,$t.install,(Join-Path $t.attempt 'backup'),$names,$retained,$true,$true,[string[]]$source.Files)
 if($journal.Files[0].Name -ne 'Dustweave.exe' -or $journal.Files.Name -notcontains 'app/coreclr.dll'){throw 'Recovery entry/runtime not journaled'}
 WriteJson (Join-Path $t.install '.dustweave-update.json') @{JobPath=$t.jobPath;Nonce=$t.job.Nonce;HelperSha256=(Get-FileHash -LiteralPath $t.helper).Hash}
 try{[void][Dustweave.DailyUpdateTransaction]::Apply($journal,[Action[int]]{param($n) if($n -eq 1){throw 'Injected interruption'}})}catch{if($_.Exception.ToString() -notmatch 'Injected interruption'){throw}}
 [IO.File]::WriteAllText((Join-Path $t.install 'app/coreclr.dll'),'Interrupted runtime file')
 $p=StartIsolated $t.target @('--desktop-session') $t.data
 WaitResult $t 'rolled_back';if(!$p.WaitForExit(15000)){throw 'Native recovery entry did not exit'}
 VerifyInstall $t (Join-Path $Bridge 'Portable');Start-Sleep -Seconds 2;CloseTestProcesses $t
 $reports.Add(@{case='interrupted-runtime';status='passed';nativeEntry=$true;brokenClr=$true;physicalPowerCut=$false});Write-Host 'Passed interrupted-runtime'
}
if('interrupted-before-entry' -in $Cases){foreach($f in @('Lite','Portable')){
 $t=Setup "interrupted-before-entry-$f" $Bridge $f;FinalJob $t;DamageTarget $t 'app/Dustweave.dll'
 $manifest=Typed $t.manifest ([type][Dustweave.DailyRepairManifest]);$retained=[Collections.Generic.Dictionary[string,string]]::new();$stage=Join-Path $t.cache 'repair-staging'
 $names=[Dustweave.DailyUpdateRepair]::VerifyStaging($manifest,$t.install,$stage,[ref]$retained)
 $source=Get-Content -LiteralPath (Join-Path $Bridge "$f/update-package.json") -Raw|ConvertFrom-Json
 $journal=[Dustweave.DailyUpdateTransaction]::Prepare($stage,$t.install,(Join-Path $t.attempt 'backup'),$names,$retained,$true,$true,[string[]]$source.Files)
 WriteJson (Join-Path $t.install '.dustweave-update.json') @{JobPath=$t.jobPath;Nonce=$t.job.Nonce;HelperSha256=(Get-FileHash -LiteralPath $t.helper).Hash}
 $p=StartIsolated $t.target @('--desktop-session') $t.data;WaitResult $t 'rolled_back'
 if(!$p.WaitForExit(15000)){throw 'Original recovery entry did not exit'}
 VerifyInstall $t (Join-Path $Bridge $f);Start-Sleep -Seconds 2;CloseTestProcesses $t
 $reports.Add(@{case="interrupted-before-entry-$f";status='passed';originalEntry=$true;newHelper=$true});Write-Host "Passed interrupted-before-entry-$f"
}}
if(@($reports|Where-Object {$_.case -eq 'rollback-Lite' -and $_.status -eq 'passed'}).Count){$reports.Add(@{case='startup-failure';status='passed';to=$final.Version})}
if(@($reports|Where-Object {$_.case -eq 'interrupted-runtime' -and $_.status -eq 'passed'}).Count){$reports.Add(@{case='bootstrap-recovery';status='passed';to=$final.Version;brokenClr=$true})}
WriteJson (Join-Path $out 'results.json') @{status='passed';target=$final.Version;cases=$reports.ToArray();preparedPayloads=$true;realGameTouched=$false;published=$false}
Write-Host "Bridge acceptance passed: $out"
