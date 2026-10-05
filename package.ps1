param(
 [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
 [Parameter(Mandatory)][string]$GameManagedDir,
 [ValidateSet('Portable','Lite')][string[]]$Flavors=@('Portable','Lite'),
 [string]$PreparedCacheDirectory=''
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)

if(!$PreparedCacheDirectory){$PreparedCacheDirectory=Join-Path $root 'artifacts/prepared-modules'}
$PreparedCacheDirectory=[IO.Path]::GetFullPath($PreparedCacheDirectory)
if(!$PreparedCacheDirectory.StartsWith([IO.Path]::GetFullPath((Get-Location).Path)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Prepared cache must remain below the current workspace.'}
$entry=Join-Path $root 'src/Desktop/BD2Daily.Desktop.csproj'
if(!(Test-Path -LiteralPath (Join-Path $root 'source-manifest.json'))){throw 'Run this script from the Dustweave repository.'}
if(!$Flavors.Count -or @($Flavors|Select-Object -Unique).Count -ne $Flavors.Count){throw 'Choose each flavor at most once.'}
$managed=[IO.Path]::GetFullPath($GameManagedDir)
if(!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))){throw 'Provide the installed game Managed directory; the game does not need to run.'}
$game=Split-Path -Parent (Split-Path -Parent $managed)
$output=Join-Path $root ('artifacts/releases/'+$Version)
if(Test-Path -LiteralPath $output){throw 'Output already exists. Preserve it and use a new release version.'}
$work=Join-Path $root ('artifacts/package-'+$Version+'-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work)|Out-Null
[IO.Directory]::CreateDirectory($output)|Out-Null
function Run([string]$file,[string[]]$arguments,[string]$name,[int]$seconds=300){
 $start=[Diagnostics.ProcessStartInfo]::new($file);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.WorkingDirectory=(Get-Location).Path
 $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $start.Environment['BD2_DAILY_DATA_ROOT']=Join-Path $work ($name+'-daily')
 $start.Environment['BD2_EQUIPMENT_DATA_ROOT']=Join-Path $work ($name+'-equipment')
 $start.Environment['DUSTWEAVE_PLUGIN']='none'
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
 $files=@(foreach($folder in @('src','tools','tests','assets','third-party','licenses','standalone','scripts','docs','.github')){
  Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}
 })+@(Get-ChildItem -LiteralPath $root -File -Force)
 $rows=$files|ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($root,$_.FullName);hash=(Get-FileHash -LiteralPath $_.FullName).Hash}}
 $rows | Sort-Object path | ConvertTo-Json -Depth 4 -Compress
}
& (Join-Path $root 'check-source.ps1')
$before=SourceState
$before|Set-Content -LiteralPath (Join-Path $work 'source-before.json') -Encoding utf8
$packageImport='-p:CustomAfterMicrosoftCommonTargets='+ (Join-Path $root 'Package.Build.targets')
Run 'dotnet' @('build',$entry,'-c','Release','-r','win-x64','-p:SelfContained=false',$packageImport,('-p:DustweavePackageLockRoot='+ (Join-Path $work 'locks/build')),'--nologo') 'build'
foreach($component in @('tools/TradeData','tools/CompatibilityCli','tests/Dustweave.Tests')){
 Run 'dotnet' @('build',(Join-Path $root $component),'-c','Release','-r','win-x64','-p:SelfContained=false',$packageImport,('-p:DustweavePackageLockRoot='+ (Join-Path $work 'locks/build')),'--nologo') ('build-'+($component -replace '/','-'))
}
& (Join-Path $root 'check-source.ps1') -AfterBuild
& (Join-Path $root 'test.ps1') -NoBuild
$tools=@('bd2-fishing','bd2-sichuan','bd2-rhythm','bd2-territory','bd2-equipment-assistant','bd2-apostle-defense','bd2-infinite-gacha','bd2-secret-vision','bd2-fiend-hunter')
$tables=Join-Path $work 'tables'
$tableNames='CookingTable,FoodTable,ProductTable,SellItemTable,ShopTable,TalentSkillTable,TalentTable,NameTextTable,CharTable,EventMissionGroupTable,GachaGroupTable,GachaTable,MissionSectionRewardTable,LocalTextTable,MissionTable,DispatchTable,EquipmentMakingTable,EquipmentTable,HuntDispatchTable,PassTable,SkyWayFieldTable,SquareRewardTable,StatueRewardTable'
$groups=[ordered]@{extra=@('EventMissionGroupTable');gacha=@('GachaGroupTable','GachaTable','MissionSectionRewardTable');missions=@('LocalTextTable','MissionTable');names=@('NameTextTable');policy=@('CharTable','DispatchTable','EquipmentMakingTable','EquipmentTable','HuntDispatchTable','MissionTable','PassTable','SkyWayFieldTable','SquareRewardTable','StatueRewardTable')}
$bootstrap=Join-Path $work 'cold-bootstrap'
Run 'dotnet' @('run','--project',(Join-Path $root 'tools/CompatibilityCli'),'-c','Release','-r','win-x64','--no-build','--','bootstrap',$managed,$bootstrap) 'cold-bootstrap-prepare'
Run 'dotnet' @('run','--project',(Join-Path $root 'tools/CompatibilityCli'),'-c','Release','-r','win-x64','--no-build','--','bootstrap-host',$managed,$bootstrap) 'cold-bootstrap-host'
Pass (Join-Path $bootstrap 'validation.json')
$flavorReports=@()
foreach($flavor in $Flavors){
 $self=if($flavor -eq 'Portable'){'true'}else{'false'}
 $bundle=Join-Path $output $flavor
 Run 'dotnet' @('publish',$entry,'-c','Release','-r','win-x64','--self-contained',$self,('-p:SelfContained='+$self),('-p:PublishSelfContained='+$self),'-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true',('-p:EnableCompressionInSingleFile='+$self),'-p:DebugType=None','-p:DebugSymbols=false',('-p:DustweaveVersion='+$Version),$packageImport,('-p:DustweavePackageLockRoot='+ (Join-Path $work ('locks/'+$flavor))),'-o',$bundle,'--nologo') ($flavor+'-publish')
 foreach($extra in Get-ChildItem -LiteralPath $bundle -File -Filter '*.runtimeconfig.json'|Where-Object Name -ne 'BD2DailyAssistant.runtimeconfig.json'){Remove-Item -LiteralPath $extra.FullName}
 $exe=Join-Path $bundle 'BD2DailyAssistant.exe'
 $checks=Join-Path $work ($flavor+'-checks');[IO.Directory]::CreateDirectory($checks)|Out-Null
 [ordered]@{protocol=1;tools=@('live','minigame','exporter','diagnostics')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $bundle 'utility-host.json') -Encoding utf8
 if($flavor -eq $Flavors[0]){
  Run $exe @('--utility','exporter','--game-root',$game,'--tables',$tableNames,'--simulator-compatible','--output',$tables) 'export-tables'
  Run 'dotnet' @('run','--project',(Join-Path $root 'tools/TradeData'),'-c','Release','-r','win-x64','--no-build','--',$tables,(Join-Path $managed 'Assembly-CSharp.dll'),(Join-Path $work 'trade-catalog.json')) 'trade-catalog'
 }
 foreach($group in $groups.GetEnumerator()){
  $target=Join-Path $bundle ('data/daily/'+$group.Key);[IO.Directory]::CreateDirectory($target)|Out-Null
  foreach($table in $group.Value){Copy-Item -LiteralPath (Join-Path $tables ($table+'.json')) -Destination $target}
  Copy-Item -LiteralPath (Join-Path $tables 'manifest.json') -Destination $target
 }
 Copy-Item -LiteralPath (Join-Path $work 'trade-catalog.json') -Destination (Join-Path $bundle 'data/trade-catalog.json')
 foreach($asset in @(@{source='assets/flows';target='flows'},@{source='assets/specs';target='connection/specs'})){
  $target=Join-Path $bundle $asset.target
  [IO.Directory]::CreateDirectory($target)|Out-Null
  Get-ChildItem -LiteralPath (Join-Path $root $asset.source) -File -Filter '*.json'|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $target}
 }
 Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $bundle -Recurse
 Copy-Item -LiteralPath (Join-Path $root 'third-party/highs/LICENSE.txt') -Destination (Join-Path $bundle 'licenses/HiGHS.txt')
 Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $bundle
 foreach($repository in $tools){
  $module=Join-Path $root ('standalone/'+$repository);$guide=Join-Path $bundle ('docs/tools/'+$repository);[IO.Directory]::CreateDirectory($guide)|Out-Null
  foreach($name in @('README.md','README.en.md','README_EN.md','THIRD_PARTY_NOTICES.md')){if(Test-Path -LiteralPath (Join-Path $module $name)){Copy-Item -LiteralPath (Join-Path $module $name) -Destination $guide}}
  Copy-Item -LiteralPath (Join-Path $module 'LICENSE') -Destination (Join-Path $bundle ('licenses/'+$repository+'-MIT.txt'))
 }
 foreach($name in @('README.md','README.en.md')){Copy-Item -LiteralPath (Join-Path $root 'docs/package' $name) -Destination $bundle}
 Run $exe @('--identity',(Join-Path $checks 'identity.txt')) ($flavor+'-identity')
 if((Get-Content -LiteralPath (Join-Path $checks 'identity.txt') -First 1) -ne ('Dustweave '+$Version)){throw 'Release label differs from executable.'}
 foreach($mode in @('smoke','check-tool-languages','check-tool-language-ui','check-stage-host','check-tool-session','check-suite-host')){
  $check=Join-Path $checks $mode;[IO.Directory]::CreateDirectory($check)|Out-Null
  Run $exe @(('--'+$mode),$check) ($flavor+'-'+$mode)
 }
 Pass (Join-Path $checks 'smoke/smoke.json');Pass (Join-Path $checks 'check-tool-languages/result.json');Pass (Join-Path $checks 'check-tool-language-ui/result.json')
 Run $exe @('--utility','live','self-test') ($flavor+'-live-self-test')
 Run $exe @('--tool-catalog',(Join-Path $bundle 'tools.json')) ($flavor+'-tool-catalog')
 if($flavor -eq $Flavors[0]){
  Run $exe @('--check-client',$managed,(Join-Path $checks 'observer-client')) 'observer-client'
  Run $exe @('--utility','live','prepare',$managed,(Join-Path $checks 'live-client')) 'live-client'
  $suiteCache=Join-Path $work 'unified-suite-daily/suite-cache'
  if(Test-Path -LiteralPath $PreparedCacheDirectory){
   [IO.Directory]::CreateDirectory($suiteCache)|Out-Null
   foreach($file in Get-ChildItem -LiteralPath $PreparedCacheDirectory -Recurse -File){
    $relative=[IO.Path]::GetRelativePath($PreparedCacheDirectory,$file.FullName)
    $target=Join-Path $suiteCache $relative;[IO.Directory]::CreateDirectory((Split-Path -Parent $target))|Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
   }
  }
  # The normal compiler revalidates client MVID, component assembly identity and payload SHA.
  # Changed modules are rebuilt; reuse never substitutes a pass result for the actual check.
  Run $exe @('--check-suite',$managed,(Join-Path $checks 'suite')) 'unified-suite' 900
  Pass (Join-Path $checks 'suite/suite.json')
  foreach($file in Get-ChildItem -LiteralPath $suiteCache -Recurse -File|Where-Object {$_.FullName -notmatch '[\\/]build-[^\\/]+[\\/]'} ){
   $target=Join-Path $PreparedCacheDirectory ([IO.Path]::GetRelativePath($suiteCache,$file.FullName));[IO.Directory]::CreateDirectory((Split-Path -Parent $target))|Out-Null
   Copy-Item -LiteralPath $file.FullName -Destination $target -Force
  }
  foreach($id in @('fishing','sichuan','rhythm','territory','equipment','apostle-defense','infinite-gacha','secret-vision','fiend-hunter')){
   $check=Join-Path $checks ('tools/'+$id);[IO.Directory]::CreateDirectory($check)|Out-Null
   Run $exe @('--tool',$id,'--smoke',$check) ('tool-'+$id)
   if(!(Get-ChildItem -LiteralPath $check -File -Recurse -Filter '*.png')){throw "Missing tool UI evidence: $id"}
   if(Get-ChildItem -LiteralPath $check -File -Recurse|Where-Object Name -in @('failure.txt','error.json','error.txt')){throw "Tool UI reported an error: $id"}
  }
 }
 $files=@(Get-ChildItem -LiteralPath $bundle -File -Recurse)
 if(@($files|Where-Object Extension -eq '.exe').Count -ne 1){throw 'Expected one application executable.'}
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
$after=SourceState
$after|Set-Content -LiteralPath (Join-Path $work 'source-after.json') -Encoding utf8
if($after -ne $before){throw 'Source or development dependency locks changed while packaging.'}
Copy-Item -LiteralPath (Join-Path $tables 'manifest.json') -Destination (Join-Path $output 'client-data-inputs.json')
$before|Set-Content -LiteralPath (Join-Path $output 'build-inputs.json') -Encoding utf8
Get-ChildItem -LiteralPath $output -Recurse -File|Where-Object Extension -in @('.exe','.zip')|ForEach-Object {"$((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant())  $([IO.Path]::GetRelativePath($output,$_.FullName))"}|Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
[ordered]@{version=$Version;product='Dustweave';channel='private-release';flavors=$flavorReports;privatePluginIncluded=$false;pythonIncluded=$false;gameAssembliesIncluded=$false;realGameTouched=$false;runtimeVerification='source_implemented_pending_runtime';publicReleaseApproved=$false;buildDirectory=$work}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $output 'release.json') -Encoding utf8
Write-Host "Private release package ready: $output"



