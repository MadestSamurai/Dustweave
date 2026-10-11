$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture=Join-Path $root ('artifacts/maintenance/guard-'+[Guid]::NewGuid().ToString('N'))
$count=0
function Put([string]$path,[string]$text){$path=Join-Path $fixture $path;[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,$text)}
function Check([bool]$condition,[string]$message){if(!$condition){throw $message};$script:count++;Write-Host "PASS $message"}
Put 'scripts/cleanup-release-artifacts.ps1' ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'cleanup-release-artifacts.ps1')))
# Synthetic signed feed and publication receipts; no network, game, signing secrets or real releases.
$key=[Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try{
 $payload=[Text.Encoding]::UTF8.GetBytes('{"Product":"Dustweave","Releases":[{"Version":"1.2.0","Assets":[],"Deltas":[]}]}')
 $feed=@{KeyId='test';Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($key.SignData($payload,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation))}|ConvertTo-Json
 Put 'assets/updates/trust.json' (@{Keys=@{test=[Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())}}|ConvertTo-Json)
 Put 'artifacts/releases/1.2.0/updates.json' $feed
 Put 'artifacts/deployments/1.2.0/public-updates.json' $feed
}finally{$key.Dispose()}
Put 'artifacts/deployments/1.2.0/public-verification.json' '{"status":"passed","version":"1.2.0","signatureVerified":true,"feedMatched":true}'
Put 'artifacts/deployments/1.2.0/github-release.json' '{"isDraft":false,"isPrerelease":false,"tagName":"v1.2.0"}'
git -C $fixture init --quiet
if($LASTEXITCODE){throw 'Cannot initialize synthetic source boundary.'}
$work='artifacts/package-1.2.0-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
Put "$work/source-before.json" '[]'
Put "$work/tables/table.json" 'generated'
Put "$work/locks/state.json" 'before'
Put "$work/live-client-daily/tracked.txt" 'source'
Put "$work/Portable-checks/evidence/result.json" '{"status":"passed"}'
Put 'artifacts/tests-20261011-000001-1234/results.json' '{"status":"passed"}'
Put 'artifacts/tests-20261011-000001-1234/sub/test.exe' 'binary copy'
Put 'artifacts/tests-20261011-000001-1234/sub/result.json' '{"evidence":true}'
Put 'artifacts/tests-20261011-000002-1234/results.json' '{"status":"failed"}'
Put 'artifacts/tests-20261011-000002-1234/test.exe' 'failure evidence'
Put 'artifacts/releases/1.2.0/Portable/Dustweave.exe' 'current application'
Put 'outside/sentinel.txt' 'never touch'
git -C $fixture add -f "$work/live-client-daily/tracked.txt"
if($LASTEXITCODE){throw 'Cannot track synthetic source file.'}
New-Item -ItemType Junction -Path (Join-Path $fixture "$work/drop-tables") -Target (Join-Path $fixture 'outside')|Out-Null
$command=Join-Path $fixture 'scripts/cleanup-release-artifacts.ps1'
$output=& (Get-Process -Id $PID).Path -NoProfile -File $command -Version 1.2.0
if($LASTEXITCODE){throw 'Preview failed.'}
$preview=($output -join "`n")|ConvertFrom-Json
$plan=Get-Content -LiteralPath $preview.plan -Raw|ConvertFrom-Json
Check ($plan.targets.Count -eq 3) 'Select only known generated targets'
Check ($plan.skipped.Count -eq 2) 'Refuse tracked files and junctions'
Put "$work/locks/state.json" 'changed after preview'
$plan.targets+=@{path=Join-Path $fixture 'outside';kind='directory';bytes=1;files=1;stamp='forged'}
$plan|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $preview.plan
$output=& (Get-Process -Id $PID).Path -NoProfile -File $command -Version 1.2.0 -Apply -Plan $preview.plan
if($LASTEXITCODE){throw 'Apply failed.'}
$result=($output -join "`n")|ConvertFrom-Json
Check ($result.targets -eq 2) 'Delete unchanged generated targets'
Check ($result.skipped.Count -eq 2) 'Refuse changed and forged targets'
foreach($path in @("$work/locks/state.json","$work/live-client-daily/tracked.txt","$work/Portable-checks/evidence/result.json",'outside/sentinel.txt','artifacts/tests-20261011-000001-1234/sub/result.json','artifacts/tests-20261011-000002-1234/test.exe','artifacts/releases/1.2.0/Portable/Dustweave.exe')){
 Check (Test-Path -LiteralPath (Join-Path $fixture $path)) "Preserve $path"
}
Check (!(Test-Path -LiteralPath (Join-Path $fixture "$work/tables"))) 'Remove generated tables'
Check (!(Test-Path -LiteralPath (Join-Path $fixture 'artifacts/tests-20261011-000001-1234/sub/test.exe'))) 'Remove successful-test binary'
. (Join-Path $PSScriptRoot 'copy-prepared-cache.ps1')
foreach($n in 1..4){
 $hash=('{0:x64}' -f $n);$client='a'*64
 Put "cache/modules/daily/$hash/$client/module.json" '{}'
 Put "cache/modules/daily/$hash/$client/module.dll" "cache$n"
 (Get-Item -LiteralPath (Join-Path $fixture "cache/modules/daily/$hash/$client/module.json")).LastWriteTimeUtc=[DateTime]::UtcNow.AddMinutes($n)
}
Put 'cache/build-old/assembly.dll' 'do not copy'
Copy-DustweavePreparedCache -Source (Join-Path $fixture 'cache') -Destination (Join-Path $fixture 'bounded-cache')
$files=@(Get-ChildItem -LiteralPath (Join-Path $fixture 'bounded-cache') -Recurse -File)
Check ($files.Count -eq 4) 'Copy only two cache generations'
Check (@($files|Where-Object Name -eq 'module.dll'|ForEach-Object {Get-Content $_.FullName}|Where-Object {$_ -notin @('cache3','cache4')}).Count -eq 0) 'Select newest cache generations'
@{status='passed';checks=$count;fixture=$fixture}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $fixture 'guard-results.json')
Write-Host "$count cleanup checks passed; evidence: $fixture"
