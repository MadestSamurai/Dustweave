param([Parameter(Mandatory)][string]$Packages,[Parameter(Mandatory)][string]$RepairDirectory,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$Packages=[IO.Path]::GetFullPath($Packages);$RepairDirectory=[IO.Path]::GetFullPath($RepairDirectory);$Output=[IO.Path]::GetFullPath($Output)
if((Test-Path -LiteralPath $Output) -or $Output -notmatch '[\\/]artifacts[\\/]'){throw 'Use a new artifacts path'}
[IO.Directory]::CreateDirectory($Output)|Out-Null
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class RepairAcceptanceWindow { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l); }
"@
function J($p,$v){[IO.Directory]::CreateDirectory((Split-Path -Parent $p))|Out-Null;[IO.File]::WriteAllText($p,($v|ConvertTo-Json -Depth 70),[Text.UTF8Encoding]::new($false))}
function CopyTree($a,$b){[IO.Directory]::CreateDirectory($b)|Out-Null;foreach($e in Get-ChildItem -LiteralPath $a){Copy-Item -LiteralPath $e.FullName -Destination $b -Recurse}}
$results=@()
foreach($flavor in @('Lite','Portable')){
 $folder=Join-Path $Output $flavor;$install=Join-Path $folder 'install';$data=Join-Path $folder 'data';CopyTree (Join-Path $Packages $flavor) $install
 $version=(Get-Content (Join-Path $install 'update-package.json') -Raw|ConvertFrom-Json).Version
 $cache=Join-Path $data "updates/$version-$flavor";CopyTree (Join-Path $Packages $flavor) (Join-Path $cache 'repair-staging')
 Copy-Item -LiteralPath (Join-Path $Packages 'updates.json') -Destination (Join-Path $cache 'signed-feed.json')
 Copy-Item -LiteralPath (Join-Path $RepairDirectory "repair/v$version/Dustweave-$version-$flavor-repair.json") -Destination (Join-Path $cache 'repair-manifest.json')
 [IO.Directory]::CreateDirectory((Join-Path $cache 'bootstrap-helper'))|Out-Null
 Copy-Item -LiteralPath (Join-Path $install 'app/support/Dustweave.Updater.exe') -Destination (Join-Path $cache 'bootstrap-helper/Dustweave.Updater.exe')
 J (Join-Path $data 'updates-preference.json') @{Automatic=$false};J (Join-Path $data 'schedule.json') @{Enabled=$false}
 J (Join-Path $data 'desktop-behavior.json') @{CloseAction='exit';RunOnOpen=$false};J (Join-Path $data 'first-run.json') @{Revision=1;State='completed';TourIndex=0}
 J (Join-Path $data 'seen-release.json') @{Version=$version;SeenVersions=@($version)}
 [IO.Directory]::CreateDirectory((Join-Path $install 'plugins'))|Out-Null;[IO.File]::WriteAllText((Join-Path $install 'plugins/acceptance.keep'),'preserve')
 [IO.File]::WriteAllText((Join-Path $data 'account.keep'),'preserve')
 # Root/native entry survives. All three managed recovery prerequisites are damaged.
 [IO.File]::WriteAllText((Join-Path $install 'update-package.json'),'{broken')
 [IO.File]::WriteAllText((Join-Path $install 'app/support/Dustweave.Updater.exe'),'broken repair helper')
 $broken=if($flavor -eq 'Portable'){'app/coreclr.dll'}else{'app/Dustweave.Launcher.dll'}
 [IO.File]::WriteAllText((Join-Path $install $broken),'broken managed host')
 if($flavor -eq 'Portable'){Remove-Item -LiteralPath (Join-Path $install 'app/hostfxr.dll')}else{[IO.File]::WriteAllText((Join-Path $install 'app/coreclr.dll'),'foreign overlaid runtime');[IO.File]::WriteAllText((Join-Path $install 'app/hostfxr.dll'),'foreign overlaid host')}
 $target=Join-Path $install 'Dustweave.exe';$start=[Diagnostics.ProcessStartInfo]::new($target);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
 $start.Environment['BD2_DAILY_DATA_ROOT']=$data;$start.Environment['DUSTWEAVE_PLUGIN']='none'
 $p=[Diagnostics.Process]::Start($start);$deadline=[DateTime]::UtcNow.AddSeconds(25)
 while(!$p.HasExited -and $p.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100;$p.Refresh()}
 if($p.HasExited -or $p.MainWindowHandle -eq 0){throw "Missing native repair prompt: $flavor"}
 # Answer only the dialog owned by the isolated executable started above.
 [void][RepairAcceptanceWindow]::PostMessage($p.MainWindowHandle,0x111,[IntPtr]6,[IntPtr]0)
 $deadline=[DateTime]::UtcNow.AddSeconds(120);$complete=$false
 while([DateTime]::UtcNow -lt $deadline){
  $path=Join-Path $data 'update-result.json'
  if(Test-Path -LiteralPath $path){$r=Get-Content $path -Raw|ConvertFrom-Json;if($r.State -eq 'completed' -and $r.Version -eq $version){$complete=$true;break}}
  Start-Sleep -Milliseconds 250
 }
 if(!$complete){throw "Native repair did not complete: $folder"}
 # Require the real application's startup acknowledgement before teardown,
 # rather than inferring success from a repaired descriptor or window title.
 $startup=Get-Content (Join-Path $data 'startup-state.json') -Raw|ConvertFrom-Json
 if($startup.stage -ne 'main-window-visible' -or $startup.version -ne $version){throw "Repaired application did not start: $folder"}
 $readyFiles=@(Get-ChildItem -LiteralPath (Join-Path $cache 'attempts') -Filter ready.json -Recurse -File)
 if(!@($readyFiles|Where-Object { $ready=Get-Content -LiteralPath $_.FullName -Raw|ConvertFrom-Json; $ready.Version -eq $version -and $ready.ProcessId -eq $startup.processId }).Count){throw "Missing repair startup acknowledgement: $folder"}
 $descriptor=Get-Content (Join-Path $Packages "$flavor/update-package.json") -Raw|ConvertFrom-Json
 foreach($name in $descriptor.Files){if((Get-FileHash -LiteralPath (Join-Path $install $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $Packages "$flavor/$name")).Hash){throw "Wrong repair bytes: $name"}}
 if((Get-Content (Join-Path $data 'account.keep')) -ne 'preserve' -or (Get-Content (Join-Path $install 'plugins/acceptance.keep')) -ne 'preserve'){throw 'User sentinel changed'}
 foreach($child in @(Get-Process Dustweave,Dustweave.Updater -ErrorAction SilentlyContinue)){
  if($child.Path -and $child.Path.StartsWith($folder+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
   # All update/startup assertions are complete. Fixture teardown does not
   # test the close button and must not depend on interactive window state.
   $child.Kill()
   if(!$child.WaitForExit(10000)){throw "Test process remains $($child.Id)"}
   J (Join-Path $folder ("teardown-"+$child.Id+".json")) @{processId=$child.Id;method='terminate-isolated-fixture-after-assertions';atUtc=[DateTimeOffset]::UtcNow.ToString('O')}
  }
 }
 $results+=@{case="native-repair-$flavor";status='passed';to=$version;confirmations=1;missingDescriptor=$true;brokenHelper=$true;brokenManagedHost=$true;verifiedCachedPayloads=$true;allFilesVerified=$true;startupAcknowledged=$true;teardown='isolated-process-disposal';computerUse=$false}
 Write-Host "Passed native-repair-$flavor"
}
J (Join-Path $Output 'results.json') @{status='passed';cases=$results;realGameTouched=$false;published=$false}
