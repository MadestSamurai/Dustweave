param(
 [Parameter(Mandatory)][string]$BaselineDirectory,
 [Parameter(Mandatory)][string]$OutputDirectory,
 [string]$Version='0.9.0'
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(!$output.StartsWith((Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Acceptance output must remain inside repository artifacts.'}
if(Test-Path $output){throw 'Preserve previous acceptance outputs.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
foreach($flavor in @('Lite','Portable')){
 $folder=Join-Path $output $flavor
 $self=if($flavor -eq 'Portable'){'true'}else{'false'}
 $args=@('publish',(Join-Path $repo 'src/Desktop/Dustweave.Desktop.csproj'),'-c','Release','-r','win-x64','--self-contained',$self,('-p:SelfContained='+$self),('-p:PublishSelfContained='+$self),'-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true',('-p:EnableCompressionInSingleFile='+$self),'-p:DebugType=None','-p:DebugSymbols=false',('-p:DustweaveVersion='+$Version),('-p:CustomAfterMicrosoftCommonTargets='+(Join-Path $repo 'Package.Build.targets')),('-p:DustweavePackageLockRoot='+(Join-Path $output ('locks/'+$flavor))),'-o',$folder,'--nologo')
 & dotnet @args *> (Join-Path $output ($flavor+'-publish.log'))
 if($LASTEXITCODE -ne 0){throw "Failed publishing $flavor"}
 foreach($extra in Get-ChildItem -LiteralPath $folder -File -Filter '*.runtimeconfig.json'|Where-Object Name -ne 'Dustweave.runtimeconfig.json'){Remove-Item -LiteralPath $extra.FullName}
 $baseline=[IO.Path]::GetFullPath((Join-Path $BaselineDirectory $flavor))
 foreach($entry in Get-ChildItem -LiteralPath $baseline|Where-Object Name -ne 'Dustweave.exe'){Copy-Item -LiteralPath $entry.FullName -Destination $folder -Recurse}
 foreach($group in @(@{source='assets/flows';target='flows'},@{source='assets/specs';target='connection/specs'})){
  $destination=Join-Path $folder $group.target
  [IO.Directory]::CreateDirectory($destination)|Out-Null
  Get-ChildItem -LiteralPath (Join-Path $repo $group.source) -File -Filter '*.json'|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $destination}
 }
 $inventory=@(Get-ChildItem -LiteralPath $folder -File -Recurse|ForEach-Object {[IO.Path]::GetRelativePath($folder,$_.FullName).Replace([IO.Path]::DirectorySeparatorChar,[char]47)})+@('update-package.json')
 [ordered]@{Version=$Version;Flavor=$flavor;Files=@($inventory|Sort-Object -Unique)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'update-package.json') -Encoding utf8
 [IO.Compression.ZipFile]::CreateFromDirectory($folder,(Join-Path $output ("Dustweave-$Version-$flavor-win-x64.zip")),[IO.Compression.CompressionLevel]::Optimal,$false)
}
& (Join-Path $PSScriptRoot 'new-update-feed.ps1') -Version $Version -PackageDirectory $output
[ordered]@{status='built';purpose='isolated OTA acceptance only';version=$Version;staticDataFrom=[IO.Path]::GetFullPath($BaselineDirectory);realGameTouched=$false;productionDeployed=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'acceptance-build.json') -Encoding utf8
Write-Host "Acceptance packages: $output"
