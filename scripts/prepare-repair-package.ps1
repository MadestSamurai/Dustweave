param(
 [Parameter(Mandatory)][string]$PackageDirectory,
 [Parameter(Mandatory)][string]$OutputDirectory,
 [string]$PrivateKeyFile=(Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi')
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root 'tools/OtaMirror/Dustweave.OtaMirror.csproj'
dotnet run --project $project -c Release -- build-repair ([IO.Path]::GetFullPath($PackageDirectory)) ([IO.Path]::GetFullPath($OutputDirectory)) (Join-Path $root 'assets/updates/trust.json')
if($LASTEXITCODE -ne 0){throw 'Repair inventory build failed.'}
$key=[Security.Cryptography.ECDsa]::Create()
try {
 $raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PrivateKeyFile)),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
 try{$key.ImportPkcs8PrivateKey($raw,[ref]([int]0))}finally{[Array]::Clear($raw,0,$raw.Length)}
 $trust=Get-Content -LiteralPath (Join-Path $root 'assets/updates/trust.json') -Raw|ConvertFrom-Json
 $public=[Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
 $ids=@($trust.Keys.PSObject.Properties|Where-Object Value -eq $public|ForEach-Object Name)
 if($ids.Count -ne 1){throw 'Signing key does not match repair trust.'}
 foreach($file in Get-ChildItem -LiteralPath (Join-Path $OutputDirectory 'repair') -Filter '*.unsigned' -Recurse -File){
  $payload=[IO.File]::ReadAllBytes($file.FullName)
  $data=[Text.Encoding]::UTF8.GetBytes("Dustweave-Repair-v1`n")+$payload
  $target=$file.FullName.Substring(0,$file.FullName.Length-9)
  if(Test-Path -LiteralPath $target){
   $old=Get-Content -LiteralPath $target -Raw|ConvertFrom-Json
   if($old.Payload -ne [Convert]::ToBase64String($payload) -or !$key.VerifyData($data,[Convert]::FromBase64String($old.Signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)){throw 'Existing immutable repair manifest differs.'}
  } else {
   $signature=$key.SignData($data,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
   $signed=[ordered]@{Schema=1;KeyId=$ids[0];Algorithm='ECDSA-P256-SHA256';Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($signature)}
   [IO.File]::WriteAllText($target,($signed|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
  }
  Remove-Item -LiteralPath $file.FullName
 }
} finally {$key.Dispose()}
Write-Output "Signed repair inventory ready: $OutputDirectory"
