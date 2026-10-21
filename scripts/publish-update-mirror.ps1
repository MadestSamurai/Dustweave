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
dotnet run --project $project -c Release --no-restore -- upload ([IO.Path]::GetFullPath($Config)) ([IO.Path]::GetFullPath($PackageDirectory)) (Join-Path $root 'assets/updates/trust.json') ([IO.Path]::GetFullPath($Receipt))
if($LASTEXITCODE -ne 0){throw 'Cloud upload or public verification failed; the current feed has not changed.'}
