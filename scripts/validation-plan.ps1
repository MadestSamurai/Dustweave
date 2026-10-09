# Reuse a full validation only when every changed input is inside the account boundary.
function Get-DustweaveBuildSettingsHash([string]$Root) {
 $text=[IO.File]::ReadAllText((Join-Path $Root 'Directory.Build.props'))
 $text=[regex]::Replace($text,'(?s)(<DustweaveVersion\b[^>]*>).*?(</DustweaveVersion>)','$1PRODUCT_VERSION$2')
 [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}
function Get-DustweaveValidationPlan([string]$Root,[string]$InputsJson,[string]$Scope='Auto',[string]$BaselineDirectory='') {
 $settings=Get-DustweaveBuildSettingsHash $Root
 $full=[ordered]@{schema=1;scope='Full';buildSettingsHash=$settings;baseline='';groups=@();changed=@();reason='Full validation requested or no eligible baseline.'}
 if($Scope -eq 'Full'){return $full}
 if(!$BaselineDirectory){
  $releases=Join-Path $Root 'artifacts/releases'
  if(Test-Path -LiteralPath $releases){
   $BaselineDirectory=Get-ChildItem -LiteralPath $releases -Directory|Where-Object {$_.Name -match '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'}|Sort-Object {[semver]$_.Name} -Descending|Where-Object {
    $record=Join-Path $_.FullName 'validation-scope.json'
    (Test-Path -LiteralPath $record) -and ((Get-Content -LiteralPath $record -Raw|ConvertFrom-Json).scope -eq 'Full')
   }|Select-Object -First 1 -ExpandProperty FullName
  }
 }
 $reason='No completed full-validation baseline.'
 if($BaselineDirectory){
  $BaselineDirectory=[IO.Path]::GetFullPath($BaselineDirectory)
  $reportPath=Join-Path $BaselineDirectory 'release.json'
  $inputsPath=Join-Path $BaselineDirectory 'build-inputs.json'
  $scopePath=Join-Path $BaselineDirectory 'validation-scope.json'
  if((Test-Path -LiteralPath $reportPath) -and (Test-Path -LiteralPath $inputsPath) -and (Test-Path -LiteralPath $scopePath)){
   $prior=Get-Content -LiteralPath $scopePath -Raw|ConvertFrom-Json
   $release=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json
   $reason='Build settings or baseline validation changed.'
   if($prior.scope -eq 'Full' -and $prior.buildSettingsHash -eq $settings -and $release.product -eq 'Dustweave'){
    $old=@{};foreach($row in (Get-Content -LiteralPath $inputsPath -Raw|ConvertFrom-Json)){$old[$row.path.Replace('\','/')]=[string]$row.hash}
    $current=@{};foreach($row in ($InputsJson|ConvertFrom-Json)){$current[$row.path.Replace('\','/')]=[string]$row.hash}
    $changed=@(@($old.Keys)+@($current.Keys)|Sort-Object -Unique|Where-Object {$old[$_] -ne $current[$_]})
    $allowed=@(
     '^(AGENTS\.md|Directory\.Build\.props|source-manifest\.json|test\.ps1|package\.ps1)$',
     '^docs/', '^scripts/validation-plan\.ps1$', '^src/Accounts/[^/]+\.cs$',
     '^src/Core/(AccountSessions|DailyCoordinator|DailyLogin[^/]*|DailySandbox[^/]*|DailyParallel[^/]*|DailyAccountIdentity|DailyAccountOrder)\.cs$',
     '^src/Desktop/(DemoEnvironment|MainWindow\.(Accounts|Sandbox|Parallel)(\.Smoke)?|DailyParallelPanel|DailyParallelWorker)\.cs$',
     '^src/Desktop/Localization/(runtime|releases|strings)\.json$', '^tests/Dustweave.Tests/Program\.cs$',
     '^tests/Dustweave.Tests/Cases/(Startup|LoginIdentity|Sandbox|Parallel|ParallelProcess|AccountOrder|AccountIdentity|AccountRestart|QueueSession)Cases\.cs$'
    ) -join '|'
    $outside=@($changed|Where-Object {$_ -notmatch $allowed})
    $selectedScope='Accounts'
    $navigation='^src/Connection/(Bridge|LiveContracts|SelfTest)\.cs$|^src/Core/DailyHomeNavigation\.cs$|^tests/Dustweave.Tests/Cases/HomeNavigationCases\.cs$'
    if($outside.Count -and $Scope -ne 'Accounts' -and !@($outside|Where-Object {$_ -notmatch $navigation}).Count){$selectedScope='Navigation';$outside=@()}
    $rewards='^assets/specs/(policy|daily)-evidence-spec\.json$|^src/Connection/(EvidenceBindings|TapManifest|RewardNative)\.cs$|^src/Connection/live-binding-contract\.json$|^src/Shared/DailyBridgeVersion\.cs$|^src/Core/(DailyMissions|DailyPreferences|DailyEventRewards|DailyHunting)\.cs$|^src/Desktop/DailyPreferencesPanel\.cs$|^tests/Dustweave.Tests/Cases/(LiveBinding|Workflow|Preference)Cases\.cs$'
    if($outside.Count -and $Scope -notin @('Accounts','Navigation') -and !@($outside|Where-Object {$_ -notmatch $navigation -and $_ -notmatch $rewards}).Count){$selectedScope='Rewards';$outside=@()}
    # Trading-only changes reuse component/client gates; every modified runtime input is explicit.
    $trading='^(source-manifest\.json|CHANGELOG\.md|package\.ps1|scripts/validation-plan\.ps1)$|^docs/|^src/Core/DailyTrade(Optimizer|Forecast|Plan|DropForecast|Drops|Execution)\.cs$|^tests/Dustweave.Tests/Cases/TradeOptimizerCases\.cs$'
    if($Scope -in @('Auto','Trading') -and $changed.Count -and !@($changed|Where-Object {$_ -notmatch $trading}).Count){$selectedScope='Trading';$outside=@()}
    $full.changed=$changed
    $reason='Changes outside the account validation boundary: '+($outside -join ', ')
    if(!$outside.Count){
     $checks=$release.flavors[0].checks
     $evidenceValid=$true
     foreach($relative in @('evidence/result.json','suite/suite.json')){
      $evidence=Join-Path $checks $relative
      if(!(Test-Path -LiteralPath $evidence) -or (Get-Content -LiteralPath $evidence -Raw|ConvertFrom-Json).status -ne 'passed'){$evidenceValid=$false}
     }
     $reason='Full baseline evidence is missing or failed.'
     if($evidenceValid){
      $groups=@('ProductIdentity','Startup','LoginIdentity','Sandbox','Parallel','ParallelProcess','AccountOrder','AccountIdentity','AccountRestart','QueueSession')
      if($selectedScope -eq 'Trading'){$groups=@('ProductIdentity','TradeOptimizer','TradeData','TradeReplan','TradeQuote','TradeResume')}
      if($selectedScope -in @('Navigation','Rewards')){$groups+=@('HomeNavigation','HomeRecovery','CommandDriver','ManagedInputs','PassiveUi')}
      if($selectedScope -eq 'Rewards'){$groups+=@('LiveBindings','Workflow','Preference','RuleData','EvidenceReadiness','UserText')}
      return [ordered]@{schema=1;scope=$selectedScope;buildSettingsHash=$settings;baseline=$BaselineDirectory;baselineInputsHash=(Get-FileHash -LiteralPath $inputsPath).Hash;groups=$groups;changed=$changed;reason='Mapped host changes; unchanged tool validation reused from a completed full baseline.'}
     }
    }
   }
  }
 }
 if($Scope -in @('Accounts','Navigation','Rewards','Trading')){throw "Account validation cannot reuse this baseline: $reason"}
 $full.reason=$reason
 return $full
}
