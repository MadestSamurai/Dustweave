param(
 [Parameter(Mandatory)][string]$PackageDirectory,
 [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path $PackageDirectory 'site'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve previous staging output; choose a new directory.'}
$source=[IO.Path]::GetFullPath($PackageDirectory)
$envelope=Get-Content -LiteralPath (Join-Path $source 'updates.json') -Raw|ConvertFrom-Json
$trust=Get-Content -LiteralPath (Join-Path $root 'assets/updates/trust.json') -Raw|ConvertFrom-Json
$public=$trust.Keys.PSObject.Properties[$envelope.KeyId].Value
$key=[Security.Cryptography.ECDsa]::Create()
try{
 if($envelope.Schema -ne 1 -or $envelope.Algorithm -ne 'ECDSA-P256-SHA256' -or !$public){throw 'Untrusted update manifest.'}
 $key.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($public),[ref]([int]0))
 $payload=[Convert]::FromBase64String($envelope.Payload)
 if(!$key.VerifyData($payload,[Convert]::FromBase64String($envelope.Signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)){throw 'Update signature invalid.'}
 $feed=[Text.Encoding]::UTF8.GetString($payload)|ConvertFrom-Json
}finally{$key.Dispose()}
$release=$feed.Releases[0]
if($feed.Schema -ne 2 -or $feed.Product -ne 'Dustweave' -or $feed.Channel -ne 'stable' -or $release.Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release identity.'}
$target=Join-Path $output ('v'+$release.Version)
[IO.Directory]::CreateDirectory($target)|Out-Null
foreach($asset in $release.Assets){
 $expected="Dustweave-$($release.Version)-$($asset.Flavor)-win-x64.zip"
 if($asset.Flavor -notin @('Portable','Lite') -or $asset.FileName -ne $expected){throw 'Invalid asset name.'}
 $file=Join-Path $source $expected
 if((Get-Item -LiteralPath $file).Length -ne $asset.Bytes -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $asset.Sha256){throw "Asset verification failed: $expected"}
 Copy-Item -LiteralPath $file -Destination $target
}
foreach($delta in @($release.Deltas)){
 if(!$delta){continue}
 $expected="Dustweave-$($release.Version)-$($delta.Flavor)-from-$($delta.FromVersion)-win-x64.delta.zip"
 if($delta.Flavor -notin @('Portable','Lite') -or $delta.FromVersion -notmatch '^\d+\.\d+\.\d+$' -or $delta.FileName -ne $expected -or $delta.Algorithm -ne 'dustweave-cdc-v1'){throw 'Invalid delta asset name.'}
 $file=Join-Path $source $expected
 if((Get-Item -LiteralPath $file).Length -ne $delta.Bytes -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $delta.Sha256){throw "Delta verification failed: $expected"}
 Copy-Item -LiteralPath $file -Destination $target
}

Copy-Item -LiteralPath (Join-Path $source 'updates.json') -Destination $output
[ordered]@{status='prepared';version=$release.Version;targetUrl='https://bd2.madsam.work/updates/dustweave/';uploadOrder=@("v$($release.Version)/",'updates.json (atomic rename last)');signed=$true;deployed=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'deployment-plan.json') -Encoding utf8
Write-Host "Static website payload ready: $output"
