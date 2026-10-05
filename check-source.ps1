param([switch]$AfterBuild)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)
$prefix=$root+[IO.Path]::DirectorySeparatorChar
& (Join-Path $root 'scripts/check-layout.ps1')
$manifest=Get-Content -LiteralPath (Join-Path $root 'source-manifest.json') -Raw|ConvertFrom-Json
$known=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($entry in $manifest.files){
 $path=[string]$entry.path
 if(!$path -or [IO.Path]::IsPathRooted($path) -or $path.Contains('\') -or $path -match '(^|/)\.\.?(/|$)' -or !$known.Add($path)){throw 'Invalid or duplicate source-manifest path.'}
}
$blockedNames=@('Assembly-CSharp.dll','GameAssembly.dll')
$ownedAssemblies=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($project in $manifest.projects){
 [xml]$definition=Get-Content -LiteralPath (Join-Path $root $project) -Raw
 $name=$definition.SelectSingleNode('//AssemblyName')
 [void]$ownedAssemblies.Add($(if($name){$name.InnerText}else{[IO.Path]::GetFileNameWithoutExtension($project)}))
}
$sourceFiles=@(foreach($folder in @('src','tools','tests','assets','third-party','licenses','standalone')){
 Get-ChildItem -LiteralPath (Join-Path $root $folder) -File -Recurse | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}
})
$errors=[Collections.Generic.List[string]]::new()
foreach($file in $sourceFiles){
 $relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
 if(!$known.Contains($relative)){$errors.Add("Unlisted source payload: $relative")}
 if($relative -match '^tests/.*/fixtures/'){$errors.Add("Captured fixture in independent source: $relative")}
 if($file.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)){$errors.Add("Reparse point in independent source: $relative")}
 if($file.Name -in $blockedNames -or $relative -match '(^|/)(references|artifacts|private-data|secrets|plugins|\.git)/' -or $file.Extension -in @('.py','.pyc','.pyd','.pfx','.key','.db')){
  $errors.Add("Forbidden source payload: $relative")
 }
 if($file.Extension -in @('.dll','.exe','.so') -and $relative -notmatch '^(third-party/|standalone/[^/]+/vendor/)'){
  $errors.Add("Unexpected binary source: $relative")
 }
}
$adjusted=[Collections.Generic.List[string]]::new()
foreach($entry in $manifest.files){
 $file=Join-Path $root $entry.path
 if(-not(Test-Path -LiteralPath $file)){ $errors.Add("Missing source: $($entry.path)");continue }
 $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
 if($hash -ne $entry.sha256){$adjusted.Add($entry.path)}
}
$buildProjects=0
$resources=[Collections.Generic.List[object]]::new()
if($AfterBuild){
 foreach($project in $manifest.projects){
  $assets=Join-Path (Split-Path -Parent (Join-Path $root $project)) 'obj/project.assets.json'
  if(-not(Test-Path -LiteralPath $assets)){$errors.Add("No restore evidence: $project");continue}
  $data=Get-Content -LiteralPath $assets -Raw|ConvertFrom-Json
  foreach($path in @($data.project.restore.projectPath)+@($data.project.restore.frameworks.PSObject.Properties.Value.projectReferences.PSObject.Properties.Name)){
   if(-not $path){continue}
   if(-not [IO.Path]::GetFullPath($path).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){$errors.Add("Build depends outside export: $path")}
  }
  $buildProjects++
 }
 $output=Join-Path $root 'src/Desktop/bin/Release/net8.0-windows/win-x64'
 Add-Type -Path (Join-Path $output 'Mono.Cecil.dll')
 foreach($file in Get-ChildItem -LiteralPath $output -Filter 'BD2*.dll'){
  if(!$ownedAssemblies.Contains($file.BaseName)){$errors.Add("Unregistered application assembly: $($file.Name)")}
  $module=[Mono.Cecil.ModuleDefinition]::ReadModule($file.FullName)
  try {
   foreach($reference in $module.AssemblyReferences){
    if(($reference.Name -match '^BD2' -and !$ownedAssemblies.Contains($reference.Name)) -or $reference.Name -eq 'Assembly-CSharp'){$errors.Add("Unregistered assembly reference: $($file.Name) => $($reference.Name)")}
   }
   foreach($resource in $module.Resources){
    if($resource.Name -match '(?i)(^|[./])plugins[./]'){$errors.Add("External plugin resource: $($file.Name) => $($resource.Name)")}
    $resources.Add([pscustomobject]@{assembly=$file.Name;resource=$resource.Name})
   }
  }finally{$module.Dispose()}
 }
}
if($errors.Count){throw ($errors -join [Environment]::NewLine)}
$result=[ordered]@{
 status='passed';sourceFiles=$sourceFiles.Count;buildProjects=$buildProjects
 exportedSourceChanges=@($adjusted);projectDependenciesInsideExport=$AfterBuild.IsPresent
 checkedResources=@($resources);privatePayloadDetected=$false;publicReleaseApproved=$false
 caveat='Automated boundary checks supplement manual review; they are not approval to publish.'
}
$out=Join-Path $root 'artifacts/source-boundary.json'
[IO.Directory]::CreateDirectory((Split-Path -Parent $out))|Out-Null
$result|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $out -Encoding utf8
[pscustomobject]@{status='passed';sourceFiles=$sourceFiles.Count;buildProjects=$buildProjects;embeddedResources=$resources.Count;adjustedSources=$adjusted.Count;report=$out}|ConvertTo-Json

