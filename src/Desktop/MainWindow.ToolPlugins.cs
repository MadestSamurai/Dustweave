extern alias liveUtility;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private DailyPluginInfo[] enabledToolPlugins = [];
    private readonly Dictionary<string, DailyToolPluginSession> toolPluginSessions = new();
    private readonly HashSet<string> startingToolPlugins = new();
    private DailyPluginStore StoreFor(DailyPluginInfo info) => info.IsTool ? new DailyToolPlugins(root).Store(info.Id) : PluginStore;
    private void InitializeToolPlugins()
    {
        L.Changed += ToolLanguageChanged;
        theme.PropertyChanged += ToolThemeChanged;
        Closed += (_,_) => { L.Changed -= ToolLanguageChanged; theme.PropertyChanged -= ToolThemeChanged; };
        pluginPanel.ToolActionRequested += async (id, fingerprint, operation) =>
        {
            if(operation == "open") { await OpenPluginToolAsync(id); return; }
            await ChangePluginAsync(async () =>
            {
                if(operation == "remove" && !PluginConfirm("plugins.remove", L.Get("plugins.remove_confirm"))) return;
                if(toolPluginSessions.Remove(id, out var session)) session.Dispose();
                var store = new DailyToolPlugins(root).Store(id);
                if(operation == "enable") await store.ActivateAsync(fingerprint, ProbePluginAsync);
                else if(operation == "disable") store.Disable();
                else if(operation == "remove") store.Remove(fingerprint);
            });
        };
    }
    private void ToolLanguageChanged(object? sender,EventArgs args) => RefreshToolPresentation();
    private void ToolThemeChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)
    { if(args.PropertyName == nameof(DailyTheme.IsDark)) RefreshToolPresentation(); }
    private void RefreshToolPresentation()
    {
        foreach(var session in toolPluginSessions.Values)session.UpdatePresentation(L.Code,theme.Preference.ToString().ToLowerInvariant());
    }
    private async Task OpenPluginToolAsync(string id)
    {
        if(smoke != null || Unavailable) return;
        var info = enabledToolPlugins.FirstOrDefault(p=>p.Id==id);
        if(info == null || !startingToolPlugins.Add(id)) return;
        try
        {
            if(toolPluginSessions.TryGetValue(id,out var old) && old.Running) { old.Show(); return; }
            if(old != null) { old.Dispose(); toolPluginSessions.Remove(id); }
            // Startup is offline. Inventory acquisition is a separate, short control lease.
            string pinned = "";
            var session = await DailyToolPluginSession.StartAsync(root,info,(operation,payload,token) => Dispatcher.InvokeAsync(async () =>
            {
                if(Unavailable) throw new IOException(L.Get("plugins.tool_busy"));
                toolChanging=true; SetBusy(busy);
                try
                {
                    await PauseAutomaticMaintenanceAsync();
                    using var lease=DailyToolControl.Acquire(root);
                    await host.PrepareAccessAsync(_=>{},token);
                    var game=host.Find() ?? throw new IOException(L.Get("plugins.tool_no_game"));
                    await host.ConnectAsync(game,_=>{},token);
                    await HostedToolPreparation.WaitForIdentityAsync(host,game,_=>{},token);
                    var snapshot=host.ReadSnapshot(game) ?? throw new IOException("plugins.tool_identity");
                    string identity=snapshot.AccountKey+"|"+snapshot.PlayerKey;
                    if(pinned.Length>0 && pinned!=identity) throw new IOException("plugins.tool_identity");
                    if(payload?["accountKey"] is JsonValue key&&key.GetValue<string>()!=DailyIdentity.Hash(identity))throw new IOException("plugins.tool_identity");
                    JsonObject result;
                    if(operation=="inventory")result=await DailyToolInventory.ReadAsync(root,host,game,snapshot,token);
                    else
                    {
                        if(info.Capabilities?.Contains("tool_operations")!=true)throw new IOException("plugins.tool_request_invalid");
                        if(!DailyPlugin.Current.HookMembers.Any(p=>p.Id==info.Id&&p.Fingerprint==info.Fingerprint))throw new IOException(L.Get("plugins.restart_confirm"));
                        await DailySuite.ActivateForSessionAsync(host,root,"daily",game,snapshot,null,_=>{},token);
                        await liveUtility::Dustweave.Connection.LiveEntry.RunAsync(["ready"],false,args=>liveUtility::Dustweave.Connection.LiveEntry.RunAsync(args,false),cancellation:token);
                        result=await DailyToolOperation.RunAsync(root,host,game,snapshot,info.Id,operation,payload,token);
                    }
                    pinned=identity; return result;
                }
                catch(Exception error) when(error is not OperationCanceledException)
                {
                    // Export the failure only, never the inventory payload or launch URL.
                    try { DailyJson.Write(Path.Combine(root,"extensions","last-error.json"),new {utc=DateTimeOffset.UtcNow,operation="tool-"+operation,plugin=info.Id,error=error.ToString()}); }
                    catch(Exception logging) when(logging is IOException or UnauthorizedAccessException) { }
                    throw new IOException(error.Message.StartsWith("plugins.",StringComparison.Ordinal) ? L.Get(error.Message) : error.Message,error);
                }
                finally { toolChanging=false; SetBusy(busy); RefreshTools(); }
            }).Task.Unwrap(),language:L.Code,dark:theme.IsDark,appearance:theme.Preference.ToString().ToLowerInvariant());
            if(finalClose || UpdateRestarting) { session.Dispose();return; }
            toolPluginSessions[id]=session;
            session.UpdatePresentation(L.Code,theme.Preference.ToString().ToLowerInvariant());
            session.Show();
        }
        catch(Exception error) { PluginError(error); toolMessage=error.Message; RefreshTools(); }
        finally { startingToolPlugins.Remove(id); }
    }
}
