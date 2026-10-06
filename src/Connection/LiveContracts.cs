#nullable disable
using System;
using System.Linq;
using System.Runtime.Serialization;
namespace BD2Daily.Live {
 public static class LiveProtocol {
  public const int BridgeVersion=DailyBridgeVersion.Current;
  public static bool IsSquareScene(string scene){return scene!=null&&scene.StartsWith("Map3009_",StringComparison.Ordinal);}
  public static readonly string[] MissionQuerySurfaces=new[]{"MenuUI","PassUI","MissionUI","EventUI","HuntOrAirwayUI","GameFieldDefaultUI","CafeteriaFieldDefaultUI","AvatarLifeGameFieldDefaultUI","AvatarFishingHarborUI","FishingGameFieldDefaultUI","AvatarFishingWorldMapUI","TotalWarUI","MyRoomUI","MiniGameHubUI","SichuanMainUI","SichuanStagePopupUI","SichuanBoardUI","SichuanStageClearPopupUI"};
  // StorySkipUI is an embedded toolbar, not a modal UIBase popup. Its native
  // confirmation is a separate StoryPopupUI and must still block input below it.
  public static Surface StoryToolbar(int id,string path,int order,bool ready,Target[] targets){
   return new Surface{Id=id,Type="StorySkipUI",NativeContext="story_skip",Path=path,Popup=false,Order=order,InputReady=ready,Targets=targets};
  }
  public static bool TransitRequiresStealth(int pack,bool requested){return requested||pack==14||pack==1003;}
  public static double NavigationSeconds(double distance){return double.IsNaN(distance)||double.IsInfinity(distance)||distance<0?60:Math.Max(60,Math.Min(300,distance/1.2+20));}
  public const string LiveEntries="runtime.json|snapshot.json|error.json|attached.json|performance.json|command.json|pause|legacy-observation|evidence-config.json|observation-request.json|evidence.json|dice-lease.json|dispatch-recovery.json|events~*|receipts~*";
  public static bool Ready(Frame f,int processId,long startTicks,long now,string previousInstance=null){
   return f!=null&&f.Protocol==1&&f.BridgeVersion==BridgeVersion&&f.ProcessId==processId&&f.ProcessStartTicks==startTicks&&f.AtUtcTicks>now-TimeSpan.FromSeconds(3).Ticks&&f.AtUtcTicks<=now+TimeSpan.FromSeconds(2).Ticks&&!string.IsNullOrEmpty(f.Instance)&&string.IsNullOrEmpty(f.Error)&&(previousInstance==null||f.Instance!=previousInstance);
  }
 }
 [DataContract] public sealed class Surface {
  [DataMember] public int Id;
  [DataMember] public string Type="",Path="";
  [DataMember] public bool Popup;
  [DataMember] public bool InputReady=true;
  [DataMember] public int Order;
  [DataMember] public string NoticeSuppression="none";
  [DataMember] public string NativeContext="";
  [DataMember] public Target[] Targets=new Target[0];
  [DataMember] public string[] Text=new string[0];
 }
 [DataContract] public sealed class Target {
  [DataMember] public int Id;
  [DataMember] public string Field="",Path="",Route="ui";
  [DataMember] public bool Enabled;
 }
 [DataContract] public sealed class Frame {
  [DataMember] public int Protocol=1,BridgeVersion=LiveProtocol.BridgeVersion,ProcessId;
  [DataMember] public long ProcessStartTicks,AtUtcTicks,Sequence;
  [DataMember] public string Instance="",Scene="",AccountKey="",PlayerKey="",UiToken="",Error="";
  [DataMember] public SquareRouteStatus SquareNavigation;
  [DataMember] public bool PluginAvailable;
  [DataMember] public string PluginFingerprint="";
  [DataMember] public Surface[] Surfaces=new Surface[0];
 }
 [DataContract] public sealed class SquareRouteStatus {
  [DataMember] public string State="idle",Reason="",Command="",Kind="",Geometry="";
  [DataMember] public float X,Y,Z,GoalX,GoalY,GoalZ;
  [DataMember] public int Plans,Expanded,PathPoints,Waypoint;
  [DataMember] public long Started,Expires,FloorHits,FloorMisses;
  [DataMember] public double Remaining,Travelled,PlanMs,MaxSliceMs;
  [DataMember] public SquareLandmark[] Landmarks=new SquareLandmark[0];
 }
 [DataContract] public sealed class SquareLandmark {
  [DataMember] public string Name;
  [DataMember] public int Id;
  [DataMember] public float X,Y,Z;
 }
 [DataContract] public sealed class Command {
  [DataMember] public string Scope="gameplay",Id="",Kind="",Instance="",AccountKey="",PlayerKey="",Scene="",UiToken="",Reason="";
  [DataMember] public int ProcessId,SurfaceId,TargetId,Value;
  [DataMember] public bool RequireStealth;
  [DataMember] public long[] Items=new long[0];
  [DataMember] public long ProcessStartTicks,ExpiresUtcTicks,ObservedUtcTicks;
 }
 [DataContract] public sealed class Receipt {
  [DataMember] public Command Command;
  [DataMember] public string State="prepared",Error="";
  [DataMember] public bool MayHaveDispatched;
  [DataMember] public long AtUtcTicks;
  [DataMember] public Frame Before,After;
 }
 public static class LivePolicy {
  public static readonly string[] PassiveSurfaces={"NoticeUI","CharNoticeUI","DetailNoticeUI","CurrencyManageUI","OverheadManageUI"};
  public static bool HiddenPickupReady(bool active,double remaining){return active&&!double.IsNaN(remaining)&&!double.IsInfinity(remaining)&&remaining>1;}
  public static bool PassiveSurface(string type){return PassiveSurfaces.Contains(type);}
  // Passive HUD/mission notices remain observable, but never invalidate an input.
  // All managed stages and native dispatch gates consume this same token.
  public static string BuildUiToken(Frame f){
   var key=f.Scene+"|"+string.Join("|",f.Surfaces.Where(s=>!PassiveSurface(s.Type)).OrderBy(s=>s.Id).Select(s=>s.Id+":"+s.Order+":"+s.NoticeSuppression+":"+s.NativeContext+":"+string.Join(",",s.Targets.OrderBy(t=>t.Id).ThenBy(t=>t.Field,StringComparer.Ordinal).ThenBy(t=>t.Route,StringComparer.Ordinal).Select(t=>t.Id+":"+t.Enabled))).ToArray());
   using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-","");
  }
  public static bool PassDefinitionReady(int passId,System.Collections.Generic.IEnumerable<int> active){return passId>0&&active!=null&&active.Contains(passId);}
  public static bool SuppressibleNotice(string type){return new[]{"NewsPopupEventUI","PackagePopupUI","AttendanceSpecialPackageUI","BundlePackageUI","BundleGroupPackageUI","BundleGroupRelayPackageUI","BonusBundleGroupPackageUI","ClearPackageUI","LoginPassPackageUI","SkinPackageUI"}.Contains(type);}
  public static bool GameplayReady(Frame f){return f!=null&&string.IsNullOrEmpty(f.Error)&&!string.IsNullOrEmpty(f.AccountKey)&&!string.IsNullOrEmpty(f.PlayerKey)&&f.Scene!="ReGame"&&!f.Surfaces.Any(u=>u.Type=="DownloadPopupUI"||u.Type=="IntroUI");}
  private static string StartupTarget(Command c,Frame f){
   if((f.Scene!="ReGame"&&f.Scene!="Splash")||c.Kind!="click")return "startup_scope_rejected";
   if(c.AccountKey!=f.AccountKey||c.PlayerKey!=f.PlayerKey)return "identity_changed";
   bool catalogRetry=f.BridgeVersion>=30&&f.Surfaces.Count(x=>x.Type=="MessagePopupUI")==1&&f.Surfaces.Any(x=>x.Type=="MessagePopupUI"&&x.Text.Any(text=>text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Contains("CLIENT_LOGIC_ERROR")&&text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Any(code=>code=="BUNDLE_CATALOG_CHECK"||(f.BridgeVersion>=31&&code=="BUNDLE_COMMON"))));
   if(!catalogRetry&&!f.Surfaces.Any(u=>u.Type=="DownloadPopupUI"))return "download_ui_missing";
   var u=f.Surfaces.SingleOrDefault(x=>x.Id==c.SurfaceId);if(u==null)return "surface_missing";
   var target=u.Targets.SingleOrDefault(t=>t.Id==c.TargetId&&t.Enabled&&t.Route=="ui");
   if(target==null)return "target_unavailable";
   bool download=u.Type=="DownloadPopupUI"&&target.Field=="_btnDownload";
   bool dismiss=u.Type=="MessagePopupUI"&&target.Field=="_buttonOK"&&u.Text.Any(t=>t=="Data not found exception. (common, id:0) - db is not loaded");
   bool retry=catalogRetry&&u.Type=="MessagePopupUI"&&target.Field=="_buttonOK";
   if(!download&&!dismiss&&!retry)return "startup_target_rejected";
   if(f.Surfaces.Any(x=>x.Popup&&x.Id!=u.Id&&!PassiveSurface(x.Type)&&x.Order>=u.Order))return "startup_popup_blocked";
   return "";
  }
  public static bool WeeklyNpcKind(string k){return new[]{"weekly_npc_board_stop","weekly_npc_board_nav","weekly_npc_board_open","weekly_npc_board_scroll","weekly_npc_board_select","weekly_npc_query","weekly_npc_accept","weekly_npc_nav","weekly_npc_auto","weekly_npc_talk","weekly_npc_interact","weekly_npc_carry_nav","weekly_npc_lift"}.Contains(k);}
  public static bool QuizKind(string k){return new[]{"quiz_hub","quiz_list","quiz_play","quiz_talk","quiz_leave"}.Contains(k);}
  public static bool RewardKind(string k){return new[]{"reward_select","reward_tab","reward_action"}.Contains(k);}
  public static bool TacticsKind(string k){return new[]{"tactics_select","tactics_priority","tactics_action_open","tactics_burst","tactics_action","tactics_move","tactics_order","tactics_start"}.Contains(k);}
  public static bool ExtensionKind(string k){return TacticsKind(k)||(EventKind(k)&&k!="event_sweep_select"&&k!="event_sweep_confirm");}
  public static bool EventKind(string k){return new[]{"event_select","event_sweep_select","event_sweep_confirm","event_action_open","event_action","event_move","event_order","event_start","event_bench","event_replace"}.Contains(k);}
  public static bool TradeKind(string k){return new[]{"trade_menu","trade_talent","trade_favorite_preview","trade_favorite_confirm","trade_buy_preview","trade_buy_confirm","trade_sell_preview","trade_sell_confirm","trade_cook_preview","trade_cook_quantity","trade_cook_confirm"}.Contains(k);}
  public static bool PowderKind(string kind){return kind=="powder_recipe"||kind=="powder_options";}
  public static bool PreviewKind(string kind){return new[]{"equipment_upgrade_preview","equipment_break_preview","equipment_refine_preview","equipment_craft_menu","equipment_craft_preview","mail_tab","square_goddess_nav","square_shop_nav","square_shop_interact","square_ranking_nav","square_goddess_interact","square_cancel_nav","square_route_probe"}.Contains(kind);}
  public static string Gate(Command c,Frame f,long now,bool otherOwner){
   Guid id;
   if(c==null||c.Id==null||c.Id.Length!=32||!Guid.TryParseExact(c.Id,"N",out id))return "invalid_command_id";
   if(c.Kind!="talent_error_ack"&&c.Kind!="story_advance"&&c.Kind!="story_skip"&&c.Kind!="notice_suppress"&&c.Kind!="click"&&c.Kind!="pointer"&&c.Kind!="back"&&c.Kind!="detach"&&c.Kind!="mirror_ready"&&c.Kind!="native_click"&&c.Kind!="dispatch_recover"&&c.Kind!="dispatch_collect_all"&&c.Kind!="dispatch_menu"&&c.Kind!="mainline_menu"&&c.Kind!="mainline_pack"&&c.Kind!="sichuan_open"&&c.Kind!="sichuan_select"&&c.Kind!="route_probe"&&c.Kind!="mainline_walk"&&c.Kind!="mainline_reposition"&&c.Kind!="mainline_cancel_nav"&&c.Kind!="mainline_approach"&&c.Kind!="mainline_interact"&&c.Kind!="mainline_talent"&&c.Kind!="collection_query_steal"&&c.Kind!="weekly_steal_talk"&&c.Kind!="weekly_steal_menu"&&c.Kind!="weekly_steal_talent"&&c.Kind!="weekly_steal_confirm"&&c.Kind!="collection_query"&&c.Kind!="weekly_book_lobby"&&c.Kind!="weekly_book_open"&&c.Kind!="monster_open"&&c.Kind!="friendship_select"&&c.Kind!="friendship_complete"&&c.Kind!="reward_refresh"&&c.Kind!="dice_query"&&c.Kind!="pass_init"&&c.Kind!="pass_select"&&c.Kind!="equipment_refine_batch"&&!PowderKind(c.Kind)&&!PreviewKind(c.Kind)&&!TradeKind(c.Kind)&&!EventKind(c.Kind)&&!RewardKind(c.Kind)&&!QuizKind(c.Kind)&&!TacticsKind(c.Kind)&&!WeeklyNpcKind(c.Kind))return "unsupported_command";
   if(c.RequireStealth&&c.Kind!="mainline_walk"&&c.Kind!="mainline_approach"&&c.Kind!="mainline_reposition")return "stealth_scope_rejected";
   bool startup=c.Scope=="startup_download";
   if(c.Scope!="gameplay"&&!startup)return "unsupported_scope";
   if(f==null||f.Protocol!=1||(f.Error.Length>0&&(!startup||f.Error!="Waiting for fresh account identity")))return "observation_unavailable";
   if(ExtensionKind(c.Kind)&&!f.PluginAvailable)return "plugin_unavailable";
   if(!f.PluginAvailable&&((c.Kind=="event_sweep_select"&&(c.Items==null||c.Items.Length!=3||c.Items[2]!=15))||(c.Kind=="event_sweep_confirm"&&(c.Items==null||c.Items.Length!=4||c.Items[2]!=15||c.Value>5))))return "public_sweep_challenge15_only";
   if(c.ExpiresUtcTicks<=now||c.ExpiresUtcTicks>now+TimeSpan.FromSeconds(15).Ticks||c.ObservedUtcTicks<now-TimeSpan.FromSeconds(15).Ticks||c.ObservedUtcTicks>now+TimeSpan.FromSeconds(2).Ticks)return "expired_observation";
   if(f.AtUtcTicks<now-TimeSpan.FromSeconds(3).Ticks||f.AtUtcTicks>now+TimeSpan.FromSeconds(2).Ticks)return "stale_frame";
   if(c.ProcessId!=f.ProcessId||c.ProcessStartTicks!=f.ProcessStartTicks||string.IsNullOrEmpty(c.Instance)||c.Instance!=f.Instance)return "process_changed";
   if(!startup&&(string.IsNullOrEmpty(c.AccountKey)||string.IsNullOrEmpty(c.PlayerKey)||c.AccountKey!=f.AccountKey||c.PlayerKey!=f.PlayerKey))return "identity_changed";
   if(c.Scene!=f.Scene||c.UiToken!=f.UiToken)return "screen_changed";
   if(otherOwner)return "another_operation_owns_game";
   if(startup)return StartupTarget(c,f);
   if(c.Kind=="detach")return "";
   var ui=f.Surfaces.SingleOrDefault(u=>u.Id==c.SurfaceId);
   if(ui==null)return "surface_missing";
   bool gachaSkip=f.BridgeVersion>=32&&ui.Type=="GachaResultUI"&&c.Kind=="click"&&ui.Targets.Any(t=>t.Id==c.TargetId&&t.Field=="_objSkipButton"&&t.Enabled&&t.Route=="ui")&&!f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)&&x.Order>=ui.Order);
   if(f.BridgeVersion>=9&&!ui.InputReady&&!gachaSkip)return "ui_not_ready";
   // The managed journal supplies the rejection proof; re-check the exact acknowledgement context at native dispatch.
   if(c.Reason!=null&&c.Reason.StartsWith("home-reset-proof|112003|",StringComparison.Ordinal)){
    if(c.Kind!="click"||c.Items==null||c.Items.Length!=0||c.Value!=0||!GameplayReady(f)||ui.Type!="MessagePopupUI"||ui.Path==null||ui.Path.IndexOf("ErrorMessagePopupUI",StringComparison.Ordinal)<0
       ||ui.Text==null||!ui.Text.Any(text=>System.Text.RegularExpressions.Regex.IsMatch(text,@"error\s*:\s*112003(?:\D|$)",System.Text.RegularExpressions.RegexOptions.CultureInvariant))
       ||!ui.Targets.Any(t=>t.Id==c.TargetId&&t.Field=="_buttonOK"&&t.Enabled&&t.Route=="ui")||ui.Targets.Any(t=>t.Field=="_buttonCancel"&&t.Enabled)
       ||f.Surfaces.Any(x=>x.Type.StartsWith("BattleUI",StringComparison.Ordinal)||x.Id!=ui.Id&&x.Popup&&!PassiveSurface(x.Type)&&x.Order>=ui.Order))return "mission_reset_context_rejected";
   }
   if(c.Kind=="back"&&SuppressibleNotice(ui.Type)&&(ui.NoticeSuppression=="unchecked"||ui.NoticeSuppression=="loading"))return "notice_checkbox_not_confirmed";
   if(c.Kind=="story_advance"){
    if(f.BridgeVersion<87||!GameplayReady(f)||ui.Type!="ScriptUI"||ui.NativeContext!="story_dialogue"||c.TargetId!=0||c.Value!=0||c.Items==null||c.Items.Length!=0||!ui.Targets.Any(t=>t.Enabled&&(t.Field=="_buttonStart"||t.Field=="_objButtonTouch")))return "story_advance_not_available";
    return f.Surfaces.Any(x=>x.Id!=ui.Id&&!PassiveSurface(x.Type)&&x.Type!="GameFieldDefaultUI"&&!(x.Type=="StorySkipUI"&&x.NativeContext=="story_skip"&&!x.Popup))?"story_popup_blocked":"";
   }
   if(c.Kind=="story_skip"){
    if(f.BridgeVersion<86||!GameplayReady(f)||ui.Type!="StorySkipUI"||ui.NativeContext!="story_skip"||c.TargetId!=0||c.Value!=0||c.Items==null||c.Items.Length!=0||!ui.Targets.Any(t=>t.Field=="_objSkipButton"&&t.Enabled))return "story_skip_not_available";
    return f.Surfaces.Any(x=>x.Id!=ui.Id&&!PassiveSurface(x.Type)&&x.Type!="GameFieldDefaultUI"&&!(x.Type=="ScriptUI"&&x.NativeContext=="story_dialogue")&&!(f.BridgeVersion>=102&&x.Type=="BalloonScriptUI"&&!x.Popup&&!string.IsNullOrEmpty(x.Path)&&ui.Path==x.Path+"/StorySkipUI"))?"story_popup_blocked":"";
   }
   if(c.Kind=="notice_suppress"){
    if(f.BridgeVersion<67||!GameplayReady(f)||!SuppressibleNotice(ui.Type)||c.TargetId!=0||c.Value!=0||c.Items==null||c.Items.Length!=0||ui.NoticeSuppression!="unchecked")return "notice_checkbox_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type))?"notice_popup_blocked":"";
   }
   if(WeeklyNpcKind(c.Kind)){
    bool interact=c.Kind=="weekly_npc_interact"||c.Kind=="weekly_npc_carry_nav"||c.Kind=="weekly_npc_lift";
    bool option=c.Kind=="weekly_npc_accept"||c.Kind=="weekly_npc_nav"||c.Kind=="weekly_npc_auto";
    bool board=c.Kind.StartsWith("weekly_npc_board_");string expected=c.Kind=="weekly_npc_accept"?"QuestPopupUI":c.Kind=="weekly_npc_board_scroll"||c.Kind=="weekly_npc_board_select"?"QuestBoardUI":c.Kind=="weekly_npc_query"&&ui.Type=="QuestBoardUI"?"QuestBoardUI":c.Kind=="weekly_npc_talk"?"BalloonScriptUI":c.Kind=="weekly_npc_auto"?"BattleUI_FieldBattle":"GameFieldDefaultUI";
    if((board||c.Kind=="weekly_npc_accept"||expected=="QuestBoardUI")&&f.BridgeVersion<107)return "weekly_npc_board_bridge_required";
    if(c.Kind=="weekly_npc_accept"&&(ui.NativeContext!="weekly_npc_accept:"+c.Value||!f.Surfaces.Any(x=>x.Type=="QuestBoardUI"&&x.NativeContext=="weekly_npc_board")))return "weekly_npc_board_required";
    if(f.BridgeVersion<100||!GameplayReady(f)||ui.Type!=expected||c.Value<=0||c.TargetId!=0||c.Items==null||c.Items.Length!=((option||interact)?1:0)||option&&(c.Items[0]<0||c.Items[0]>1)||interact&&(f.BridgeVersion<102||c.Items[0]==0))return "weekly_npc_context_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)&&!(c.Kind=="weekly_npc_accept"&&x.Type=="QuestBoardUI"&&x.NativeContext=="weekly_npc_board"&&x.Order<=ui.Order))?"weekly_npc_popup_blocked":"";
   }
   if(c.Kind=="talent_error_ack"){
    if(f.BridgeVersion<101||!GameplayReady(f)||ui.Type!="MessagePopupUI"||ui.NativeContext!="talent_inactive_ack_only"||c.TargetId!=0||c.Value!=100005||!ui.InputReady)return "talent_error_context_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type))?"talent_error_popup_blocked":"";
   }
   if(c.Kind.StartsWith("weekly_steal_")){
    string expected=c.Kind=="weekly_steal_talk"?"BalloonScriptUI":c.Kind=="weekly_steal_menu"?"GameFieldDefaultUI":c.Kind=="weekly_steal_talent"?"QuickMenuUI":"StealInfoPopupUI";
    int count=c.Kind=="weekly_steal_talk"?0:c.Kind=="weekly_steal_menu"?0:c.Kind=="weekly_steal_talent"?2:1;
    if(f.BridgeVersion<98||!GameplayReady(f)||ui.Type!=expected||c.TargetId!=0||c.Value==0||c.Items==null||c.Items.Length!=count||c.Items.Any(x=>x<=0||x>int.MaxValue))return "weekly_steal_context_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type))?"weekly_steal_popup_blocked":"";
   }
   if(c.Kind=="collection_query"||c.Kind=="collection_query_steal"){
    if(c.Kind=="collection_query_steal"&&f.BridgeVersion<98)return "weekly_steal_bridge_required";
    if(f.BridgeVersion<63||!GameplayReady(f)||ui.Type!="GameFieldDefaultUI"||c.TargetId!=0||c.Value<=0||c.Items==null||c.Items.Length<1||c.Items.Length>30||c.Items.Any(x=>x<=0||x>int.MaxValue)||c.Items.Distinct().Count()!=c.Items.Length)return "collection_query_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type))?"collection_popup_blocked":"";
   }
   if(c.Kind=="friendship_select"||c.Kind=="friendship_complete"){
    if(c.Kind=="friendship_complete"&&ui.Type!="FriendshipManageUI")return "friendship_surface_changed";
    if(f.BridgeVersion<68||!GameplayReady(f)||c.TargetId!=0||c.Value<=0||c.Items==null||c.Items.Length!=0||!(ui.Type=="MenuUI"||ui.Type=="FriendshipUI"||ui.Type=="FriendshipManageUI"))return "friendship_context_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)&&x.Order>=ui.Order)?"friendship_popup_blocked":"";
   }
   if(c.Kind=="weekly_book_open"||c.Kind=="weekly_book_lobby"){
    if(f.BridgeVersion<73||!GameplayReady(f)||ui.Type!=(c.Kind=="weekly_book_open"?"MenuUI":"GameFieldDefaultUI")||c.TargetId!=0||c.Value!=0||c.Items==null||c.Items.Length!=0)return "weekly_book_context_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type))?"weekly_book_popup_blocked":"";
   }
   if(c.Kind=="monster_open"){
    if(f.BridgeVersion<28||c.Value<0||c.Value>1||(c.Value==1&&f.BridgeVersion<68)||c.Items==null||c.Items.Length!=0||ui.Type!="MenuUI"||c.TargetId!=0||!GameplayReady(f))return "monster_context_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)))return "monster_popup_blocked";
    return "";
   }
   if(QuizKind(c.Kind)){
    if(f.BridgeVersion<47||!GameplayReady(f)||c.TargetId!=0||c.Value<0||c.Items==null)return "quiz_context_rejected";
    var expected=c.Kind=="quiz_hub"?"MenuUI":c.Kind=="quiz_list"||c.Kind=="quiz_leave"?"MiniEventMainUI":c.Kind=="quiz_play"?"MiniEventQuizUI":"BalloonScriptUI";
    if(ui.Type!=expected)return "quiz_surface_changed";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type)))return "quiz_popup_blocked";
    return c.Kind=="quiz_play"||c.Kind=="quiz_talk" ? (c.Items.Length==3&&c.Items.All(x=>x>0)?"":"quiz_identity_invalid") : (c.Items.Length==0?"":"quiz_identity_invalid");
   }
   if(RewardKind(c.Kind)){
    if(f.BridgeVersion<46||!GameplayReady(f)||ui.Type!="EventUI"||c.TargetId!=0||c.Value<0||c.Items==null)return "reward_context_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)))return "reward_popup_blocked";
    if(c.Kind=="reward_select")return c.Value>0&&c.Items.Length==0?"":"reward_selection_invalid";
    if(c.Items.Length!=1||c.Items[0]<=0)return "reward_selection_invalid";
    return c.Kind=="reward_tab" ? (c.Value<100?"":"reward_tab_invalid") : (c.Value>=1&&c.Value<=(f.BridgeVersion>=105?8:f.BridgeVersion>=47?7:5)?"":"reward_action_invalid");
   }
   if(TacticsKind(c.Kind)){
    if(f.BridgeVersion<57||!GameplayReady(f)||c.TargetId!=0||c.Items==null)return "tactics_context_rejected";
    if(f.Surfaces.Any(x=>x.Type=="BattleResultUI"||(x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type))))return "tactics_popup_blocked";
    if(c.Kind=="tactics_select")return ui.Type=="TarosTacticsBingoUI"&&c.Value>0&&c.Value<=25&&c.Items.Length==2&&c.Items.All(x=>x>0)?"":"tactics_selection_invalid";
    if(ui.Type!="BattleUI_TarosTactics"||c.Value<1||c.Value>99||c.Value%2!=1||c.Items.Length<3||c.Items[0]<=0)return "tactics_turn_rejected";
    if(c.Kind=="tactics_priority")return f.BridgeVersion>=77&&c.Value==1&&c.Items.Length==3&&c.Items[1]!=0&&(c.Items[2]==0||c.Items[2]==1)?"":"tactics_priority_invalid";
    if(c.Kind=="tactics_start")return c.Items.Length>=6&&c.Items.Length<=61&&(c.Items.Length-1)%5==0?"":"tactics_plan_invalid";
    return c.Items.Length==3&&c.Items[1]!=0&&c.Items[2]>=(c.Kind=="tactics_action"?-1:0)?"":"tactics_plan_invalid";
   }
   if(c.Kind=="event_sweep_confirm"){
    if(f.BridgeVersion<97||!GameplayReady(f)||ui.Type!="BattleSkipPopupUI"||c.TargetId!=0||c.Items==null||c.Items.Length!=4||c.Items.Any(x=>x<=0)||c.Value<1)return "event_sweep_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type)))return "event_popup_blocked";
    return "";
   }
   if(c.Kind=="event_select"||c.Kind=="event_sweep_select"){
    if(c.Kind=="event_sweep_select"&&(f.BridgeVersion<97||c.Value<0||c.Value>1))return "event_sweep_selection_invalid";
    if(f.BridgeVersion<41||!GameplayReady(f)||ui.Type!="EventBattleUI"||c.TargetId!=0||c.Items==null||c.Items.Length!=3||c.Items.Any(x=>x<=0))return "event_lobby_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)))return "event_popup_blocked";
    return "";
   }
   if(EventKind(c.Kind)){
    if(f.BridgeVersion<38||!GameplayReady(f)||ui.Type!="BattleUI_EventBattle"||c.TargetId!=0||c.Value<1||c.Value>99||c.Value%2!=1)return "event_context_rejected";
    if(f.Surfaces.Any(x=>x.Type=="BattleResultUI"||(x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type))))return "event_popup_blocked";
    if(c.Items==null||c.Items.Length<3||c.Items[0]<=0)return "event_plan_missing";
    if(c.Kind=="event_start")return c.Items.Length>=5&&c.Items.Length<=21&&(c.Items.Length-1)%4==0?"":"event_plan_invalid";
    return c.Items.Length==3&&c.Items[1]>0&&c.Items[2]>=0?"":"event_selection_invalid";
   }
   if(TradeKind(c.Kind)){
    if(f.BridgeVersion<25||!GameplayReady(f)||c.TargetId!=0)return "trade_context_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type)))return "trade_popup_blocked";
    string expected=c.Kind=="trade_menu"?"GameFieldDefaultUI":c.Kind=="trade_talent"?"QuickMenuUI":c.Kind=="trade_favorite_confirm"?"BuyFavoritePopupUI":c.Kind=="trade_buy_confirm"||c.Kind=="trade_sell_confirm"?"ShopPopupUI":c.Kind=="trade_cook_preview"?"CookingSelectUI":c.Kind.StartsWith("trade_cook_")?"CookingUI":"ShopUI";
    if(ui.Type!=expected||c.Items==null||c.Value==0)return "trade_surface_rejected";
    if(c.Kind=="trade_menu")return c.Value==7?"":"trade_talent_rejected";
    if(c.Kind=="trade_talent")return "";
    if(c.Value<0)return "trade_value_rejected";
    int n=c.Items.Length;
    if(c.Kind.StartsWith("trade_favorite_"))return n>=4&&n<=1600&&n%4==0&&c.Items.All(x=>x>0)?"":"trade_selection_rejected";
    if(c.Kind.StartsWith("trade_buy_"))return n==4&&c.Items.All(x=>x>0)?"":"trade_selection_rejected";
    if(c.Kind.StartsWith("trade_sell_"))return n==5&&c.Items.All(x=>x>0)?"":"trade_selection_rejected";
    if(c.Kind=="trade_cook_confirm")return n==2&&c.Items.All(x=>x>0)?"":"trade_selection_rejected";
    return n==0?"":"trade_selection_rejected";
   }
   if(c.Kind=="equipment_refine_batch"){
    if(f.BridgeVersion<61||!GameplayReady(f)||c.TargetId!=0||ui.Type!="EquipmentUpgradeUI")return "refine_batch_context_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type)))return "refine_batch_popup_blocked";
    // instance, count, maximum gold and powder for this native batch.
    return c.Value>=1&&c.Value<=24&&c.Items!=null&&c.Items.Length==4&&c.Items.All(x=>x>0)&&c.Items[1]<=5000&&c.Items[2]<=2000000000&&c.Items[3]<=2000000000?"":"refine_batch_budget_rejected";
   }
   if(PowderKind(c.Kind)){
    if(f.BridgeVersion<60||!GameplayReady(f)||c.TargetId!=0||c.Items==null)return "powder_context_rejected";
    var expected=c.Kind=="powder_recipe"?"EquipmentMakingSelectUI":"EquipmentUpgradePopupUI";
    if(ui.Type!=expected||f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type)))return "powder_surface_rejected";
    if(c.Kind=="powder_recipe")return c.Value>=1&&c.Value<=10&&c.Items.Length==0?"":"powder_recipe_rejected";
    return c.Value>=1&&c.Value<=9&&c.Items.Length==1&&c.Items[0]>=1&&c.Items[0]<=int.MaxValue?"":"powder_options_rejected";
   }
   if(PreviewKind(c.Kind)){
    if(f.BridgeVersion<11||!GameplayReady(f)||c.TargetId!=0)return "preview_context_rejected";
    if(c.Kind.StartsWith("square_")){
     if(f.BridgeVersion<12||ui.Type!="GameFieldDefaultUI"||!f.Scene.StartsWith("Map3009_")||f.Surfaces.Any(x=>x.Popup&&!PassiveSurface(x.Type)))return "square_context_rejected";
     if(c.Kind=="square_route_probe")return f.BridgeVersion>=106&&c.Value==0&&c.Items!=null&&c.Items.Length==3&&c.Items.All(x=>x>=-200000&&x<=200000)?"":"square_probe_rejected";
     if(c.Kind.StartsWith("square_shop_"))return f.BridgeVersion>=35&&c.Value==0?"":"square_merchant_rejected";
       return c.Value>=0&&c.Value<=100?"":"square_target_rejected";
    }
    var expected=c.Kind=="mail_tab"?"MailUI":c.Kind=="equipment_craft_menu"?"MenuUI":c.Kind=="equipment_craft_preview"?"EquipmentMakingSelectUI":"InventoryManageUI";
    if(ui.Type!=expected||f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type)))return "preview_surface_rejected";
    if(c.Kind=="mail_tab")return c.Value>=0&&c.Value<=1?"":"mail_tab_rejected";
    if(c.Kind=="equipment_craft_menu"||c.Kind=="equipment_craft_preview")return "";
    if(c.Items==null||c.Items.Length==0||c.Items.Length>30||c.Items.Any(id=>id<=0)||c.Items.Distinct().Count()!=c.Items.Length)return "equipment_selection_rejected";
    if(c.Kind=="equipment_refine_preview"&&c.Items.Length!=1)return "refine_selection_rejected";
    return "";
   }
   if(c.Kind=="reward_refresh"||c.Kind=="dice_query"){
    if(f.Surfaces.Any(x=>x.Type.StartsWith("BattleUI")||x.Type=="BattleResultUI"))return "reward_query_battle_blocked";
    if(f.BridgeVersion<93||!GameplayReady(f)||c.TargetId!=0||c.Items==null||c.Items.Length!=0||c.Value!=0)return "reward_query_rejected";
    if(c.Kind=="dice_query"?ui.Type!="EventUI":!(LiveProtocol.MissionQuerySurfaces.Contains(ui.Type)))return "reward_query_context_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)&&x.Order>=ui.Order)?"reward_query_popup_blocked":"";
   }
   if(c.Kind=="pass_select"){
    if(f.BridgeVersion<74||!GameplayReady(f)||ui.Type!="PassUI"||c.TargetId!=0||c.Value<=0||c.Items==null||c.Items.Length!=0)return "pass_selection_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)&&x.Order>=ui.Order)?"pass_popup_blocked":"";
   }
   if(c.Kind=="pass_init"){
    if(f.BridgeVersion<10||ui.Type!="PassUI"||c.TargetId!=0)return "pass_initialization_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)&&x.Order>=ui.Order))return "pass_popup_blocked";
    return "";
   }
   if(c.Kind=="mirror_ready"){
    if(f.Scene!="Map3001_001"||ui.Type!="GameFieldDefaultUI"||c.TargetId!=0)return "mirror_entry_context_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&!PassiveSurface(x.Type)))return "mirror_entry_popup_blocked";
    return "";
   }
   if(c.Kind=="mainline_talent"){
    if(f.BridgeVersion<21||!GameplayReady(f)||ui.Type!="QuickMenuUI"||c.TargetId!=0||c.Value==0)return "mainline_talent_context_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type)))return "mainline_popup_blocked";
    return "";
   }
   if(c.Kind=="mainline_cancel_nav"||c.Kind=="mainline_approach"){
    if(!GameplayReady(f)||f.BridgeVersion<71||ui.Type!="GameFieldDefaultUI"||c.TargetId!=0||c.Items==null||c.Items.Length!=0||(c.Kind=="mainline_approach"?c.Value==0:c.Value!=0))return "mainline_approach_context_rejected";
    return f.Surfaces.Any(x=>x.Id!=ui.Id&&!PassiveSurface(x.Type))?"mainline_popup_blocked":"";
   }
   if(c.Kind=="sichuan_open"||c.Kind=="sichuan_select"){
    if(!GameplayReady(f)||f.BridgeVersion<80||c.TargetId!=0||c.Value<1||c.Items==null)return "sichuan_context_rejected";
    if(c.Kind=="sichuan_open"&&(ui.Type!="MiniGameHubUI"||c.Items.Length!=1||c.Items[0]<1))return "sichuan_hub_rejected";
    if(c.Kind=="sichuan_select"&&(ui.Type!="SichuanStagePopupUI"||c.Items.Length!=0||c.Value>1000))return "sichuan_stage_rejected";
    return f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&!PassiveSurface(x.Type))?"sichuan_popup_blocked":"";
   }
   if(c.Kind=="route_probe"){
    if(!GameplayReady(f)||f.BridgeVersion<78||ui.Type!="GameFieldDefaultUI"||c.TargetId!=0||c.Value<=0||c.Items==null||c.Items.Length!=1||c.Items[0]<1||c.Items[0]>8)return "route_probe_context_rejected";
    return f.Surfaces.Any(x=>x.Id!=ui.Id&&!PassiveSurface(x.Type))?"mainline_popup_blocked":"";
   }
   if(c.Kind=="mainline_reposition"){
    if(!GameplayReady(f)||f.BridgeVersion<69||ui.Type!="GameFieldDefaultUI"||c.TargetId!=0||c.Value!=0||c.Items==null||c.Items.Length!=0)return "waypoint_reposition_context_rejected";
    return f.Surfaces.Any(x=>x.Id!=ui.Id&&!PassiveSurface(x.Type))?"mainline_popup_blocked":"";
   }
   if(c.Kind=="mainline_menu"||c.Kind=="mainline_pack"||c.Kind=="mainline_walk"||c.Kind=="mainline_interact"){
    if(!GameplayReady(f)||f.BridgeVersion<16||c.TargetId!=0)return "mainline_context_rejected";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type)))return "mainline_popup_blocked";
    if(c.Kind=="mainline_pack")return ui.Type=="PackListUI"&&((f.BridgeVersion>=84&&((c.Value>=1&&c.Value<100)||(c.Value>=1001&&c.Value<2000)))||(c.Value>=1&&c.Value<=(f.BridgeVersion>=29?19:f.BridgeVersion>=26?15:8))||(f.BridgeVersion>=29&&c.Value>=1001&&c.Value<=(f.BridgeVersion>=42?1007:1003))||(f.BridgeVersion>=42&&new[]{2001,2002,2005,2007}.Contains(c.Value)))?"":"mainline_chapter_rejected";
    if(ui.Type!="GameFieldDefaultUI"||((c.Kind=="mainline_walk"||c.Kind=="mainline_interact")?c.Value==0:!(new[]{3,4,6,15,20}.Contains(c.Value)||(f.BridgeVersion>=26&&new[]{2,17}.Contains(c.Value)))))return "mainline_talent_rejected";
    if(f.Surfaces.Any(x=>x.Id!=ui.Id&&!PassiveSurface(x.Type)))return "mainline_popup_blocked";
    return "";
   }
   if(c.Kind=="dispatch_recover"){
     if(!GameplayReady(f)||f.BridgeVersion<66||ui.Type!="GameFieldDefaultUI"||c.TargetId!=0||c.Value!=0||c.Items==null||c.Items.Length!=0)return "dispatch_recovery_context_rejected";
     return f.Surfaces.Any(x=>x.Id!=ui.Id&&!PassiveSurface(x.Type))?"dispatch_recovery_popup_blocked":"";
    }
    if(c.Kind=="dispatch_collect_all"){
    if(!GameplayReady(f)||f.BridgeVersion<15||ui.Type!="GameFieldDefaultUI"||c.TargetId!=0||c.Value==0)return "dispatch_collect_context_rejected";
    if(f.Surfaces.Any(x=>x.Id!=ui.Id&&!PassiveSurface(x.Type)))return "dispatch_collect_popup_blocked";
    return "";
   }
   if(c.Kind=="dispatch_menu"){
    if(!GameplayReady(f)||f.BridgeVersion<6||ui.Type!="GameFieldDefaultUI"||!ui.Targets.Any(t=>t.Id==c.TargetId&&t.Enabled&&t.Field=="_buttonFieldSkill"))return "dispatch_entry_context_rejected";
    if(f.Surfaces.Any(x=>x.Id!=ui.Id&&!PassiveSurface(x.Type)))return "dispatch_entry_popup_blocked";
    return "";
   }
   if(c.Kind=="native_click"){
    if(!new[]{"BattleUI_PVP","BattleAutoSettingPopupUI","BattleAutoCurrencyAccelSettingPopupUI","BattleResultUI","PVPAutoHistoryPopupUI"}.Contains(ui.Type))return "native_target_scope_rejected";
    if(!ui.Targets.Any(t=>t.Id==c.TargetId&&t.Enabled))return "target_unavailable";
    if(f.Surfaces.Any(x=>x.Popup&&x.Id!=ui.Id&&x.Order>=ui.Order&&!PassiveSurface(x.Type)))return "native_popup_blocked";
    return "";
   }
   if((c.Kind=="click"||c.Kind=="pointer")&&!ui.Targets.Any(t=>t.Id==c.TargetId&&t.Enabled&&(c.Kind=="pointer"?t.Route=="pointer":t.Route=="ui")))return "target_unavailable";
   return "";
  }
 }
}
