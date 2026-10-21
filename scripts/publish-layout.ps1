$ErrorActionPreference='Stop'
$script:DustweaveNativeEntries=@{}
function Complete-DustweaveLayout {
 param([string]$Root,[string]$Destination)
 $destinationRoot=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
 if(!$destinationRoot.StartsWith((Join-Path ([IO.Path]::GetFullPath($Root)) 'artifacts')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Nested layout must remain under repository artifacts.'}
 $app=Join-Path $destinationRoot 'app'
 if(Test-Path -LiteralPath $app){throw 'Nested layout already exists.'}
 [IO.Directory]::CreateDirectory($app)|Out-Null
 foreach($entry in Get-ChildItem -LiteralPath $destinationRoot | Where-Object Name -notin @('app','Dustweave.exe','README.md','README.en.md','LICENSE','update-package.json')){
  $source=[IO.Path]::GetFullPath($entry.FullName);$target=[IO.Path]::GetFullPath((Join-Path $app $entry.Name))
  if(!$source.StartsWith($destinationRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or !$target.StartsWith($app+'\',[StringComparison]::OrdinalIgnoreCase) -or $entry.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)){throw 'Invalid nested package move.'}
  Move-Item -LiteralPath $source -Destination $target
 }
 $componentsPath=Join-Path $app 'package-components.json'
 $components=Get-Content -LiteralPath $componentsPath -Raw|ConvertFrom-Json
 if($components.nativeEntry){
  $entry=$script:DustweaveNativeEntries[$destinationRoot]
  if(!$entry -or !(Test-Path -LiteralPath $entry)){throw 'Native entry was not prepared by this packaging run.'}
  Copy-Item -LiteralPath $entry -Destination (Join-Path $destinationRoot 'Dustweave.exe') -Force
  $script:DustweaveNativeEntries.Remove($destinationRoot)
 }
 if(!$components.nativeEntry){
 $sdkVersion=(& dotnet --version).Trim()
 $dotnetRoot=Split-Path -Parent (Get-Command dotnet).Source
 Add-Type -Path (Join-Path $dotnetRoot "sdk/$sdkVersion/Microsoft.NET.HostModel.dll")
 $assets=Get-Content -LiteralPath (Join-Path $Root 'src/Launcher/obj/project.assets.json') -Raw|ConvertFrom-Json
 $templates=@($assets.packageFolders.PSObject.Properties.Name | ForEach-Object {Join-Path $_ 'microsoft.netcore.app.host.win-x64/8.0.30/runtimes/win-x64/native/apphost.exe'} | Where-Object {Test-Path -LiteralPath $_})
 if($templates.Count -ne 1){throw 'Pinned .NET apphost template is unavailable.'}
 [Microsoft.NET.HostModel.AppHost.HostWriter]::CreateAppHost($templates[0],(Join-Path $destinationRoot 'Dustweave.exe'),'app/Dustweave.Launcher.dll',$true,(Join-Path $app 'Dustweave.Launcher.dll'),$false,$false,$null)
 }
 $components.bootstrapFiles=@($components.bootstrapFiles|ForEach-Object {if($_ -eq 'Dustweave.exe'){$_}else{'app/'+$_}})
 if($components.bridge){
  # Old installers require every bootstrap byte to remain unchanged. Copy from
  # the original published archive, never from a mutable extracted installation.
  $archive=Join-Path $Root ('artifacts/releases/1.4.14/Dustweave-1.4.14-'+$components.flavor+'-win-x64.zip')
  $sum=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($archive)
  if($sum -notin (Get-Content -LiteralPath (Join-Path (Split-Path -Parent $archive) 'SHA256SUMS.txt'))){throw 'Bridge baseline archive failed verification.'}
  $zip=[IO.Compression.ZipFile]::OpenRead($archive)
  try{
   $reader=[IO.StreamReader]::new($zip.GetEntry('update-package.json').Open())
   try{$baseline=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
   if($baseline.Version -ne '1.4.14' -or $baseline.Flavor -ne $components.flavor){throw 'Bridge baseline identity differs.'}
   foreach($name in $baseline.BootstrapFiles){
    $destination=[IO.Path]::GetFullPath((Join-Path $destinationRoot $name))
    if(!$destination.StartsWith($destinationRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or $name -match '(^|/)\.\.?(/|$)'){throw 'Invalid bridge bootstrap path.'}
    [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($name),$destination,$true)
   }
   $components.bootstrapFiles=$baseline.BootstrapFiles
  }finally{$zip.Dispose()}
 }
 $components|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $componentsPath -Encoding utf8
}
function Publish-DustweaveLayout {
 param([string]$Root,[string]$Work,[string]$Destination,[string]$Version,[ValidateSet('Lite','Portable')][string]$Flavor)
 $self=if($Flavor -eq 'Portable'){'true'}else{'false'}
 $runtime='8.0.30'
 $bridge=$Version -eq '1.4.15-bridge.1'
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
 $helperOptions=@('-p:PublishSingleFile=true',('-p:EnableCompressionInSingleFile='+$self))
 if(!$bridge){$helperOptions+=@('-p:SelfContained=true','-p:PublishSelfContained=true','-p:EnableCompressionInSingleFile=true',('-p:RuntimeFrameworkVersion='+$runtime))}
 Publish-Project 'src/Updater/Dustweave.Updater.csproj' $helper ($helperOptions+@('-p:DustweaveVersion='+$Version)) 'updater'
 $support=Join-Path $Destination 'support';[IO.Directory]::CreateDirectory($support)|Out-Null
 Copy-Item -LiteralPath (Join-Path $helper 'Dustweave.Updater.exe') -Destination $support
 if(!$bridge){
  $identity=Join-Path $Work ('bootstrap-'+$Flavor+'.json')
  $startup=@(Get-ChildItem -LiteralPath $Destination -File | Where-Object {
   $_.Name -match '^Dustweave\..*\.(dll|deps\.json|runtimeconfig\.json)$' -or
   $_.Name -in @('hostfxr.dll','hostpolicy.dll','coreclr.dll','System.Private.CoreLib.dll')
  } | Sort-Object Name | ForEach-Object {@{Name=('app/'+$_.Name);Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
  [ordered]@{Version=$Version;Flavor=$Flavor;HelperSha256=(Get-FileHash -LiteralPath (Join-Path $support 'Dustweave.Updater.exe')).Hash;StartupFiles=$startup}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $identity -Encoding utf8
  $native=Join-Path $Work ('bootstrap-'+$Flavor)
  & dotnet publish (Join-Path $Root 'src/Bootstrap/Dustweave.Bootstrap.csproj') -c Release -r win-x64 --self-contained true -p:PublishAot=true ('-p:DustweaveBootstrapInfo='+$identity) -o $native --nologo > (Join-Path $Work ($Flavor+'-bootstrap.log')) 2>&1
  if($LASTEXITCODE -ne 0){throw "Native startup publish failed: $Flavor. See $Work"}
  # Flat intermediate folders still run exporters through the managed launcher.
  $script:DustweaveNativeEntries[[IO.Path]::GetFullPath($Destination).TrimEnd('\')]=Join-Path $native 'Dustweave.Bootstrap.exe'
 }
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
 [ordered]@{layout=2;runtime=$runtime;bootstrapFiles=$bootstrap;launcher=$(if($bridge){'1.0.0'}else{'2.0.0'});updater='2.0.0';bridge=$bridge;nativeEntry=(!$bridge);flavor=$Flavor;sharedResources=$resources}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $Destination 'package-components.json') -Encoding utf8
}
