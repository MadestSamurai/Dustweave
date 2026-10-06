using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using UnityEngine;
using gamfs;
namespace BD2Daily.Live {
 [DataContract] internal sealed class DispatchRecoveryResult {
  [DataMember] public string Command,State,Error="";
  [DataMember] public string[] Keys=new string[0];
  [DataMember] public long At;
 }
 internal static class DispatchRecoveryNative {
  static readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant","live");
  static readonly FieldInfo pendingField=typeof(GameFieldManager).GetField("ὮὩὡὢὦὥὪὧὭὩὢ",BindingFlags.NonPublic|BindingFlags.Instance);
  static readonly FieldInfo loadsField=typeof(FieldObjectBase).GetField("ὣὬὬὩὫὢὤὡὠὪὮ",BindingFlags.NonPublic|BindingFlags.Static);
  static readonly FieldInfo cacheField=typeof(FieldObjectBase).GetField("ὥὠὬὦὤὫὮὩὮὪὠ",BindingFlags.NonPublic|BindingFlags.Static);
  static readonly HashSet<string> attempts=new HashSet<string>();
  static DispatchRecoveryResult active;
  internal static void Cancel(){active=null;}
  static void Save(DispatchRecoveryResult r){
   r.At=DateTime.UtcNow.Ticks;using(var memory=new MemoryStream()){new DataContractJsonSerializer(typeof(DispatchRecoveryResult)).WriteObject(memory,r);if(BD2.LocalIpc.RuntimeFiles.Write(Path.Combine(root,"dispatch-recovery.json"),memory.ToArray()))return;}var file=Path.Combine(root,"dispatch-recovery.json");var temp=file+"."+r.Command+".tmp";
   using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.Read))new DataContractJsonSerializer(typeof(DispatchRecoveryResult)).WriteObject(f,r);
   if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
  }
  static GameFieldManager Context(Command c){
   var f=Bridge.CurrentFrame;
   if(f==null||f.ProcessId!=c.ProcessId||f.ProcessStartTicks!=c.ProcessStartTicks||f.Instance!=c.Instance||f.AccountKey!=c.AccountKey||f.PlayerKey!=c.PlayerKey||f.Scene!=c.Scene||!LivePolicy.GameplayReady(f)||DateTime.UtcNow.Ticks-f.AtUtcTicks>TimeSpan.FromSeconds(3).Ticks||(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(root,"pause"))!=null))throw new InvalidOperationException("Recovery context changed or paused");
   if(f.Surfaces.Any(x=>x.Type!="GameFieldDefaultUI"&&!LivePolicy.PassiveSurface(x.Type)))throw new InvalidOperationException("Recovery field is covered");
   var g=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var pack=Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ;var camera=GameCameraManager.ὪὫὢὨὯὭὦὪὦὨὣ;
   if(g==null||!g.CanSpawnAnyObject()||!g.ὢὩὠὢὫὧὠὣὤὩὫ||g.ὮὬὬὮὠὮὪὠὧὩὪ==null||g.ὮὬὬὮὠὮὪὠὧὩὪ.IsInsideMap!=0||g.ὪὨὯὢὫὮὨὩὮὡὬ==null||g.ὪὨὯὢὫὮὨὩὮὡὬ.ὠὭὬὪὫὣὠὡὡὠὫ.ToString()!="Stop"||camera==null||camera.ὥὦὥὨὫὡὦὮὧὦὢ==null||TimelineSignalManager.ὪὫὢὨὯὭὦὪὦὨὣ!=null||pack.IsNoneFieldPack()||pack.HasNotCheckedDispatchObject()||ὥὭὪὩὢὭὨὢὧὮὢ.ὢὡὥὫὥὯὫὦὦὨὤ||ὤὯὯὬὧὬὯὣὢὯὨ.ὮὩὩὦὨὪὫὦὩὯὧ)throw new InvalidOperationException("Recovery requires a stationary outdoor field");
   if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE"||ὫὨὪὠὢὨὮὤὩὤὮ.ὩὮὠὠὩὭὭὠὥὬὪ.ὭὬὠὫὯὭὮὭὥὦὭ||ὫὨὪὠὢὨὮὤὩὤὮ.ὥὮὯὪὡὢὭὠὣὩὣ.ὧὩὤὩὤὦὯὢὪὢὩ||pack.waitResetPackKeyWordSet.Contains("DispatchAutoSupport")||ὣὡὧὡὦὣὣὬὨὪὫ.ὡὩὧὩὡὤὤὭὠὫὣ)throw new InvalidOperationException("Native work is in progress");
   return g;
  }
  static List<(int,ὢὩὭὬὪὪὬὩὫὪὬ)> Pending(GameFieldManager g){return (List<(int,ὢὩὭὬὪὪὬὩὫὪὬ)>)pendingField.GetValue(g);}
  static List<(int,ὢὩὭὬὪὪὬὩὫὪὬ)> Stale(GameFieldManager g){
   var due=ὢὯὯὣὧὦὯὠὢὪὬ.ὡὯὠὢὨὩὪὪὬὧὡ();
   if(ὧὯὫὪὨὢὬὮὮὠὦ.ὪὯὬὥὡὧὨὬὪὬὭ){var t=ὢὯὯὣὧὦὯὠὢὪὬ.ὮὫὦὧὣὫὡὯὭὦὦ();if(t.HasValue)due.Add(t.Value);}
   if(ὠὮὤὧὠὣὯὯὯὪὤ.ὣὤὯὢὫὢὢὢὩὢὫ)due.Add(ὢὯὯὣὧὦὯὠὢὪὬ.ὠὢὧὤὬὫὪὨὦὣὪ());
   var pending=Pending(g);var loads=(IDictionary)loadsField.GetValue(null);var cache=(IDictionary)cacheField.GetValue(null);var objects=g.ὫὢὬὢὧὫὭὧὫὡὡ;
   return due.Where(x=>x.ὠὭὫὣὢὫὯὨὢὩὦ==ὢὩὭὬὪὪὬὩὫὪὬ.DispatchObject||x.ὠὭὫὣὢὫὯὨὢὩὦ==ὢὩὭὬὪὪὬὩὫὪὬ.TotalWarRewardObject||x.ὠὭὫὣὢὫὯὨὢὩὦ==ὢὩὭὬὪὪὬὩὫὪὬ.EvilCastleRewardObject).Select(x=>(x.ὠὣὫὡὪὨὬὬὢὦὤ,x.ὠὭὫὣὢὫὯὨὢὩὦ)).Distinct().Where(k=>{
    string path="Map/ControllerPrefabs/"+FieldObjectBase.GetFieldObjectSamplePrefabName(k.Item2)+".prefab";
    return pending.Contains(k)&&!loads.Contains(path)&&!cache.Contains(path)&&!objects.Any(x=>x!=null&&x.ὪὬὣὧὥὨὦὯὨὮὬ==k.Item1&&x.ὪὭὨὥὡὧὮὮὫὨὠ==k.Item2);
   }).OrderBy(x=>(int)x.Item2).ThenBy(x=>x.Item1).ToList();
  }
  internal static void Execute(Command c){
   if(active!=null&&active.State=="watching")throw new InvalidOperationException("Recovery already observing");
   var g=Context(c);var keys=Stale(g);if(keys.Count==0)throw new InvalidOperationException("No proven stale messenger load markers");
   var r=new DispatchRecoveryResult{Command=c.Id,State="watching",Keys=keys.Select(x=>x.Item2+":"+x.Item1).ToArray()};active=r;Save(r);g.StartCoroutine(Recover(c,g,r));
  }
  static IEnumerator Recover(Command c,GameFieldManager g,DispatchRecoveryResult r){
   var watch=new DispatchRecoveryGuard();double deadline=Time.realtimeSinceStartup+8;
   while(object.ReferenceEquals(active,r)){
    try{
     if(Context(c)!=g)throw new InvalidOperationException("Field manager changed");
     var keys=Stale(g);string fp=string.Join("|",keys.Select(x=>x.Item2+":"+x.Item1).ToArray());
     if(fp!=string.Join("|",r.Keys))throw new InvalidOperationException("Messenger loading state changed during recovery");
     if(watch.Sample(fp,Time.realtimeSinceStartup)){
      // At most once per account/server day and key set in this bridge session.
      string attempt=c.AccountKey+"|"+ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStamp()/86400000+"|"+fp;
      if(!attempts.Add(attempt))throw new InvalidOperationException("Messenger recovery was already attempted; inspect resource failure");
      watch.Consume();var pending=Pending(g);
      // Only markers are repaired. Native duplicate checking and normal loading stay authoritative.
      foreach(var key in keys)pending.RemoveAll(x=>x.Equals(key));
      r.State="retry_requested";Save(r);
      foreach(var key in keys)g.LoadDynamicFieldObjectCheckDuplication(key.Item2,key.Item1);
      yield break;
     }
     if(Time.realtimeSinceStartup>=deadline)throw new InvalidOperationException("Recovery observation did not stabilize");
    }catch(Exception ex){r.State="aborted";r.Error=ex.GetType().Name+": "+ex.Message;Save(r);yield break;}
    yield return new WaitForSecondsRealtime(.2f);
   }
  }
 }
}
