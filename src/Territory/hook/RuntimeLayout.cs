using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using FieldEvent.Life;
using FieldEvent.Life.Chunk;
using Proto.Design.common;
using Proto.Net;
using UnityEngine;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private long lastLayoutWorld,layoutAt;private LayoutWorld layoutWorld;private LayoutRequest layoutJob;private LayoutStatus layoutStatus=new LayoutStatus();
  private LayoutQuote layoutQuote;private string preparedSignature="",layoutJournal="";private int layoutStep,layoutIndex;private bool layoutOwnPreview,layoutLoaded;
  private AvatarLifeHousingEditUI housing;private AvatarLifeHousingEditUI_EditUI editor;
  private readonly Dictionary<string,List<LayoutItem>> plannedCells=new Dictionary<string,List<LayoutItem>>();
  private static T ReadLayoutFile<T>(string path) where T:class
  {if(BD2.LocalIpc.RuntimeFiles.Handles(path)){var bytes=BD2.LocalIpc.RuntimeFiles.Read(path);if(bytes==null)return null;if(bytes.Length>2000000)throw new InvalidDataException("Layout request exceeds 2 MB");using(var memory=new MemoryStream(bytes))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(memory);}if(!File.Exists(path))return null;using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(f.Length>2000000)throw new InvalidOperationException("布局文件超过 2 MB");return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(f);}}
  private GroundChunkManager LayoutGround()=>UnityEngine.Object.FindObjectsOfType<GroundChunkManager>().FirstOrDefault(B.Active);
  private LayoutWorld CaptureLayoutWorld(long now)
  {
   var user=(UserDBInfo)B.Read("Account.User",null);var life=(LifeUserDBInfo)B.Read("Inventory.User",null);var gm=LayoutGround();
   if(user==null||user.OwnerIndex<=0||life==null||gm==null)throw new InvalidOperationException("等待领地账号与地图载入。");
   var w=new LayoutWorld{Account=user.OwnerIndex.ToString(),WorldId=life.LifeWorldId,ProcessId=pid,CapturedUtcTicks=now};
   w.Chunks=life.ChunkId.Where(id=>gm.FindChunkById(id)!=null).Select(id=>{var xy=gm.GetChunkGridPosition(gm.FindChunkById(id));return new LayoutChunk{Id=id,X=xy.x,Y=xy.y};}).ToArray();
   var cache=B.Read("Inventory.World",null) as IEnumerable<LifeWorldObjectPlaceDBInfo>;if(cache==null)throw new InvalidOperationException("等待服务器布局缓存。");
   var groups=((IEnumerable<LifeObjectGroupTable>)B.Invoke("Tables.Objects")).ToArray();var items=new List<LayoutItem>();
   foreach(var g in groups.Where(v=>v.IsUserEditable!=0&&(v.ObjectType==1||v.ObjectType==3||v.ObjectType==4)))
   {
    var item=new LayoutItem{Id=g.Id,Name=(string)B.Invoke("Text.Name",g.NameTextId),Layer=g.LayerLevel,OverlapLayers=g.AntiOverlapLayerLevel.ToArray(),MaxCount=g.MaxCount,LayerLimit=g.ObjectType==4?Convert.ToInt32(B.Invoke("Layout.LayerLimit",g.LayerLevel)):0,Unlocked=(bool)B.Invoke("Layout.Unlocked",g.UnlockContentType,g.UnlockContentLevel)};
    if(g.ObjectType==1)
    {
     var materials=B.Read("Inventory.Materials",null) as IEnumerable<ItemDBInfo>;
     long owned=materials==null?0:materials.Where(v=>v.Id==g.Id).Sum(v=>(long)v.Count);
     owned=Math.Max(0,owned-cache.Sum(c=>c.Object.Count(v=>v.ObjectId==g.Id)));
     item.Costs=new[]{new LayoutCost{Type=65,Id=g.Id,Name=item.Name+"（库存家具）",Count=1,Owned=owned}};items.Add(item);continue;
    }
    int[] types,ids,counts;
    if(g.ObjectType==3){var b=(LifeBuildingObjectTable)B.Invoke("Tables.Building",g.Id);if(b==null)continue;item.Function=b.FunctionType;types=b.RequireItemType.ToArray();ids=b.RequireItemId.ToArray();counts=b.RequireItemCount.ToArray();}
    else {var b=(LifeDecoObjectTable)B.Invoke("Tables.Deco",g.Id);if(b==null)continue;types=b.RequireItemType.ToArray();ids=b.RequireItemId.ToArray();counts=b.RequireItemCount.ToArray();}
    if(ids.Length!=types.Length||counts.Length<ids.Length||types.Any(t=>t!=64&&t!=66))continue;
    // The native decoration placement request consumes only FirstOrDefault; refuse multi-cost decorations.
    if(g.ObjectType==4&&ids.Length>1)continue;
    item.Costs=ids.Select((id,i)=>{var table=(LifeItemTable)B.Invoke("Tables.ItemById",id);return new LayoutCost{Type=types[i],Id=id,Name=types[i]==64?"领地币":table==null?"材料 "+id:(string)B.Invoke("Text.Name",table.NameTextId),Count=counts[i],Owned=types[i]==64?Convert.ToInt64(B.Invoke("Inventory.Currency",B.EnumObject("CurrencyKind","LocalMileage"))):Convert.ToInt64(B.Invoke("Inventory.Count",id))};}).ToArray();items.Add(item);
   }
   w.Catalog=items.ToArray();var world=B.Read("Inventory.World",null) as IEnumerable<LifeWorldObjectPlaceDBInfo>;
   if(world==null)throw new InvalidOperationException("等待服务器布局缓存。");
   w.Objects=world.SelectMany(c=>c.Object.Select(o=>new LayoutEntry{ChunkId=c.ChunkId,ObjectId=o.ObjectId,X=o.X,Y=o.Y,Rotate=o.Rotate})).ToArray();return w;
  }
  private void WriteLayoutStatus(string state,string message)
  {layoutStatus.State=state;layoutStatus.Message=message;layoutStatus.Done=layoutIndex;layoutStatus.CapturedUtcTicks=DateTime.UtcNow.Ticks;LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"layout-status.json"),layoutStatus);}
  private bool LayoutBusy()=>layoutOwnPreview||layoutStep==3;
  private void CancelLayoutPreview()
  {
   if(!layoutOwnPreview)return;
   if(editor==null&&housing!=null)editor=B.Read("Layout.Edit",housing) as AvatarLifeHousingEditUI_EditUI;
   var lpo=editor==null?null:B.Read("Layout.Selected",editor) as LifePlaceableObject;
   if(lpo!=null){var expected=layoutQuote!=null&&layoutIndex<layoutQuote.Missing.Length?layoutQuote.Missing[layoutIndex]:null;var db=B.Read("Farm.Db",lpo) as LifeWorldObjectDBInfo;
    if(expected==null||db==null||db.ObjectId!=expected.ObjectId)throw new InvalidOperationException("游戏选中项已被手动改变，请取消编辑。");
    if(!(bool)B.Read("Layout.Temporary",lpo)&&(layoutQuote==null||layoutIndex>=layoutQuote.Sources.Length||layoutQuote.Sources[layoutIndex]==null))throw new InvalidOperationException("选中了已有设施，请手动取消编辑。");editor.OnClickCancel(false,true);layoutOwnPreview=false;}
   else if(layoutLoaded)layoutOwnPreview=false;
  }
  private bool LayoutTick(TerritorySnapshot s,TerritoryControl c,long now)
  {
   bool active=c.Valid(now,pid)&&!string.IsNullOrEmpty(c.LayoutToken);
   try
   {
    if(s.Ready&&now-lastLayoutWorld>TimeSpan.FromSeconds(2).Ticks)
    {layoutWorld=CaptureLayoutWorld(now);lastLayoutWorld=now;LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"layout-world.json"),layoutWorld);}
    if(!active)
    {
     if(layoutJob!=null&&layoutStatus.State!="complete"&&layoutStatus.State!="ready"&&layoutStatus.State!="error")WriteLayoutStatus("paused","已暂停；已提交的购买仍等待确认，重新预检可接续已完成部分。");
     if(layoutStep!=3)CancelLayoutPreview();
     else if(!network.Waiting){layoutStep=0;CancelLayoutPreview();}
     return false;
    }
    StopMotion();s.Enabled=true;
    if(!s.Ready||layoutWorld==null){s.Reason="等待进入领地";return true;}
    if(layoutJob==null||layoutJob.Token!=c.LayoutToken)
    {
     if(layoutOwnPreview||network.Waiting||farmStage>0||progress!=null&&progress.PendingToken.Length>0||tool!=null&&((bool)B.Read("Tool.Busy",tool)||ToolLoading())){s.Reason="等待上个动作和服务器回执完成";return true;}
     var job=ReadLayoutFile<LayoutRequest>(Path.Combine(LocalStorage.DataRoot,"layout-request.json"));
     if(job==null||job.Token!=c.LayoutToken){s.Reason="等待布局指令";return true;}
     layoutJob=job;layoutIndex=0;layoutStep=0;plannedCells.Clear();layoutAt=now;layoutStatus=new LayoutStatus{Token=job.Token};
     layoutWorld=CaptureLayoutWorld(now);
     var planting=LoadProgress(Path.Combine(LocalStorage.DataRoot,"progress-"+layoutWorld.Account+".json"),layoutWorld.Account);if(!string.IsNullOrEmpty(planting.PendingToken))throw new InvalidOperationException("当前账号仍有播种回执待核对，请先启动种植完成核对后再调整布局。");
     if(job.Account!=layoutWorld.Account)throw new InvalidOperationException("账号发生变化，请重新预检。");
     if(job.Operation!="preview"&&job.Operation!="apply")throw new InvalidOperationException("未知布局操作。");
     layoutJournal=Path.Combine(LocalStorage.DataRoot,"layout-pending-"+layoutWorld.Account+"-"+layoutWorld.WorldId+".json");
     var pending=ReadLayoutFile<LayoutIntent>(layoutJournal);
     if(pending!=null)
     {
      if(pending.Account!=layoutWorld.Account||pending.WorldId!=layoutWorld.WorldId||pending.Entry==null||!layoutWorld.Objects.Any(v=>v.Key==pending.Entry.Key))throw new InvalidOperationException("上次摆放结果未知，请重新进入领地让服务器同步后再预检；不会重复付款。");
      File.Delete(layoutJournal);
     }
     layoutQuote=LayoutPlanner.Quote(layoutWorld,job.Document);layoutStatus.Quote=layoutQuote;layoutStatus.Total=layoutQuote.Missing.Length;
     if(job.Operation=="apply")
     {
      if(string.IsNullOrEmpty(preparedSignature)||preparedSignature!=job.QuoteSignature||preparedSignature!=layoutQuote.Signature)throw new InvalidOperationException("布局或费用已变化，请重新预检。");
      if(layoutQuote.Costs.Any(v=>v.Missing>0))throw new InvalidOperationException("建材或领地币不足，请先补齐。");
      var backup=new LayoutDocument{Name="导入前布局",WorldId=layoutWorld.WorldId,Objects=layoutWorld.Objects.Where(v=>layoutWorld.Catalog.Any(i=>i.Id==v.ObjectId)).ToArray()};
      LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"layout-backup-"+job.Token+".json"),backup);preparedSignature="";
     }
     network.ClearError();WriteLayoutStatus("running",job.Operation=="preview"?"逐格检查原生摆放条件，不扣费":"准备购买并导入");
    }
    if(layoutStatus.State=="error"||layoutStatus.State=="ready"||layoutStatus.State=="complete"||layoutStatus.State=="paused"){s.Reason=layoutStatus.Message;return true;}
    var liveUser=(UserDBInfo)B.Read("Account.User",null);var liveLife=(LifeUserDBInfo)B.Read("Inventory.User",null);
    if(liveUser==null||liveUser.OwnerIndex.ToString()!=layoutJob.Account||liveLife?.LifeWorldId!=layoutJob.Document.WorldId)throw new InvalidOperationException("账号或领地已切换，已暂停布局操作。");
    if(network.Error.Length>0){if(layoutStep==3&&network.LayoutRejectedKey==layoutQuote.Missing[layoutIndex].Key&&File.Exists(layoutJournal))File.Delete(layoutJournal);throw new InvalidOperationException(network.Error+"；如摆放被拒绝，请重新进入领地同步后再预检。");}
    if(layoutIndex>=layoutQuote.Missing.Length)
    {
     CancelLayoutPreview();layoutStep=0;
     if(layoutJob.Operation=="preview"){preparedSignature=layoutQuote.Signature;WriteLayoutStatus("ready",layoutQuote.Costs.Any(v=>v.Missing>0)?"占地检查完成；请先补齐费用清单中的缺口。":"预检通过，可按清单购买并导入。");}
     else WriteLayoutStatus("complete","布局导入完成，全部摆放已在服务器缓存中回读确认。");
     s.Reason=layoutStatus.Message;return true;
    }
    var e=layoutQuote.Missing[layoutIndex];var source=layoutQuote.Sources[layoutIndex];var item=layoutWorld.Catalog.Single(v=>v.Id==e.ObjectId);
    s.Target=item.Name+" · 区域 "+e.ChunkId+"（"+e.X+","+e.Y+"）";s.Reason="布局 "+(layoutIndex+1)+" / "+layoutQuote.Missing.Length;
    housing=UnityEngine.Object.FindObjectsOfType<AvatarLifeHousingEditUI>().FirstOrDefault(B.Active);
    if(layoutStep==3)
    {
     if(network.Waiting){if(now-layoutAt>TimeSpan.FromSeconds(30).Ticks)throw new InvalidOperationException("摆放回执超时；已保留付款记录，不能重复提交。");return true;}
     var world=CaptureLayoutWorld(now);
     if(!world.Objects.Any(v=>v.Key==e.Key)){if(now-layoutAt>TimeSpan.FromSeconds(30).Ticks)throw new InvalidOperationException("未回读到目标摆放；结果未知，请重新进入领地同步。");return true;}
     File.Delete(layoutJournal);layoutIndex++;layoutStep=0;layoutAt=now;WriteLayoutStatus("running","已确认 "+layoutIndex+" 处摆放");return true;
    }
    var upgrade=surfaces.FirstOrDefault(v=>B.Active(v)&&(v is AvatarLifeLevelUpPopupUI||v is AvatarLifeNewItemPopupUI));
    if(upgrade!=null){ConfirmTerritoryPopup(upgrade,s,now);return true;}
    if(popupProgress.WaitForSettle(now)){s.Reason="等待建造升级／解锁提示完成";return true;}
    if(surfaces.Any(v=>B.Active(v)&&(bool)B.Read("Ui.Visible",v)&&!(bool)B.Invoke("Ui.IsHud",v)&&!(v is AvatarLifeHousingEditUI))){s.Reason="布局暂停，等待关闭其他游戏弹窗";return true;}
    if(housing==null)
    {
     if(now-layoutAt>TimeSpan.FromSeconds(20).Ticks)throw new InvalidOperationException("建造界面未打开，请关闭弹窗后重新预检。");
     if(layoutStep==-1)return true;
     var hud=surfaces.OfType<AvatarLifeGameFieldDefaultUI>().FirstOrDefault(B.Active);if(hud==null)return true;
     var button=(AvatarLifeGameFieldDefaultUI.DefaultButton)B.Read("Layout.HousingButton",hud);
     if(button.button==null||!B.Active(button.button)||!button.button.interactable)throw new InvalidOperationException("当前不可打开领地建造。");
     hud.OnClickUI(button.button.gameObject);layoutStep=-1;return true;
    }
    editor=B.Read("Layout.Edit",housing) as AvatarLifeHousingEditUI_EditUI;if(editor==null)throw new InvalidOperationException("领地建造编辑器尚未就绪。");
    var gm=LayoutGround();if(gm==null)throw new InvalidOperationException("领地区域尚未载入。");
    if(layoutStep<=0)
    {
     if(B.Read("Layout.Selected",editor) is LifePlaceableObject)throw new InvalidOperationException("请先取消游戏中手动选中的设施，再重新预检。");
     layoutOwnPreview=true;layoutLoaded=false;layoutAt=now;layoutStep=1;if(source==null)housing.PlaceItemNearPosition(e.ObjectId,gm.GetCellWorldPosition(e.ChunkId,e.X,e.Y),0);
     else
     {
      var existing=UnityEngine.Object.FindObjectsOfType<LifePlaceableObject>().FirstOrDefault(v=>{var db=B.Read("Farm.Db",v) as LifeWorldObjectDBInfo;return B.Active(v)&&!(bool)B.Read("Layout.Temporary",v)&&db!=null&&db.ObjectId==source.ObjectId&&db.X==source.X&&db.Y==source.Y&&Convert.ToInt32(B.Read("Farm.Chunk",v))==source.ChunkId;});
      if(existing==null)throw new InvalidOperationException("未找到需要移动的已有设施，请重新进入领地后预检。");B.InvokeOn("Layout.Select",housing,existing);layoutLoaded=true;
     }
     return true;
    }
    var lpo=B.Read("Layout.Selected",editor) as LifePlaceableObject;
    if(lpo==null){if(now-layoutAt>TimeSpan.FromSeconds(20).Ticks)throw new InvalidOperationException("设施预览载入超时，已停止。");return true;}
    if((bool)B.Read("Layout.Temporary",lpo)!=(source==null)||((LifeWorldObjectDBInfo)B.Read("Farm.Db",lpo))?.ObjectId!=e.ObjectId)throw new InvalidOperationException("设施预览与任务不一致，请取消手动编辑后重试。");
    layoutLoaded=true;int rotation=lpo.RotationIndexToRotation(e.Rotate);if(!lpo.IsRotationAllowed(rotation)||lpo.RotationToRotationIndex(rotation)!=e.Rotate)throw new InvalidOperationException("设施不支持布局中的朝向。");
    if(!gm.CanPlaceObject(e.ChunkId,e.X,e.Y,lpo,rotation))throw new InvalidOperationException(s.Target+" 无法摆放：请检查占地、水面、边界和已解锁区域。");
    var cell=new CellInfo(e.ChunkId,e.X,e.Y);lpo.transform.position=gm.GetCellWorldPosition(e.ChunkId,e.X,e.Y,lpo,rotation);lpo.ApplyVisualRotation(rotation);lpo.UpdatePlacementPreview(gm,cell,rotation);
    if(layoutJob.Operation=="preview")
    {
     var chunk=gm.GetChunkGridPosition(gm.FindChunkById(e.ChunkId));var footprint=lpo.GetOccupiedCellsWithRotation(rotation);if(footprint.Count==0)throw new InvalidOperationException("设施占地为空。");
     foreach(var offset in footprint)
     {
      string key=(chunk.x*10+e.X+offset.x)+":"+(chunk.y*10+e.Y+offset.y);
      if(!plannedCells.TryGetValue(key,out var overlap))plannedCells[key]=overlap=new List<LayoutItem>();
      if(overlap.Any(v=>v.Layer==item.Layer||v.OverlapLayers.Contains(item.Layer)||item.OverlapLayers.Contains(v.Layer)))throw new InvalidOperationException("布局中设施相互重叠："+item.Name);
      overlap.Add(item);
     }
     CancelLayoutPreview();layoutIndex++;layoutStep=0;layoutAt=now;WriteLayoutStatus("running","已检查 "+layoutIndex+" / "+layoutStatus.Total);return true;
    }
    if(now-layoutAt<TimeSpan.FromMilliseconds(Math.Max(350,c.IntervalMs)).Ticks)return true;
    var group=(LifeObjectGroupTable)B.Invoke("Tables.Object",e.ObjectId);
    if(source==null&&(!(bool)B.Invoke("Layout.Unlocked",group.UnlockContentType,group.UnlockContentLevel)||!(bool)B.Invoke("Layout.CanBuild",group)))throw new InvalidOperationException("游戏当前不允许建造 "+item.Name+"；请检查库存、等级和容量。");
    var controller=B.Read("Layout.Controller",editor) as LifeChunkController;if(controller==null)throw new InvalidOperationException("领地建造控制器缺失。");
    var old=lpo.GetPlacementData();
    LocalStorage.WriteJsonAtomically(layoutJournal,new LayoutIntent{Account=layoutJob.Account,WorldId=layoutJob.Document.WorldId,Entry=e,Token=layoutJob.Token});
    layoutStep=3;layoutAt=now;layoutOwnPreview=false;lpo.HidePlacementPreview();gm.HideWaterCellOverlay();controller.DetachGridGuide();editor.SetActive(false);
    // Same native confirmation as the check button, without its automatic 'place another' callback.
    B.InvokeOn("Layout.Confirm",controller,lpo,old,cell,rotation,new Action(()=>{if(housing!=null)housing.RefreshBuildList();}),false);
    LocalStorage.Log("布局提交 "+layoutJob.Token+" "+e.Key);WriteLayoutStatus("running","已提交，等待服务器摆放回执");return true;
   }
   catch(Exception ex)
   {
    if(!active){LocalStorage.Log("layout read "+ex.GetBaseException().Message);return false;}
    try{if(layoutStep!=3)CancelLayoutPreview();}catch{}
    WriteLayoutStatus("error",ex.GetBaseException().Message);s.Reason=layoutStatus.Message;return true;
   }
  }
 }
}
