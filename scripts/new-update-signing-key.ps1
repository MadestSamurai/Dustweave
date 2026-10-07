param(
 [string]$PrivateKeyFile = (Join-Path $env:LOCALAPPDATA 'DustweavePublisher/update-signing.dpapi'),
 [string]$PublicConfig = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets/updates/trust.json')
)
$ErrorActionPreference='Stop'
$secret=[IO.Path]::GetFullPath($PrivateKeyFile)
$repo=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if($secret.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Keep signing keys outside the source repository.'}
if(Test-Path -LiteralPath $secret){throw 'Signing key already exists. Do not rotate without an overlap release.'}
if(Test-Path -LiteralPath $PublicConfig){throw 'Public configuration already exists. Preserve the established trust root.'}
$folder=Split-Path -Parent $secret
[IO.Directory]::CreateDirectory($folder)|Out-Null
$acl=[Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false)
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $folder -AclObject $acl
$key=[Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try{
 $raw=$key.ExportPkcs8PrivateKey()
 try{
  $encrypted=[Security.Cryptography.ProtectedData]::Protect($raw,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
  [IO.File]::WriteAllBytes($secret,$encrypted)
 }finally{[Array]::Clear($raw,0,$raw.Length)}
 $public=[Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
 $id='dustweave-'+[DateTime]::UtcNow.ToString('yyyyMMdd')
 $config=[ordered]@{Keys=@{$id=$public};Sources=@([ordered]@{Id='cn';FeedUrl='https://bd2.madsam.work/updates/dustweave/updates.json';PackageBaseUrl='https://bd2.madsam.work/updates/dustweave'})}
 [IO.Directory]::CreateDirectory((Split-Path -Parent $PublicConfig))|Out-Null
 [IO.File]::WriteAllText([IO.Path]::GetFullPath($PublicConfig),($config|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
 Write-Host "Created protected signing key outside the repository. Public key ID: $id"
}finally{$key.Dispose()}
