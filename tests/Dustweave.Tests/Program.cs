using Dustweave;

// Only the existing synthetic cases are linked. Captured account fixtures stay in the parent repo.
if (args.Length == 2 && args[0] == "--check-plugin")
{
    if (Environment.GetEnvironmentVariable("DUSTWEAVE_TEST_PROBE") == "hang") { await Task.Delay(60000); return; }
    if (Environment.GetEnvironmentVariable("DUSTWEAVE_TEST_PROBE") == "fail") { Environment.ExitCode = 1; return; }
    var info = DailyPlugin.Current;
    var extension = DailyExtensionLoader.Load(info) ?? throw new Exception("No extension");
    DailyJson.Write(args[1], new { status = "passed", info, api = extension.ApiVersion, realGameTouched = false }); return;
}
if (args.Length == 2 && args[0] == "--control-lock-child")
{
    using var owner = new DailyControlOwner(args[1]);
    try { owner.Acquire(); Console.WriteLine("owned"); }
    catch (IOException) { Console.WriteLine("blocked"); }
    return;
}
if (args.Length == 1 && args[0] == "--watchdog-child") { await Task.Delay(60000); return; }
if (args.Length == 2 && args[0] == "--parallel-worker-fixture") { await ParallelProcessCases.Child(args[1]); return; }
if (args.Length == 4 && args[0] == "--plugin-package")
{
    string isolated = Path.GetFullPath(args[2]);
    if (Directory.Exists(isolated)) throw new IOException("Choose a new isolated output directory.");
    Environment.SetEnvironmentVariable("BD2_DAILY_DATA_ROOT", isolated);
    Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGIN", null);
    var store = new DailyPluginStore(isolated);
    using var package = store.Prepare(args[1]);
    var installed = store.Install(package);
    await store.ActivateAsync(installed.Fingerprint, (info, token) => DailyPluginProbe.RunAsync(args[3], isolated, info, token));
    var pinned = DailyPlugin.Current;
    if (pinned.Fingerprint != installed.Fingerprint) throw new Exception("Installed version not resolved");
    store.Disable();
    if (DailyPlugin.ResolveRoot() != "" || DailyPlugin.Current.Fingerprint != pinned.Fingerprint) throw new Exception("Selection was not isolated from pinned session");
    await store.ActivateAsync(installed.Fingerprint, (info, token) => DailyPluginProbe.RunAsync(args[3], isolated, info, token));
    DailyJson.Write(Path.Combine(isolated, "lifecycle.json"), new { status = "passed", installed.Version, installed.Fingerprint, imported = true, loadedByDesktopChild = true, disabledAndReactivated = true, runningSessionPinned = true, realGameTouched = false });
    return;
}
if (args.Length != 1) throw new ArgumentException("Supply an isolated output directory.");
var root = Path.GetFullPath(args[0]);
if (Directory.Exists(root)) throw new InvalidOperationException("Preserve previous test evidence; choose a new output directory.");
Directory.CreateDirectory(root);
var selection = (Environment.GetEnvironmentVariable("DUSTWEAVE_TEST_GROUPS") ?? "")
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var scope = selection.Count == 0 ? "full synthetic regressions" : "selected synthetic regressions";
var cases = new List<string>();
var groups = new List<object>();
async Task Group(string name, Func<Task> run)
{
    available.Add(name);
    if (selection.Count > 0 && !selection.Contains(name)) return;
    var before = cases.Count;
    var watch = System.Diagnostics.Stopwatch.StartNew();
    try { await run(); }
    catch (Exception error)
    {
        groups.Add(new { name, checks = cases.Count - before, milliseconds = watch.ElapsedMilliseconds, failed = true });
        DailyJson.Write(Path.Combine(root, "results.json"), new
        {
            status = "failed", scope, selectedGroups = selection, count = cases.Count,
            groups, cases, failedGroup = name, error = error.ToString(), sourceRoot = TestPaths.SourceRoot,
            capturedFixturesIncluded = false, privatePluginIncluded = false, realGameTouched = false
        });
        throw;
    }
    groups.Add(new { name, checks = cases.Count - before, milliseconds = watch.ElapsedMilliseconds });
    Console.WriteLine($"PASS {name}: {cases.Count - before}");
}
Task Sync(Action run) { run(); return Task.CompletedTask; }
await Group("ProductIdentity", () => Sync(() => ProductIdentityCases.Run(cases)));
await Group("ClientUpdate", () => Sync(() => ClientUpdateCases.Run(root,cases)));
await Group("LiveBindings", () => Sync(() => LiveBindingCases.Run(root,cases)));
await Group("Parallel", () => ParallelCases.Run(root, cases));
await Group("ParallelProcess", () => ParallelProcessCases.Run(root, cases));
await Group("Sandbox", () => SandboxCases.Run(root, cases));
await Group("AccountOrder", () => AccountOrderCases.Run(root, cases));
await Group("Guild", () => GuildCases.Run(root, cases));
await Group("Startup", () => StartupCases.Run(root, cases));
await Group("LoginIdentity", () => LoginIdentityCases.Run(root, cases));
await Group("QueueSession", () => QueueSessionCases.Run(root, cases));
await Group("QueueExecutionThread", () => QueueExecutionThreadCases.Run(root, cases));
await Group("RuleData", () => RuleDataCases.Run(root, cases));
await Group("TravelTransition", () => TravelTransitionCases.Run(root, cases));
await Group("CafeteriaRecovery", () => CafeteriaRecoveryCases.Run(root, cases));
await Group("QueuePeriod", () => QueuePeriodCases.Run(root, cases));
await Group("AccountRestart", () => AccountRestartCases.Run(root, cases));
await Group("QueueEngine", () => QueueEngineCases.Run(root, cases));
await Group("StageProtocol", () => StageProtocolCases.Run(root, cases));
await Group("CommandDriver", () => CommandDriverCases.Run(root, cases));
await Group("EvidenceReadiness", () => EvidenceReadinessCases.Run(root, cases));
await Group("FieldTalent", () => FieldTalentCases.Run(root, cases));
await Group("WeeklyNpcQuery", () => WeeklyNpcQueryCases.Run(root, cases));
await Group("WeeklyNpcBoard", () => WeeklyNpcBoardCases.Run(root, cases));
await Group("CollectionReadiness", () => CollectionReadinessCases.Run(root, cases));
await Group("TradeData", () => TradeDataCases.Run(root, cases));
await Group("TradeResume", () => TradeResumeCases.Run(root, cases));
await Group("ManagedInputs", () => ManagedInputsCases.Run(root, cases));
await Group("PassiveUi", () => PassiveUiCases.Run(root, cases));
await Group("BusinessScope", () => BusinessScopeCases.Run(root, cases));
await Group("BusinessJournal", () => BusinessJournalCases.Run(root, cases));
await Group("FreeDrawStage", () => FreeDrawStageCases.Run(root, cases));
await Group("ManagedBootstrap", () => ManagedBootstrapCases.Run(root, cases));
await Group("ManagementStage", () => ManagementStageCases.Run(root, cases));
await Group("FreeDrawData", () => FreeDrawDataCases.Run(root, cases));
await Group("EmptyScene", () => EmptySceneCases.Run(root, cases));
await Group("SquareApproach", () => SquareApproachCases.Run(root, cases));
await Group("HomeNavigation", () => HomeNavigationCases.Run(root, cases));
await Group("HomeRecovery", () => HomeRecoveryCases.Run(root, cases));
await Group("MirrorStage", () => MirrorStageCases.Run(root, cases));
await Group("DispatchRecovery", () => DispatchRecoveryCases.Run(root, cases));
await Group("TradeReplan", () => TradeReplanCases.Run(root, cases));
await Group("TradeQuote", () => TradeQuoteCases.Run(root, cases));
await Group("SchedulingUpdates", () => SchedulingUpdatesCases.Run(root, cases));
await Group("UpdateSafety", () => UpdateSafetyCases.Run(root, cases));
await Group("UpdateDelta", () => UpdateDeltaCases.Run(root, cases));
await Group("Storage", () => StorageCases.Run(root, cases));
await Group("AccountIdentity", () => Sync(() => AccountIdentityCases.Run(root, cases)));
await Group("GameInstallation", () => Sync(() => GameInstallationCases.Run(root, cases)));
await Group("FirstRun", () => FirstRunCases.Run(root, cases));
await Group("Preference", () => Sync(() => PreferenceCases.Run(root, cases)));
await Group("PackagedUtility", () => Sync(() => PackagedUtilityCases.Run(root, cases)));
await Group("Plugin", () => Sync(() => PluginCases.Run(root, cases)));
await Group("PluginStore", () => PluginStoreCases.Run(root, cases));
await Group("ToolMenu", () => Sync(() => ToolMenuCases.Run(root, cases)));
await Group("ActivityLease", () => Sync(() => ActivityLeaseCases.Run(root, cases)));
await Group("UserText", () => Sync(() => UserTextCases.Run(cases)));
await Group("NativeCancellation", () => Sync(() => NativeCancellationCases.Run(cases)));
await Group("WeeklyGoal", () => Sync(() => WeeklyGoalCases.RunSynthetic(cases)));
await Group("Mansion", () => Sync(() => MansionCases.Run(cases)));
await Group("UnifiedSuite", () => Sync(() => UnifiedSuiteCases.Run(cases)));
await Group("Workflow", () => Sync(() => WorkflowCases.Run(cases)));
await Group("EventTradeBoundary", () => Sync(() => EventTradeBoundaryCases.Run(cases)));
await Group("ConnectionAccess", () => ConnectionAccessCases.Run(cases));
await Group("HelperLifetime", () => HelperLifetimeCases.Run(cases));
await Group("PackagedTravel", () => PackagedUtilityCases.Travel(root, cases));
var unknown = selection.Except(available, StringComparer.OrdinalIgnoreCase).ToArray();
if (unknown.Length > 0) throw new ArgumentException("Unknown test groups: " + string.Join(", ", unknown));
if (groups.Count == 0) throw new InvalidOperationException("No test groups ran.");
DailyJson.Write(Path.Combine(root, "results.json"), new
{
    status = "passed", scope, selectedGroups = selection, count = cases.Count,
    groups, cases, sourceRoot = TestPaths.SourceRoot, capturedFixturesIncluded = false,
    privatePluginIncluded = false, realGameTouched = false
});
Console.WriteLine($"PASS {cases.Count} synthetic checks. No captured account data or game connection.");
