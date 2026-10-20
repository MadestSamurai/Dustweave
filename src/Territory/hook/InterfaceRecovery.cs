using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 public sealed class InterfaceRecoveryReport
 {
  public string AtUtc{get;set;}public string Action{get;set;}public string Error{get;set;}="";public string Scene{get;set;}public string MoveState{get;set;}
  public int ProcessId{get;set;}public int Fields{get;set;}public bool PlayerPresent{get;set;}public float TimeScale{get;set;}
  public string[] HudStates{get;set;}public string[] ActivePopups{get;set;}
 }
 public static class InterfaceRecovery
 {
  private static bool pending,restore,resolver;private static DateTime restoredAt;
  private static class Pump{internal static readonly Harmony Patch=new Harmony("bd2.territory.ui-recovery."+typeof(InterfaceRecovery).Assembly.GetName().Name);}
  public static void Inspect(){Begin(false);}public static void Restore(){Begin(true);}
  private static void Begin(bool repair)
  {
   if(pending)return;if(!resolver){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;resolver=true;}
   restore=repair;restoredAt=DateTime.MinValue;pending=true;Arm();
  }
  [MethodImpl(MethodImplOptions.NoInlining)]private static void Arm()=>Pump.Patch.Patch(typeof(GameCameraManager).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic),postfix:new HarmonyMethod(typeof(InterfaceRecovery),nameof(Frame)));
  private static UIBase[] Surfaces()=>Resources.FindObjectsOfTypeAll<UIBase>().Where(v=>v!=null&&v.gameObject.scene.IsValid()&&v.gameObject.scene.isLoaded).ToArray();
  private static InterfaceRecoveryReport Snapshot(string action)
  {
   var ui=Surfaces();var field=B.Read("Field.Instance",null);return new InterfaceRecoveryReport{AtUtc=DateTime.UtcNow.ToString("O"),Action=action,Scene=SceneManager.GetActiveScene().name,ProcessId=System.Diagnostics.Process.GetCurrentProcess().Id,Fields=UnityEngine.Object.FindObjectsOfType<LifeFarmFieldObject>().Length,PlayerPresent=field!=null&&B.Read("Field.Player",field)!=null,MoveState=field==null?"":B.Read("Field.MoveState",field).ToString(),TimeScale=Time.timeScale,HudStates=ui.OfType<AvatarLifeGameFieldDefaultUI>().Select(v=>"id="+v.GetInstanceID()+" self="+v.gameObject.activeSelf+" hierarchy="+v.gameObject.activeInHierarchy+" visible="+B.Read("Ui.Visible",v)).ToArray(),ActivePopups=ui.Where(v=>B.Active(v)&&(bool)B.Read("Ui.Visible",v)&&!(bool)B.Invoke("Ui.IsHud",v)).Select(v=>v.GetType().Name).ToArray()};
  }
  private static bool ControlActive()
  {
   var path=Path.Combine(LocalStorage.DataRoot,"control.json");if(!File.Exists(path))return false;
   using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))return ((TerritoryControl)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(TerritoryControl)).ReadObject(s)).Valid(DateTime.UtcNow.Ticks,System.Diagnostics.Process.GetCurrentProcess().Id);
  }
  private static void Frame()
  {
   if(!pending)return;
   try
   {
    B.ValidateCompiledClient();B.Validate();
    if(restoredAt!=DateTime.MinValue)
    {if((DateTime.UtcNow-restoredAt).TotalSeconds<1)return;LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"interface-recovery.json"),Snapshot("restored"));Finish();return;}
    var before=Snapshot(restore?"before_restore":"inspect");LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"interface-recovery.json"),before);
    if(!restore){Finish();return;}
    if(ControlActive())throw new InvalidOperationException("请先暂停领地工具；恢复不会自动开始采集。");
    if(!before.PlayerPresent||before.Fields==0||!before.Scene.StartsWith("Map3011",StringComparison.Ordinal))throw new InvalidOperationException("当前不是已载入的领地现场。");
    if(before.ActivePopups.Length>0)throw new InvalidOperationException("仍有活动弹窗："+string.Join(",",before.ActivePopups));
    foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a=>(a.GetName().Name??"").StartsWith("BD2Territory.Runtime",StringComparison.Ordinal)))
    {
     var loader=assembly.GetType("BD2Territory.Runtime.Loader");var engine=loader?.GetField("engine",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null);
     if(engine!=null&&!(bool)loader.GetMethod("SafeToRetire",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{engine}))throw new InvalidOperationException("领地动作或回执仍未结束，暂不恢复界面。");
    }
    var hud=Surfaces().OfType<AvatarLifeGameFieldDefaultUI>().ToArray();if(hud.Length!=1)throw new InvalidOperationException("无法唯一确认现有领地操作界面。");
    LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"interface-recovery-before.json"),before);
    hud[0].SetActive(true);restoredAt=DateTime.UtcNow;LocalStorage.Log("手动恢复升级关闭后隐藏的领地操作界面，保持自动化暂停");
   }
   catch(Exception ex)
   {var report=new InterfaceRecoveryReport{AtUtc=DateTime.UtcNow.ToString("O"),Action="error",Error=ex.GetBaseException().Message,ProcessId=System.Diagnostics.Process.GetCurrentProcess().Id};LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"interface-recovery.json"),report);Finish();}
  }
  private static void Finish(){pending=false;Pump.Patch.UnpatchAll(Pump.Patch.Id);}
  private static Assembly Resolve(object sender,ResolveEventArgs args)
  {if(new AssemblyName(args.Name).Name!="0Harmony")return null;using(var s=typeof(InterfaceRecovery).Assembly.GetManifestResourceStream("BD2Territory.Harmony.dll"))using(var b=new MemoryStream()){s.CopyTo(b);return Assembly.Load(b.ToArray());}}
 }
}
