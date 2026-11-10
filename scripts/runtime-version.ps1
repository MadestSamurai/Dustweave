# Single source for runtime packs, package descriptors and acceptance builds.
function Get-DustweaveRuntimeVersion {
 param([Parameter(Mandatory)][string]$Root)
 [xml]$props=Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw
 $pins=@($props.SelectNodes('/Project/PropertyGroup/DustweaveRuntimeVersion'))
 $version=$null
 if($pins.Count -ne 1 -or ![Version]::TryParse($pins[0].InnerText,[ref]$version) -or $version.Build -lt 0){throw 'A single explicit DustweaveRuntimeVersion is required.'}
 return $version.ToString()
}

function Assert-DustweavePublishedRuntime {
 param([string]$Application,[string]$Evidence,[string]$Runtime,[string]$Flavor)
 if(Test-Path -LiteralPath $Evidence){Remove-Item -LiteralPath $Evidence}
 $start=[Diagnostics.ProcessStartInfo]::new($Application)
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
 # Use the build's desktop runtime for Lite without changing the machine installation.
 $start.Environment['DOTNET_ROOT_X64']=Split-Path -Parent (Get-Command dotnet).Source
 $start.Environment['DOTNET_DISABLE_GUI_ERRORS']='1'
 $start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=Join-Path (Split-Path -Parent $Evidence) 'runtime-extract'
 $start.ArgumentList.Add('--package-runtime-check');$start.ArgumentList.Add($Evidence)
 $process=[Diagnostics.Process]::Start($start)
 try {
  if(!$process.WaitForExit(60000)){throw 'Published runtime probe did not finish; inspect the exact test process.'}
  if($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $Evidence)){throw "Published runtime probe failed: $Flavor"}
  $proof=Get-Content -LiteralPath $Evidence -Raw|ConvertFrom-Json
  foreach($name in @('runtime','core','compression','desktop')){
   # WPF includes a servicing build suffix in its informational version.
   $actual=[Version](($proof.$name -split '[+-]')[0])
   $expected=[Version]$Runtime
   if($actual.Major -ne $expected.Major -or $actual.Minor -ne $expected.Minor -or $actual -lt $expected -or ($Flavor -eq 'Portable' -and $actual -ne $expected)){
    throw "Published $Flavor $name version $actual does not match runtime policy $Runtime"
   }
  }
 } finally {$process.Dispose()}
}
