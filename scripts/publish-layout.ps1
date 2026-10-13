$ErrorActionPreference='Stop'
function Publish-DustweaveLayout {
 param([string]$Root,[string]$Work,[string]$Destination,[string]$Version,[ValidateSet('Lite','Portable')][string]$Flavor)
 $self=if($Flavor -eq 'Portable'){'true'}else{'false'}
 $runtime='8.0.30'
 $framework=if($Flavor -eq 'Portable'){$runtime}else{'8.0.0'}
 $catalog=Join-Path $Work ('resources-'+$Flavor)
 [IO.Directory]::CreateDirectory($catalog)|Out-Null
 $common=@('-c','Release','-r','win-x64','--self-contained',$self,('-p:SelfContained='+$self),('-p:PublishSelfContained='+$self),'-p:DebugType=None','-p:DebugSymbols=false','-p:IncludeSourceRevisionInInformationalVersion=false',('-p:RuntimeFrameworkVersion='+$framework),('-p:CustomAfterMicrosoftCommonTargets='+ (Join-Path $Root 'Package.Build.targets')),('-p:DustweavePackageLockRoot='+ (Join-Path $Work ('locks/'+$Flavor))),'--nologo')
 function Publish-Project([string]$project,[string]$output,[string[]]$extra,[string]$name) {
  & dotnet publish (Join-Path $Root $project) @common @extra -o $output > (Join-Path $Work ($Flavor+'-'+$name+'.log')) 2>&1
  if($LASTEXITCODE -ne 0){throw "Publish failed: $Flavor-$name. See $Work"}
 }
 Publish-Project 'src/Desktop/Dustweave.Desktop.csproj' $Destination @('-p:PublishSingleFile=false','-p:EnableCompressionInSingleFile=false','-p:DustweaveSharedResources=true',('-p:DustweaveResourceCatalog='+$catalog),('-p:DustweaveVersion='+$Version)) 'application'
 $launcher=Join-Path $Work ('launcher-'+$Flavor)
 Publish-Project 'src/Launcher/Dustweave.Launcher.csproj' $launcher @('-p:PublishSingleFile=false') 'launcher'
 foreach($file in Get-ChildItem -LiteralPath $launcher -File){
  if($file.Name -eq 'Dustweave.Launcher.exe'){continue}
  $target=Join-Path $Destination $file.Name
  if(!$file.Name.StartsWith('Dustweave.Launcher.') -and (Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw "Launcher/runtime mismatch: $($file.Name)"}
  Copy-Item -LiteralPath $file.FullName -Destination $target -Force
 }
 Copy-Item -LiteralPath (Join-Path $launcher 'Dustweave.Launcher.exe') -Destination (Join-Path $Destination 'Dustweave.exe') -Force
 # Child tools run inside the host. Only the stable launcher and isolated updater are executable entry points.
 foreach($extra in Get-ChildItem -LiteralPath $Destination -File -Filter '*.exe' | Where-Object Name -ne 'Dustweave.exe'){Remove-Item -LiteralPath $extra.FullName}
 $helper=Join-Path $Work ('updater-'+$Flavor)
 Publish-Project 'src/Updater/Dustweave.Updater.csproj' $helper @('-p:PublishSingleFile=true',('-p:EnableCompressionInSingleFile='+$self)) 'updater'
 $support=Join-Path $Destination 'support';[IO.Directory]::CreateDirectory($support)|Out-Null
 Copy-Item -LiteralPath (Join-Path $helper 'Dustweave.Updater.exe') -Destination $support
 $shared=Join-Path $Destination 'resources/shared';[IO.Directory]::CreateDirectory($shared)|Out-Null
 $resources=@(foreach($index in Get-ChildItem -LiteralPath $catalog -Filter '*.txt'){
  foreach($line in Get-Content -LiteralPath $index.FullName){
   $parts=$line.Split('|');if($parts.Count -ne 3 -or $parts[0] -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Malformed resource catalog'}
   if((Get-FileHash -LiteralPath $parts[1]).Hash -ne $parts[0]){throw 'Resource changed during publish'}
   $name=$parts[0].ToLowerInvariant()+'.bin';$path=Join-Path $shared $name
   if(!(Test-Path -LiteralPath $path)){Copy-Item -LiteralPath $parts[1] -Destination $path}
   [ordered]@{assembly=$index.BaseName;resource=$parts[2];file=('resources/shared/'+$name);bytes=(Get-Item -LiteralPath $path).Length}
  }
 })
 $bootstrap=@(Get-ChildItem -LiteralPath $launcher -File | Where-Object {$_.Extension -ne '.exe' -or $_.Name -eq 'Dustweave.Launcher.exe'} | ForEach-Object {if($_.Name -eq 'Dustweave.Launcher.exe'){'Dustweave.exe'}else{$_.Name}} | Sort-Object)
 foreach($name in $bootstrap){if(!(Test-Path -LiteralPath (Join-Path $Destination $name))){throw "Missing bootstrap file: $name"}}
 [ordered]@{layout=2;runtime=$runtime;bootstrapFiles=$bootstrap;launcher='1.0.0';updater='1.0.0';sharedResources=$resources}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $Destination 'package-components.json') -Encoding utf8
}
