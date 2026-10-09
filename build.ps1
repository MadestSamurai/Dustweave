param([ValidateSet('Build','Preview','Smoke')][string]$Mode='Build',[switch]$RefreshDependencyLocks)
$ErrorActionPreference='Stop'
& dotnet --version
if($LASTEXITCODE -ne 0){throw 'The pinned .NET SDK is required.'}
$entry=Join-Path $PSScriptRoot 'src/Desktop/Dustweave.Desktop.csproj'
if(-not(Test-Path -LiteralPath $entry)){throw 'Dustweave source tree is incomplete.'}
$restoreMode=if($RefreshDependencyLocks){'--force-evaluate'}else{'--locked-mode'}
# RID-specific builds belong to project roots; MSBuild does not support -r on a solution.
$projects=@($entry)+@('tools/TradeData','tools/CompatibilityCli','tests/Dustweave.Tests'|ForEach-Object {Join-Path $PSScriptRoot $_})
foreach($project in $projects){
 & dotnet restore $project $restoreMode -r win-x64 -p:SelfContained=false --nologo
 if($LASTEXITCODE -ne 0){throw "Dependency restore failed: $project"}
 & dotnet build $project -c Release -r win-x64 -p:SelfContained=false --no-restore --nologo
 if($LASTEXITCODE -ne 0){throw "Build failed: $project"}
}
# The publishing service is cross-platform and keeps both RID targets in its dependency lock.
$channel=Join-Path $PSScriptRoot 'tools/OtaChannel/Dustweave.OtaChannel.csproj'
& dotnet restore $channel $restoreMode --nologo
if($LASTEXITCODE -ne 0){throw 'OTA channel restore failed.'}
& dotnet build $channel -c Release --no-restore --nologo
if($LASTEXITCODE -ne 0){throw 'OTA channel build failed.'}
if($Mode -eq 'Build'){return}
$exe=Join-Path (Split-Path -Parent $entry) 'bin/Release/net8.0-windows/win-x64/Dustweave.exe'
$runRoot=Join-Path $PSScriptRoot ('artifacts/'+$Mode.ToLowerInvariant()+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-ffff'))
[IO.Directory]::CreateDirectory($runRoot)|Out-Null
$start=[Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute=$false
$start.WorkingDirectory=(Get-Location).Path
$start.Environment['DUSTWEAVE_PLUGIN']='none'
$start.ArgumentList.Add($(if($Mode -eq 'Smoke'){'--smoke'}else{'--design-preview'}))
$start.ArgumentList.Add($runRoot)
$start.WindowStyle=$(if($Mode -eq 'Smoke'){[Diagnostics.ProcessWindowStyle]::Hidden}else{[Diagnostics.ProcessWindowStyle]::Normal})
$process=[Diagnostics.Process]::Start($start)
if($Mode -eq 'Preview'){Write-Host "Isolated preview: $runRoot";return}
$deadline=[DateTime]::UtcNow.AddMinutes(3)
while(-not $process.WaitForExit(1000)){
 if([DateTime]::UtcNow -gt $deadline){throw "UI smoke did not finish. Inspect process $($process.Id) and $runRoot before retrying."}
}
$result=Join-Path $runRoot 'smoke.json'
if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $result)){throw "UI smoke failed: $runRoot"}
$report=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
if($report.status -ne 'passed' -or $report.realGameTouched -ne $false){throw "UI smoke did not pass: $runRoot"}
Get-Content -LiteralPath $result -Raw
Write-Host "Isolated UI evidence: $runRoot"





