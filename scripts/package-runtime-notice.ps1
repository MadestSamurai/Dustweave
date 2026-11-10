param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$outputPath=[IO.Path]::GetFullPath($Output)
$artifacts=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
if(!$outputPath.StartsWith($artifacts,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $outputPath)){throw 'Use a new output under repository artifacts.'}
[IO.Directory]::CreateDirectory($outputPath)|Out-Null
$native=Join-Path $outputPath 'native'
& dotnet publish (Join-Path $repo 'tools/RuntimeNotice/Dustweave.RuntimeNotice.csproj') -c Release -r win-x64 -o $native --nologo *> (Join-Path $outputPath 'build.log')
if($LASTEXITCODE -ne 0){throw "Notice build failed: $outputPath/build.log"}
$exe=Join-Path $native 'Dustweave.exe'
if((Get-Item -LiteralPath $exe).Length -gt 8MB){throw 'The native notice unexpectedly grew; do not publish a runtime-sized reminder.'}
$test=Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
if(!$test.WaitForExit(15000) -or $test.ExitCode -ne 0){throw 'Native notice signature self-test failed.'}
$runtime=([xml](Get-Content (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.DustweaveRuntimeVersion|Where-Object {$_}|Select-Object -First 1
$hashes=@()
foreach($flavor in @('Lite','Portable')){
 $folder=Join-Path $outputPath $flavor;[IO.Directory]::CreateDirectory($folder)|Out-Null
 Copy-Item -LiteralPath $exe -Destination (Join-Path $folder 'Dustweave.exe')
 Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $folder 'LICENSE')
 $readme=@'
# Dustweave 1.6.10 — 升级提醒 / Upgrade notice

这不是完整应用。请下载并解压 1.7.0 完整包，再打开其中的 Dustweave.exe。
账号、设置与插件仍保留在原位置。不要删除用户数据，不要在游戏中退出账号。

This is an upgrade notice, not the full application. Download and extract the
complete 1.7.0 package, then run its Dustweave.exe. Keep existing user data and
stay signed in to the game. Portable includes the security-patched runtime.
Lite users should also update their installed .NET 8 Desktop Runtime to 8.0.33+.

https://github.com/MadestSamurai/Dustweave/releases/tag/v1.7.0
https://pan.quark.cn/s/0ee553c60c0d?pwd=5AgC

Dustweave is free software. If you paid a seller for it, request a refund.
'@
 [IO.File]::WriteAllText((Join-Path $folder 'README.md'),$readme,[Text.UTF8Encoding]::new($false))
 [ordered]@{Version='1.6.10';Flavor=$flavor;Layout=3;InstallerProtocol=1;RuntimeVersion=$runtime;BootstrapFiles=@();Files=@('Dustweave.exe','LICENSE','README.md','update-package.json')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'update-package.json') -Encoding utf8
 $name="Dustweave-1.6.10-$flavor-win-x64.zip";$zip=Join-Path $outputPath $name
 [IO.Compression.ZipFile]::CreateFromDirectory($folder,$zip,[IO.Compression.CompressionLevel]::SmallestSize,$false)
 $hashes+=(Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()+'  '+$name
}
[IO.File]::WriteAllLines((Join-Path $outputPath 'SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
Get-ChildItem -LiteralPath $outputPath -File -Filter '*.zip'|Select-Object Name,Length
