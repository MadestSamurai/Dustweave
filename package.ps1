param(
 [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')][string]$Version,
 [Parameter(Mandatory)][string]$GameManagedDir,
 [ValidateSet('Portable','Lite')][string[]]$Flavors=@('Portable','Lite'),
 [string]$PreparedCacheDirectory='',
 [ValidateSet('Auto','Accounts','Navigation','Rewards','Trading','Weekly','Updates','UiReadiness','Full')][string]$ValidationScope='Auto',
 [string]$ValidationBaseline='',
 [string]$SyntheticEvidence='',
 [string]$SyntheticInputs='',
 [string]$OutputDirectory=''
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)
. (Join-Path $root 'scripts/copy-prepared-cache.ps1')
. (Join-Path $root 'scripts/publish-layout.ps1')

if(!$PreparedCacheDirectory){$PreparedCacheDirectory=Join-Path $root 'artifacts/prepared-modules'}
$PreparedCacheDirectory=[IO.Path]::GetFullPath($PreparedCacheDirectory)
if(!$PreparedCacheDirectory.StartsWith([IO.Path]::GetFullPath((Get-Location).Path)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Prepared cache must remain below the current workspace.'}
$entry=Join-Path $root 'src/Desktop/Dustweave.Desktop.csproj'
if(!(Test-Path -LiteralPath (Join-Path $root 'source-manifest.json'))){throw 'Run this script from the Dustweave repository.'}
if(!$Flavors.Count -or @($Flavors|Select-Object -Unique).Count -ne $Flavors.Count){throw 'Choose each flavor at most once.'}
$managed=[IO.Path]::GetFullPath($GameManagedDir)
if(!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))){throw 'Provide the installed game Managed directory; the game does not need to run.'}
$game=Split-Path -Parent (Split-Path -Parent $managed)
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root ('artifacts/releases/'+$Version)}
if(!$output.StartsWith((Join-Path $root 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Release output must remain inside repository artifacts.'}
if(Test-Path -LiteralPath $output){throw 'Output already exists. Preserve it and use a new release version.'}
$work=Join-Path $root ('artifacts/package-'+$Version+'-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work)|Out-Null
[IO.Directory]::CreateDirectory($output)|Out-Null
function Run([string]$file,[string[]]$arguments,[string]$name,[int]$seconds=300,[string]$smokeScope=''){
 $start=[Diagnostics.ProcessStartInfo]::new($file);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.WorkingDirectory=(Get-Location).Path
 $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $start.Environment['BD2_DAILY_DATA_ROOT']=Join-Path $work ($name+'-daily')
 $start.Environment['BD2_EQUIPMENT_DATA_ROOT']=Join-Path $work ($name+'-equipment')
 $start.Environment['DUSTWEAVE_PLUGIN']='none'
 $start.Environment.Remove('DUSTWEAVE_UI_SMOKE_SCOPE')|Out-Null
 if($smokeScope){$start.Environment['DUSTWEAVE_UI_SMOKE_SCOPE']=$smokeScope}
 foreach($arg in $arguments){$start.ArgumentList.Add($arg)}
 $proc=[Diagnostics.Process]::Start($start)
 $stdout=$proc.StandardOutput.ReadToEndAsync();$stderr=$proc.StandardError.ReadToEndAsync()
 $watch=[Diagnostics.Stopwatch]::StartNew()
 while(!$proc.WaitForExit(1000)){if($watch.Elapsed.TotalSeconds -gt $seconds){throw "Operation is still running (PID $($proc.Id)): $name. Inspect it before retrying."}}
 $stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()|Set-Content -LiteralPath (Join-Path $work ($name+'.log')) -Encoding utf8
 if($proc.ExitCode -ne 0){throw "Failed $name ($($proc.ExitCode)); see $work"}
 $proc.Dispose()
}
function Pass([string]$path){
 if(!(Test-Path -LiteralPath $path)){throw "Missing verification: $path"}
 $value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
 if($value.status -ne 'passed'){throw "Verification did not pass: $path"}
}
function SourceState {
 $files=@(foreach($folder in @('src','tools','tests','assets','third-party','licenses','scripts','docs','.github')){
  Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}
 })+@(Get-ChildItem -LiteralPath $root -File -Force)
 $rows=$files|ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($root,$_.FullName);hash=(Get-FileHash -LiteralPath $_.FullName).Hash}}
 $rows | Sort-Object path | ConvertTo-Json -Depth 4 -Compress
}
& (Join-Path $root 'check-source.ps1')
$before=SourceState
. (Join-Path $root 'scripts/validation-plan.ps1')
$validation=Get-DustweaveValidationPlan $root $before $ValidationScope $ValidationBaseline
Write-Host ("Validation scope: "+$validation.scope+" — "+$validation.reason)
$before|Set-Content -LiteralPath (Join-Path $work 'source-before.json') -Encoding utf8
$packageImport='-p:CustomAfterMicrosoftCommonTargets='+ (Join-Path $root 'Package.Build.targets')
Run 'dotnet' @('build',$entry,'-c','Release','-r','win-x64','-p:SelfContained=false',$packageImport,('-p:DustweavePackageLockRoot='+ (Join-Path $work 'locks/build')),'--nologo') 'build'
foreach($component in @('tools/TradeData','tools/CompatibilityCli','tests/Dustweave.Tests','src/Launcher','src/Updater')){
 Run 'dotnet' @('build',(Join-Path $root $component),'-c','Release','-r','win-x64','-p:SelfContained=false',$packageImport,('-p:DustweavePackageLockRoot='+ (Join-Path $work 'locks/build')),'--nologo') ('build-'+($component -replace '/','-'))
}
Run 'dotnet' @('build',(Join-Path $root 'src/Bootstrap'),'-c','Release','-r','win-x64','--nologo') 'build-bootstrap'
& (Join-Path $root 'check-source.ps1') -AfterBuild
if($SyntheticEvidence){
 $proof=Get-Content -LiteralPath $SyntheticEvidence -Raw|ConvertFrom-Json
 if($validation.scope -ne 'Full' -or $proof.status -ne 'passed' -or $proof.scope -ne 'full synthetic regressions' -or $proof.realGameTouched -ne $false -or $proof.sourceRoot -ne $root){throw 'Invalid synthetic validation baseline.'}
 $old=@{};foreach($row in (Get-Content -LiteralPath $SyntheticInputs -Raw|ConvertFrom-Json)){$old[$row.path.Replace('\','/')]=[string]$row.hash}
 $current=@{};foreach($row in ($before|ConvertFrom-Json)){$current[$row.path.Replace('\','/')]=[string]$row.hash}
 # Package-only targets are exercised by actual artifact gates below; synthetic builds do not enable shared resources.
 $changed=@(@($old.Keys)+@($current.Keys)|Sort-Object -Unique|Where-Object {$_ -match '^(src|tools|tests|assets|standalone|third-party)/|^Directory.Build.props$|^global.json$' -and $old[$_] -ne $current[$_]})
 # These UI sources are not compiled into Dustweave.Tests. Full package gates rerun
 # the host's localization checks for every flavor and each listed tool's actual UI smoke.
 # Native entry sources are not compiled into synthetic tests. Actual package
 # startup/UI gates run again; native recovery also has its own acceptance.
 $publisherRechecks=@($changed|Where-Object {$_ -match '^tools/OtaChannel/(Rules|Mirror|CapabilitySelfTest)\.cs$'})
 $artifactRechecks=@($changed|Where-Object {$_ -in $publisherRechecks -or $_ -match '^src/Bootstrap/(Program|Recovery|RepairBootstrap)\.cs$' -or $_ -in @('src/Desktop/MainWindow.Localization.cs','src/Fishing/desktop/Smoke.cs','src/Rhythm/desktop/Smoke.cs','src/Sichuan/desktop/SichuanWindow.xaml.cs','src/InfiniteGacha/desktop/MainWindow.xaml.cs')})
 # These WPF host sources are not compiled into Dustweave.Tests. The actual
 # Portable/Lite startup and UI gates below exercise them on every package.
 $artifactRechecks+=@($changed|Where-Object {$_ -in @('src/Desktop/MainWindow.xaml.cs','src/Desktop/MainWindow.Updates.Smoke.cs','src/Desktop/Localization/runtime.json','src/Desktop/Localization/notices.json')})
 $syntheticChanges=@($changed|Where-Object {$_ -notin $artifactRechecks})
 if($syntheticChanges.Count){throw ('Synthetic baseline inputs changed: '+($syntheticChanges -join ', '))}
 # Publisher sources are outside Dustweave.Tests and have their own executable gate.
 if($publisherRechecks.Count){Run 'dotnet' @('run','--project',(Join-Path $root 'tools/OtaChannel'),'-c','Release','--','self-test') 'ota-channel-self-test'}
 $validation.syntheticEvidence=[IO.Path]::GetFullPath($SyntheticEvidence)
 $validation.syntheticInputs=[IO.Path]::GetFullPath($SyntheticInputs)
 $validation.artifactRechecks=$artifactRechecks
 Copy-Item -LiteralPath $SyntheticEvidence -Destination (Join-Path $work 'reused-synthetic-results.json')
}else{& (Join-Path $root 'test.ps1') -NoBuild -Groups $validation.groups}
Run 'dotnet' @('run','--project',(Join-Path $root 'tools/CompatibilityCli'),'-c','Release','-r','win-x64','--no-build','--','client-inputs',$managed,(Join-Path $work 'client-before.json')) 'client-before'
$tools=@('bd2-fishing','bd2-sichuan','bd2-rhythm','bd2-territory','bd2-equipment-assistant','bd2-apostle-defense','bd2-infinite-gacha','bd2-secret-vision','bd2-fiend-hunter')
$tables=Join-Path $work 'tables'
$tableNames='CookingTable,FoodTable,ProductTable,SellItemTable,ShopTable,TalentSkillTable,TalentTable,NameTextTable,CharTable,EventMissionGroupTable,GachaGroupTable,GachaTable,MissionSectionRewardTable,LocalTextTable,MissionTable,DispatchTable,EquipmentMakingTable,EquipmentTable,HuntDispatchTable,PassTable,SkyWayFieldTable,SquareRewardTable,StatueRewardTable,RewardGroupTable,RandomBoxTable,CostumeTable,PackTable,MapTable'
$groups=[ordered]@{extra=@('EventMissionGroupTable');gacha=@('GachaGroupTable','GachaTable','MissionSectionRewardTable');missions=@('LocalTextTable','MissionTable');names=@('NameTextTable');policy=@('CharTable','DispatchTable','EquipmentMakingTable','EquipmentTable','HuntDispatchTable','MissionTable','PassTable','SkyWayFieldTable','SquareRewardTable','StatueRewardTable')}
$groups['draw-report']=@('CostumeTable','CharTable','EquipmentTable','NameTextTable')
if($validation.scope -ne 'Full'){
 $currentClient=Get-Content -LiteralPath (Join-Path $work 'client-before.json') -Raw|ConvertFrom-Json
 $baselineClient=Get-Content -LiteralPath (Join-Path $validation.baseline 'client-code-inputs.json') -Raw|ConvertFrom-Json
 if($currentClient.gameKey -ne $baselineClient.gameKey){throw 'Game code changed; rerun with -ValidationScope Full.'}
}
if($validation.scope -eq 'Full'){
$bootstrap=Join-Path $work 'cold-bootstrap'
Run 'dotnet' @('run','--project',(Join-Path $root 'tools/CompatibilityCli'),'-c','Release','-r','win-x64','--no-build','--','bootstrap',$managed,$bootstrap) 'cold-bootstrap-prepare'
Run 'dotnet' @('run','--project',(Join-Path $root 'tools/CompatibilityCli'),'-c','Release','-r','win-x64','--no-build','--','bootstrap-host',$managed,$bootstrap) 'cold-bootstrap-host'
Pass (Join-Path $bootstrap 'validation.json')
}
$flavorReports=@()
foreach($flavor in $Flavors){
 $self=if($flavor -eq 'Portable'){'true'}else{'false'}
 $bundle=Join-Path $output $flavor
 Publish-DustweaveLayout -Root $root -Work $work -Destination $bundle -Version $Version -Flavor $flavor
 $exe=Join-Path $bundle 'Dustweave.exe'
 $checks=Join-Path $work ($flavor+'-checks');[IO.Directory]::CreateDirectory($checks)|Out-Null
 [ordered]@{protocol=1;tools=@('live','minigame','exporter','diagnostics')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $bundle 'utility-host.json') -Encoding utf8
 if($flavor -eq $Flavors[0]){
  Run $exe @('--utility','exporter','--game-root',$game,'--tables',$tableNames,'--simulator-compatible','--output',$tables) 'export-tables'
  Run 'dotnet' @('run','--project',(Join-Path $root 'tools/TradeData'),'-c','Release','-r','win-x64','--no-build','--',$tables,(Join-Path $managed 'Assembly-CSharp.dll'),(Join-Path $work 'trade-catalog.json')) 'trade-catalog'
  $dropTables=Join-Path $work 'drop-tables';[IO.Directory]::CreateDirectory((Join-Path $dropTables 'common'))|Out-Null
  foreach($name in @('RewardGroupTable.json','RandomBoxTable.json','manifest.json')){Copy-Item -LiteralPath (Join-Path $tables $name) -Destination (Join-Path $dropTables 'common')}
  $collection=Get-Content -LiteralPath (Join-Path $root 'assets/flows/collection-catalog.json') -Raw|ConvertFrom-Json
  $dropPacks=@((1..19)+@($collection.cartridges | ForEach-Object {$_.id}) | Sort-Object -Unique)
  foreach($pack in $dropPacks){Run $exe @('--utility','exporter','--game-root',$game,'--pack',[string]$pack,'--tables','FieldRewardObjectTable,FieldRewardObjectGroupTable,FieldMonsterTable,FieldMonsterRegenTable,BattleDeckTable','--simulator-compatible','--output',(Join-Path $dropTables ([string]$pack))) ('drop-tables-'+$pack)}
  Run 'dotnet' @('run','--project',(Join-Path $root 'tools/TradeData'),'-c','Release','-r','win-x64','--no-build','--','--drop-model',$dropTables,(Join-Path $work 'trade-catalog.json'),(Join-Path $work 'trade-drops.json')) 'trade-drops'
 }
 foreach($group in $groups.GetEnumerator()){
  $target=Join-Path $bundle ('data/daily/'+$group.Key);[IO.Directory]::CreateDirectory($target)|Out-Null
  foreach($table in $group.Value){Copy-Item -LiteralPath (Join-Path $tables ($table+'.json')) -Destination $target}
  Copy-Item -LiteralPath (Join-Path $tables 'manifest.json') -Destination $target
 }
 $locations=Join-Path $bundle 'data/locations/locations';[IO.Directory]::CreateDirectory($locations)|Out-Null
 foreach($name in @('PackTable.json','MapTable.json','NameTextTable.json','manifest.json')){
  Copy-Item -LiteralPath (Join-Path $tables $name) -Destination $locations
 }
 Copy-Item -LiteralPath (Join-Path $work 'trade-catalog.json') -Destination (Join-Path $bundle 'data/trade-catalog.json')
 Copy-Item -LiteralPath (Join-Path $work 'trade-drops.json') -Destination (Join-Path $bundle 'data/trade-drops.json')
 foreach($asset in @(@{source='assets/flows';target='flows'},@{source='assets/specs';target='connection/specs'})){
  $target=Join-Path $bundle $asset.target
  [IO.Directory]::CreateDirectory($target)|Out-Null
  Get-ChildItem -LiteralPath (Join-Path $root $asset.source) -File -Filter '*.json'|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $target}
 }
 Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $bundle -Recurse
 Copy-Item -LiteralPath (Join-Path $root 'third-party/highs/LICENSE.txt') -Destination (Join-Path $bundle 'licenses/HiGHS.txt')
 Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $bundle
 Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $bundle
 foreach($repository in $tools){
  $sourceDirectory=switch($repository){'bd2-fishing'{'src/Fishing'} 'bd2-territory'{'src/Territory'} 'bd2-equipment-assistant'{'src/Equipment'} 'bd2-apostle-defense'{'src/ApostleDefense'} 'bd2-infinite-gacha'{'src/InfiniteGacha'} 'bd2-fiend-hunter'{'src/FiendHunter'} 'bd2-rhythm'{'src/Rhythm'} 'bd2-sichuan'{'src/Sichuan'} 'bd2-secret-vision'{'src/SecretVision'} default{'standalone/'+$repository}}
  $module=Join-Path $root $sourceDirectory;$guide=Join-Path $bundle ('docs/tools/'+$repository);[IO.Directory]::CreateDirectory($guide)|Out-Null
  foreach($name in @('README.md','README.en.md','README_EN.md','THIRD_PARTY_NOTICES.md')){if(Test-Path -LiteralPath (Join-Path $module $name)){Copy-Item -LiteralPath (Join-Path $module $name) -Destination $guide}}
  Copy-Item -LiteralPath (Join-Path $module 'LICENSE') -Destination (Join-Path $bundle ('licenses/'+$repository+'-MIT.txt'))
 }
 foreach($name in @('README.md','README.en.md')){Copy-Item -LiteralPath (Join-Path $root 'docs/package' $name) -Destination $bundle}
 Complete-DustweaveLayout -Root $root -Destination $bundle
 Run $exe @('--identity',(Join-Path $checks 'identity.txt')) ($flavor+'-identity')
 if((Get-Content -LiteralPath (Join-Path $checks 'identity.txt') -First 1) -ne ('Dustweave '+$Version)){throw 'Release label differs from executable.'}
 $utilityAssemblies=[ordered]@{live='Dustweave.Connection';minigame='Dustweave.ToolHost';exporter='Dustweave.TableExporter';diagnostics='Dustweave.RuntimeCheck'}
 foreach($utility in $utilityAssemblies.GetEnumerator()){
  $description=Join-Path $checks ('utility-'+$utility.Key+'.json')
  Run $exe @('--utility',$utility.Key,'--describe',$description) ($flavor+'-utility-'+$utility.Key)
  $identity=Get-Content -LiteralPath $description -Raw|ConvertFrom-Json
  if($identity.assembly -ne $utility.Value -or $identity.tool -ne $utility.Key -or $identity.realGameTouched -ne $false){throw "Packaged utility identity differs: $($utility.Key)"}
 }
 $modes=if($validation.scope -in @('Full','Updates')){@('smoke','check-tool-languages','check-tool-language-ui','check-stage-host','check-tool-session','check-suite-host')}else{@('smoke')}
 foreach($mode in $modes){
  $check=Join-Path $checks $mode;[IO.Directory]::CreateDirectory($check)|Out-Null
  Run $exe @(('--'+$mode),$check) ($flavor+'-'+$mode)
 }
 Pass (Join-Path $checks 'smoke/smoke.json')
 if($validation.scope -in @('Full','Updates')){
  foreach($scope in @('outcomes','apostle','tool-activity','run-report','tool-preparation')){
   $check=Join-Path $checks ('smoke-'+$scope)
   Run $exe @('--smoke',$check) ($flavor+'-smoke-'+$scope) 180 $scope
   Pass (Join-Path $check 'smoke.json')
  }
 }
 if($validation.scope -in @('Full','Updates')){Pass (Join-Path $checks 'check-tool-languages/result.json');Pass (Join-Path $checks 'check-tool-language-ui/result.json')}
 Run $exe @('--utility','live','self-test') ($flavor+'-live-self-test')
 Run $exe @('--tool-catalog',(Join-Path $bundle 'app/tools.json')) ($flavor+'-tool-catalog')
 if($flavor -eq $Flavors[0] -and $validation.scope -in @('Navigation','Rewards','UiReadiness')){
  Run $exe @('--utility','live','prepare',$managed,(Join-Path $checks 'live-client')) 'live-client'
  if($validation.scope -eq 'Rewards'){
   Run $exe @('--utility','live','taps-all',$managed,(Join-Path $root 'assets/specs'),(Join-Path $checks 'evidence')) 'evidence-configs'
   Pass (Join-Path $checks 'evidence/result.json')
  }
 }
 if($flavor -eq $Flavors[0] -and $validation.scope -eq 'Full'){
  Run $exe @('--check-client',$managed,(Join-Path $checks 'observer-client')) 'observer-client'
  Run $exe @('--utility','live','prepare',$managed,(Join-Path $checks 'live-client')) 'live-client'
  if($validation.scope -eq 'Rewards'){
   Run $exe @('--utility','live','taps-all',$managed,(Join-Path $root 'assets/specs'),(Join-Path $checks 'evidence')) 'evidence-configs'
   Pass (Join-Path $checks 'evidence/result.json')
  }
  Run $exe @('--utility','live','taps-all',$managed,(Join-Path $bundle 'app/connection/specs'),(Join-Path $checks 'evidence')) 'evidence-configs'
  Pass (Join-Path $checks 'evidence/result.json')
  $suiteCache=Join-Path $work 'unified-suite-daily/suite-cache'
  Copy-DustweavePreparedCache -Source $PreparedCacheDirectory -Destination $suiteCache
  # The normal compiler revalidates all managed inputs, component assembly identity and payload SHA.
  # Changed modules are rebuilt; reuse never substitutes a pass result for the actual check.
  Run $exe @('--check-suite',$managed,(Join-Path $checks 'suite')) 'unified-suite' 900
  Pass (Join-Path $checks 'suite/suite.json')
  Copy-DustweavePreparedCache -Source $suiteCache -Destination $PreparedCacheDirectory
  foreach($id in @('fishing','sichuan','rhythm','territory','equipment','apostle-defense','infinite-gacha','secret-vision','fiend-hunter','mansion-runaway','pandora')){
   $check=Join-Path $checks ('tools/'+$id);[IO.Directory]::CreateDirectory($check)|Out-Null
   Run $exe @('--tool',$id,'--smoke',$check) ('tool-'+$id)
   if(!(Get-ChildItem -LiteralPath $check -File -Recurse -Filter '*.png')){throw "Missing tool UI evidence: $id"}
   if(Get-ChildItem -LiteralPath $check -File -Recurse|Where-Object Name -in @('failure.txt','error.json','error.txt')){throw "Tool UI reported an error: $id"}
  }
 }
 $inventory=@(Get-ChildItem -LiteralPath $bundle -File -Recurse|ForEach-Object {[IO.Path]::GetRelativePath($bundle,$_.FullName).Replace([IO.Path]::DirectorySeparatorChar,[char]47)})+@('update-package.json')
 [ordered]@{Version=$Version;Flavor=$flavor;Layout=$(if([Version]($Version.Split('-')[0]) -ge [Version]'1.5.0'){3}else{2});InstallerProtocol=$(if([Version]($Version.Split('-')[0]) -ge [Version]'1.5.0'){1}elseif($Version -eq '1.4.15-bridge.1'){1}else{2});RuntimeVersion='8.0.30';BootstrapFiles=(Get-Content -LiteralPath (Join-Path $bundle 'app/package-components.json') -Raw|ConvertFrom-Json).bootstrapFiles;Files=@($inventory|Sort-Object -Unique)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $bundle 'update-package.json') -Encoding utf8
 $files=@(Get-ChildItem -LiteralPath $bundle -File -Recurse)
 if([Version]($Version.Split('-')[0]) -ge [Version]'1.5.0'){if(@($files|Where-Object Extension -eq '.exe').Count -ne 1){throw 'Expected exactly one self-updating executable.'}}elseif(@($files|Where-Object Extension -eq '.exe').Count -ne 2 -or !(Test-Path -LiteralPath (Join-Path $bundle 'app/support/Dustweave.Updater.exe'))){throw 'Expected launcher plus independent updater.'}
 if(@(Get-ChildItem -LiteralPath $bundle -File|Where-Object Name -notin @('Dustweave.exe','README.md','README.en.md','LICENSE','update-package.json')).Count -or @(Get-ChildItem -LiteralPath $bundle -Directory|Where-Object Name -ne 'app').Count){throw 'Package root must expose only the launcher, guides, license and update inventory.'}
 if($files|Where-Object {$_.Name -match 'Assembly-CSharp|GameAssembly|python.*\.(exe|dll)' -or $_.Extension -in @('.py','.pyd','.pyc','.pfx','.key') -or $_.FullName -match '[\\/]plugins[\\/]'}){throw 'Forbidden private/legacy payload in release.'}
 $archive=Join-Path $output ('Dustweave-'+$Version+'-'+$flavor+'-win-x64.zip')
 [IO.Compression.ZipFile]::CreateFromDirectory($bundle,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
 $zip=[IO.Compression.ZipFile]::OpenRead($archive)
 try{
  if($zip.Entries.Count -ne $files.Count){throw 'Archive has missing or extra entries.'}
  foreach($file in $files){
   $relative=[IO.Path]::GetRelativePath($bundle,$file.FullName).Replace('\','/');$part=$zip.GetEntry($relative)
   if(!$part -or $part.Length -ne $file.Length){throw "Archive member differs: $relative"}
   $stream=$part.Open();try{$digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
   if($digest -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw "Archive hash differs: $relative"}
  }
 }finally{$zip.Dispose()}
 $flavorReports+= [ordered]@{flavor=$flavor;selfContained=($self -eq 'true');exeBytes=(Get-Item -LiteralPath $exe).Length;zipBytes=(Get-Item -LiteralPath $archive).Length;checks=$checks}
}
Run 'dotnet' @('run','--project',(Join-Path $root 'tools/CompatibilityCli'),'-c','Release','-r','win-x64','--no-build','--','client-inputs',$managed,(Join-Path $work 'client-after.json')) 'client-after'
$clientBefore=Get-Content -LiteralPath (Join-Path $work 'client-before.json') -Raw|ConvertFrom-Json
$clientAfter=Get-Content -LiteralPath (Join-Path $work 'client-after.json') -Raw|ConvertFrom-Json
if($clientBefore.gameKey -ne $clientAfter.gameKey){throw 'Installed game code changed while packaging; do not distribute this candidate.'}
Copy-Item -LiteralPath (Join-Path $work 'client-before.json') -Destination (Join-Path $output 'client-code-inputs.json')
$after=SourceState
$after|Set-Content -LiteralPath (Join-Path $work 'source-after.json') -Encoding utf8
if($after -ne $before){throw 'Source or development dependency locks changed while packaging.'}
Copy-Item -LiteralPath (Join-Path $tables 'manifest.json') -Destination (Join-Path $output 'client-data-inputs.json')
$before|Set-Content -LiteralPath (Join-Path $output 'build-inputs.json') -Encoding utf8
Get-ChildItem -LiteralPath $output -Recurse -File|Where-Object Extension -in @('.exe','.zip')|ForEach-Object {"$((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant())  $([IO.Path]::GetRelativePath($output,$_.FullName))"}|Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
$validation|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $output 'validation-scope.json') -Encoding utf8
[ordered]@{validationScope=$validation.scope;validationBaseline=$validation.baseline;version=$Version;product='Dustweave';channel='release-candidate';flavors=$flavorReports;privatePluginIncluded=$false;pythonIncluded=$false;gameAssembliesIncluded=$false;realGameTouched=$false;runtimeVerification='source_implemented_pending_runtime';publicReleaseApproved=$false;buildDirectory=$work}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $output 'release.json') -Encoding utf8
Write-Host "Release candidate package ready: $output"
