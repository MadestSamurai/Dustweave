using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
internal static class UiInteractionReadinessCases
{
 public static void Run(List<string> cases)
 {
  using var stream=typeof(UiInteractionReadinessCases).Assembly.GetManifestResourceStream("Tests.UiInteractionReadiness.cs")!;
  using var reader=new StreamReader(stream);
  var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p));
  var compilation=CSharpCompilation.Create("UiLifecycle"+Guid.NewGuid().ToString("N"),
   new[]{CSharpSyntaxTree.ParseText(reader.ReadToEnd()),CSharpSyntaxTree.ParseText(Fixture)},references,
   new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
  using var buffer=new MemoryStream();var result=compilation.Emit(buffer);
  if(!result.Success)throw new Exception(string.Join("\n",result.Diagnostics));
  var runner=Assembly.Load(buffer.ToArray()).GetType("Runner")!;
  cases.AddRange((string[])runner.GetMethod("Run")!.Invoke(null,null)!);
 }
 const string Fixture="""
using System;
using System.Collections.Generic;
using System.Linq;
using BD2Daily.Live;
using UnityEngine;
using UnityEngine.UI;
// Model only the client object lifecycle used by the production readiness
// adapter. No game assembly, connection, mutation or native assets are loaded.
namespace UnityEngine {
 public class GameObject {
  public bool activeInHierarchy=true;
  public GameObject Parent;
  public readonly List<Component> Components=new();
  public readonly List<GameObject> Children=new();
  public T Add<T>(T component) where T:Component {component.gameObject=this;Components.Add(component);return component;}
  public GameObject Child(){var child=new GameObject{Parent=this};Children.Add(child);return child;}
  public T GetComponentInParent<T>() where T:Component=>Components.OfType<T>().FirstOrDefault()??Parent?.GetComponentInParent<T>();
  public T[] GetComponentsInChildren<T>(bool inactive) where T:Component=>
   (!inactive&&!activeInHierarchy?Enumerable.Empty<T>():Components.OfType<T>().Concat(Children.SelectMany(c=>c.GetComponentsInChildren<T>(inactive)))).ToArray();
 }
 public class Component {
  public GameObject gameObject;
  public T[] GetComponentsInChildren<T>(bool inactive) where T:Component=>gameObject.GetComponentsInChildren<T>(inactive);
 }
}
namespace UnityEngine.UI {
 public class ScrollData {public LoopScrollItemBase _originItem;}
 public class LoopScrollRect:UnityEngine.Component {public ScrollData scrollData=new();public int totalCount;}
}
public class UIBase:UnityEngine.Component {public bool CloseAllowed=true;public bool CanCloseUI()=>CloseAllowed;}
public class MissionData {}
public class TarosTacticsMissionPopupUI:UIBase {
 private UnityEngine.UI.LoopScrollRect _scrollMission;
 public List<MissionData> Rows {get;set;}
 public void SetScroll(UnityEngine.UI.LoopScrollRect scroll)=>_scrollMission=scroll;
}
public class LoopScrollItemBase:UnityEngine.Component {
 protected UnityEngine.UI.LoopScrollRect _loopScrollRect;
 protected UIBase _uiBase;
 public bool IsPooled,LoadComplete=true;
 public void BindOwner(UIBase owner,UnityEngine.UI.LoopScrollRect scroll){_uiBase=owner;_loopScrollRect=scroll;}
}
public class TarosTacticsMissionPopupUIScrollItem:LoopScrollItemBase {
 private MissionData data;
 public void Bind(MissionData value)=>data=value;
}

public static class Runner { public static string[] Run(){



var checks=new List<string>();
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
var root=new GameObject();
var popup=root.Add(new TarosTacticsMissionPopupUI());
var scroll=root.Child().Add(new LoopScrollRect());popup.SetScroll(scroll);
var origin=scroll.gameObject.Child().Add(new TarosTacticsMissionPopupUIScrollItem());
origin.BindOwner(popup,scroll);scroll.scrollData._originItem=origin;
Check(!UiInteractionReadiness.TargetReady(popup,origin.gameObject),"active template is not a target");
Check(!UiInteractionReadiness.MissionReady(popup),"created popup without model is not ready");
popup.Rows=new(){new(),new(),new()};
Check(!UiInteractionReadiness.MissionReady(popup),"model exists before delayed AddItem");
scroll.totalCount=3;
Check(!UiInteractionReadiness.MissionReady(popup),"template alone does not prove list rendered");
var row=scroll.gameObject.Child().Add(new TarosTacticsMissionPopupUIScrollItem());row.BindOwner(popup,scroll);
Check(!UiInteractionReadiness.MissionReady(popup),"new cell without bound mission cannot be used");
row.Bind(popup.Rows[0]);
Check(UiInteractionReadiness.MissionReady(popup),"visible viewport subset is enough; no full-scroll requirement");
var button=row.gameObject.Child();
Check(UiInteractionReadiness.TargetReady(popup,button),"real cell descendant button allowed");
row.IsPooled=true;
Check(!UiInteractionReadiness.TargetReady(popup,button)&&!UiInteractionReadiness.MissionReady(popup),"active pooled cell rejected");
row.IsPooled=false;row.LoadComplete=false;
Check(!UiInteractionReadiness.TargetReady(popup,button)&&!UiInteractionReadiness.MissionReady(popup),"loading cell rejected");
row.LoadComplete=true;row.Bind(new());
Check(!UiInteractionReadiness.MissionReady(popup),"recycled cell carrying old model rejected");
row.Bind(popup.Rows[0]);row.BindOwner(new UIBase(),scroll);
Check(!UiInteractionReadiness.TargetReady(popup,button)&&!UiInteractionReadiness.MissionReady(popup),"foreign owner rejected");
row.BindOwner(popup,scroll);popup.CloseAllowed=false;
Check(!UiInteractionReadiness.MissionReady(popup),"native close refusal cannot release visible resources");
popup.CloseAllowed=true;
Check(UiInteractionReadiness.MissionReady(popup),"native open animation ended");
// Replay the real Init/claim refresh ordering: ClearCells, two scheduled ticks,
// AddItem, then data binding. No fixed delay or repeated click is necessary.
int clicks=0;
foreach(int phase in Enumerable.Range(0,5)){
 if(phase==0)scroll.totalCount=0;
 if(phase==3){scroll.totalCount=3;row.Bind(null);}
 if(phase==4)row.Bind(popup.Rows[0]);
 if(UiInteractionReadiness.MissionReady(popup))clicks++;
 if(phase<4)Check(clicks==0,"no claim/close during deferred refresh phase "+phase);
}
Check(clicks==1,"one operation after current cells rebound");
popup.Rows.Clear();scroll.totalCount=0;row.gameObject.activeInHierarchy=false;
Check(UiInteractionReadiness.MissionReady(popup),"legitimate initialized empty list can close");
Check(UiInteractionReadiness.TargetReady(popup,root.Child()),"ordinary non-list controls unaffected");
Check(UiInteractionReadiness.MissionReady(new UIBase()),"unrelated UI unchanged");
// The common target guard also covers non-tactics pooled lists.
var other=new GameObject().Add(new UIBase());
var otherScroll=other.gameObject.Child().Add(new LoopScrollRect());
var otherRow=otherScroll.gameObject.Child().Add(new LoopScrollItemBase());otherRow.BindOwner(other,otherScroll);
otherScroll.scrollData._originItem=otherRow;
Check(!UiInteractionReadiness.TargetReady(other,otherRow.gameObject),"other mission/item templates excluded");
otherScroll.scrollData._originItem=null;
Check(UiInteractionReadiness.TargetReady(other,otherRow.gameObject),"other bound rows remain usable");
return checks.ToArray();

} }
""";
}