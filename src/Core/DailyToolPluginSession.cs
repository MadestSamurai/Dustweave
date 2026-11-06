using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
namespace Dustweave;

/// <summary>A private worker sees only a per-launch exchange directory. It is
/// never given the host data root, login store or arbitrary command access.</summary>
public sealed class DailyToolPluginSession : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Process process;
    private readonly string exchange;
    private readonly Func<string,JsonObject?,CancellationToken, Task<JsonObject>> operation;
    private readonly Task loop;
    public bool Running => !process.HasExited;
    public void Show()
    {
        process.Refresh();
        if(process.HasExited)return;
        var handle=process.MainWindowHandle;
        if(handle!=0){ShowWindow(handle,9);SetForegroundWindow(handle);}
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window,int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    private DailyToolPluginSession(Process process, string exchange, Func<string,JsonObject?,CancellationToken, Task<JsonObject>> operation)
    { this.process=process; this.exchange=exchange; this.operation=operation; loop=ListenAsync(); }
    public static async Task<DailyToolPluginSession> StartAsync(string root, DailyPluginInfo info, Func<string,JsonObject?,CancellationToken, Task<JsonObject>> operation, CancellationToken token=default,string language="zh-CN",bool dark=false,string? appearance=null,string? hostExecutable=null)
    {
        root=Path.GetFullPath(root);
        var verified=await Task.Run(()=>DailyPlugin.Inspect(info.Root),token);
        if(!verified.Available||!verified.IsTool||verified.Fingerprint!=info.Fingerprint)throw new IOException("plugins.invalid_hash");
        string exchange=Path.Combine(root,"tool-plugins","sessions",Guid.NewGuid().ToString("N"));
        DailyPluginStore.NoLinks(exchange);Directory.CreateDirectory(exchange);
        var start=new ProcessStartInfo(info.ToolApiVersion == 2 ? hostExecutable ?? Environment.ProcessPath! : Path.Combine(info.Root,info.ToolExecutable)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=info.Root};
        if(info.ToolApiVersion == 2) { start.ArgumentList.Add("--tool-plugin"); start.ArgumentList.Add(info.Root); start.ArgumentList.Add(info.Fingerprint); }
        appearance ??= dark ? "dark" : "light";
        DailyJson.Write(Path.Combine(exchange,"appearance.json"),new DailyToolPresentation(1,language,appearance));
        start.ArgumentList.Add("--dustweave-session");start.ArgumentList.Add(exchange);
        start.ArgumentList.Add("--parent-pid");start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("--language");start.ArgumentList.Add(language);
        start.ArgumentList.Add("--appearance");start.ArgumentList.Add(appearance);
        if(dark)start.ArgumentList.Add("--dark");
        // Only a private workspace is inherited; do not let a worker resolve
        // the host's real account data via integration environment variables.
        start.Environment.Remove("BD2_DAILY_DATA_ROOT");start.Environment.Remove("DUSTWEAVE_PLUGIN");
        start.Environment.Remove("DUSTWEAVE_PLUGINS");
        var child=Process.Start(start)??throw new IOException("plugins.tool_start_failed");
        var session=new DailyToolPluginSession(child,exchange,operation);
        try
        {
            for(int i=0;i<300;i++)
            {
                token.ThrowIfCancellationRequested();
                var ready=DailyJson.TryRead<JsonObject>(Path.Combine(exchange,"ready.json"));
                if(ready?["ready"] is JsonValue value && value.TryGetValue<bool>(out var isReady) && isReady)
                {
                    if(ready["processId"]?.GetValue<int>()!=child.Id||ready["toolApiVersion"]?.GetValue<int>()!=info.ToolApiVersion)throw new IOException("plugins.tool_start_failed");
                    child.Refresh();
                    if(child.MainWindowHandle!=0)return session;
                }
                if(child.HasExited)throw new IOException("plugins.tool_start_failed");
                await Task.Delay(200,token);
            }
            throw new IOException("plugins.tool_start_failed");
        }
        catch{session.Dispose();throw;}
    }
    public void UpdatePresentation(string language,string appearance)
    {
        if(stop.IsCancellationRequested)return;
        // Presentation failure must never stop inventory access or computation.
        try { DailyJson.Write(Path.Combine(exchange,"appearance.json"),new DailyToolPresentation(1,language,appearance)); }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) { }
    }
    private async Task ListenAsync()
    {
        string last="";
        try
        {
            while(!stop.IsCancellationRequested&&!process.HasExited)
            {
                string path=Path.Combine(exchange,"request.json");
                if(File.Exists(path)&&new FileInfo(path).Length<=256*1024)
                {
                    var request=DailyJson.TryRead<JsonObject>(path);string id=request?["id"] is JsonValue idValue && idValue.TryGetValue<string>(out var parsedId) ? parsedId : "";
                    if(Guid.TryParseExact(id,"N",out _)&&id!=last)
                    {
                        last=id;JsonObject response;
                        try
                        {
                            string name=request?["operation"]?.GetValue<string>()??"";
                            if(name.Length is <1 or >64||name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'))throw new IOException("plugins.tool_request_invalid");
                            using var bounded=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);bounded.CancelAfter(TimeSpan.FromSeconds(180));
                            response=new JsonObject{["id"]=id,["result"]=await operation(name,request?["payload"] as JsonObject,bounded.Token)};
                        }
                        catch(Exception error){response=new JsonObject{["id"]=id,["error"]=error.Message};}
                        if(!stop.IsCancellationRequested)DailyJson.Write(Path.Combine(exchange,"response.json"),response);
                    }
                }
                await Task.Delay(150,stop.Token);
            }
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }
    public void Dispose()
    {
        if(stop.IsCancellationRequested)return;
        stop.Cancel();
        // This process was created by this session; never locate or stop other apps.
        try { if(!process.HasExited)process.Kill(entireProcessTree:true); } catch(InvalidOperationException) { }
        // Keep small startup reports; remove only this session's inventory exchange.
        foreach(string name in new[]{"request.json","response.json"})try{File.Delete(Path.Combine(exchange,name));}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
