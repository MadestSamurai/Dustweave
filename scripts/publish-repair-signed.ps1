param(
 [Parameter(Mandatory)][string]$Config,
 [Parameter(Mandatory)][string]$RepairDirectory,
 [Parameter(Mandatory)][string]$Operation,
 [string]$CloudConfig,
 [switch]$RefreshOrigin,
 [switch]$Capable,
 [string]$PrivateKeyFile=(Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi')
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$settings=Get-Content -LiteralPath $Config -Raw|ConvertFrom-Json
if($settings.Host -notmatch '^[a-zA-Z0-9.-]+$' -or $settings.User -ne 'dustweave-ota-v2'){throw 'Repair requires the constrained v2 publishing account.'}
if(Test-Path -LiteralPath $Operation){throw 'Operation exists. Query its saved nonce with the normal status command before retrying.'}
function InvokeRepairChannel([string]$command,[byte[]]$header=$null,[object[]]$files=@()){
 if($Capable){$command=$command.Replace('dustweave-ota ','dustweave-ota capable ')}
 $info=[Diagnostics.ProcessStartInfo]::new('ssh');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden'
 $info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($arg in @('-i',$settings.IdentityFile,'-o','BatchMode=yes','-o','IdentitiesOnly=yes','-o','ConnectTimeout=8','-o','ServerAliveInterval=15','-o','ServerAliveCountMax=3',"$($settings.User)@$($settings.Host)",$command)){$info.ArgumentList.Add($arg)}
 $process=[Diagnostics.Process]::Start($info);$output=$process.StandardOutput.ReadToEndAsync();$errors=$process.StandardError.ReadToEndAsync()
 try{
  if($header){
   $prefix=[BitConverter]::GetBytes([int]$header.Length);if([BitConverter]::IsLittleEndian){[Array]::Reverse($prefix)}
   $process.StandardInput.BaseStream.Write($prefix);$process.StandardInput.BaseStream.Write($header)
   foreach($file in $files){$stream=[IO.File]::OpenRead($file.Path);try{$stream.CopyTo($process.StandardInput.BaseStream)}finally{$stream.Dispose()}}
  }
  $process.StandardInput.Close()
  if(!$process.WaitForExit(960000)){throw 'Repair publication timed out; query its saved operation.'}
  $result=$output.GetAwaiter().GetResult();$errorText=$errors.GetAwaiter().GetResult()
  if($process.ExitCode -ne 0){throw "Repair channel failed: $result $errorText"}
  return $result|ConvertFrom-Json
 }finally{$process.Dispose()}
}
$directory=[IO.Path]::GetFullPath($RepairDirectory)
if($CloudConfig){
 dotnet run --project (Join-Path $root 'tools/OtaMirror/Dustweave.OtaMirror.csproj') -c Release -- upload-repair ([IO.Path]::GetFullPath($CloudConfig)) $directory (Join-Path $root 'assets/updates/trust.json') ($Operation+'.mirror.json')
 if($LASTEXITCODE -ne 0){throw 'Repair cloud verification failed.'}
}
$current=InvokeRepairChannel 'dustweave-ota status'
$feedBytes=[IO.File]::ReadAllBytes((Join-Path $directory 'updates.json'))
$feedEnvelope=[Text.Encoding]::UTF8.GetString($feedBytes)|ConvertFrom-Json
$feed=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($feedEnvelope.Payload))|ConvertFrom-Json
$release=$feed.Releases[0]
$manifests=@();$manifestHashes=@();$blocks=@{}
foreach($flavor in @('Lite','Portable')){
 $path=Join-Path $directory "repair/v$($release.Version)/Dustweave-$($release.Version)-$flavor-repair.json"
 $bytes=[IO.File]::ReadAllBytes($path);$manifests += [Convert]::ToBase64String($bytes)
 $manifestHashes += [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
 $envelope=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
 $manifest=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.Payload))|ConvertFrom-Json
 foreach($file in $manifest.Files){foreach($block in $file.Blocks){$name=$block.Sha256.ToLowerInvariant()+'.bin';$blocks[$name]=[ordered]@{FileName=$name;Bytes=[long]$block.Bytes;Sha256=$block.Sha256.ToLowerInvariant()}}}
}
# This read-only index avoids retransmitting unchanged runtime blocks every release.
# The server independently checks every omitted block against the signed inventory.
$index=InvokeRepairChannel 'dustweave-ota repair-index'
$available=@{};foreach($item in $index.files){$available[$item.FileName]=[long]$item.Bytes}
$blobs=@($blocks.Values|Where-Object {$RefreshOrigin -or !$available.ContainsKey($_.FileName) -or $available[$_.FileName] -ne $_.Bytes}|Sort-Object FileName -CaseSensitive)
foreach($blob in $blobs){
 $path=Join-Path $directory "repair/objects/$($blob.FileName)"
 if((Get-Item -LiteralPath $path).Length -ne $blob.Bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $blob.Sha256){throw 'Repair block changed locally.'}
}
$now=[DateTimeOffset]::UtcNow;$nonce=[Guid]::NewGuid().ToString('N')
$request=[ordered]@{Schema=1;Product='Dustweave';Version=$release.Version;ExpectedFeedSha256=$current.feedSha256;FeedSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($feedBytes)).ToLowerInvariant();Nonce=$nonce;IssuedUtc=$now.ToString('O');ExpiresUtc=$now.AddMinutes(15).ToString('O');ManifestHashes=$manifestHashes;Files=$blobs}
$payload=[Text.Encoding]::UTF8.GetBytes(($request|ConvertTo-Json -Depth 10 -Compress));$key=[Security.Cryptography.ECDsa]::Create()
try{
 $raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PrivateKeyFile)),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
 try{$key.ImportPkcs8PrivateKey($raw,[ref]([int]0))}finally{[Array]::Clear($raw,0,$raw.Length)}
 $trust=Get-Content -LiteralPath (Join-Path $root 'assets/updates/trust.json') -Raw|ConvertFrom-Json
 $public=[Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo());$ids=@($trust.Keys.PSObject.Properties|Where-Object Value -eq $public|ForEach-Object Name)
 if($ids.Count -ne 1){throw 'Unexpected repair signing key.'}
 $signature=$key.SignData(([Text.Encoding]::UTF8.GetBytes("Dustweave-Repair-Publish-v1`n")+$payload),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
 $signed=[ordered]@{Schema=1;KeyId=$ids[0];Algorithm='ECDSA-P256-SHA256';Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($signature)}
}finally{$key.Dispose()}
$header=[Text.Encoding]::UTF8.GetBytes((@{Request=$signed;Feed=[Convert]::ToBase64String($feedBytes);Manifests=$manifests}|ConvertTo-Json -Depth 20 -Compress))
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Operation),(@{nonce=$nonce;version=$release.Version;kind='repair';state='submission_started';startedUtc=$now.ToString('O')}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$files=@($blobs|ForEach-Object {@{Path=Join-Path $directory "repair/objects/$($_.FileName)"}})
$result=InvokeRepairChannel 'dustweave-ota repair' $header $files
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Operation)+'.result.json',($result|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
$result|ConvertTo-Json -Depth 10
if($result.status -ne 'completed'){throw 'Repair publication incomplete; query the saved operation.'}
