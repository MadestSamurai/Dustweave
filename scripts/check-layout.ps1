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
foreach($relative in $declared){
 $project=[IO.Path]::GetFullPath((Join-Path $root $relative))
 if(!$project.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $project -PathType Leaf)){$errors.Add("Missing/outside project: $relative");continue}
 [xml]$xml=Get-Content -LiteralPath $project -Raw
 if($relative -notlike 'standalone/*'){
  if([IO.Path]::GetFileName($relative) -notlike 'Dustweave.*.csproj'){$errors.Add("Owned project does not use the product name: $relative")}
  $assembly=$xml.SelectSingleNode('//AssemblyName')
  if($assembly -and $assembly.InnerText -ne 'Dustweave' -and $assembly.InnerText -notlike 'Dustweave.*'){$errors.Add("Owned assembly does not use the product name: $relative")}
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
