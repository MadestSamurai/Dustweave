using System.Text.Json;
using Mono.Cecil;

namespace Dustweave.Compatibility;

// Discover obfuscated loading callers in the preparation process, never by
// resolving 100k+ IL bodies on the game's main thread during scene startup.
internal static class LoadingTraceIndex
{
    internal static string Source(MetadataIndex game,string managed,CancellationToken cancellation)
    {
        var rows=new List<string>();
        void Scan(ModuleDefinition module)
        {
            foreach(var type in MetadataIndex.Walk(module.Types))foreach(var method in type.Methods){
                cancellation.ThrowIfCancellationRequested();
                if(!method.HasBody||method.IsAbstract||method.IsSpecialName||method.HasGenericParameters)continue;
                if(method.Body.Instructions.Any(i=>i.Operand is MethodReference target&&LoadingApi(target.DeclaringType.FullName,target.Name)))
                    rows.Add(module.Assembly.Name.Name+"|"+module.Mvid.ToString("D")+"|"+method.MetadataToken.ToInt32());
            }
        }
        Scan(game.Module);
        foreach(string name in new[]{"Unity.ResourceManager.dll","Unity.Addressables.dll"}){
            string path=Path.Combine(managed,name);if(!File.Exists(path))continue;
            using var module=ModuleDefinition.ReadModule(path);Scan(module);
        }
        return "namespace BD2Daily.Runtime { internal static class LoadingTraceIndex { internal static readonly string[] Entries = new string[]{"+string.Join(",",rows.Order(StringComparer.Ordinal).Select(s=>JsonSerializer.Serialize(s)))+"}; } }";
    }
    internal static bool LoadingApi(string type,string name)
    {
        if(type=="UnityEngine.SceneManagement.SceneManager")return name.StartsWith("LoadScene")||name.StartsWith("UnloadScene");
        if(type=="UnityEngine.AssetBundle")return name.StartsWith("LoadFrom")||name=="UnloadAsync";
        if(type=="UnityEngine.Networking.UnityWebRequest")return name=="SendWebRequest";
        if(type=="UnityEngine.AddressableAssets.Addressables")return name.StartsWith("LoadScene")||name.StartsWith("UnloadScene")||name.StartsWith("LoadAsset")||name.StartsWith("DownloadDependencies")||name=="InitializeAsync";
        // Observe game-owned callers of blocking primitives. Do not patch the
        // framework primitives globally: our own writer also uses locks/I/O,
        // and a global patch would create recursion and distort the experiment.
        if(type is "System.Threading.WaitHandle" or "System.Threading.Mutex" or "System.Threading.Semaphore")return name is "WaitOne" or "WaitAny" or "WaitAll";
        if(type=="System.Threading.SemaphoreSlim")return name=="Wait";
        if(type=="System.Threading.Monitor")return name is "Enter" or "Wait" or "TryEnter";
        if(type=="System.Threading.Thread")return name is "Join" or "Sleep";
        if(type.StartsWith("System.Threading.Tasks.Task",StringComparison.Ordinal))return name is "Wait" or "WaitAll" or "WaitAny" or "get_Result";
        if(type is "System.Threading.SynchronizationContext" or "UnityEngine.UnitySynchronizationContext")return name=="Send";
        return type.StartsWith("UnityEngine.ResourceManagement.")&&(name=="WaitForCompletion"||name=="QueueRequest"||name=="BeginOperation"||name=="StartOperation"||name=="Complete");
    }
}
