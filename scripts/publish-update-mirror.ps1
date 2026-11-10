param(
 [Parameter(Mandatory)][string]$Config,
 [Parameter(Mandatory)][string]$PackageDirectory,
 [Parameter(Mandatory)][string]$Receipt
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root 'tools/OtaMirror/Dustweave.OtaMirror.csproj'
dotnet restore $project --locked-mode
if($LASTEXITCODE -ne 0){throw 'Cloud uploader restore failed.'}
dotnet build $project -c Release --no-restore --nologo
if($LASTEXITCODE -ne 0){throw 'Cloud uploader build failed.'}
$uploader=Join-Path $root 'tools/OtaMirror/bin/Release/net8.0/Dustweave.OtaMirror.dll'
$publicTrust=Join-Path $root 'assets/updates/trust.json'
$retentionPlan=[IO.Path]::GetFullPath($Receipt)+'.retention-plan.json'
# Recheck live signed feeds immediately before applying this exact, owned-key plan.
# Each publication also enforces the whole-bucket capacity before its first PUT.
dotnet $uploader prune-plan ([IO.Path]::GetFullPath($Config)) $publicTrust $retentionPlan
if($LASTEXITCODE -ne 0){throw 'Cannot confirm live download dependencies; publication has not started.'}
$retention=Get-Content -LiteralPath $retentionPlan -Raw|ConvertFrom-Json
Write-Host ('Verified expired objects: '+$retention.Delete.Count+'; bytes: '+(($retention.Delete|Measure-Object Bytes -Sum).Sum))
dotnet $uploader prune-apply ([IO.Path]::GetFullPath($Config)) $publicTrust $retentionPlan ([IO.Path]::GetFullPath($Receipt)+'.retention.json')
if($LASTEXITCODE -ne 0){throw 'Cloud retention stopped; inspect its audit before publication.'}
dotnet run --project $project -c Release --no-restore -- upload ([IO.Path]::GetFullPath($Config)) ([IO.Path]::GetFullPath($PackageDirectory)) (Join-Path $root 'assets/updates/trust.json') ([IO.Path]::GetFullPath($Receipt))
if($LASTEXITCODE -ne 0){throw 'Cloud upload or public verification failed; the current feed has not changed.'}
