param([Parameter(Mandatory)][string]$Packages,[Parameter(Mandatory)][string]$Output,[string]$GameManagedDir='')
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($Output)
if($out -notmatch '[\\/]artifacts[\\/]' -or (Test-Path -LiteralPath $out)){throw 'Use a new isolated artifacts directory.'}
[IO.Directory]::CreateDirectory($out)|Out-Null
$results=[Collections.Generic.List[object]]::new()
function Run($exe,$arguments,$name,$scope=''){
 $dir=Join-Path $out $name;[IO.Directory]::CreateDirectory($dir)|Out-Null
 $info=[Diagnostics.ProcessStartInfo]::new($exe);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden';$info.WorkingDirectory=(Get-Location).Path
 $info.Environment['BD2_DAILY_DATA_ROOT']=Join-Path $dir 'data';$info.Environment['BD2_EQUIPMENT_DATA_ROOT']=Join-Path $dir 'equipment';$info.Environment['DUSTWEAVE_PLUGIN']='none'
 $info.Environment.Remove('DUSTWEAVE_UI_SMOKE_SCOPE')|Out-Null
 if($scope){$info.Environment['DUSTWEAVE_UI_SMOKE_SCOPE']=$scope}
 $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($arg in $arguments){$info.ArgumentList.Add($arg)}
 $watch=[Diagnostics.Stopwatch]::StartNew();$proc=[Diagnostics.Process]::Start($info)
 $stdout=$proc.StandardOutput.ReadToEndAsync();$stderr=$proc.StandardError.ReadToEndAsync()
 if(!$proc.WaitForExit(180000)){throw "Test is still active ($($proc.Id)): $name"}
 $stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()|Set-Content (Join-Path $dir 'process.log')
 if($proc.ExitCode -ne 0){throw "Test failed: $name ($($proc.ExitCode))"}
 $results.Add(@{case=$name;status='passed';seconds=$watch.Elapsed.TotalSeconds});$proc.Dispose()
 Write-Host "Passed $name"
}
foreach($flavor in @('Lite','Portable')){
 $folder=[IO.Path]::GetFullPath((Join-Path $Packages $flavor));$exe=Join-Path $folder 'Dustweave.exe'
 if(@(Get-ChildItem $folder -Filter *.dll).Count -ne 0 -or (Test-Path (Join-Path $folder 'app/Dustweave.dll'))){throw 'Loose host assemblies remain.'}
 foreach($mode in @('identity','check-tool-languages','check-tool-language-ui')){
  $name="$flavor-$mode";$dest=Join-Path $out $name
  $argument=if($mode -eq 'identity'){Join-Path $dest 'identity.txt'}else{$dest}
  Run $exe @("--$mode",$argument) $name
  if($mode -eq 'identity'){
   $identity=Get-Content $argument -Raw
   if($identity -notmatch "Package $flavor" -or $identity -notmatch '/all-in-one/releases.json' -or $identity -match '/v2/updates'){
    throw 'The bundled host edition or update source is incorrect.'
   }
  }
  if($mode -ne 'identity'){
   $report=Get-Content (Join-Path $dest 'result.json') -Raw|ConvertFrom-Json
   if($report.status -ne 'passed'){throw "Resource validation failed: $name"}
  }
 }
 foreach($scope in @('tool-preparation','tool-activity')){
  $name="$flavor-$scope";$dest=Join-Path $out $name
  Run $exe @('--smoke',$dest) $name $scope
  if((Get-Content (Join-Path $dest 'smoke.json') -Raw|ConvertFrom-Json).status -ne 'passed'){throw "Host smoke failed: $name"}
 }
 # Exercise each real built-in window from the installed executable, offline.
 foreach($id in @('fishing','sichuan','rhythm','territory','equipment','apostle-defense','infinite-gacha','secret-vision','fiend-hunter','mansion-runaway','pandora')){
  $name="$flavor-tool-$id";$dest=Join-Path $out $name
  Run $exe @('--tool',$id,'--smoke',$dest) $name
  if(@(Get-ChildItem $dest -Filter *.png -Recurse).Count -eq 0){throw "Missing tool render: $name"}
  if(@(Get-ChildItem $dest -File -Recurse|Where-Object Name -Match '(failure|error)\.(json|txt)$').Count){throw "Tool reported an error: $name"}
 }
 foreach($id in @('live','minigame','exporter','diagnostics')){
  $name="$flavor-utility-$id";Run $exe @('--utility',$id,'--describe',(Join-Path $out "$name/identity.json")) $name
 }
 if($GameManagedDir){
  $name="$flavor-suite";Run $exe @('--check-suite',$GameManagedDir,(Join-Path $out $name)) $name
 }
}
@{status='passed';cases=$results.ToArray();realGameTouched=$false;published=$false}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'results.json')
