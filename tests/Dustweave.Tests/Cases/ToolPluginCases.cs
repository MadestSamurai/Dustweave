using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Dustweave;
using Dustweave.Compatibility;

internal static class ToolPluginCases
{
    public static async Task Run(string output,List<string> cases)
    {
        string root=Path.Combine(output,"tool-plugins");Directory.CreateDirectory(root);
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("tool-plugin: "+label);}
        string package=Path.Combine(root,"source");Directory.CreateDirectory(Path.Combine(package,"tool"));
        File.WriteAllText(Path.Combine(package,"tool/Tool.exe"),"synthetic executable; never run");
        var manifest=new JsonObject{["id"]="example.tool",["version"]="1.0.0",["kind"]="tool",["runtime"]="standalone-windows-x64",["apiVersion"]=4,["bridgeExtensionApi"]=1,["toolApiVersion"]=1,["toolExecutable"]="tool/Tool.exe",["capabilities"]=new JsonArray("inventory_read"),["hookSources"]=new JsonArray(),["files"]=new JsonArray(new JsonObject{["path"]="tool/Tool.exe",["sha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(package,"tool/Tool.exe"))))})};
        void Save()=>File.WriteAllText(Path.Combine(package,"plugin.json"),manifest.ToJsonString());Save();
        Check(DailyPlugin.Inspect(package).IsTool,"standalone tool accepted without a daily assembly");
        Check(DailyExtensionLoader.Load(DailyPlugin.Inspect(package))==null,"tool is never loaded as a daily extension");
        manifest["capabilities"]=new JsonArray("inventory_read","arbitrary_command");Save();
        Check(!DailyPlugin.Inspect(package).Available,"unsupported tool command capability rejected");
        manifest["capabilities"]=new JsonArray("inventory_read");manifest["toolApiVersion"]=99;Save();
        Check(!DailyPlugin.Inspect(package).Available,"unknown protocol rejected before launch");
        manifest["toolApiVersion"]=1;Save();
        string zip=Path.Combine(root,"tool.zip");ZipFile.CreateFromDirectory(package,zip);
        string data=Path.Combine(root,"data");var daily=new DailyPluginStore(data);daily.Disable();
        var before=daily.Read();var registry=new DailyToolPlugins(data);var store=registry.Store("example.tool");
        using(var prepared=daily.Prepare(zip))
        {
            var installed=store.Install(prepared);
            await store.ActivateAsync(installed.Fingerprint,(_,_)=>Task.CompletedTask);
            Check(registry.Enabled().Single().Id=="example.tool","tool enabled in independent registry");
            Check(daily.Read().Active==before.Active&&daily.Read().OverrideLocal==before.OverrideLocal,"daily selection unchanged");
            store.Disable();Check(registry.Enabled().Count==0,"disable takes effect without restarting daily plugin");
            store.Remove(installed.Fingerprint);Check(registry.Installed().Count==0,"tool removal leaves daily store intact");
        }
        using var index=new MetadataIndex(typeof(ToolPluginCases).Assembly.Location);
        Check(InventoryHookContract.Source(index).Contains("Inventory cache unresolved"),"missing optional cache produces inventory error only");
        string DailyPackage(string id,string stage,string version="1.0.0")
        {
            string folder=Path.Combine(root,id+"-"+version);Directory.CreateDirectory(Path.Combine(folder,"managed"));
            File.Copy(typeof(SyntheticPlugin).Assembly.Location,Path.Combine(folder,"managed/Sample.dll"));
            var m=new JsonObject{["id"]=id,["version"]=version,["runtime"]="net8.0-windows-x64",["apiVersion"]=4,["bridgeExtensionApi"]=1,["entryAssembly"]="managed/Sample.dll",["entryType"]="SyntheticPlugin",["hookSources"]=new JsonArray(),["capabilities"]=new JsonArray(stage),["files"]=new JsonArray(new JsonObject{["path"]="managed/Sample.dll",["sha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,"managed/Sample.dll"))))})};
            File.WriteAllText(Path.Combine(folder,"plugin.json"),m.ToJsonString());string archive=folder+".zip";ZipFile.CreateFromDirectory(folder,archive);return archive;
        }
        async Task<DailyInstalledPlugin> Enable(string id,string stage)
        {
            using var p=daily.Prepare(DailyPackage(id,stage));var record=daily.Install(p);
            await daily.ActivateAsync(record.Fingerprint,(info,token)=>DailyPluginProbe.RunAsync(Environment.ProcessPath!,Path.Combine(root,"combined-probe"),info,token));return record;
        }
        var a=await Enable("example.daily-a","stage_a");var b=await Enable("example.daily-b","stage_b");
        Check(daily.Read().ActiveFingerprints.Length==2,"two independent daily plugins remain enabled");
        DailyPluginInfo SelectedFixture()
        {
            string? one=Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGIN"),many=Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGINS");
            try {Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGIN",null);Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGINS",null);return DailyPlugin.Selected(data);}
            finally {Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGIN",one);Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGINS",many);}
        }
        var combined=SelectedFixture();
        Check(combined.Supports("stage_a")&&combined.Supports("stage_b")&&combined.Members.Length==2,"combined capabilities retain each plugin");
        Check(DailyExtensionLoader.Load(combined)!=null,"multiple managed extensions load in separate contexts");
        var start=new System.Diagnostics.ProcessStartInfo();DailyPlugin.PinEnvironment(start,combined);
        Check(start.Environment.ContainsKey("DUSTWEAVE_PLUGINS"),"helpers inherit complete pinned selection");
        bool conflict=false;try{await Enable("example.daily-c","stage_a");}catch(InvalidDataException error)when(error.Message=="plugins.task_conflict"){conflict=true;}
        Check(conflict&&daily.Read().ActiveFingerprints.Length==2,"same-task conflict is explicit and leaves both selections intact");
        using(var update=daily.Prepare(DailyPackage("example.daily-b","stage_b","1.1.0")))
        {
            var next=daily.Install(update);await daily.ActivateAsync(next.Fingerprint,(_,_)=>Task.CompletedTask);
            Check(daily.Read().ActiveFingerprints.Contains(a.Fingerprint)&&daily.Read().ActiveFingerprints.Contains(next.Fingerprint)&&!daily.Read().ActiveFingerprints.Contains(b.Fingerprint),"upgrade replaces only its own previous version");b=next;
        }
        daily.Disable(a.Fingerprint);Check(daily.Read().ActiveFingerprints.SequenceEqual(new[]{b.Fingerprint}),"disable one daily plugin preserves the other");
        daily.Remove(a.Fingerprint);Check(SelectedFixture().Supports("stage_b"),"removing an inactive plugin preserves execution capability");
        await InventoryExchange(Path.Combine(root,"inventory"),Check);
        await OperationExchange(Path.Combine(root,"operations"),Check);
        ManagedManifest(root,Check);
    }
    private static void ManagedManifest(string root,Action<bool,string> check)
    {
        string package=Path.Combine(root,"managed-tool");Directory.CreateDirectory(Path.Combine(package,"tool"));
        File.WriteAllText(Path.Combine(package,"tool/Tool.dll"),"synthetic assembly, never loaded");
        var manifest=new JsonObject{["id"]="example.managed",["version"]="1.0.0",["kind"]="tool",["runtime"]="host-net8.0-windows-x64",["apiVersion"]=4,["bridgeExtensionApi"]=1,["toolApiVersion"]=2,["uiApiVersion"]=1,["minHostVersion"]="1.6.2-local.3",["toolAssembly"]="tool/Tool.dll",["toolType"]="Example.Entry",["capabilities"]=new JsonArray("inventory_read"),["hookSources"]=new JsonArray(),["files"]=new JsonArray(new JsonObject{["path"]="tool/Tool.dll",["sha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(package,"tool/Tool.dll"))))})};
        void Save()=>File.WriteAllText(Path.Combine(package,"plugin.json"),manifest.ToJsonString());Save();
        var info=DailyPlugin.Inspect(package,"1.6.2-local.3");
        check(info.Available&&info.IsTool&&info.ToolApiVersion==2&&info.ToolExecutable=="","managed plugin accepted without executable/runtime");
        check(DailyExtensionLoader.Load(info)==null,"managed tool never loads in main daily extension process");
        check(DailyPlugin.Inspect(package,"1.6.1").State=="incompatible_host","old host rejected with update guidance");
        manifest["uiApiVersion"]=2;Save();check(DailyPlugin.Inspect(package,"1.6.2").State=="incompatible","unknown UI contract rejected before executing code");
        manifest["uiApiVersion"]=1;manifest["toolAssembly"]="tool/Other.dll";Save();check(DailyPlugin.Inspect(package,"1.6.2").State=="invalid_entrypoint","undeclared managed entry rejected");
        manifest["toolAssembly"]="../Tool.dll";Save();check(!DailyPlugin.Inspect(package,"1.6.2").Available,"managed entry cannot escape package");
        manifest["toolAssembly"]="tool/Tool.dll";Save();File.AppendAllText(Path.Combine(package,"tool/Tool.dll"),"tampered");
        check(DailyPlugin.Inspect(package,"1.6.2").State=="invalid_hash","changed managed assembly rejected before launch");
        check(new DailyToolPresentation(1,"en-US","system").Valid&&!new DailyToolPresentation(2,"en-US","system").Valid&&!new DailyToolPresentation(1,"xx","dark").Valid&&!new DailyToolPresentation(1,"zh-CN","unknown").Valid,"presentation schema and locale bounded without game state");
    }
    private sealed class InventoryHost : IGameHost
    {
        public GameInstance Game=new(1234,9876,"synthetic");
        public DailySnapshot Snapshot=new(){State="identified",AccountKey="account",PlayerKey="player",InstanceId="session"};
        public GameInstance? Find()=>Game;
        public DailySnapshot? ReadSnapshot()=>Snapshot;
        public DailyRuntimeStatus? ReadStatus()=>null;
        public Task ConnectAsync(GameInstance game,Action<string> progress,CancellationToken token)=>throw new Exception("Offline test must not connect");
        public Task CloseAsync(GameInstance game,CancellationToken token)=>throw new Exception("Offline test must not close game");
    }
    private static async Task InventoryExchange(string root,Action<bool,string> check)
    {
        Directory.CreateDirectory(root);var host=new InventoryHost();var expected=host.Snapshot;
        string requestPath=Path.Combine(root,"tool-inventory-request.json"),responsePath=Path.Combine(root,"tool-inventory-response.json");
        void Reply()
        {
            var request=DailyJson.TryRead<ToolInventoryRequest>(requestPath)!;
            DailyJson.Write(responsePath,new ToolInventoryResponse{Id=request.Id,ProcessId=1234,ProcessStartTicks=9876,InstanceId="session",AccountKey="account",PlayerKey="player",PlayerName="Synthetic",Json="{\"components\":[]}"});
        }
        var read=DailyToolInventory.ReadAsync(root,host,host.Game,expected,CancellationToken.None);Reply();
        var result=await read;check(result["accountName"]!.GetValue<string>()=="Synthetic","matching inventory response succeeds");
        check(DailyJson.TryRead<ToolInventoryRequest>(requestPath)!.ExpiresUtcTicks==0&&DailyJson.TryRead<ToolInventoryResponse>(responsePath)!.Id.Length>0,"inventory exchange expires without attempting to overwrite runtime-owned response");
        read=DailyToolInventory.ReadAsync(root,host,host.Game,expected,CancellationToken.None);Reply();host.Snapshot=new(){State="identified",AccountKey="other",PlayerKey="player",InstanceId="session"};
        bool changed=false;try{await read;}catch(IOException error)when(error.Message=="plugins.tool_identity"){changed=true;}
        check(changed,"changed account cannot consume previous inventory");
        host.Snapshot=expected;
        using var cancel=new CancellationTokenSource();read=DailyToolInventory.ReadAsync(root,host,host.Game,expected,cancel.Token);cancel.Cancel();
        try{await read;}catch(OperationCanceledException){}
        read=DailyToolInventory.ReadAsync(root,host,host.Game,expected,CancellationToken.None);Reply();
        check((await read)["inventory"]!=null,"cancelled inventory read can retry successfully");
    }
    private static async Task OperationExchange(string root,Action<bool,string> check)
    {
        string live=Path.Combine(root,"live");Directory.CreateDirectory(live);var host=new InventoryHost();var expected=host.Snapshot;
        void Frame()=>DailyJson.Write(Path.Combine(live,"snapshot.json"),new{Instance="session",AccountKey="account",PlayerKey="player",ProcessId=1234,ProcessStartTicks=9876,AtUtcTicks=DateTime.UtcNow.Ticks});
        string Reply()
        {
            var request=DailyJson.TryRead<JsonObject>(Path.Combine(live,"plugin-tool-request.json"))!;string id=request["Id"]!.GetValue<string>();
            DailyJson.Write(Path.Combine(live,"plugin-tool-response.json"),new{Id=id,Complete=true,Instance="session",Error="",Result="{\"verified\":true}"});return id;
        }
        Frame();var pending=DailyToolOperation.RunAsync(root,host,host.Game,expected,"example.tool","preset-save",null,CancellationToken.None);string id=Reply();
        host.Snapshot=new(){State="loading"};await Task.Delay(250);check(!pending.IsCompleted,"completed receipt waits through temporarily missing identity");
        host.Snapshot=expected;check((await pending)["verified"]!.GetValue<bool>(),"same identity recovers and consumes existing receipt");
        check(DailyJson.TryRead<JsonObject>(Path.Combine(live,"plugin-tool-request.json"))!["Id"]!.GetValue<string>()==id,"recovery does not resubmit a save");
        Frame();pending=DailyToolOperation.RunAsync(root,host,host.Game,expected,"example.tool","preset-read",null,CancellationToken.None);Reply();host.Snapshot=new(){AccountKey="other",PlayerKey="player"};
        bool changed=false;try{await pending;}catch(IOException e)when(e.Message=="plugins.tool_identity"){changed=true;}check(changed,"confirmed account change still rejects operation receipt");
    }
    public static async Task Package(string zip,string output,string managed,string? hostExecutable=null)
    {
        Directory.CreateDirectory(output);
        var daily=new DailyPluginStore(output);daily.Disable();
        using var prepared=daily.Prepare(zip);var store=new DailyToolPlugins(output).Store(prepared.Info.Id);
        var installed=store.Install(prepared);
        string game=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(managed)!)!,"BrownDust II.exe");
        await store.ActivateAsync(installed.Fingerprint,(info,token)=>DailyPluginProbe.RunAsync(hostExecutable??Environment.ProcessPath!,output,info,game,token));
        if(daily.Read().Active!=null)throw new Exception("Daily selection changed");
        using(var index=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll")))
        {
            string source=InventoryHookContract.Source(index);
            if(source.Contains("Inventory cache unresolved"))throw new Exception(source);
        }
        var runtime=DailyHookCompiler.Prepare(managed);
        using(var session=await DailyToolPluginSession.StartAsync(output,store.Inspect(installed),(_,_,_)=>throw new InvalidOperationException("Offline package check cannot access the game"),hostExecutable:hostExecutable))
        {
            if(!session.Running)throw new Exception("Packaged native window did not start");
        }
        DailyJson.Write(Path.Combine(output,"package-check.json"),new {status="passed",tool=installed.Id,hookBytes=runtime.Payload.Length,realGameTouched=false});
    }
}
