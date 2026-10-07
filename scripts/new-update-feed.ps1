param(
 [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
 [Parameter(Mandatory)][string]$PackageDirectory,
 [string]$PreviousFeed,
 [string]$Output,
 [string]$PrivateKeyFile = (Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi')
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$folder=[IO.Path]::GetFullPath($PackageDirectory)
if(!$Output){$Output=Join-Path $folder 'updates.json'}
$trust=Get-Content -LiteralPath (Join-Path $root 'assets/updates/trust.json') -Raw|ConvertFrom-Json
function VerifyEnvelope($envelope) {
 if($envelope.Schema -ne 1 -or $envelope.Algorithm -ne 'ECDSA-P256-SHA256'){throw 'Invalid signed envelope.'}
 $public=$trust.Keys.PSObject.Properties[$envelope.KeyId].Value
 if(!$public){throw 'Unknown update signing key.'}
 $verifier=[Security.Cryptography.ECDsa]::Create()
 try {
  $verifier.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($public),[ref]([int]0))
  $payload=[Convert]::FromBase64String($envelope.Payload)
  if(!$verifier.VerifyData($payload,[Convert]::FromBase64String($envelope.Signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)){throw 'Update signature verification failed.'}
  return [Text.Encoding]::UTF8.GetString($payload)|ConvertFrom-Json
 }finally{$verifier.Dispose()}
}
$history=@(Get-Content -LiteralPath (Join-Path $root 'src/Desktop/Localization/releases.json') -Raw|ConvertFrom-Json)
$note=$history|Where-Object Version -eq $Version|Select-Object -First 1
if(!$note){throw 'Add release notes in all three languages before creating the OTA feed.'}
foreach($language in @('zh-CN','zh-TW','en-US')){if(!@($note.Notes.$language).Count){throw "Missing release notes: $language"}}
$prior=@()
if($PreviousFeed){
 $feed=VerifyEnvelope (Get-Content -LiteralPath $PreviousFeed -Raw|ConvertFrom-Json)
 if($feed.Schema -ne 2 -or $feed.Product -ne 'Dustweave' -or $feed.Channel -ne 'stable'){throw 'Invalid previous update feed.'}
 if(@($feed.Releases|Where-Object {[version]$_.Version -ge [version]$Version}).Count){throw 'Publishing must advance the version. Never silently replace a published version.'}
 $prior=@($feed.Releases|Sort-Object {[version]$_.Version} -Descending)
 if(@($prior|Group-Object Version|Where-Object Count -gt 1).Count){throw 'Duplicate historical versions.'}
 for($i=0;$i -lt $prior.Count-1;$i++){if($prior[$i].PreviousVersion -ne $prior[$i+1].Version){throw 'Broken historical release chain.'}}
}
$assets=@()
foreach($flavor in @('Portable','Lite')){
 $name="Dustweave-$Version-$flavor-win-x64.zip";$path=Join-Path $folder $name
 if(!(Test-Path -LiteralPath $path)){throw "Required release package is missing: $name"}
 $archive=[IO.Compression.ZipFile]::OpenRead($path)
 try{
  $metadata=$archive.GetEntry('update-package.json')
  if(!$metadata -or !$archive.GetEntry('Dustweave.exe')){throw "Package has no OTA descriptor: $name"}
  $reader=[IO.StreamReader]::new($metadata.Open())
  try{$info=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
  if($info.Version -ne $Version -or $info.Flavor -ne $flavor){throw "Package descriptor mismatch: $name"}
  $members=@($archive.Entries|Where-Object {!$_.FullName.EndsWith('/')}|ForEach-Object FullName|Sort-Object)
  if(!$info.Files -or (Compare-Object $members @($info.Files|Sort-Object))){throw 'Package inventory must match the ZIP exactly.'}
 }finally{$archive.Dispose()}
 $file=Get-Item -LiteralPath $path
 $assets+=[ordered]@{Flavor=$flavor;FileName=$name;Bytes=$file.Length;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$previous=if($prior.Count){$prior[0].Version}else{$null}
$current=[ordered]@{Version=$Version;PreviousVersion=$previous;Layout=1;Notes=$note.Notes;Assets=$assets}
$feed=[ordered]@{Schema=2;Product='Dustweave';Channel='stable';Releases=@($current)+$prior}
$key=[Security.Cryptography.ECDsa]::Create()
try {
 # CI may supply the PKCS#8 key as a secret; local publishing uses a current-user DPAPI file.
 if($env:DUSTWEAVE_UPDATE_SIGNING_KEY){$raw=[Convert]::FromBase64String($env:DUSTWEAVE_UPDATE_SIGNING_KEY)}
 else {$raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PrivateKeyFile)),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)}
 try{$key.ImportPkcs8PrivateKey($raw,[ref]([int]0))}finally{[Array]::Clear($raw,0,$raw.Length)}
 $public=[Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
 $keyId=@($trust.Keys.PSObject.Properties|Where-Object Value -eq $public|ForEach-Object Name)
 if($keyId.Count -ne 1){throw 'Signing key does not match the embedded public key.'}
 $payload=[Text.Encoding]::UTF8.GetBytes(($feed|ConvertTo-Json -Depth 15 -Compress))
 $signature=$key.SignData($payload,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
 $envelope=[ordered]@{Schema=1;KeyId=$keyId[0];Algorithm='ECDSA-P256-SHA256';Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($signature)}
 [void](VerifyEnvelope ([pscustomobject]$envelope))
 [IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),($envelope|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 Write-Host "Signed OTA feed created: $Output"
}finally{$key.Dispose()}
