param(
 [Parameter(Mandatory)][ValidateSet('status','submit')][string]$Action,
 [Parameter(Mandatory)][string]$Config,
 [string]$PackageDirectory,
 [string]$Evidence,
 [string]$ExpectedCurrent,
 [string]$Operation,
 [switch]$Wait,
 [string]$PrivateKeyFile=(Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi')
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$settings=Get-Content -LiteralPath $Config -Raw|ConvertFrom-Json
if($settings.Host -notmatch '^[a-zA-Z0-9.-]+$' -or $settings.User -ne 'dustweave-ota'){throw 'Invalid constrained publisher endpoint.'}
function InvokeChannel([string]$command,[byte[]]$header=$null,[object[]]$files=@()){
 $info=[Diagnostics.ProcessStartInfo]::new('ssh');$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden'
 $info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.WorkingDirectory=(Get-Location).Path
 foreach($arg in @('-i',$settings.IdentityFile,'-o','BatchMode=yes','-o','IdentitiesOnly=yes','-o','ConnectTimeout=8','-o','ServerAliveInterval=15','-o','ServerAliveCountMax=3',"$($settings.User)@$($settings.Host)",$command)){$info.ArgumentList.Add($arg)}
 $process=[Diagnostics.Process]::Start($info)
 $output=$process.StandardOutput.ReadToEndAsync();$errorText=$process.StandardError.ReadToEndAsync()
 try {
  if($header){
   $prefix=[BitConverter]::GetBytes([int]$header.Length);if([BitConverter]::IsLittleEndian){[Array]::Reverse($prefix)}
   $process.StandardInput.BaseStream.Write($prefix);$process.StandardInput.BaseStream.Write($header)
   foreach($file in $files){$stream=[IO.File]::OpenRead($file.Path);try{$stream.CopyTo($process.StandardInput.BaseStream)}finally{$stream.Dispose()}}
  }
  $process.StandardInput.Close()
  if(!$process.WaitForExit(960000)){throw 'Publisher timed out. Query the saved operation before retrying.'}
  $text=$output.GetAwaiter().GetResult();$errors=$errorText.GetAwaiter().GetResult()
  if($process.ExitCode -ne 0){throw "Channel failed: $text $errors"}
  return $text|ConvertFrom-Json
 }finally{$process.Dispose()}
}
function WriteJson($path,$value){[IO.File]::WriteAllText([IO.Path]::GetFullPath($path),($value|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))}
if($Action -eq 'status'){
 $command='dustweave-ota status'
 if($Operation){$saved=Get-Content -LiteralPath $Operation -Raw|ConvertFrom-Json;if($saved.nonce -notmatch '^[0-9a-f]{32}$'){throw 'Invalid operation ID.'};$command+=" $($saved.nonce)"}
 do {
  $status=InvokeChannel $command;$status|ConvertTo-Json -Depth 20
  if($status.State -in @('failed','error','needs_attention')){throw "Publication requires attention: $($status.Error)"}
  if(!$Wait -or !$Operation -or $status.State -notin @('receiving','accepted','publishing')){break}
  Start-Sleep -Seconds 3
 }while($true)
 exit
}
if(!$PackageDirectory -or !$Evidence -or !$Operation -or !$ExpectedCurrent){throw 'Submit requires packages, packaged acceptance evidence, expected current version and a new operation file.'}
if(Test-Path -LiteralPath $Operation){throw 'Operation exists. Query its status; never blindly resubmit.'}
$current=InvokeChannel 'dustweave-ota status'
if($current.version -ne $ExpectedCurrent){throw "Current OTA is $($current.version), not $ExpectedCurrent"}
$folder=[IO.Path]::GetFullPath($PackageDirectory)
# This also verifies the signed feed and every immutable archive before opening an upload.
& (Join-Path $PSScriptRoot 'prepare-update-site.ps1') -PackageDirectory $folder -VerifyOnly
$feedBytes=[IO.File]::ReadAllBytes((Join-Path $folder 'updates.json'))
$feedEnvelope=[Text.Encoding]::UTF8.GetString($feedBytes)|ConvertFrom-Json
$feed=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($feedEnvelope.Payload))|ConvertFrom-Json
$release=$feed.Releases[0]
if([semver]$release.Version -le [semver]$current.version){throw 'Release must advance.'}
$evidenceBytes=[IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Evidence))
$proof=[Text.Encoding]::UTF8.GetString($evidenceBytes)|ConvertFrom-Json
if($proof.status -ne 'passed'){throw 'Packaged acceptance must pass.'}
foreach($flavor in @('Lite','Portable')){
 if(!@($proof.cases|Where-Object {$_.case -eq "upgrade-$flavor" -and $_.status -eq 'passed' -and $_.to -eq $release.Version -and $_.baselineHelper}).Count){throw "Missing actual baseline-helper acceptance: $flavor"}
}
$blobs=@(@($release.Assets)+@($release.Deltas)|Sort-Object FileName -CaseSensitive|ForEach-Object {[ordered]@{FileName=$_.FileName;Bytes=[long]$_.Bytes;Sha256=$_.Sha256.ToLowerInvariant()}})
$nonce=[Guid]::NewGuid().ToString('N');$now=[DateTimeOffset]::UtcNow
$request=[ordered]@{Schema=1;Product='Dustweave';Version=$release.Version;ExpectedFeedSha256=$current.feedSha256;FeedSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($feedBytes)).ToLowerInvariant();EvidenceSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($evidenceBytes)).ToLowerInvariant();Nonce=$nonce;IssuedUtc=$now.ToString('O');ExpiresUtc=$now.AddMinutes(15).ToString('O');Files=$blobs}
$payload=[Text.Encoding]::UTF8.GetBytes(($request|ConvertTo-Json -Depth 10 -Compress));$key=[Security.Cryptography.ECDsa]::Create()
try {
 $raw=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($PrivateKeyFile)),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
 try{$key.ImportPkcs8PrivateKey($raw,[ref]([int]0))}finally{[Array]::Clear($raw,0,$raw.Length)}
 $trust=Get-Content -LiteralPath (Join-Path $root 'assets/updates/trust.json') -Raw|ConvertFrom-Json
 $public=[Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo());$keyIds=@($trust.Keys.PSObject.Properties|Where-Object Value -eq $public|ForEach-Object Name)
 if($keyIds.Count -ne 1){throw 'Publisher key does not match trusted OTA key.'}
 $signedBytes=[Text.Encoding]::UTF8.GetBytes("Dustweave-OTA-Publish-v1`n")+$payload
 $signature=$key.SignData($signedBytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
 $envelope=[ordered]@{Schema=1;KeyId=$keyIds[0];Algorithm='ECDSA-P256-SHA256';Payload=[Convert]::ToBase64String($payload);Signature=[Convert]::ToBase64String($signature)}
}finally{$key.Dispose()}
$header=[Text.Encoding]::UTF8.GetBytes((@{Request=$envelope;Feed=[Convert]::ToBase64String($feedBytes);Evidence=[Convert]::ToBase64String($evidenceBytes)}|ConvertTo-Json -Depth 20 -Compress))
WriteJson $Operation @{nonce=$nonce;version=$release.Version;expectedCurrent=$current.version;feedSha256=$request.FeedSha256;state='submission_started';startedUtc=$now.ToString('O')}
$paths=@($blobs|ForEach-Object {@{Path=Join-Path $folder $_.FileName}})
try{$result=InvokeChannel 'dustweave-ota submit' $header $paths;$result|ConvertTo-Json -Depth 20}
catch{throw "Submission result is uncertain. Query -Action status -Operation '$Operation' before retrying. $($_.Exception.Message)"}
if($Wait){& $PSCommandPath -Action status -Config $Config -Operation $Operation -Wait}
