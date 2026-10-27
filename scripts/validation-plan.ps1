# Reuse a full validation only when every changed input is inside an explicit boundary.
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
     '^(AGENTS\.md|CHANGELOG\.md|Directory\.Build\.props|source-manifest\.json|test\.ps1|package\.ps1)$',
     '^docs/', '^scripts/validation-plan\.ps1$', '^src/Accounts/[^/]+\.cs$',
     '^src/Core/(AccountSessions|DailyCoordinator|DailyLogin[^/]*|DailySandbox[^/]*|DailyParallel[^/]*|DailyAccountIdentity|DailyAccountOrder)\.cs$',
     '^src/Desktop/(DemoEnvironment|MainWindow\.(Accounts|Sandbox|Parallel)(\.Smoke)?|DailyParallelPanel|DailyParallelWorker)\.cs$',
     # Account task presentation is checked by each actual Portable/Lite smoke.
     # Runtime, XAML themes and shared contracts remain outside this boundary.
     '^src/Desktop/(DailyRunPanel|MainWindow\.Presentation)\.cs$',
     '^src/Desktop/Localization/(runtime|releases|strings)\.json$', '^tests/Dustweave.Tests/Program\.cs$',
     '^tests/Dustweave.Tests/Cases/(Startup|LoginIdentity|Sandbox|Parallel|ParallelProcess|AccountOrder|AccountIdentity|AccountRestart|QueueSession)Cases\.cs$'
    ) -join '|'
    $outside=@($changed|Where-Object {$_ -notmatch $allowed})
    $selectedScope='Accounts'
    $navigation='^src/Connection/(Bridge|LiveContracts|SelfTest)\.cs$|^src/Core/(DailyHomeNavigation|DailyTravel)\.cs$|^tests/Dustweave.Tests/Cases/(HomeNavigation|TravelTransition)Cases\.cs$'
    if($outside.Count -and $Scope -ne 'Accounts' -and !@($outside|Where-Object {$_ -notmatch $navigation}).Count){$selectedScope='Navigation';$outside=@()}
    $rewards='^assets/specs/(policy|daily)-evidence-spec\.json$|^src/Connection/(EvidenceBindings|TapManifest|RewardNative)\.cs$|^src/Connection/live-binding-contract\.json$|^src/Shared/DailyBridgeVersion\.cs$|^src/Core/(DailyMissions|DailyPreferences|DailyEventRewards|DailyEventNative|DailyEventSweep|DailyTradeData|DailyTradeCatalog|DailyHunting)\.cs$|^src/Desktop/DailyPreferencesPanel\.cs$|^src/Desktop/Localization/notices\.json$|^tests/Dustweave.Tests/Cases/(LiveBinding|Workflow|Preference|NativeDailyBattle|EventTradeBoundary|BattleArrivalRecovery|TradeData)Cases\.cs$'
    if($outside.Count -and $Scope -notin @('Accounts','Navigation') -and !@($outside|Where-Object {$_ -notmatch $navigation -and $_ -notmatch $rewards}).Count){$selectedScope='Rewards';$outside=@()}
    # Trading-only changes reuse component/client gates; every modified runtime input is explicit.
    $trading='^(Directory\.Build\.props|source-manifest\.json|CHANGELOG\.md|package\.ps1|scripts/validation-plan\.ps1)$|^docs/|^src/Core/DailyTrade(Optimizer|Forecast|Plan|DropForecast|Drops|Execution)\.cs$|^src/Desktop/Localization/(releases|strings)\.json$|^tests/Dustweave.Tests/Cases/Trade(Optimizer|Replan)Cases\.cs$'
    if($Scope -in @('Auto','Trading') -and $changed.Count -and !@($changed|Where-Object {$_ -notmatch $trading}).Count){$selectedScope='Trading';$outside=@()}
    # Weekly orchestration does not change the native components or other tools.
    $weekly='^(Directory\.Build\.props|source-manifest\.json|package\.ps1|scripts/validation-plan\.ps1|CHANGELOG\.md)$|^docs/|^src/Core/(DailyWeeklyRoute|DailyPickupRecovery|DailyManagedBusiness|DailyQueueEngine|DailyBusinessJournal|DailyCollectionNavigator|DailyCollectionProgress|DailyFieldRoute|DailyFieldTalents|DailyQueueSession)\.cs$|^src/Core/Localization/Messages\.zh-CN\.txt$|^src/Desktop/(DailyDialogs|DailyRunPanel|MainWindow\.Presentation|MainWindow\.xaml)\.cs$|^src/Desktop/Timeline\.xaml$|^src/Desktop/Localization/(releases|notices|runtime|strings)\.json$|^tests/Dustweave.Tests/(Program\.cs|Cases/(WeeklyIsolation|PickupRecovery|PickupQueueRecovery|QueueEngine|FieldTalent|QueueSession)Cases\.cs)$'
    if($Scope -in @('Auto','Weekly') -and $changed.Count -and !@($changed|Where-Object {$_ -notmatch $weekly}).Count){$selectedScope='Weekly';$outside=@()}
    # Update-only finalization reuses unchanged game components, not updater acceptance.
    # Actual package UI/tool gates still run; signed publication also requires real upgrade/recovery evidence.
    $updates='^(source-manifest\.json|package\.ps1|scripts/(validation-plan|publish-layout|test-update-packages|test-layout-update|build-update-acceptance|new-update-deltas|new-update-feed|prepare-update-site|publish-update-signed)\.ps1)$|^docs/|^src/Desktop/MainWindow\.Updates\.cs$|^src/Core/DailySuiteDiagnostics\.cs$|^src/Launcher/Program\.cs$|^tools/OtaChannel/(Rules|Program|SelfTest)\.cs$|^\.github/workflows/updates\.yml$'
    $updates+='|^src/Bootstrap/((Program|Recovery|RepairBootstrap)\.cs|Dustweave\.Bootstrap\.csproj)$|^scripts/test-(bridge-update|native-repair)\.ps1$'
    # Layout-3 differential finalization changes only the distribution/update boundary.
    $updates+='|^README(?:\.en)?\.md$|^src/Core/(DailyUpdates|DailyUpdateTransport|DailyUpdateEngine|DailyFullPackageRepair)\.cs$|^src/Desktop/MainWindow\.Repair\.cs$|^src/Desktop/Localization/(releases|strings)\.json$|^scripts/test-bundled-update\.ps1$|^tests/Dustweave.Tests/Cases/UpdateLayoutCases\.cs$|^tools/OtaChannel/(Mirror|MirrorNginx)\.cs$|^tools/OtaMirror/Program\.cs$'
    # The retired-lane notice changes startup gating only. Both package editions
    # render the notice; actual upgrade and isolated process gates remain required.
    $updates+='|^Directory\.Build\.props$|^src/Core/DailyLegacyMigration\.cs$|^src/Desktop/(Program|App\.xaml|LegacyMigrationStartup|LegacyMigrationWindow|MainWindow\.StartupFeatures)\.cs$|^tests/Dustweave.Tests/(Program\.cs|Cases/LegacyMigrationCases\.cs)$'
    if($Scope -in @('Auto','Updates') -and $changed.Count -and !@($changed|Where-Object {$_ -notmatch $updates}).Count){$selectedScope='Updates';$outside=@()}
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
      $groups=@('ProductIdentity','Startup','LoginIdentity','Sandbox','Parallel','ParallelProcess','AccountOrder','AccountIdentity','AccountRestart','QueueSession','QueuePeriod')
      if($selectedScope -eq 'Trading'){$groups=@('ProductIdentity','TradeOptimizer','TradeData','TradeReplan','TradeQuote','TradeResume','TalentPotion','BargainTraining','UserText')}
      if($selectedScope -eq 'Updates'){$groups=@('ProductIdentity','LegacyMigration','Startup','UpdateLayout','UpdateDelta','UpdateSafety','UpdateRepair','UpdateInventory','CacheStorage','SchedulingUpdates','UnifiedSuite','SuiteOwnerProbe','UserText')}
      if($selectedScope -eq 'Weekly'){$groups=@('ProductIdentity','WeeklyIsolation','WeeklyGoal','CollectionReadiness','FieldTalent','WeeklyNpcBoard','WeeklyNpcQuery','WeeklyNpcInteraction','CommandDriver','ManagedInputs','QueueSession','QueueEngine','QueuePeriod','QueueExecutionThread','PickupRecovery','BusinessJournal','BusinessScope','MirrorStage','FreeDrawStage','DispatchRecovery','ManagementStage','TradeResume','TravelTransition','UserText')}
      if($selectedScope -in @('Navigation','Rewards')){$groups+=@('HomeNavigation','HomeRecovery','TravelTransition','WeeklyGoal','CollectionReadiness','CafeteriaRecovery','CommandDriver','ManagedInputs','PassiveUi','UserText')}
      if($selectedScope -eq 'Rewards'){
       $groups+=@('LiveBindings','Workflow','Preference','RuleData','EvidenceReadiness','UserText','NativeDailyBattle','EventTradeBoundary','BattleArrivalRecovery','BusinessJournal')
       if($changed -match '^src/Core/DailyTrade(Data|Catalog)\.cs$|^tests/Dustweave.Tests/Cases/TradeDataCases\.cs$'){
        $groups+=@('TradeData','TradeOptimizer','TradeReplan','TradeQuote','TradeResume')
       }
      }
      return [ordered]@{schema=1;scope=$selectedScope;buildSettingsHash=$settings;baseline=$BaselineDirectory;baselineInputsHash=(Get-FileHash -LiteralPath $inputsPath).Hash;groups=$groups;changed=$changed;reason='Mapped host changes; unchanged tool validation reused from a completed full baseline.'}
     }
    }
   }
  }
 }
 if($Scope -in @('Accounts','Navigation','Rewards','Trading','Weekly','Updates')){throw "Scoped validation cannot reuse this baseline: $reason"}
 $full.reason=$reason
 return $full
}
