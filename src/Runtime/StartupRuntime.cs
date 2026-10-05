using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace BD2Daily.Runtime {
 internal sealed class StartupRuntime {
  private int attemptedTitle,attemptedDownload,downloadTitle;private string action="";
  private static object Field(string role,object obj){return ((FieldInfo)StartupBindings.Get(role)).GetValue(obj);}
  private static object Read(string role,object obj=null){return ((MethodInfo)StartupBindings.Get(role)).Invoke(obj,null);}
  internal void Tick(DailySnapshot s){
   var t=new StartupObservation{Supported=StartupBindings.Supported,Action=action};s.Startup=t;
   if(!t.Supported){t.BlockReason="本机启动页接口未匹配，请手动点击 TOUCH TO START";return;}
   try{
    var titles=UnityEngine.Object.FindObjectsOfType<IntroUI>().Where(x=>x!=null&&x.isActiveAndEnabled&&x.gameObject.activeInHierarchy).ToArray();
    if(titles.Length!=1){if(titles.Length>1)t.BlockReason="检测到多个启动页，等待界面稳定";return;}
    var title=titles[0];t.Visible=true;t.TitleInstanceId=title.GetInstanceID();t.Attempted=attemptedTitle==t.TitleInstanceId;
    t.Stage=Field("Title.State",title).ToString();var enter=(GameObject)Field("Title.Enter",title);
    t.Ready=t.Stage=="IntroWorkFinished"&&enter!=null&&enter.activeInHierarchy;
    t.ClickEnabled=(bool)Read("Title.Enabled",title);
    var downloads=UnityEngine.Object.FindObjectsOfType<DownloadPopupUI>().Where(u=>u!=null&&u.gameObject.activeInHierarchy).ToArray();
    DownloadPopupUI download=downloads.Length==1?downloads[0]:null;GameObject downloadButton=null;
    t.DownloadInProgress=downloadTitle==t.TitleInstanceId&&t.Stage=="CheckDownload";
    if(download!=null){
     downloadButton=(GameObject)Field("Download.Button",download);var selectable=downloadButton==null?null:downloadButton.GetComponent<UnityEngine.UI.Selectable>();
     t.DownloadVisible=true;t.DownloadInstanceId=download.GetInstanceID();t.DownloadAttempted=attemptedDownload==t.DownloadInstanceId;
     t.DownloadReady=downloadButton!=null&&downloadButton.activeInHierarchy&&selectable!=null&&selectable.isActiveAndEnabled&&selectable.IsInteractable();
    }
    var app=(AppManager)Read("Services.App");
    var modal=UnityEngine.Object.FindObjectsOfType<UIBase>().Where(u=>u!=null&&u.gameObject.activeInHierarchy&&(bool)Field("UI.Popup",u)).Select(u=>u.GetType().Name).ToList();
    if(UnityEngine.Object.FindObjectsOfType<IntroMessagePopupUI>().Any(u=>u.gameObject.activeInHierarchy))modal.Add("IntroMessagePopupUI");
    if(UnityEngine.Object.FindObjectsOfType<IntroExitPopupUI>().Any(u=>u.gameObject.activeInHierarchy))modal.Add("IntroExitPopupUI");
    if(download!=null)modal.Remove("DownloadPopupUI");
    if(modal.Count>0)t.BlockReason="请先处理启动页弹窗："+string.Join("、",modal.Distinct().ToArray());
    else if(app.IsActiveUILoading()||app.IsPlayAnimUILoading())t.BlockReason="等待游戏加载完成";
    if(string.IsNullOrEmpty(s.ErrorCode))s.State=t.Ready?"waiting_start":"starting_game";
    var permit=LocalStorage.Read<StartupPermit>("startup-permit.json");
    string decision=StartupPolicy.Decide(s,permit,DateTime.UtcNow.Ticks);
    if(decision=="wrong_account"){t.BlockReason="启动页账号与目标账号不同，已阻止自动进入";return;}
    if(decision=="click_download"){
     GuildStore.Write(Path.Combine(LocalStorage.Root,"startup","last-download.json"),new StartupRecord{Owner=permit.Owner,AtUtcTicks=DateTime.UtcNow.Ticks,Before=s,Decision=decision});
     if(StartupPolicy.Decide(s,LocalStorage.Read<StartupPermit>("startup-permit.json"),DateTime.UtcNow.Ticks)!="click_download")return;
     attemptedDownload=t.DownloadInstanceId;downloadTitle=t.TitleInstanceId;t.DownloadAttempted=true;t.DownloadInProgress=true;
     action="已开始下载游戏资源，等待下载和解压完成";t.Action=action;
     download.OnClickUI(downloadButton);return;
    }
    if(t.Ready&&downloadTitle==t.TitleInstanceId&&decision!="click_start")t.Action="游戏资源已就绪，等待自动进入";
    if(decision!="click_start")return;
    // A persistent record precedes the ordinary UI event; the title instance is never clicked twice.
    var record=new StartupRecord{Owner=permit.Owner,AtUtcTicks=DateTime.UtcNow.Ticks,Before=s,Decision=decision};
    GuildStore.Write(Path.Combine(LocalStorage.Root,"startup","last-entry.json"),record);
    if(StartupPolicy.Decide(s,LocalStorage.Read<StartupPermit>("startup-permit.json"),DateTime.UtcNow.Ticks)!="click_start")return;
    attemptedTitle=t.TitleInstanceId;t.Attempted=true;action="已点击 TOUCH TO START，等待进入游戏";t.Action=action;
    ((MethodInfo)StartupBindings.Get("Title.Click")).Invoke(title,null);
   }catch(Exception e){t.BlockReason="启动页读取／操作异常："+e.GetBaseException().Message;}
  }
 }
}
