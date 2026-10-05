param([switch]$NoBuild)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)
$project=Join-Path $root 'tests/Dustweave.Tests/Dustweave.Tests.csproj'
if(!(Test-Path -LiteralPath (Join-Path $root 'source-manifest.json'))){throw 'Run the test entry from an Dustweave repository.'}
if(!$NoBuild){
 & dotnet restore $project --locked-mode -r win-x64 -p:SelfContained=false --nologo
 if($LASTEXITCODE -ne 0){throw 'Test dependency restore failed.'}
 & dotnet build $project -c Release -r win-x64 -p:SelfContained=false --no-restore --nologo
 if($LASTEXITCODE -ne 0){throw 'Test build failed.'}
}
$exe=Join-Path (Split-Path -Parent $project) 'bin/Release/net8.0-windows/win-x64/BD2Daily.Tests.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Build the synthetic regression runner first.'}
$output=Join-Path $root ('artifacts/tests-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-ffff'))
$start=[Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
$start.WorkingDirectory=(Get-Location).Path
$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$start.Environment['DUSTWEAVE_TEST_SOURCE_ROOT']=$root
$start.Environment['BD2_DAILY_DATA_ROOT']=Join-Path $output 'isolated-user'
$start.Environment['DUSTWEAVE_PLUGIN']='none'
$start.ArgumentList.Add($output)
$proc=[Diagnostics.Process]::Start($start)
$stdout=$proc.StandardOutput.ReadToEndAsync();$stderr=$proc.StandardError.ReadToEndAsync()
$deadline=[DateTime]::UtcNow.AddMinutes(3)
while(!$proc.WaitForExit(1000)){
 if([DateTime]::UtcNow -gt $deadline){throw "Tests remain active (PID $($proc.Id)): $output. Inspect before retrying."}
}
[IO.Directory]::CreateDirectory($output)|Out-Null
$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()|Set-Content -LiteralPath (Join-Path $output 'run.log') -Encoding utf8
if($proc.ExitCode -ne 0){throw "Regression failed; see $output"}
$report=Get-Content -LiteralPath (Join-Path $output 'results.json') -Raw|ConvertFrom-Json
if($report.status -ne 'passed' -or $report.realGameTouched -ne $false -or $report.capturedFixturesIncluded -ne $false){throw 'Unexpected test evidence.'}
[pscustomobject]@{status=$report.status;checks=$report.count;groups=$report.groups.Count;sourceRoot=$report.sourceRoot;output=$output}|ConvertTo-Json
