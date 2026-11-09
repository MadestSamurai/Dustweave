param([string]$SourceRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
# Official build policy, not DRM or a change to LICENSE. Runtime operation must
# never depend on this check. See the maintenance comment next to the shell notice.
[xml]$shell=Get-Content -LiteralPath (Join-Path $SourceRoot 'src/Desktop/MainWindow.xaml') -Raw
$ns=[Xml.XmlNamespaceManager]::new($shell.NameTable)
$ns.AddNamespace('w','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$notice=$shell.SelectSingleNode('//w:Grid[@x:Name="PageHeader"]/w:Border[@x:Name="FreeDistributionNotice"]',$ns)
if(!$notice){throw 'Official build is missing the persistent free-software notice.'}
foreach($node in @($notice.SelectNodes('ancestor-or-self::*'))+@($notice.SelectNodes('.//*'))){
 foreach($attribute in @('Visibility','Opacity')){
  if($node.HasAttribute($attribute) -and $node.GetAttribute($attribute) -ne $(if($attribute -eq 'Visibility'){'Visible'}else{'1'})){
   throw 'The free-software notice must initially be visible; only a verified local acknowledgement can hide it at runtime.'
  }
 }
}
$close=$notice.SelectSingleNode('.//w:Button[@x:Name="CloseFreeDistributionNoticeButton"]',$ns)
if(!$close -or $close.GetAttribute('Click') -ne 'CloseFreeDistributionNotice_Click' -or $close.GetAttribute('Content') -ne '{l:Tr distribution.close}'){
 throw 'The notice must offer the explicit typed-confirmation dismissal flow.'
}
foreach($pair in @(@('FreeDistributionTitle','distribution.free_title'),@('FreeDistributionRefund','distribution.refund'))){
 $label=$notice.SelectSingleNode('.//w:TextBlock[@x:Name="'+$pair[0]+'"]',$ns)
 if(!$label -or $label.GetAttribute('Text') -ne ('{l:Tr '+$pair[1]+'}') -or $label.GetAttribute('TextWrapping') -ne 'Wrap'){
  throw "Missing or clipped free-distribution text binding: $($pair[1])"
 }
}
[xml]$project=Get-Content -LiteralPath (Join-Path $SourceRoot 'src/Desktop/Dustweave.Desktop.csproj') -Raw
if(!$project.SelectSingleNode('//EmbeddedResource[@Include="Localization/strings.json"]')){
 throw 'Free-distribution translations must be compiled into the application.'
}
$strings=Get-Content -LiteralPath (Join-Path $SourceRoot 'src/Desktop/Localization/strings.json') -Raw|ConvertFrom-Json -AsHashtable
$terms=@{
 'zh-CN'=@('免费','请勿','确认收货','立即','退款')
 'zh-TW'=@('免費','請勿','確認收貨','立即','退款')
 'en-US'=@('free','do not confirm receipt','immediately','refund')
}
foreach($locale in $terms.Keys){
 $title=[string]$strings['distribution.free_title'][$locale]
 $refund=[string]$strings['distribution.refund'][$locale]
 if([string]::IsNullOrWhiteSpace($title) -or [string]::IsNullOrWhiteSpace($refund)){throw "Missing free-distribution translation: $locale"}
 foreach($key in @('distribution.close','distribution.close_title','distribution.local_confirmation','distribution.confirm_prompt','distribution.confirm_close','distribution.save_failed')){
  if([string]::IsNullOrWhiteSpace([string]$strings[$key][$locale])){throw "Missing confirmation translation: $key ($locale)"}
 }
 foreach($term in $terms[$locale]){
  if(!($title+' '+$refund).Contains($term,[StringComparison]::OrdinalIgnoreCase)){throw "Free/refund guidance is incomplete: $locale ($term)"}
 }
}
Write-Host 'Free-distribution notice: embedded translations and persistent shell bindings verified.'
