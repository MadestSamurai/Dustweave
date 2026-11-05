using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private DailyLogPolicy logPolicy = new();
    private CancellationTokenSource? logCleanupCancellation;
    private DateTime nextLogCleanup = DateTime.UtcNow.AddMinutes(1);
    private DateTime nextEvidenceCleanup = DateTime.UtcNow.AddSeconds(15);
    private DateTime nextUpdateCacheCleanup = DateTime.UtcNow.AddMinutes(1);
    private bool logCleanupRunning;
    private bool compressionPending;
    private DateTime lastRetentionUtc=DateTime.MinValue;
    private bool manualCacheChanging, automaticCacheChanging;
    private bool maintenanceYielding;
    private bool MaintenanceWindowOpen => ToolOpen || OwnedWindows.Count > 0;
    private bool cacheChanging => manualCacheChanging || automaticCacheChanging;
    private Task logCleanupCompletion = Task.CompletedTask;
    private void SetCacheChanging(bool value){manualCacheChanging=value;SetBusy(busy);}
    private void SetAutomaticCacheChanging(bool value){automaticCacheChanging=value;SetBusy(busy);}
    private async Task PauseAutomaticMaintenanceAsync(CancellationToken token=default)
    {
        // Reserve a quiet interval before awaiting: no timer may start a second
        // maintenance pass between cancellation and the foreground lease.
        var retry=DateTime.UtcNow.AddMinutes(5);
        if(nextLogCleanup<retry)nextLogCleanup=retry;
        if(nextUpdateCacheCleanup<retry)nextUpdateCacheCleanup=retry;
        nextEvidenceCleanup=DateTime.UtcNow.AddMinutes(1);
        token.ThrowIfCancellationRequested();
        if(!logCleanupRunning)return;
        maintenanceYielding=true;SetBusy(busy);
        L.Text(ProgressText,"logs.yielding");
        try
        {
            logCleanupCancellation?.Cancel();
            await logCleanupCompletion.WaitAsync(token);
        }
        finally {maintenanceYielding=false;SetBusy(busy);}
    }
    private async Task<bool> PrepareSettingsToolAsync()
    {
        if(Unavailable || finalClose)return false;
        await PauseAutomaticMaintenanceAsync();
        return !finalClose && !Unavailable;
    }
    private sealed record LogCleanupRun(DateTime At, int Files, long Bytes, int Skipped, string? Version = null, bool MoreWork = false, int CompressedFiles = 0, DateTime? RetentionAt = null);
    private string? LegacyEquipmentLogRoot => smoke == null
        ? Environment.GetEnvironmentVariable("BD2_EQUIPMENT_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2EquipmentAssistant")
        : null;
    private void InitializeLogCleanup()
    {
        logPolicy=DailyLogCleanup.Load(root);
        var last=DailyJson.TryRead<LogCleanupRun>(Path.Combine(root,"log-cleanup-last.json"));
        compressionPending=last?.Version==DailyProductVersion.Current && last.MoreWork;
        if(last?.Version==DailyProductVersion.Current)lastRetentionUtc=last.RetentionAt??last.At;
        if(!compressionPending && lastRetentionUtc>DateTime.UtcNow.AddHours(-12) && lastRetentionUtc<=DateTime.UtcNow)nextLogCleanup=lastRetentionUtc.AddHours(12);
        if(LegacyEquipmentLogRoot is { } legacy && DailyLogCleanup.HasLegacyEquipmentLogs(legacy))nextLogCleanup=DateTime.UtcNow.AddMinutes(1);
        Closed+=(_,_)=>logCleanupCancellation?.Cancel();
    }
    private void SaveLogCleanupResult(DailyLogCleanupResult result,bool retention)
    {
        var now=DateTime.UtcNow;
        if(retention)lastRetentionUtc=now;
        compressionPending=result.MoreWork;
        DailyJson.Write(Path.Combine(root,"log-cleanup-last.json"),new LogCleanupRun(now,result.Files,result.Bytes,result.Skipped,DailyProductVersion.Current,result.MoreWork,result.CompressedFiles,lastRetentionUtc));
        var retentionDue=lastRetentionUtc.AddHours(12);
        nextLogCleanup=result.MoreWork&&now.AddMinutes(5)<retentionDue?now.AddMinutes(5):retentionDue;
    }
    private async Task CheckLogCleanupAsync()
    {
        // Maintenance yields to an accepted restart, including while it owns
        // the storage/control leases. It must not cancel itself for those leases.
        if(UpdateRestarting || finalClose){logCleanupCancellation?.Cancel();return;}
        if(Unavailable || MaintenanceWindowOpen || dailyQueue.IsRunning || scheduleChecking || DailyDialogs.ModalDepth>0)
        { logCleanupCancellation?.Cancel();return; }
        if(logCleanupRunning)return;
        bool retentionDue=logPolicy.Automatic&&DateTime.UtcNow>=nextLogCleanup;
        bool updatesDue=DateTime.UtcNow>=nextUpdateCacheCleanup;
        if(!retentionDue && !updatesDue && (DateTime.UtcNow<nextEvidenceCleanup || !DailyLogEvidence.HasPending(root)))return;
        nextEvidenceCleanup=DateTime.UtcNow.AddMinutes(1);
        if(retentionDue)nextLogCleanup=DateTime.UtcNow.AddMinutes(5);
        if(updatesDue)nextUpdateCacheCleanup=DateTime.UtcNow.AddMinutes(5);
        await RunAutomaticMaintenanceAsync(async token => {
            await DailyLogEvidence.FlushAsync(root,token);
            if(!retentionDue && !updatesDue)return;
            bool continuing=compressionPending&&lastRetentionUtc>DateTime.UtcNow.AddHours(-12)&&lastRetentionUtc<=DateTime.UtcNow;
            if(retentionDue&&!continuing&&LegacyEquipmentLogRoot is { } legacy)await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(root,legacy,token);
            if(Unavailable || MaintenanceWindowOpen || dailyQueue.IsRunning || scheduleChecking || DailyDialogs.ModalDepth>0 || updateChecking || updateInstalling)return;
            // Open tool windows may start independently of the host. Defer
            // maintenance until they close, while retaining the real file locks.
            SetAutomaticCacheChanging(true);
            try {
                var maintenance=new DailyStorageMaintenance(root);
                if(updatesDue&&(!retentionDue||continuing))
                {
                    var updates=await maintenance.CleanUpdateCacheAsync(logPolicy,token);
                    nextUpdateCacheCleanup=DateTime.UtcNow.AddMinutes(updates.Skipped>0?5:30);
                }
                if(retentionDue)
                {
                    var result=continuing?await maintenance.ContinueCompressionAsync(token):await maintenance.CleanAsync(logPolicy,token);
                    if(!continuing)nextUpdateCacheCleanup=DateTime.UtcNow.AddMinutes(result.Skipped>0?5:30);
                    SaveLogCleanupResult(result,!continuing);
                }
            }
            finally {SetAutomaticCacheChanging(false);}
        });
    }
    private async Task RunAutomaticMaintenanceAsync(Func<CancellationToken,Task> maintain)
    {
        var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logCleanupRunning=true;logCleanupCompletion=completed.Task;
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(30));logCleanupCancellation=lifetime;
        try { await maintain(lifetime.Token); }
        catch(OperationCanceledException) { }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { /* Never stop automation for retention. Retry on the next idle interval. */ }
        finally {logCleanupCancellation=null;logCleanupRunning=false;completed.TrySetResult();}
    }
    private async void CleanupDiagnostics_Click(object sender,RoutedEventArgs e)
    {
        try { await PauseAutomaticMaintenanceAsync(); if(!finalClose)DailyDialogs.ShowModal(CreateLogCleanupWindow()); }
        catch(Exception error){ShowError(error);}
    }
}
