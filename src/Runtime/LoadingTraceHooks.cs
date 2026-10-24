using System;
using System.Linq;
using System.Reflection;

using System.Collections.Generic;
using HarmonyLib;
using BD2.LocalIpc;
namespace BD2Daily.Runtime
{
    // Diagnostic-only managed boundaries. Arguments, results and network bodies
    // are never recorded. Async state-machine MoveNext is timed as well as its
    // factory; elapsed factory time alone does not describe a loading coroutine.
    internal static class LoadingTraceHooks
    {
        static Harmony patch;
        static readonly System.Collections.Concurrent.ConcurrentDictionary<MethodBase,string> names=new System.Collections.Concurrent.ConcurrentDictionary<MethodBase,string>();
        static long nextUi;
        static string lastUi="";
        sealed class RequestState {internal long Stamp;internal string Endpoint;internal int Finished;}
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UnityEngine.Networking.UnityWebRequest,RequestState> requests=new System.Runtime.CompilerServices.ConditionalWeakTable<UnityEngine.Networking.UnityWebRequest,RequestState>();
        internal static void Start()
        {
            var config=LoadingTrace.ReadControl();if(config==null||!config.DeepHooks||patch!=null)return;
            patch=new Harmony("dustweave.loading.experiment.v1");
            LoadingTrace.Note("game-build","version="+UnityEngine.Application.version+";unity="+UnityEngine.Application.unityVersion+";data="+UnityEngine.Application.dataPath);
            var assembly=typeof(AppManager).Assembly;
            var roots=assembly.GetTypes().Where(t=>!t.IsNested&&(t.Name=="AppManager"||t.Name=="PackManager"||t.Name=="LoadingUI"||t.Name=="GameFieldManager"||t.Name=="ResourceManager"||t.Name=="AssetBundleManager"||t.Name=="SceneManager"||t.Name=="GameSceneManager"||t.Name=="NetworkManager")).ToList();
            try{var service=GuildBindings.Get("Services.Network") as MethodInfo;if(service!=null&&!roots.Contains(service.ReturnType))roots.Add(service.ReturnType);}catch(Exception e){LoadingTrace.Note("hook-unavailable","network-type:"+e.GetType().Name);}
            var selected=new HashSet<MethodInfo>();
            foreach(var type in roots.Concat(roots.SelectMany(t=>t.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))))
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) {
                // Omit per-frame UI/render/property accessors. Loading coroutine
                // and managed request paths remain observable without recursion.
                if(method.IsAbstract||method.IsGenericMethod||method.GetMethodBody()==null||method.IsSpecialName||method.Name=="Update"||method.Name=="LateUpdate"||method.Name=="FixedUpdate")continue;
                if(type.IsNested&&method.Name!="MoveNext")continue;
                if(!type.IsNested&&!new[]{"Load","Scene","Pack","Asset","Request","Response","Send","Download","Init","Start"}.Any(s=>method.Name.IndexOf(s,StringComparison.OrdinalIgnoreCase)>=0))continue;
                selected.Add(method);
            }
            // Game type/method names are obfuscated. Discover callers by the
            // actual Unity/Addressables API they invoke, not an English-name
            // allowlist. Capture async MoveNext boundaries too. No arguments,
            // response bodies or authentication data enter this trace.
            long scan=LoadingTrace.Begin("diagnostic.bind-loading-boundaries");int visited=0;
            var loaded=AppDomain.CurrentDomain.GetAssemblies().GroupBy(a=>a.GetName().Name).ToDictionary(g=>g.Key,g=>g.First());
            foreach(string entry in LoadingTraceIndex.Entries){
                string[] fields=entry.Split('|');Assembly target;int token;
                if(fields.Length!=3||!int.TryParse(fields[2],out token)||!loaded.TryGetValue(fields[0],out target)||target.ManifestModule.ModuleVersionId.ToString("D")!=fields[1]){
                    LoadingTrace.Note("hook-index-unavailable",entry);continue;
                }
                visited++;try{var method=target.ManifestModule.ResolveMethod(token) as MethodInfo;if(method!=null)selected.Add(method);}catch(Exception e){LoadingTrace.Note("hook-index-unavailable",entry+":"+e.GetType().Name);}
            }
            LoadingTrace.End("diagnostic.bind-loading-boundaries",scan);
            LoadingTrace.Note("hook-discovery-summary","indexed="+visited+";selected="+selected.Count+";discovery=offline");
            foreach(var method in selected){
                if(method.ContainsGenericParameters||method.DeclaringType.ContainsGenericParameters)continue;
                string name=Name(method);
                try{patch.Patch(method,prefix:new HarmonyMethod(typeof(LoadingTraceHooks),"Before"),finalizer:new HarmonyMethod(typeof(LoadingTraceHooks),"After"));LoadingTrace.Note("hook-installed",name);}
                catch(Exception e){LoadingTrace.Note("hook-unavailable",name+":"+e.GetType().Name);}
            }
            try{
                var send=typeof(UnityEngine.Networking.UnityWebRequest).GetMethod("SendWebRequest",Type.EmptyTypes);
                patch.Patch(send,prefix:new HarmonyMethod(typeof(LoadingTraceHooks),"BeforeRequest"),postfix:new HarmonyMethod(typeof(LoadingTraceHooks),"AfterRequest"));
                var dispose=typeof(UnityEngine.Networking.UnityWebRequest).GetMethod("Dispose",Type.EmptyTypes);
                patch.Patch(dispose,prefix:new HarmonyMethod(typeof(LoadingTraceHooks),"BeforeRequestDispose"));
                LoadingTrace.Note("hook-installed","UnityWebRequest.SendWebRequest + asynchronous completion");
                LoadingTrace.Note("hook-installed","UnityWebRequest.Dispose: preserve result before native handle release");
            }catch(Exception e){LoadingTrace.Note("hook-unavailable","UnityWebRequest.SendWebRequest:"+e.GetType().Name);}
        }
        static void BeforeRequest(UnityEngine.Networking.UnityWebRequest __instance,out RequestState __state)
        {
            __state=null;if(!LoadingTrace.Enabled)return;
            try{
            string endpoint="unavailable";
            try{var uri=new Uri(__instance.url);endpoint=uri.Scheme+"://"+uri.Host+System.Text.RegularExpressions.Regex.Replace(uri.AbsolutePath,@"[a-zA-Z0-9_-]{32,}","[id]");}catch{}
            __state=new RequestState{Stamp=System.Diagnostics.Stopwatch.GetTimestamp(),Endpoint=endpoint};
            requests.Remove(__instance);requests.Add(__instance,__state);
            LoadingTrace.Note("web-request-start",endpoint+";request="+__state.Stamp+";method="+__instance.method+";timeout="+__instance.timeout);
            }catch(Exception e){LoadingTrace.Note("web-request-observation-unavailable","start:"+e.GetType().Name);}
        }
        static void AfterRequest(UnityEngine.Networking.UnityWebRequest __instance,UnityEngine.Networking.UnityWebRequestAsyncOperation __result,RequestState __state)
        {
            if(__state==null||__result==null)return;
            try{var request=__instance;var state=__state;__result.completed+=_=>RecordRequest(request,state,"callback");}
            catch(Exception e){LoadingTrace.Note("web-request-observation-unavailable","subscribe:"+e.GetType().Name);}
        }
        static void BeforeRequestDispose(UnityEngine.Networking.UnityWebRequest __instance)
        {
            // UniTask/game continuations can Dispose the request before Unity
            // invokes our completed subscriber. Read metadata at this boundary;
            // do not delay disposal, retain native handles or alter callbacks.
            try{RequestState state;if(LoadingTrace.Enabled&&requests.TryGetValue(__instance,out state))RecordRequest(__instance,state,"dispose");}
            catch(Exception e){LoadingTrace.Note("web-request-observation-unavailable","dispose:"+e.GetType().Name);}
        }
        static void RecordRequest(UnityEngine.Networking.UnityWebRequest request,RequestState state,string phase)
        {
            if(!LoadingTrace.Enabled||System.Threading.Interlocked.CompareExchange(ref state.Finished,1,0)!=0)return;
            string metadata=state.Endpoint+";request="+state.Stamp+";elapsedMs="+((System.Diagnostics.Stopwatch.GetTimestamp()-state.Stamp)*1000d/System.Diagnostics.Stopwatch.Frequency)+";phase="+phase;
            // Record the observed terminal boundary even if one native getter
            // is unavailable. A disposed request is never reported as success.
            try{
                bool done=request.isDone;
                string status=RequestValue(()=>request.responseCode.ToString());
                string result=RequestValue(()=>request.result.ToString());
                string downloaded=RequestValue(()=>request.downloadedBytes.ToString());
                LoadingTrace.Note(done?"web-request-completed":"web-request-disposed",metadata+";done="+done+";status="+status+";result="+result+";downloaded="+downloaded);
            }catch(Exception e){LoadingTrace.Note("web-request-result-unavailable",metadata+";error="+e.GetType().Name);}
        }
        static string RequestValue(Func<string> read)
        {
            try{return read();}catch(Exception e){return "unavailable:"+e.GetType().Name;}
        }
        internal static void Observe()
        {
            if(patch==null||!LoadingTrace.Enabled||DateTime.UtcNow.Ticks<nextUi)return;
            nextUi=DateTime.UtcNow.AddSeconds(2).Ticks;
            long at=LoadingTrace.Begin("diagnostic.popup-state");
            try{
                var field=(FieldInfo)StartupBindings.Get("UI.Popup");
                var rows=new List<string>();
                foreach(var popup in UnityEngine.Object.FindObjectsOfType<UIBase>()){
                    if(popup==null||!popup.gameObject.activeInHierarchy||!(bool)field.GetValue(popup))continue;
                    var words=new List<string>();
                    foreach(var text in popup.GetComponentsInChildren<UnityEngine.UI.Text>())
                        if(text.isActiveAndEnabled&&text.GetComponentInParent<UnityEngine.UI.InputField>()==null)words.Add(text.text);
                    foreach(var component in popup.GetComponentsInChildren<UnityEngine.MonoBehaviour>()){
                        if(component==null||!component.isActiveAndEnabled||component.GetType().FullName!="TMPro.TextMeshProUGUI")continue;
                        if(component.GetComponentsInParent<UnityEngine.MonoBehaviour>().Any(c=>c!=null&&c.GetType().FullName=="TMPro.TMP_InputField"))continue;
                        var property=component.GetType().GetProperty("text");if(property!=null)words.Add(property.GetValue(component,null) as string??"");
                    }
                    rows.Add(popup.GetType().FullName+"#"+popup.GetInstanceID()+":"+string.Join(" | ",words.Distinct().ToArray()));
                }
                string current=string.Join("\n",rows.ToArray());if(current!=lastUi){lastUi=current;LoadingTrace.Note("popup-state",current);}
            }catch(Exception e){LoadingTrace.Note("popup-observation-unavailable",e.GetType().Name);}
            finally{LoadingTrace.End("diagnostic.popup-state",at);}
        }
        static string Name(MethodBase method){return names.GetOrAdd(method,m=>"game."+m.DeclaringType.FullName+"."+m.Name+"#"+m.MetadataToken);}
        static void Before(MethodBase __originalMethod,out long __state){__state=LoadingTrace.Enabled?LoadingTrace.Begin(Name(__originalMethod)):0;}
        static Exception After(MethodBase __originalMethod,long __state,Exception __exception){if(__state!=0){LoadingTrace.End(Name(__originalMethod),__state);if(__exception!=null)LoadingTrace.Note("game-exception",Name(__originalMethod)+":"+__exception.GetType().Name);}return __exception;}
        internal static void Stop(){if(patch!=null){patch.UnpatchAll(patch.Id);patch=null;}}
    }
}
