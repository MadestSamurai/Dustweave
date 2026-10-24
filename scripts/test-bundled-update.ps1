param(
 [Parameter(Mandatory)][string]$Packages,
 [Parameter(Mandatory)][string]$Baseline,
 [Parameter(Mandatory)][string]$CoreAssembly,
 [Parameter(Mandatory)][string]$Output,
 [ValidateSet('upgrade','rollback','interrupt-host','interrupt-helper','damaged-base')][string[]]$Cases=@('upgrade','rollback','interrupt-host','interrupt-helper','damaged-base')
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$out=[IO.Path]::GetFullPath($Output);$Packages=[IO.Path]::GetFullPath($Packages);$Baseline=[IO.Path]::GetFullPath($Baseline)
if(!$out.StartsWith((Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $out)){throw 'Use a new isolated artifacts directory.'}
[IO.Directory]::CreateDirectory($out)|Out-Null
Add-Type -Path ([IO.Path]::GetFullPath($CoreAssembly))
$trust=Get-Content (Join-Path $repo 'assets/updates/trust-aio.json') -Raw|ConvertFrom-Json
$reports=[Collections.Generic.List[object]]::new()
function WriteJson($path,$value){[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 70),[Text.UTF8Encoding]::new($false))}
function Typed($value,$type){return [Text.Json.JsonSerializer]::Deserialize(($value|ConvertTo-Json -Depth 70),$type)}
function Sign($value,[string]$domain=''){
 $key=[Security.Cryptography.ECDsa]::Create()
 try{
  $raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
  try{$key.ImportPkcs8PrivateKey($raw,[ref]([int]0))}finally{[Array]::Clear($raw,0,$raw.Length)}
  $public=[Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo());$id=@($trust.Keys.PSObject.Properties|Where-Object Value -eq $public|ForEach-Object Name)
  if($id.Count -ne 1){throw 'Fixture key does not match shipped trust.'}
  $payload=[Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 70 -Compress));$data=[Text.Encoding]::UTF8.GetBytes($domain)+$payload
  return @{Schema=1;KeyId=$id[0];Algorithm='ECDSA-P256-SHA256';Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($key.SignData($data,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation))}
 }finally{$key.Dispose()}
}
function Release($folder){
 $p=Get-Content (Join-Path $folder 'Lite/update-package.json') -Raw|ConvertFrom-Json
 return @{Version=$p.Version;PreviousVersion=$null;Layout=3;RuntimeVersion=$p.RuntimeVersion;InstallerProtocol=1;Notes=@{'zh-CN'=@('隔离成品验收');'zh-TW'=@('隔離成品驗收');'en-US'=@('Isolated package acceptance')};Deltas=@();Assets=@(foreach($f in @('Lite','Portable')){$name="Dustweave-$($p.Version)-$f-win-x64.zip";$path=Join-Path $folder $name;@{Flavor=$f;FileName=$name;Bytes=(Get-Item $path).Length;Sha256=(Get-FileHash $path).Hash}})}
}
function CopyTree($source,$destination){[IO.Directory]::CreateDirectory($destination)|Out-Null;foreach($entry in Get-ChildItem -LiteralPath $source){Copy-Item -LiteralPath $entry.FullName -Destination $destination -Recurse}}
function StartIsolated($exe,$arguments,$data){
 $i=[Diagnostics.ProcessStartInfo]::new($exe);$i.UseShellExecute=$false;$i.CreateNoWindow=$true;$i.WindowStyle='Hidden';$i.WorkingDirectory=$out
 $i.Environment['BD2_DAILY_DATA_ROOT']=$data;$i.Environment['DUSTWEAVE_PLUGIN']='none'
 foreach($arg in $arguments){$i.ArgumentList.Add($arg)}
 return [Diagnostics.Process]::Start($i)
}
$old=Release $Baseline;$next=Release $Packages;$next.PreviousVersion=$old.Version
$patches=Join-Path $out 'patches';[IO.Directory]::CreateDirectory($patches)|Out-Null
foreach($flavor in @('Lite','Portable')){
 $oldZip=Join-Path $Baseline ($old.Assets|Where-Object Flavor -eq $flavor).FileName
 $newZip=Join-Path $Packages ($next.Assets|Where-Object Flavor -eq $flavor).FileName
 $delta=[Dustweave.DailyUpdateDeltaPackage]::Create($oldZip,$newZip,$patches)
 if($delta.Bytes -gt 10MB){throw 'Acceptance delta exceeds production limit'}
 $next.Deltas+=($delta|ConvertTo-Json|ConvertFrom-Json)
}
$feed=@{Schema=3;Product='Dustweave';Channel='stable';Releases=@($next,$old)}
[Dustweave.DailyUpdates]::Validate((Typed $feed ([type][Dustweave.DailyUpdateFeed])))
$signed=Sign $feed;WriteJson (Join-Path $out 'feed.json') $signed
function Setup($name,$flavor){
 $folder=Join-Path $out "$name-$flavor";$install=Join-Path $folder 'install';$data=Join-Path $folder 'data'
 CopyTree (Join-Path $Baseline $flavor) $install
 foreach($r in @(@('updates-preference.json',@{Automatic=$false}),@('seen-release.json',@{Version=$next.Version;SeenVersions=@($old.Version,$next.Version)}),@('schedule.json',@{Enabled=$false}),@('desktop-behavior.json',@{CloseAction='exit';RunOnOpen=$false}),@('first-run.json',@{Revision=1;State='completed';TourIndex=0}))){WriteJson (Join-Path $data $r[0]) $r[1]}
 [IO.Directory]::CreateDirectory((Join-Path $install 'plugins'))|Out-Null
 [IO.File]::WriteAllText((Join-Path $install 'plugins/acceptance.keep'),'plugin sentinel');[IO.File]::WriteAllText((Join-Path $data 'account-preservation.keep'),'account sentinel')
 $cache=Join-Path $data "updates/$($next.Version)-$flavor";[IO.Directory]::CreateDirectory($cache)|Out-Null
 WriteJson (Join-Path $cache 'signed-feed.json') $signed
 $delta=$next.Deltas|Where-Object Flavor -eq $flavor
 Copy-Item (Join-Path $patches $delta.FileName) (Join-Path $cache $delta.FileName)
 $nonce=[Guid]::NewGuid().ToString('N');$attempt=Join-Path $cache "attempts/$nonce";[IO.Directory]::CreateDirectory($attempt)|Out-Null
 $helper=Join-Path $attempt 'Dustweave.Updater.exe'
 Copy-Item (Join-Path $Baseline "$flavor/Dustweave.exe") $helper
 $target=Join-Path $install 'Dustweave.exe'
 $job=@{Directory=$cache;Target=$target;ParentPid=[int]::MaxValue;ParentStartTicks=0;Release=$next;Asset=($next.Assets|Where-Object Flavor -eq $flavor);Nonce=$nonce;OriginalSha256=(Get-FileHash $target).Hash;DeltaFileName=$delta.FileName;Repair=$false;FromVersion=$old.Version}
 $jobPath=Join-Path $attempt 'job.json';WriteJson $jobPath $job
 return @{folder=$folder;install=$install;target=$target;data=$data;cache=$cache;attempt=$attempt;helper=$helper;job=$job;jobPath=$jobPath;flavor=$flavor;delta=$delta}
}
function Verify($t,$expected){
 $descriptor=Get-Content (Join-Path $expected 'update-package.json') -Raw|ConvertFrom-Json
 foreach($name in $descriptor.Files){if((Get-FileHash (Join-Path $t.install $name)).Hash -ne (Get-FileHash (Join-Path $expected $name)).Hash){throw "Installed bytes differ: $name"}}
 if((Get-Content (Join-Path $t.install 'plugins/acceptance.keep') -Raw) -ne 'plugin sentinel' -or (Get-Content (Join-Path $t.data 'account-preservation.keep') -Raw) -ne 'account sentinel'){throw 'User data changed'}
 if(Test-Path (Join-Path $t.install '.dustweave-update.json')){throw 'Recovery marker remains'}
 if(Test-Path (Join-Path $t.data 'run.json')){throw 'Unexpected automation'}
}
function WaitResult($t,$state){
 $deadline=[DateTime]::UtcNow.AddSeconds(150);$path=Join-Path $t.data 'update-result.json'
 while([DateTime]::UtcNow -lt $deadline){
  if(Test-Path $path){$r=Get-Content $path -Raw|ConvertFrom-Json;if($r.Version -eq $next.Version -and $r.State -eq $state){return}}
  Start-Sleep -Milliseconds 250
 }
 throw "Expected ${state}: inspect $($t.folder)"
}
function Finish($t,$name,$state,$expected){
 WaitResult $t $state;Verify $t $expected
 $version=(Get-Content (Join-Path $expected 'update-package.json') -Raw|ConvertFrom-Json).Version
 $deadline=[DateTime]::UtcNow.AddSeconds(30);$visible=$false
 while([DateTime]::UtcNow -lt $deadline){
  $path=Join-Path $t.data 'startup-state.json'
  if(Test-Path $path){$v=Get-Content $path -Raw|ConvertFrom-Json;if($v.stage -eq 'main-window-visible' -and $v.version -eq $version){$visible=$true;break}}
  Start-Sleep -Milliseconds 200
 }
 if(!$visible){throw 'Expected package never reached its window'}
 # Test teardown only, after byte-level/result/startup assertions.
 foreach($p in @(Get-Process Dustweave -ErrorAction SilentlyContinue|Where-Object {[string]::Equals($_.Path,$t.target,[StringComparison]::OrdinalIgnoreCase)})){
  if(!$p.Path.StartsWith($out+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Not a fixture process'}
  $p.Kill();if(!$p.WaitForExit(10000)){throw 'Fixture teardown timed out'}
 }
 $reports.Add(@{case="$name-$($t.flavor)";status='passed';state=$state;to=$next.Version;baselineHelper=($name -eq 'upgrade');byteExact=$true;userDataPreserved=$true;startupAcknowledged=$true})
 WriteJson (Join-Path $out 'results.json') @{status='running';cases=$reports.ToArray()};Write-Host "Passed $name-$($t.flavor)"
}
function FaultTarget($t){
 $badZip=Join-Path $t.folder 'bad-target.zip'
 Copy-Item (Join-Path $Packages ($next.Assets|Where-Object Flavor -eq $t.flavor).FileName) $badZip
 $zip=[IO.Compression.ZipFile]::Open($badZip,[IO.Compression.ZipArchiveMode]::Update)
 try{$zip.GetEntry('Dustweave.exe').Delete();$stream=$zip.CreateEntry('Dustweave.exe').Open();try{$stream.Write([Text.Encoding]::UTF8.GetBytes('Invalid executable: startup rollback fixture'))}finally{$stream.Dispose()}}finally{$zip.Dispose()}
 $faultFeed=$feed|ConvertTo-Json -Depth 70|ConvertFrom-Json;$asset=$faultFeed.Releases[0].Assets|Where-Object Flavor -eq $t.flavor
 $asset.Bytes=(Get-Item $badZip).Length;$asset.Sha256=(Get-FileHash $badZip).Hash
 $delta=[Dustweave.DailyUpdateDeltaPackage]::Create((Join-Path $Baseline ($old.Assets|Where-Object Flavor -eq $t.flavor).FileName),$badZip,(Join-Path $t.folder 'bad-patch'))
 Copy-Item (Join-Path $t.folder "bad-patch/$($delta.FileName)") (Join-Path $t.cache $delta.FileName) -Force
 $faultFeed.Releases[0].Deltas=@($faultFeed.Releases[0].Deltas|Where-Object Flavor -ne $t.flavor)+@($delta)
 WriteJson (Join-Path $t.cache 'signed-feed.json') (Sign $faultFeed)
 $t.job.Release=$faultFeed.Releases[0];$t.job.Asset=$asset;WriteJson $t.jobPath $t.job
}
foreach($flavor in @('Lite','Portable')){foreach($case in $Cases){
 $t=Setup $case $flavor
 switch($case){
  'upgrade'{

   $p=StartIsolated $t.helper @('--apply-update',$t.jobPath) $t.data
   Finish $t $case 'completed' (Join-Path $Packages $flavor)
  }
  'rollback'{
   FaultTarget $t;$p=StartIsolated $t.helper @('--apply-update',$t.jobPath) $t.data
   Finish $t $case 'rolled_back' (Join-Path $Baseline $flavor)
  }
  {$_ -in @('interrupt-host','interrupt-helper')}{
   $stage=Join-Path $t.attempt 'staging'
   $names=[Dustweave.DailyUpdateDeltaPackage]::Extract((Join-Path $t.cache $t.delta.FileName),$t.install,$stage,(Typed $t.job.Release ([type][Dustweave.DailyUpdateRelease])),(Typed $t.job.Asset ([type][Dustweave.DailyUpdateAsset])),(Typed $t.delta ([type][Dustweave.DailyUpdateDelta])),[Threading.CancellationToken]::None,$true)
   $journal=[Dustweave.DailyUpdateTransaction]::Prepare($stage,$t.install,(Join-Path $t.attempt 'backup'),$names)
   WriteJson (Join-Path $t.install '.dustweave-update.json') @{JobPath=$t.jobPath;Nonce=$t.job.Nonce;HelperSha256=(Get-FileHash $t.helper).Hash}
   try{[void][Dustweave.DailyUpdateTransaction]::Apply($journal,[Action[int]]{param($n) if($n -eq 1){throw 'Injected interruption'}})}catch{if($_.Exception.ToString() -notmatch 'Injected interruption'){throw}}
   if($case -eq 'interrupt-helper'){
    [IO.File]::WriteAllText($t.target,'Interrupted bundle bytes')
    $p=StartIsolated $t.helper @('--recover-update',$t.jobPath) $t.data
   }else{$p=StartIsolated $t.target @('--desktop-session') $t.data}
   Finish $t $case 'rolled_back' (Join-Path $Baseline $flavor)
  }
  'damaged-base'{
   [IO.File]::WriteAllText($t.target,'Damaged original bundle')
   $t.job.OriginalSha256=(Get-FileHash $t.target).Hash;WriteJson $t.jobPath $t.job
   $before=@{};Get-ChildItem -LiteralPath $t.install -Recurse -File|ForEach-Object {$before[$_.FullName]=(Get-FileHash $_.FullName).Hash}
   $p=StartIsolated $t.helper @('--apply-update',$t.jobPath) $t.data
   WaitResult $t 'failed'
   $result=Get-Content (Join-Path $t.data 'update-result.json') -Raw
   if($result -notmatch 'updates.manual_delta'){throw 'Expected manual download guidance'}
   foreach($name in $before.Keys){if((Get-FileHash $name).Hash -ne $before[$name]){throw 'Damaged installation was modified'}}
   if(Test-Path (Join-Path $t.install '.dustweave-update.json')){throw 'Failed delta left a transaction marker'}
   $reports.Add(@{case="damaged-base-$flavor";status='passed';to=$next.Version;manualDownload=$true;installationUnchanged=$true;fullPackageDownloaded=$false})
   Write-Host "Passed damaged-base-$flavor"
  }
 }
 if(!$p.WaitForExit(10000)){throw "Helper is still active: $($p.Id)"}
}}
WriteJson (Join-Path $out 'results.json') @{status='passed';baseline=$old.Version;target=$next.Version;assets=$next.Assets;cases=$reports.ToArray();preparedPayloads=$true;physicalPowerCut=$false;realGameTouched=$false;published=$false}
