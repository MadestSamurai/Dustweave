param(
 [Parameter(Mandatory)][string]$BaselineDirectory,
 [Parameter(Mandatory)][string]$OutputDirectory,
 [string]$Version='0.9.0'
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'publish-layout.ps1')
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(!$output.StartsWith((Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Acceptance output must remain inside repository artifacts.'}
if(Test-Path $output){throw 'Preserve previous acceptance outputs.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
foreach($flavor in @('Lite','Portable')){
 $folder=Join-Path $output $flavor
 Publish-DustweaveLayout -Root $repo -Work $output -Destination $folder -Version $Version -Flavor $flavor
 $baseline=[IO.Path]::GetFullPath((Join-Path $BaselineDirectory $flavor))
 foreach($entry in Get-ChildItem -LiteralPath $baseline|Where-Object Name -in @('data','docs','licenses','flows','connection','utility-host.json','tools.json','README.md','README.en.md','LICENSE','THIRD_PARTY_NOTICES.md')){Copy-Item -LiteralPath $entry.FullName -Destination $folder -Recurse}
 foreach($group in @(@{source='assets/flows';target='flows'},@{source='assets/specs';target='connection/specs'})){
  $destination=Join-Path $folder $group.target
  [IO.Directory]::CreateDirectory($destination)|Out-Null
  Get-ChildItem -LiteralPath (Join-Path $repo $group.source) -File -Filter '*.json'|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $destination}
 }
 $inventory=@(Get-ChildItem -LiteralPath $folder -File -Recurse|ForEach-Object {[IO.Path]::GetRelativePath($folder,$_.FullName).Replace([IO.Path]::DirectorySeparatorChar,[char]47)})+@('update-package.json')
 [ordered]@{Version=$Version;Flavor=$flavor;Layout=2;RuntimeVersion='8.0.30';BootstrapFiles=(Get-Content -LiteralPath (Join-Path $folder 'package-components.json') -Raw|ConvertFrom-Json).bootstrapFiles;Files=@($inventory|Sort-Object -Unique)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'update-package.json') -Encoding utf8
 [IO.Compression.ZipFile]::CreateFromDirectory($folder,(Join-Path $output ("Dustweave-$Version-$flavor-win-x64.zip")),[IO.Compression.CompressionLevel]::Optimal,$false)
}
# Signed acceptance feed is prepared by the caller with its explicit baseline.
[ordered]@{status='built';purpose='isolated OTA acceptance only';version=$Version;staticDataFrom=[IO.Path]::GetFullPath($BaselineDirectory);realGameTouched=$false;productionDeployed=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'acceptance-build.json') -Encoding utf8
Write-Host "Acceptance packages: $output"
