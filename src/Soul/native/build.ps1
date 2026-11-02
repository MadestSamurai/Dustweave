param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$destination=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
$sources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cpp' | Sort-Object Name | ForEach-Object FullName)
$compiler=(Get-Command g++ -ErrorAction Stop).Source
& $compiler '-std=c++17' '-shared' '-Wall' '-Wextra' '-Werror' '-ffp-contract=off' '-fno-fast-math' '-O3' '-DNDEBUG' '-flto' '-static' "-I$(Join-Path $PSScriptRoot 'include')" @sources '-o' $destination
if($LASTEXITCODE -ne 0){throw 'Soul native build failed.'}
Get-Item -LiteralPath $destination | Select-Object FullName,Length
Get-FileHash -LiteralPath $destination -Algorithm SHA256
