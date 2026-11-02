using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using HarmonyLib;
using BD2ReplayCapture;
namespace BD2Daily.Live {
 internal static class SoulResearchTrace {
  const string Id="bd2.soul.research.readonly.trace";
  static Harmony patch;static InventoryBattleRuntimeShape shape;static object system;
  static bool armed;static long until;static int count,dropped,errors,batch;static string run;
  static readonly List<Record> records=new List<Record>();
  [DataContract] internal sealed class Player {
   [DataMember]public int Hp,MaxHp,Guard;[DataMember]public float Stamina,MaxStamina;
  }
  [DataContract] internal sealed class Record {
   [DataMember]public string Kind,Side;[DataMember]public long InventoryIndex;[DataMember]public float Time;
   [DataMember]public bool Hit;[DataMember]public Player Blue,Red,AfterBlue,AfterRed;[DataMember]public ulong[] RngBefore,RngAfter;
   [DataMember]public float UnitCooldown,UnitProgress,UnitModifier;
  }
  [DataContract] sealed class Batch {
   [DataMember]public string Role="soul.runtime",Kind="batch",Run;
   [DataMember]public bool Accepted=true;[DataMember]public long AtUtcTicks;
   [DataMember]public int Errors,Dropped;[DataMember]public Record[] Records;
  }
  static Player Read(object p){return new Player{Hp=Convert.ToInt32(shape.CurrentHealthField.GetValue(p)),MaxHp=Convert.ToInt32(shape.MaximumHealthField.GetValue(p)),Guard=shape.EnergyGuardField==null?0:Convert.ToInt32(shape.EnergyGuardField.GetValue(p)),Stamina=Convert.ToSingle(shape.CurrentStaminaField.GetValue(p)),MaxStamina=Convert.ToSingle(shape.MaximumStaminaField.GetValue(p))};}
  static ulong[] Rng(){var r=shape.RngField.GetValue(system);var a=new List<ulong>();foreach(var f in shape.RngStateFields){var v=f.GetValue(r);if(v is ulong)a.Add((ulong)v);else if(v is ulong[])a.AddRange((ulong[])v);}return a.ToArray();}
  static Record Snapshot(string kind){return new Record{Kind=kind,Time=Convert.ToSingle(shape.TimeProperty.GetValue(system,null)),Blue=Read(shape.BluePlayerProperty.GetValue(system,null)),Red=Read(shape.RedPlayerProperty.GetValue(system,null)),RngBefore=Rng()};}
  static bool Active(){if(armed&&DateTime.UtcNow.Ticks>until){armed=false;Flush();}return armed;}
  static void Add(Record row){if(count++>=20000){dropped++;armed=false;Flush();return;}records.Add(row);if(records.Count>=128)Flush();}
  static void Flush(){
   if(records.Count==0&&errors==0&&dropped==0)return;
   var data=new Batch{Run=run,AtUtcTicks=DateTime.UtcNow.Ticks,Errors=errors,Dropped=dropped,Records=records.ToArray()};
   using(var bytes=new MemoryStream()){
    new DataContractJsonSerializer(typeof(Batch)).WriteObject(bytes,data);
    var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant","live","events",data.AtUtcTicks+"-soultrace-"+(batch++)+".json");
    if(!BD2.LocalIpc.RuntimeFiles.Write(path,bytes.ToArray()))throw new InvalidOperationException("Research trace IPC unavailable");
   }records.Clear();
  }
  internal static void Start(){
   Stop();if(shape==null)shape=InventoryBattleRuntimeShapeResolver.Resolve(typeof(InventoryBattleManager).Assembly);
   if(!shape.Resolved)throw new InvalidOperationException("Runtime trace shape: "+string.Join(",",shape.Blockers));
   count=dropped=errors=batch=0;system=null;run=Guid.NewGuid().ToString("N");until=DateTime.UtcNow.AddMinutes(5).Ticks;
   patch=new Harmony(Id);
   patch.Patch(shape.ActivateMethod,postfix:new HarmonyMethod(typeof(SoulResearchTrace),"Activated"));
   patch.Patch(shape.AttackMethod,prefix:new HarmonyMethod(typeof(SoulResearchTrace),"AttackBefore"),postfix:new HarmonyMethod(typeof(SoulResearchTrace),"AttackAfter"));
   patch.Patch(shape.EndRequestMethod,prefix:new HarmonyMethod(typeof(SoulResearchTrace),"Ending"));
   armed=true;
  }
  internal static void Stop(){armed=false;try{Flush();}catch{}if(patch!=null){patch.UnpatchAll(Id);patch=null;}records.Clear();system=null;}
  static void Activated(object __instance){if(!Active())return;try{system=__instance;Add(Snapshot("activated"));}catch{errors++;}}
  static void AttackBefore(object __instance,object[] __args,out Record __state){
   __state=null;if(!Active())return;
   try{system=__instance;var row=Snapshot("attack");row.Side=ReferenceEquals(shape.BluePlayerProperty.GetValue(system,null),__args[1])?"BLUE":"RED";row.InventoryIndex=Convert.ToInt64(shape.UnitInventoryIndexField.GetValue(__args[0]));
    var u=__args[0];var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    // Audited current-client fields; the entire bridge is gated by its MVID.
    row.UnitCooldown=Convert.ToSingle(u.GetType().GetField("ὨὭὦὯὮὥὥὨὧὬὤ",flags).GetValue(u));
    row.UnitProgress=Convert.ToSingle(u.GetType().GetField("ὯὦὮὢὧὯὧὠὭὫὥ",flags).GetValue(u));
    row.UnitModifier=Convert.ToSingle(u.GetType().GetField("ὯὣὬὭὣὨὦὡὩὭὯ",flags).GetValue(u));__state=row;}catch{errors++;}
  }
  static void AttackAfter(bool __result,Record __state){
   if(__state==null||!armed)return;
   try{__state.Hit=__result;__state.AfterBlue=Read(shape.BluePlayerProperty.GetValue(system,null));__state.AfterRed=Read(shape.RedPlayerProperty.GetValue(system,null));__state.RngAfter=Rng();Add(__state);}catch{errors++;}
  }
  static void Ending(){if(!Active())return;try{if(system!=null)Add(Snapshot("ending"));Flush();}catch{errors++;}finally{armed=false;}}
 }
}
