param([string]$Installation='C:\Program Files\Sandboxie-Plus', [string]$Output)
$ErrorActionPreference='Stop'
if(!$Output){$Output=Join-Path $PSScriptRoot ('../artifacts/resource-view-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))}
$Output=[IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory($Output)|Out-Null
$id=[Guid]::NewGuid().ToString('N').Substring(0,12)
$boxes=@("DWResource_${id}_A","DWResource_${id}_B")
$cases=[Collections.Generic.List[string]]::new()
function Run([string]$File,[string[]]$Arguments){
 [IO.File]::AppendAllText((Join-Path $Output 'commands.txt'),([DateTime]::UtcNow.ToString('O')+' '+$File+' '+($Arguments -join ' ') + "`n"))
 $start=[Diagnostics.ProcessStartInfo]::new($File);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
 $start.WorkingDirectory=[Environment]::SystemDirectory
 $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 foreach($arg in $Arguments){$start.ArgumentList.Add($arg)}
 $process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
 if(!$process.WaitForExit(90000)){try{$process.Kill()}catch{};throw "Test process did not exit: $($process.Id) $($Arguments -join ' ')"}
 $text=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()
 if($process.ExitCode -ne 0){throw "Test command failed ($($process.ExitCode)): $text"};return $text.Trim()
}
function Check([bool]$Ok,[string]$Label){if(!$Ok){throw $Label};$cases.Add($Label)}
function Overlay([int]$Box,[string]$Path){Join-Path (Join-Path $Output "box-$Box") ('drive\'+$Path.Substring(0,1)+'\'+$Path.Substring(3))}
$source=Join-Path $Output 'banks/host-bank.bin';[IO.Directory]::CreateDirectory((Split-Path $source))|Out-Null;[IO.File]::WriteAllText($source,'bank-v1')
$helper=Join-Path $Output 'fixture.exe'
$helperSource=Join-Path $Output 'fixture.cs'
@'
using System; using System.IO;
class Fixture { static int Main(string[] a) {
 try {
  if(a[2]=="write") File.WriteAllText(a[0],a[3]);
  if(a[2]=="delete") File.Delete(a[0]);
  if(a[2]=="recreate-parent") { Directory.Delete(Path.GetDirectoryName(a[0]),true); Directory.CreateDirectory(Path.GetDirectoryName(a[0])); File.WriteAllText(a[0],a[3]); }
  bool exists=File.Exists(a[0]);
  File.WriteAllText(a[1], "{\"Exists\":"+(exists?"true":"false")+",\"Data\":\""+(exists?File.ReadAllText(a[0]):"")+"\",\"Error\":\"\"}");
  return 0;
 } catch(Exception e) { File.WriteAllText(a[1],e.ToString()); return 1; }
} }
'@ |Set-Content -LiteralPath $helperSource -Encoding utf8
$null=Run 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' @('/nologo','/target:exe',('/out:'+$helper),$helperSource)
$step=0
function Idle([int]$Box){
 for($try=0;$try -lt 60;$try++){
  if((Run (Join-Path $Installation 'Start.exe') @(('/box:'+$boxes[$Box]),'/listpids')) -eq '0'){return $true}
  Start-Sleep -Milliseconds 500
 };return $false
}
function Probe([int]$Box,[string]$Mode='read',[string]$Value=''){
 # The service finishes last-process teardown asynchronously, after /wait returns.
 Start-Sleep -Milliseconds 1500
 $script:step++;$report=Join-Path $Output "read-$script:step.json"
 $null=Run (Join-Path $Installation 'Start.exe') @(('/box:'+$boxes[$Box]),'/wait',$helper,$source,$report,$Mode,$Value)
 $saved=Overlay $Box $report
 if(!(Test-Path -LiteralPath $saved)){throw "Missing isolated report: $saved"}
 $result=Get-Content -LiteralPath $saved -Raw|ConvertFrom-Json
 Copy-Item -LiteralPath $saved -Destination $report
 if($result.Error){throw $result.Error};return $result
}
$created=[Collections.Generic.List[int]]::new()
$removed=[Collections.Generic.List[int]]::new()
try {
 for($i=0;$i -lt 2;$i++){
  $existing=Run (Join-Path $Installation 'SbieIni.exe') @('query',$boxes[$i],'FileRootPath')
  if($existing){throw 'Test box name collision'}
  $created.Add($i)
  foreach($setting in @{FileRootPath=(Join-Path $Output "box-$i");Enabled='y';ConfigLevel='10';AutoDelete='n'}.GetEnumerator()){
   $null=Run (Join-Path $Installation 'SbieIni.exe') @('set',$boxes[$i],$setting.Key,$setting.Value)
  }
 }
 Check ((Probe 0).Data -eq 'bank-v1') 'Normal virtualized reads see the host bank'
 Check ((Probe 0 'write' 'bank-v1').Data -eq 'bank-v1') 'A private identical overlay can be created'
 $overlay=Overlay 0 $source;Check (Test-Path -LiteralPath $overlay) 'The private overlay exists'
 Check (Idle 0) 'The test box is idle before maintenance'
 # Delete this exact test-created file only, from outside the sandbox.
 $resolved=[IO.Path]::GetFullPath($overlay);if(!$resolved.StartsWith($Output+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Test path escaped its root'}
 Remove-Item -LiteralPath $resolved
 Check ((Probe 0).Data -eq 'bank-v1') 'Removing an identical overlay reveals the host bank'
 Check ((Probe 0 'write' 'sandbox-v2').Data -eq 'sandbox-v2') 'Subsequent updates remain writable'
 Check ([IO.File]::ReadAllText($source) -eq 'bank-v1') 'A sandbox update leaves the host unchanged'
 $copiedProgram=Overlay 1 $helper;[IO.Directory]::CreateDirectory((Split-Path $copiedProgram))|Out-Null
 Copy-Item -LiteralPath $helper -Destination $copiedProgram
 Check ((Probe 1).Data -eq 'bank-v1') 'A sandbox update leaves the second account unchanged'
 Check (Test-Path -LiteralPath $copiedProgram) 'A sandbox with its own program copy uses the same resource view'
 [IO.File]::WriteAllText($source,'host-v3')
 Check ((Probe 0).Data -eq 'sandbox-v2') 'A newer private bank remains isolated from host updates'
 Check ((Probe 1).Data -eq 'host-v3') 'An account with no overlay sees the updated host bank'
 Check (!(Probe 0 'delete').Exists) 'Sandbox deletion remains private'
 Check ((Probe 1).Data -eq 'host-v3') 'Deleting a bank in one account leaves other accounts intact'
 Check ((Probe 0 'write' 'host-v3').Data -eq 'host-v3') 'A bank can be downloaded again after private deletion'
 Check (Idle 0) 'The restored bank is idle before repeated maintenance'
 Remove-Item -LiteralPath $resolved
 Check ((Probe 0).Data -eq 'host-v3') 'Repeated overlay cleanup after delete and repair still reveals the host'
 Check ((Probe 0 'recreate-parent' 'host-v3').Data -eq 'host-v3') 'A privately recreated directory can contain an identical bank'
 Check (Idle 0) 'The recreated directory is idle'
 $marker=[IO.Directory]::GetCreationTimeUtc((Split-Path $resolved)).ToFileTimeUtc()
 @{creationTime=$marker;isV1DeletionMarker=($marker -eq 0x01B01234DEAD44A0)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'parent-marker.json')
 if($marker -eq 0x01B01234DEAD44A0){
  Remove-Item -LiteralPath $resolved
  Check (!(Probe 0).Exists) 'A deleted-parent marker hides the host bank and must prevent optimization'
 }
}finally {
 foreach($i in $created){
  if(Idle $i){
   $null=Run (Join-Path $Installation 'SbieIni.exe') @('set',$boxes[$i],'*','')
   $removed.Add($i)
  }
 }
}
@{status='passed';realGameTouched=$false;existingSandboxesTouched=$false;cases=$cases.ToArray()}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $Output 'result.json') -Encoding utf8
# Only this run's generated roots, after successful checks and confirmed process exit.
foreach($i in 0..1){
 $target=[IO.Path]::GetFullPath((Join-Path $Output "box-$i"))
 if(!$target.StartsWith($Output+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture cleanup escaped its output'}
 if(!$removed.Contains($i)){throw 'Fixture still active; root retained'}
 if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Recurse -Force}
}
@{status='passed';checks=$cases.Count;output=$Output}|ConvertTo-Json
