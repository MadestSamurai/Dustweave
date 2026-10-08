using Dustweave.Compatibility;
using Mono.Cecil;

static class ClientUpdateCases
{
    public static void Run(string root,List<string> cases)
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add(name);}
        void Reject(Action action,string code,string name)
        {
            try{action();}catch(Exception error) when(error.Message==code){cases.Add(name);return;}
            throw new Exception(name);
        }
        string game=Path.Combine(root,"client-update"),managed=Path.Combine(game,"BrownDust II_Data","Managed");Directory.CreateDirectory(managed);
        using(var module=ModuleDefinition.CreateModule("Assembly-CSharp",ModuleKind.Dll))module.Write(Path.Combine(managed,"Assembly-CSharp.dll"));
        string sdk=Path.Combine(managed,"Neo.Unity.Neon.dll"),core=Path.Combine(managed,"mscorlib.dll");
        File.WriteAllText(sdk,"sdk-v1");File.WriteAllText(core,"core-v1");
        var first=ClientInputs.ReadManaged(managed);
        Check(first.Key==ClientInputs.ReadManaged(managed).Key,"unchanged compiler inputs reuse the same client key");
        File.WriteAllText(Path.Combine(managed,"notes.txt"),"diagnostic output");
        Check(first.Key==ClientInputs.ReadManaged(managed).Key,"unrelated diagnostic files do not invalidate compiler cache");
        DateTime timestamp=File.GetLastWriteTimeUtc(sdk);File.WriteAllText(sdk,"sdk-v2");File.SetLastWriteTimeUtc(sdk,timestamp);
        var second=ClientInputs.ReadManaged(managed);
        Check(first.MainMvid==second.MainMvid&&first.Key!=second.Key,"SDK-only update with same main MVID, size and timestamp invalidates cache");
        Reject(()=>ClientInputs.RequireStable(managed,first.Key),"client_update.changing","client changing during compilation is rejected before cache publication");
        File.WriteAllText(core,"core-v2");var third=ClientInputs.ReadManaged(managed);
        Check(second.Key!=third.Key,"framework dependency update invalidates cache");
        string added=Path.Combine(managed,"NewDependency.dll");File.WriteAllText(added,"added");var fourth=ClientInputs.ReadManaged(managed);
        Check(third.Key!=fourth.Key,"added dependency changes the compiler input set");
        File.Delete(added);Check(ClientInputs.ReadManaged(managed).Key==third.Key,"removed dependency restores only the matching input identity");
        ClientInputs.RequireRunning(third.Key,42,1,third.Key,42,1);cases.Add("same client and process remain reusable");
        Reject(()=>ClientInputs.RequireRunning(first.Key,42,1,third.Key,42,1),"client_update.running_changed","changed files cannot reconnect to the known old running process");
        ClientInputs.RequireRunning(first.Key,42,1,third.Key,42,2);cases.Add("a newly started game can prepare against new inputs");
        string exe=Path.Combine(game,"BrownDust II.exe"),unity=Path.Combine(game,"UnityPlayer.dll"),mono=Path.Combine(game,"MonoBleedingEdge","EmbedRuntime","mono-2.0-bdwgc.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(mono)!);File.WriteAllText(exe,"exe-v1");File.WriteAllText(unity,"unity-v1");File.WriteAllText(mono,"mono-v1");
        var original=ClientInputs.ReadGame(exe);ClientInputs.RequireSame(original,ClientInputs.ReadGame(exe));cases.Add("identical host and isolated client pass without requiring duplicate installs");
        File.WriteAllText(unity,"unity-v2");var updated=ClientInputs.ReadGame(exe);
        Check(original!=updated,"Unity-only client update changes isolated launch identity");
        Reject(()=>ClientInputs.RequireSame(original,updated),"client_update.isolated_mismatch","stale isolated executable overlay is rejected before sign-in writes or launch");
        Reject(()=>ClientInputs.RequireSame("",updated),"client_update.isolated_mismatch","a missing host client identity cannot authorize isolated launch");
        File.WriteAllText(mono,"mono-v2");Check(ClientInputs.ReadGame(exe)!=updated,"Mono runtime update changes isolated launch identity");
        File.Delete(core);Reject(()=>ClientInputs.ReadManaged(managed),"client_update.incomplete","incomplete installed client does not prepare a connection");
    }
}
