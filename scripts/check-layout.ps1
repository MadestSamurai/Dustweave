$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$prefix=$root+[IO.Path]::DirectorySeparatorChar
$manifest=Get-Content -LiteralPath (Join-Path $root 'source-manifest.json') -Raw|ConvertFrom-Json
[xml]$solution=Get-Content -LiteralPath (Join-Path $root 'Dustweave.slnx') -Raw
$solutionProjects=@($solution.SelectNodes('//Project')|ForEach-Object {$_.Path})
$declared=@($manifest.projects)
$errors=[Collections.Generic.List[string]]::new()
if(@(Compare-Object ($declared|Sort-Object) ($solutionProjects|Sort-Object)).Count){$errors.Add('Solution and source manifest project lists differ.')}
if(@($solutionProjects|Group-Object|Where-Object Count -gt 1).Count){$errors.Add('Duplicate solution projects.')}
if($manifest.entryProject -ne 'src/Desktop/Dustweave.Desktop.csproj'){$errors.Add('Unexpected application entry project.')}

$references=0;$resources=0
# These owned projects retain the original assembly names used by the hosted
# loader, WPF resources and package dependency locks. Do not exempt other names.
$legacyToolAssemblies=@{
 'src/Fishing/core/Dustweave.Fishing.Core.csproj'='BD2Fishing.Core'
 'src/Fishing/compatibility/Dustweave.Fishing.Compatibility.csproj'='BD2Fishing.Compatibility'
 'src/Fishing/desktop/Dustweave.Fishing.Desktop.csproj'='BD2Fishing'
 'src/Territory/core/Dustweave.Territory.Core.csproj'='BD2Territory.Core'
 'src/Territory/compatibility/Dustweave.Territory.Compatibility.csproj'='BD2Territory.Compatibility'
 'src/Territory/desktop/Dustweave.Territory.Desktop.csproj'='BD2Territory'

 'src/Sichuan/compatibility/Dustweave.Sichuan.Compatibility.csproj'='BD2Sichuan.Compatibility'
 'src/Sichuan/core/Dustweave.Sichuan.Core.csproj'='BD2Sichuan.Core'
 'src/Sichuan/desktop/Dustweave.Sichuan.Desktop.csproj'='BD2Sichuan'
 'src/SecretVision/compatibility/Dustweave.SecretVision.Compatibility.csproj'='BD2SecretVision.Compatibility'
 'src/SecretVision/core/Dustweave.SecretVision.Core.csproj'='BD2SecretVision.Core'
 'src/SecretVision/desktop/Dustweave.SecretVision.Desktop.csproj'='BD2SecretVision'

 'src/Rhythm/compatibility/Dustweave.Rhythm.Compatibility.csproj'='BD2Rhythm.Compatibility'
 'src/Rhythm/core/Dustweave.Rhythm.Core.csproj'='BD2Rhythm.Core'
 'src/Rhythm/desktop/Dustweave.Rhythm.Desktop.csproj'='BD2Rhythm'
 'src/Equipment/connection/Dustweave.Equipment.Connection.csproj'='BD2Equipment.Connection'
 'src/Equipment/core/Dustweave.Equipment.Core.csproj'='BD2Equipment.Core'
 'src/Equipment/desktop/Dustweave.Equipment.Desktop.csproj'='BD2EquipmentAssistant'
 'src/ApostleDefense/compatibility/Dustweave.ApostleDefense.Compatibility.csproj'='BD2ApostleDefense.Compatibility'
 'src/ApostleDefense/core/Dustweave.ApostleDefense.Core.csproj'='BD2ApostleDefense.Core'
 'src/ApostleDefense/desktop/Dustweave.ApostleDefense.Desktop.csproj'='BD2ApostleDefense'
 'src/InfiniteGacha/compatibility/Dustweave.InfiniteGacha.Compatibility.csproj'='BD2InfiniteGacha.Compatibility'
 'src/InfiniteGacha/core/Dustweave.InfiniteGacha.Core.csproj'='BD2InfiniteGacha.Core'
 'src/InfiniteGacha/desktop/Dustweave.InfiniteGacha.Desktop.csproj'='BD2InfiniteGacha'
 'src/FiendHunter/compatibility/Dustweave.FiendHunter.Compatibility.csproj'='BD2FiendHunter.Compatibility'
 'src/FiendHunter/core/Dustweave.FiendHunter.Core.csproj'='BD2FiendHunter.Core'
 'src/FiendHunter/desktop/Dustweave.FiendHunter.Desktop.csproj'='BD2FiendHunter'
}
foreach($relative in $declared){
 $project=[IO.Path]::GetFullPath((Join-Path $root $relative))
 if(!$project.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $project -PathType Leaf)){$errors.Add("Missing/outside project: $relative");continue}
 [xml]$xml=Get-Content -LiteralPath $project -Raw
 if($relative -notlike 'standalone/*'){
  if([IO.Path]::GetFileName($relative) -notlike 'Dustweave.*.csproj'){$errors.Add("Owned project does not use the product name: $relative")}
  $assembly=$xml.SelectSingleNode('//AssemblyName')
  if($assembly -and $assembly.InnerText -ne 'Dustweave' -and $assembly.InnerText -notlike 'Dustweave.*' -and $assembly.InnerText -ne $legacyToolAssemblies[$relative]){$errors.Add("Owned assembly does not use the product name: $relative")}
 }
 foreach($item in $xml.SelectNodes('//ProjectReference[@Include]|//Compile[@Include]|//EmbeddedResource[@Include]')){
  foreach($include in $item.Include.Split(';')){
   if($include -match '\$\(|%\('){continue}
   $target=[IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $project) $include))
   if(!$target.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){$errors.Add("Source reference leaves repository: $relative => $include");continue}
   if(!(Test-Path -Path $target -PathType Leaf)){$errors.Add("Source reference has no input: $relative => $include")}
   if($item.Name -eq 'ProjectReference'){
    $targetRelative=[IO.Path]::GetRelativePath($root,$target).Replace('\','/')
    if($targetRelative -notin $declared){$errors.Add("Unregistered project reference: $relative => $include")}
    $references++
   }elseif($item.Name -eq 'EmbeddedResource'){$resources++}
  }
 }
}
if($errors.Count){throw ($errors -join [Environment]::NewLine)}
[pscustomobject]@{status='passed';projects=$declared.Count;projectReferences=$references;resourceInputs=$resources;repository=$root}|ConvertTo-Json -Compress
