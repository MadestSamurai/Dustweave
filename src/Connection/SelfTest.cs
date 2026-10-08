using BD2Daily.Live;
static class SelfTest {
 internal static void Run(){
  long now=DateTime.UtcNow.Ticks;int cases=0;
  Frame Frame()=>new(){ProcessId=7,ProcessStartTicks=8,Instance="i",AtUtcTicks=now,Scene="home",AccountKey="a",PlayerKey="p",UiToken="ui",PluginAvailable=true,Surfaces=[new(){Id=42,Targets=[new(){Id=43,Enabled=true}]}]};
  Command Command()=>new(){Id=Guid.NewGuid().ToString("N"),Kind="click",ProcessId=7,ProcessStartTicks=8,Instance="i",Scene="home",AccountKey="a",PlayerKey="p",UiToken="ui",ExpiresUtcTicks=now+TimeSpan.FromSeconds(10).Ticks,ObservedUtcTicks=now,SurfaceId=42,TargetId=43};
  void Check(bool ok,string reason){if(!ok)throw new Exception(reason);cases++;}
  {
   string error="Neo.Unity.Http.NeoNetworkException:/api/billing/mycard/check/mycard/nation, 0, ConnectionError, Request timeout";
   Check(BackgroundErrorPolicy.RegionLookupTimeout(new[]{"付款失敗",error}),"region lookup identified independently of UI language");
   Check(!BackgroundErrorPolicy.RegionLookupTimeout(new[]{"付款失敗",error.Replace("/check/mycard/nation","/purchase")}),"purchase network failures not acknowledged");
   var f=Frame();var c=Command();var u=f.Surfaces[0];u.Type="MessagePopupUI";u.Popup=true;u.Order=10000;u.NativeContext=BackgroundErrorPolicy.Context;u.Text=new[]{error};u.Targets[0].Field="_buttonOK";c.Kind="background_error_ack";c.TargetId=0;
   Check(LivePolicy.Gate(c,f,now,false)=="","callback-verified background notification accepted");
   u.NativeContext="";Check(LivePolicy.Gate(c,f,now,false)!="","text alone cannot authorize acknowledgement");u.NativeContext=BackgroundErrorPolicy.Context;
   u.Text=new[]{"Confirm purchase"};Check(LivePolicy.Gate(c,f,now,false)!="","same popup with changed text rejected");u.Text=new[]{error};
   u.Targets=u.Targets.Concat(new[]{new Target{Field="_buttonCancel",Enabled=true}}).ToArray();Check(LivePolicy.Gate(c,f,now,false)!="","two-choice popup rejected");u.Targets=u.Targets.Take(1).ToArray();
   f.Surfaces=f.Surfaces.Append(new Surface{Id=100,Type="PurchaseConfirmationUI",Popup=true,Order=10001}).ToArray();Check(LivePolicy.Gate(c,f,now,false)!="","higher popup blocks acknowledgement");f.Surfaces=f.Surfaces.Take(1).ToArray();
   u.InputReady=false;Check(LivePolicy.Gate(c,f,now,false)!="","animation waits before acknowledgement");u.InputReady=true;
   c.Items=new long[]{1};Check(LivePolicy.Gate(c,f,now,false)!="","unexpected payload rejected");c.Items=new long[0];
   f.Surfaces=f.Surfaces.Append(new Surface{Id=100,Type="BattleUI_FieldBattle"}).ToArray();Check(LivePolicy.Gate(c,f,now,false)!="","battle retains ownership");
  }
  Check(LiveProtocol.IsSquareScene("Map3009_001"),"plaza routes permit A*");
  foreach(var scene in new[]{"Map0001_001","Map0006_008","Map1003_001","Map30090_001","",null})
   Check(!LiveProtocol.IsSquareScene(scene),"A* never handles cartridge or unknown scene: "+scene);
  {
   var f=Frame();f.PluginAvailable=false;var c=Command();c.Kind="tactics_select";
   Check(LivePolicy.Gate(c,f,now,false)=="plugin_unavailable","unavailable extension rejects commands before dispatch");
   c.Kind="event_start";Check(LivePolicy.Gate(c,f,now,false)=="plugin_unavailable","unavailable extension rejects delegated actions");
   c.Kind="event_sweep_confirm";c.Items=new long[]{8,5,14,115};c.Value=5;
   Check(LivePolicy.Gate(c,f,now,false)=="public_sweep_challenge15_only","base never sweeps another stage");
   c.Items[2]=15;c.Value=6;Check(LivePolicy.Gate(c,f,now,false)=="public_sweep_challenge15_only","base caps native sweep count");
   c.Value=5;c.TargetId=0;f.Surfaces[0].Type="BattleSkipPopupUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","base accepts valid challenge15 sweep guard");
  }
  {
   var f=Frame();var ui=f.Surfaces[0];ui.Type="MessagePopupUI";ui.Path="Root/ErrorMessagePopupUI";ui.Text=new[]{"error : 112003"};ui.Targets[0].Field="_buttonOK";ui.Targets[0].Route="ui";
   var c=Command();c.Reason="home-reset-proof|112003|owned";
   Check(LivePolicy.Gate(c,f,now,false)=="","owned reset native acknowledgement accepts its exact non-spending context");
   ui.Text=new[]{"购买确认"};Check(LivePolicy.Gate(c,f,now,false)=="mission_reset_context_rejected","same popup instance cannot turn into a purchase confirmation");
   ui.Text=new[]{"error : 1120030"};Check(LivePolicy.Gate(c,f,now,false)=="mission_reset_context_rejected","reset code requires a complete numeric token");ui.Text=new[]{"error : 112003"};
   ui.Targets=ui.Targets.Concat(new[]{new Target{Id=45,Field="_buttonCancel",Enabled=true}}).ToArray();Check(LivePolicy.Gate(c,f,now,false)=="mission_reset_context_rejected","reset acknowledgement never confirms a two-choice dialog");ui.Targets=ui.Targets.Take(1).ToArray();
   ui.Path="Root/MessagePopupUI";Check(LivePolicy.Gate(c,f,now,false)=="mission_reset_context_rejected","matching text cannot authorize an unrelated popup class");ui.Path="Root/ErrorMessagePopupUI";
   f.Surfaces=f.Surfaces.Concat(new[]{new Surface{Id=46,Type="BattleUI_FieldBattle"}}).ToArray();Check(LivePolicy.Gate(c,f,now,false)=="mission_reset_context_rejected","reset acknowledgement preserves battle context");
  }
  foreach(int pack in new[]{1,4,19,1001,1008}){
   Check(!LiveProtocol.TransitRequiresStealth(pack,false),"normal daily does not require stealth");
   Check(LiveProtocol.TransitRequiresStealth(pack,true),"explicit route test requires stealth");
  }
  foreach(int pack in new[]{14,1003})Check(LiveProtocol.TransitRequiresStealth(pack,false),"known patrol still requires stealth");
  {
   var f=Frame();var c=Command();c.RequireStealth=true;
   Check(LivePolicy.Gate(c,f,now,false)=="stealth_scope_rejected","test protection cannot change unrelated operation");
   f.Surfaces[0].Type="GameFieldDefaultUI";c.TargetId=0;
   foreach(string kind in new[]{"mainline_walk","mainline_approach","mainline_reposition"}){
    c.Kind=kind;c.Value=kind=="mainline_reposition"?0:7;
    Check(LivePolicy.Gate(c,f,now,false)=="","explicit navigation protection accepted");
   }
  }
  {
   var f=Frame();f.Surfaces[0].Type="ScriptUI";f.Surfaces[0].NativeContext="story_dialogue";f.Surfaces[0].Targets[0].Field="_objButtonTouch";
   var c=Command();c.Kind="story_advance";c.TargetId=0;
   Check(LivePolicy.Gate(c,f,now,false)=="","native story dialogue allowed");
   f.Surfaces[0].InputReady=false;Check(LivePolicy.Gate(c,f,now,false)!="","busy dialogue blocked");f.Surfaces[0].InputReady=true;
   f.Surfaces[0].NativeContext="other";Check(LivePolicy.Gate(c,f,now,false)!="","unrelated script blocked");f.Surfaces[0].NativeContext="story_dialogue";
   f.Surfaces=f.Surfaces.Concat(new[]{new Surface{Id=44,Type="BattleUI_FieldBattle"}}).ToArray();Check(LivePolicy.Gate(c,f,now,false)!="","dialogue during battle blocked");
  }
  {
   var f=Frame();f.Surfaces[0].Type="StorySkipUI";f.Surfaces[0].NativeContext="story_skip";f.Surfaces[0].Targets[0].Field="_objSkipButton";
   var c=Command();c.Kind="story_skip";c.TargetId=0;
   Check(LivePolicy.Gate(c,f,now,false)=="","story native skip callback");
   f.Surfaces[0].Path="Root/BalloonScriptUI(Clone)/StorySkipUI";
   var balloon=new Surface{Id=45,Type="BalloonScriptUI",Path="Root/BalloonScriptUI(Clone)",Popup=false};
   f.Surfaces=[f.Surfaces[0],balloon];
   Check(LivePolicy.Gate(c,f,now,false)=="","quest story skip accepts its enclosing balloon");
   balloon.Path="Root/UnrelatedBalloon";Check(LivePolicy.Gate(c,f,now,false)!="","unrelated balloon still blocks skip");
   balloon.Path="Root/BalloonScriptUI(Clone)";balloon.Popup=true;Check(LivePolicy.Gate(c,f,now,false)!="","modal balloon still blocks skip");balloon.Popup=false;
   f.Surfaces=[f.Surfaces[0],balloon,new Surface{Id=46,Type="VisualNovelSelectChoiceUI",Popup=true}];Check(LivePolicy.Gate(c,f,now,false)!="","quest choice is not skipped");
   f.Surfaces=[f.Surfaces[0]];

   f.Surfaces[0].Targets[0].Enabled=false;Check(LivePolicy.Gate(c,f,now,false)!="","story disabled skip");f.Surfaces[0].Targets[0].Enabled=true;
   f.Surfaces[0].NativeContext="story_replay";Check(LivePolicy.Gate(c,f,now,false)!="","story cannot start a replay");f.Surfaces[0].NativeContext="story_skip";
   f.Surfaces=[f.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true}];Check(LivePolicy.Gate(c,f,now,false)!="","story leaves unknown confirmation alone");
  }
  {
   var f=Frame();f.Surfaces[0].Type="GameFieldDefaultUI";var c=Command();c.Kind="weekly_npc_interact";c.TargetId=0;c.Value=100062;c.Items=[-123];
   Check(LivePolicy.Gate(c,f,now,false)=="","current quest object command accepts signed instance");
   c.Items=[0];Check(LivePolicy.Gate(c,f,now,false)!="","quest object identity required");
   c.Items=[];Check(LivePolicy.Gate(c,f,now,false)!="","quest object payload required");c.Items=[-123];
   f.BridgeVersion=101;Check(LivePolicy.Gate(c,f,now,false)!="","quest interaction needs new bridge");f.BridgeVersion=102;
   f.Surfaces=[f.Surfaces[0],new Surface{Id=44,Type="MessagePopupUI",Popup=true}];Check(LivePolicy.Gate(c,f,now,false)!="","quest interaction cannot bypass popup");
  }
  {
   var f=Frame();f.Surfaces[0].Type="GameFieldDefaultUI";var c=Command();c.Kind="weekly_npc_lift";c.TargetId=0;c.Value=100062;c.Items=[-123];
   Check(LivePolicy.Gate(c,f,now,false)=="","current quest object command accepts signed instance");
   c.Items=[0];Check(LivePolicy.Gate(c,f,now,false)!="","quest object identity required");
   c.Items=[];Check(LivePolicy.Gate(c,f,now,false)!="","quest object payload required");c.Items=[-123];
   f.BridgeVersion=101;Check(LivePolicy.Gate(c,f,now,false)!="","quest interaction needs new bridge");f.BridgeVersion=102;
   f.Surfaces=[f.Surfaces[0],new Surface{Id=44,Type="MessagePopupUI",Popup=true}];Check(LivePolicy.Gate(c,f,now,false)!="","quest interaction cannot bypass popup");
  }
  {
   var f=Frame();f.Surfaces[0].Type="GameFieldDefaultUI";var c=Command();c.Kind="weekly_npc_carry_nav";c.TargetId=0;c.Value=100062;c.Items=[-123];
   Check(LivePolicy.Gate(c,f,now,false)=="","current quest object command accepts signed instance");
   c.Items=[0];Check(LivePolicy.Gate(c,f,now,false)!="","quest object identity required");
   c.Items=[];Check(LivePolicy.Gate(c,f,now,false)!="","quest object payload required");c.Items=[-123];
   f.BridgeVersion=101;Check(LivePolicy.Gate(c,f,now,false)!="","quest interaction needs new bridge");f.BridgeVersion=102;
   f.Surfaces=[f.Surfaces[0],new Surface{Id=44,Type="MessagePopupUI",Popup=true}];Check(LivePolicy.Gate(c,f,now,false)!="","quest interaction cannot bypass popup");
  }
  foreach(var distance in new[]{double.NaN,double.PositiveInfinity,-1d,0d,10d})Check(LiveProtocol.NavigationSeconds(distance)==60,"short or invalid path retains bounded minimum");
  Check(LiveProtocol.NavigationSeconds(240)==220,"long route is not cut off by old 75 second watchdog");
  Check(LiveProtocol.NavigationSeconds(1000)==300,"native path deadline remains bounded");
  {
   foreach(var kind in new[]{"sichuan_open","sichuan_select"}){
    var f=Frame();f.Surfaces[0].Type=kind=="sichuan_open"?"MiniGameHubUI":"SichuanStagePopupUI";
    var c=Command();c.Kind=kind;c.TargetId=0;c.Value=1;c.Items=kind=="sichuan_open"?[12]:[];
    Check(LivePolicy.Gate(c,f,now,false)=="","native regular Sichuan routing");
    f.BridgeVersion=79;Check(LivePolicy.Gate(c,f,now,false)!="","Sichuan requires updated bridge");f.BridgeVersion=80;
    f.Surfaces=[f.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true}];Check(LivePolicy.Gate(c,f,now,false)!="","Sichuan respects foreground popup");
    f.Surfaces=[f.Surfaces[0]];c.Value=0;Check(LivePolicy.Gate(c,f,now,false)!="","Sichuan rejects unresolved stage");
   }
   var field=Frame();field.Surfaces[0].Type="GameFieldDefaultUI";var probe=Command();probe.Kind="route_probe";probe.TargetId=0;probe.Value=25;probe.Items=[2];
   Check(LivePolicy.Gate(probe,field,now,false)=="","route probe accepts observed point identity");
   probe.Items=[9];Check(LivePolicy.Gate(probe,field,now,false)!="","route probe rejects unobserved point");
   probe.Items=[1];field.Surfaces=[field.Surfaces[0],new(){Id=45,Type="BattleUI"}];Check(LivePolicy.Gate(probe,field,now,false)!="","route probe preserves active battle");
  }
  {
   foreach(var kind in new[]{"mainline_cancel_nav","mainline_approach"}){
    var f=Frame();f.Surfaces[0].Type="GameFieldDefaultUI";f.Scene="Map0006_024";var c=Command();c.Scene=f.Scene;c.Kind=kind;c.TargetId=0;c.Items=[];c.Value=kind=="mainline_approach"?12:0;
    Check(LivePolicy.Gate(c,f,now,false)=="","map-independent travel recovery command");
    f.Surfaces=[f.Surfaces[0],new(){Id=8,Type="MessagePopupUI"}];Check(LivePolicy.Gate(c,f,now,false)!="","travel never ignores a popup");
    f.Surfaces=[f.Surfaces[0]];c.Items=[1];Check(LivePolicy.Gate(c,f,now,false)!="","travel takes only observed object identity");
   }
  }
  {
   var f=Frame();f.Surfaces[0].Type="GameFieldDefaultUI";var c=Command();c.Kind="dispatch_recover";c.TargetId=0;c.Items=[];
   Check(LivePolicy.Gate(c,f,now,false)=="","explicit messenger recovery");
   foreach(var change in new Action<Frame>[]{x=>x.BridgeVersion=64,x=>x.Surfaces[0].Type="MenuUI",x=>x.Surfaces=[x.Surfaces[0],new(){Id=8,Type="RewardReceivePopupUI"}]}){var v=Frame();v.Surfaces[0].Type="GameFieldDefaultUI";change(v);Check(LivePolicy.Gate(c,v,now,false)!="","covered field or old bridge rejected");}
   c.Items=[1];Check(LivePolicy.Gate(c,f,now,false)!="","no arbitrary recovery keys");c.Items=[];c.Value=1;Check(LivePolicy.Gate(c,f,now,false)!="","no forced loads");
   var w=new DispatchRecoveryGuard();Check(!w.Sample("keys",0),"first observation");Check(!w.Sample("keys",.8),"not yet stale");Check(!w.Sample("keys",1.6),"stable observation");Check(w.Sample("keys",2.1),"stable two seconds");w.Consume();try{w.Sample("keys",2.2);Check(false,"cannot repeat");}catch(InvalidOperationException){Check(true,"consumed");}
   w=new DispatchRecoveryGuard();w.Sample("keys",0);Check(!w.Sample("keys",3),"gap resets");Check(!w.Sample("new",3.5),"changed keys reset");w.Sample("",4);Check(!w.Sample("new",4.5),"loading resets");
  }
  {
   var f=Frame();f.Surfaces[0].Type="GameFieldDefaultUI";var c=Command();c.Kind="collection_query";c.TargetId=0;c.Value=2;c.Items=[21,22,23];
   Check(LivePolicy.Gate(c,f,now,false)=="","collection read accepts explicit cartridge maps");
   foreach(var edit in new Action<Frame>[] {v=>v.BridgeVersion=61,v=>v.Surfaces[0].Type="BattleUI",v=>v.Surfaces[0].InputReady=false,v=>v.Surfaces=[v.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true}]}){var v=Frame();v.Surfaces[0].Type="GameFieldDefaultUI";edit(v);Check(LivePolicy.Gate(c,v,now,false)!="","collection read rejects invalid context");}
   c.Items=[21,21];Check(LivePolicy.Gate(c,f,now,false)!="","collection maps must be unique");
  }
  {var f=Frame();Check(f.BridgeVersion==LiveProtocol.BridgeVersion,"frame version shares handshake constant");
   Check(LiveProtocol.Ready(f,7,8,now),"current bridge handshake");
   f.BridgeVersion=LiveProtocol.BridgeVersion-1;Check(!LiveProtocol.Ready(f,7,8,now),"old bridge cannot complete handshake");f.BridgeVersion=LiveProtocol.BridgeVersion;
   Check(!LiveProtocol.Ready(f,7,9,now),"reused pid handshake rejected");Check(!LiveProtocol.Ready(f,7,8,now,"i"),"handoff needs new instance");
   Check(!LiveProtocol.Ready(f,7,8,now+TimeSpan.FromSeconds(5).Ticks),"stale handshake rejected");f.Error="broken";Check(!LiveProtocol.Ready(f,7,8,now),"failed frame rejected");
  }
  Check(LivePolicy.Gate(Command(),Frame(),now,false)=="","valid command");
  Check(LivePolicy.Gate(Command(),Frame(),now,true)=="another_operation_owns_game","other owner");
  foreach(var change in new Action<Command>[] {c=>c.Id="../invalid",c=>c.Kind="invoke",c=>c.ProcessId++,c=>c.ProcessStartTicks++,c=>c.Instance="old",c=>c.AccountKey="b",c=>c.PlayerKey="b",c=>c.Scene="battle",c=>c.UiToken="old",c=>c.ExpiresUtcTicks=now,c=>c.ExpiresUtcTicks=now+TimeSpan.FromMinutes(1).Ticks,c=>c.ObservedUtcTicks=now-TimeSpan.FromSeconds(16).Ticks,c=>c.ObservedUtcTicks=now+TimeSpan.FromSeconds(4).Ticks,c=>c.SurfaceId++,c=>c.TargetId++}){var c=Command();change(c);Check(LivePolicy.Gate(c,Frame(),now,false)!="","unsafe command accepted");}
  foreach(var change in new Action<Frame>[] {f=>f.Error="loading",f=>f.AtUtcTicks=now-TimeSpan.FromSeconds(4).Ticks,f=>f.AtUtcTicks=now+TimeSpan.FromSeconds(3).Ticks,f=>f.Protocol=2,f=>f.Surfaces[0].Targets[0].Enabled=false}){var f=Frame();change(f);Check(LivePolicy.Gate(Command(),f,now,false)!="","unsafe frame accepted");}
  foreach(var kind in new[]{"back","detach"}){var c=Command();c.Kind=kind;Check(LivePolicy.Gate(c,Frame(),now,false)=="",kind);}
  {var c=Command();c.Kind="pointer";var f=Frame();Check(LivePolicy.Gate(c,f,now,false)=="target_unavailable","pointer must not use a UI-only target");f.Surfaces[0].Targets[0].Route="pointer";Check(LivePolicy.Gate(c,f,now,false)=="","observed pointer route");c.Kind="click";Check(LivePolicy.Gate(c,f,now,false)=="target_unavailable","UI route must not invoke pointer-only target");}
  {
   var f=Frame();f.BridgeVersion=41;f.Surfaces[0].Type="BattleUI_EventBattle";var c=Command();c.Kind="event_action";c.TargetId=0;c.Value=1;c.Items=[6921001,123,0];
   Check(LivePolicy.Gate(c,f,now,false)=="","event native action guard");
   foreach(var change in new Action<Command>[] {v=>v.Value=2,v=>v.Value=0,v=>v.Items=[0,123,0],v=>v.Items=[6921001,0,0],v=>v.TargetId=7}){
    var test=Command();test.Kind="event_action";test.TargetId=0;test.Value=1;test.Items=[6921001,123,0];change(test);Check(LivePolicy.Gate(test,f,now,false)!="","event invalid action rejected");
   }
   f.Surfaces=[f.Surfaces[0],new(){Id=45,Type="BattleResultUI"}];Check(LivePolicy.Gate(c,f,now,false)=="event_popup_blocked","event cannot dispatch behind result");
  }
  {
    var f=Frame();f.Surfaces[0].Type="EventBattleUI";var c=Command();c.Kind="event_sweep_select";c.TargetId=0;c.Items=[1,2,3];c.Value=0;
    Check(LivePolicy.Gate(c,f,now,false)=="","sweep focus accepted");
    c.Value=1;Check(LivePolicy.Gate(c,f,now,false)=="","sweep selection accepted");
    f.BridgeVersion=96;Check(LivePolicy.Gate(c,f,now,false)=="event_sweep_selection_invalid","sweep requires updated bridge");
    f.BridgeVersion=97;c.Kind="event_sweep_confirm";c.Items=[1,2,3,4];c.Value=20;f.Surfaces[0].Type="BattleSkipPopupUI";
    Check(LivePolicy.Gate(c,f,now,false)=="","event sweep confirmation gate");
    c.Value=0;Check(LivePolicy.Gate(c,f,now,false)=="event_sweep_rejected","zero sweep rejected");
    c.Value=20;f.Surfaces=[f.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true}];Check(LivePolicy.Gate(c,f,now,false)=="event_popup_blocked","sweep behind popup rejected");
   }
   {
   var f=Frame();f.Surfaces[0].Type="EventBattleUI";var c=Command();c.Kind="event_select";c.TargetId=0;c.Items=[1691,1,6];
   Check(LivePolicy.Gate(c,f,now,false)=="","event frontier selection");
   f.BridgeVersion=40;Check(LivePolicy.Gate(c,f,now,false)=="event_lobby_rejected","old bridge cannot select stage");
   f.BridgeVersion=41;c.Items=[1691,1,0];Check(LivePolicy.Gate(c,f,now,false)=="event_lobby_rejected","invalid frontier rejected");
   c.Items=[1691,1,6];f.Surfaces=[f.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true}];Check(LivePolicy.Gate(c,f,now,false)=="event_popup_blocked","frontier behind popup rejected");
  }
  {
   var f=Frame();f.Surfaces[0].Type="BattleUI_TarosTactics";
   var c=Command();c.Kind="tactics_action";c.TargetId=0;c.Value=3;c.Items=[691001,-123,-1];
   Check(LivePolicy.Gate(c,f,now,false)=="","tactics runtime identity may be negative; knockback supported");
   c.Value=2;Check(LivePolicy.Gate(c,f,now,false)!="","enemy turn not editable");
   c.Value=3;c.Kind="tactics_start";c.Items=[691001,-123,303330,0,0,3];Check(LivePolicy.Gate(c,f,now,false)=="","burst is part of start guard");
   c.Items=new long[31];c.Items[0]=691001;Check(LivePolicy.Gate(c,f,now,false)=="","five characters plus summon start guard");
   c.Items=new long[66];c.Items[0]=691001;Check(LivePolicy.Gate(c,f,now,false)!="","oversized board rejected");
   c.Items=[691001,-123,303330,0,0];Check(LivePolicy.Gate(c,f,now,false)!="","incomplete burst plan rejected");
   c.Kind="tactics_priority";c.Value=1;c.Items=[691001,-123,1];Check(LivePolicy.Gate(c,f,now,false)=="","initial priority toggle allowed");
   c.Value=3;Check(LivePolicy.Gate(c,f,now,false)!="","priority cannot be changed mid-battle");
   c.Value=1;c.Items[2]=2;Check(LivePolicy.Gate(c,f,now,false)!="","priority requires boolean value");
   c.Items[2]=0;f.BridgeVersion=76;Check(LivePolicy.Gate(c,f,now,false)!="","old priority handler rejected");f.BridgeVersion=LiveProtocol.BridgeVersion;
   c.Kind="tactics_action";c.Items=[691001,0,0];Check(LivePolicy.Gate(c,f,now,false)!="","missing runtime identity rejected");
  }
  {
   var f=Frame();f.Surfaces[0].Type="EquipmentUpgradePopupUI";var c=Command();c.Kind="powder_options";c.TargetId=0;c.Value=7;c.Items=[100];
   Check(LivePolicy.Gate(c,f,now,false)=="","bounded powder preview");
   foreach(var change in new Action<Command>[] {v=>v.Value=0,v=>v.Value=10,v=>v.Items=[0],v=>v.Items=[(long)int.MaxValue+1],v=>v.Items=[1,2],v=>v.TargetId=43}){
    var test=Command();test.Kind="powder_options";test.TargetId=0;test.Value=7;test.Items=[100];change(test);Check(LivePolicy.Gate(test,f,now,false)!="","invalid powder settings rejected");
   }
   f.BridgeVersion=59;Check(LivePolicy.Gate(c,f,now,false)!="","old powder bridge rejected");f.BridgeVersion=60;
   c.Items=[10000];Check(LivePolicy.Gate(c,f,now,false)=="","native bounded large powder preview");
   f.Surfaces[0].Type="EquipmentMakingSelectUI";c.Kind="powder_recipe";c.Value=4;c.Items=[];Check(LivePolicy.Gate(c,f,now,false)=="","N recipe selection");
   c.Value=11;Check(LivePolicy.Gate(c,f,now,false)!="","non N recipe rejected");
   c.Value=4;f.Surfaces=[f.Surfaces[0],new(){Id=77,Type="MessagePopupUI",Popup=true,Order=50}];Check(LivePolicy.Gate(c,f,now,false)!="","powder modal guard");
  }
  {
   var f=Frame();f.Surfaces[0].Type="EquipmentUpgradeUI";var c=Command();c.Kind="equipment_refine_batch";c.TargetId=0;c.Value=22;c.Items=[99,5000,400000,150000];
   Check(LivePolicy.Gate(c,f,now,false)=="","5000 native batch allowed");
   foreach(int target in new[]{1,20,22,23,24}){c.Value=target;Check(LivePolicy.Gate(c,f,now,false)=="","adjustable native target");}
   foreach(var edit in new Action<Command>[] {v=>v.Value=0,v=>v.Value=25,v=>v.Items=[99,5001,400000,150000],v=>v.Items=[99,0,1,1],v=>v.Items=[0,1,80,30],v=>v.Items=[99,1,0,30],v=>v.Items=[99,1,80,0],v=>v.Items=[99,1],v=>v.TargetId=43}){
    var test=Command();test.Kind="equipment_refine_batch";test.TargetId=0;test.Value=22;test.Items=[99,5000,400000,150000];edit(test);Check(LivePolicy.Gate(test,f,now,false)!="","invalid refinement batch rejected");
   }
   f.BridgeVersion=60;Check(LivePolicy.Gate(c,f,now,false)!="","old bridge rejected");f.BridgeVersion=61;
   f.Surfaces[0].InputReady=false;Check(LivePolicy.Gate(c,f,now,false)!="","running native batch rejected");f.Surfaces[0].InputReady=true;
   f.Surfaces=[f.Surfaces[0],new(){Id=77,Type="MessagePopupUI",Popup=true,Order=50}];Check(LivePolicy.Gate(c,f,now,false)!="","batch behind modal rejected");
  }
  Frame Startup(){var f=Frame();f.Scene="ReGame";f.AccountKey=f.PlayerKey="";f.Error="Waiting for fresh account identity";f.Surfaces=[new(){Id=42,Type="DownloadPopupUI",Popup=true,Order=50,Targets=[new(){Id=43,Field="_btnDownload",Enabled=true,Route="ui"}]}];return f;}
  Command Download(){var c=Command();c.Scene="ReGame";c.AccountKey=c.PlayerKey="";c.Scope="startup_download";return c;}
  Check(LivePolicy.Gate(Download(),Startup(),now,false)=="","scoped download without account");
  foreach(var edit in new Action<Command>[] {c=>c.Scope="gameplay",c=>c.Kind="back",c=>c.Kind="pointer",c=>c.AccountKey="old",c=>c.TargetId++,c=>c.Scope="anything"}){var c=Download();edit(c);Check(LivePolicy.Gate(c,Startup(),now,false)!="","startup escape accepted");}
  foreach(var edit in new Action<Frame>[] {f=>f.Surfaces[0].Targets[0].Field="_btnCancel",f=>f.Surfaces[0].Targets[0].Enabled=false,f=>f.Surfaces[0].Type="ShopUI",f=>f.Error="Unexpected error",f=>f.Scene="Map1"}){var f=Startup();edit(f);Check(LivePolicy.Gate(Download(),f,now,false)!="","unsafe startup frame");}
  {var f=Startup();f.Surfaces=[f.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true,Order=60,Text=["Data not found exception. (common, id:0) - db is not loaded"],Targets=[new(){Id=45,Field="_buttonOK",Enabled=true,Route="ui"}]}];
   Check(LivePolicy.Gate(Download(),f,now,false)=="startup_popup_blocked","download behind modal");var c=Download();c.SurfaceId=44;c.TargetId=45;Check(LivePolicy.Gate(c,f,now,false)=="","known database popup");f.Surfaces[1].Text=["Buy items?"];Check(LivePolicy.Gate(c,f,now,false)=="startup_target_rejected","unknown prompt not dismissed");}
  {
   var c=Command();c.Kind="monster_open";c.TargetId=0;var f=Frame();f.BridgeVersion=28;f.Surfaces[0].Type="MenuUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","monster current season entry");
   foreach(var change in new Action<Frame>[] {v=>v.BridgeVersion=27,v=>v.Surfaces[0].Type="GameFieldDefaultUI",v=>v.Surfaces[0].InputReady=false,v=>v.Surfaces=[v.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true}],v=>v.Scene="ReGame"}){
    var test=Frame();test.BridgeVersion=28;test.Surfaces[0].Type="MenuUI";change(test);Check(LivePolicy.Gate(c,test,now,false)!="","monster entry rejects unsafe context");
   }
   c.Value=1;Check(LivePolicy.Gate(c,f,now,false)!="","rerun requires new bridge");
   f.BridgeVersion=68;Check(LivePolicy.Gate(c,f,now,false)=="","rerun entry supported");
   c.Value=2;Check(LivePolicy.Gate(c,f,now,false)!="","unknown season kind rejected");
   c.Kind="friendship_select";c.Value=10;Check(LivePolicy.Gate(c,f,now,false)=="","friendship selection");
   c.Kind="friendship_complete";Check(LivePolicy.Gate(c,f,now,false)!="","counseling requires selected costume screen");
   f.Surfaces[0].Type="FriendshipManageUI";Check(LivePolicy.Gate(c,f,now,false)=="","ordinary counseling request gate");
   c.Kind="monster_open";f.Surfaces[0].Type="MenuUI";c.Value=0;
   c.TargetId=43;Check(LivePolicy.Gate(c,f,now,false)!="","monster entry cannot carry unrelated target");
  }
  {var f=Startup();f.Scene="Splash";f.Surfaces=[new(){Id=42,Type="MessagePopupUI",Popup=true,Text=["Connection lost\r\nCLIENT_LOGIC_ERROR\r\nBUNDLE_CATALOG_CHECK\r\n"],Targets=[new(){Id=43,Field="_buttonOK",Enabled=true,Route="ui"}]}];var c=Download();c.Scene="Splash";
   Check(LivePolicy.Gate(c,f,now,false)=="","catalog timeout native retry");
   foreach(var text in new[]{"Buy?","CLIENT_LOGIC_ERROR","BUNDLE_CATALOG_CHECK","CLIENT_LOGIC_ERROR\nBUNDLE_CATALOG_UPDATE","CLIENT_LOGIC_ERROR\nBUNDLE_CATALOG_CHECK_EXTRA"}){f.Surfaces[0].Text=[text];Check(LivePolicy.Gate(c,f,now,false)!="","unknown startup error not acknowledged");}
  }
  {var f=Frame();f.Surfaces[0].Type="GachaResultUI";f.Surfaces[0].InputReady=false;f.Surfaces[0].Targets[0].Field="_objSkipButton";var c=Command();
   Check(LivePolicy.Gate(c,f,now,false)=="","native gacha skip remains available during reveal animation");
   foreach(var field in new[]{"_objBackButton","_objOneGachaButton","_objRedrawGachaButton"}){f.Surfaces[0].Targets[0].Field=field;Check(LivePolicy.Gate(c,f,now,false)!="","animation exception cannot extend to other buttons");}
  }
  Check(LivePolicy.GameplayReady(Frame()),"normal gameplay reads");Check(!LivePolicy.GameplayReady(Startup()),"startup must not read tables");
  {var f=Frame();f.Scene="ReGame";Check(!LivePolicy.GameplayReady(f),"cached identity does not prove loaded");f.Scene="home";f.Surfaces[0].Type="DownloadPopupUI";Check(!LivePolicy.GameplayReady(f),"download pauses table reads");}
  {var c=Command();c.Kind="mirror_ready";c.TargetId=0;c.Scene="Map3001_001";var f=Frame();f.Scene=c.Scene;f.Surfaces[0].Type="GameFieldDefaultUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","mirror lobby native entry");
   f.Surfaces=[f.Surfaces[0],new(){Type="MessagePopupUI",Popup=true}];Check(LivePolicy.Gate(c,f,now,false)!="","mirror popup blocks native entry");
   f=Frame();Check(LivePolicy.Gate(c,f,now,false)!="","mirror entry cannot escape lobby");}
  {var c=Command();c.Kind="native_click";var f=Frame();f.Surfaces[0].Type="BattleAutoSettingPopupUI";f.Surfaces[0].Targets[0].Route="pointer";
   Check(LivePolicy.Gate(c,f,now,false)=="","native mirror UI dispatch");f.Surfaces[0].Type="ShopUI";Check(LivePolicy.Gate(c,f,now,false)!="","native mirror dispatch is scoped");}
  {var c=Command();c.Kind="dispatch_menu";var f=Frame();f.Surfaces[0].Type="GameFieldDefaultUI";f.Surfaces[0].Targets[0].Field="_buttonFieldSkill";
   Check(LivePolicy.Gate(c,f,now,false)=="","native dispatch talent entry");
   foreach(var edit in new Action<Frame>[] {v=>v.BridgeVersion=5,v=>v.Surfaces[0].Type="ShopUI",v=>v.Surfaces[0].Targets[0].Field="_buttonMenu",v=>v.Surfaces[0].Targets[0].Enabled=false,v=>v.Surfaces=[v.Surfaces[0],new(){Id=44,Type="MenuUI"}]}){var v=Frame();v.Surfaces[0].Type="GameFieldDefaultUI";v.Surfaces[0].Targets[0].Field="_buttonFieldSkill";edit(v);Check(LivePolicy.Gate(c,v,now,false)!="","dispatch navigation rejects invalid entry");}
  }
  {var f=Frame();f.Surfaces[0].InputReady=false;
   Check(LivePolicy.Gate(Command(),f,now,false)=="ui_not_ready","native animation/cooldown blocks dispatch");
   f.Surfaces[0].InputReady=true;Check(LivePolicy.Gate(Command(),f,now,false)=="","native readiness restores dispatch");}
  {Check(!LivePolicy.PassDefinitionReady(0,new[]{131}),"uninitialized pass cannot call native table getters");
   Check(!LivePolicy.PassDefinitionReady(131,new[]{140}),"stale pass cannot call native table getters");
   Check(!LivePolicy.PassDefinitionReady(131,null),"missing active list remains not ready");
   Check(LivePolicy.PassDefinitionReady(131,new[]{140,131}),"active initialized return pass can be read");}
  foreach(var background in LivePolicy.PassiveSurfaces){
   var c=Command();c.Kind="mail_tab";c.TargetId=0;var f=Frame();f.Surfaces[0].Type="MailUI";
   f.Surfaces=new[]{f.Surfaces[0],new Surface{Id=45,Type=background,Popup=true,Order=1001}};
   Check(LivePolicy.Gate(c,f,now,false)=="","Passive reward notice does not block mailbox");
   f.Surfaces[1].Type="MessagePopupUI";
   Check(LivePolicy.Gate(c,f,now,false)!="","Actual confirmation still blocks mailbox");
  }
  foreach(var kind in new[]{"reward_refresh","dice_query"}){
   var c=Command();c.Kind=kind;c.TargetId=0;var f=Frame();f.Surfaces[0].Type="EventUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","Read-only reward query");
   c.Value=1;Check(LivePolicy.Gate(c,f,now,false)!="","Query rejects parameters");c.Value=0;
   f.Surfaces[0].Type="BattleUI_PVP";Check(LivePolicy.Gate(c,f,now,false)!="","Query rejects battle");
  }
  foreach(var surface in LiveProtocol.MissionQuerySurfaces){
   var c=Command();c.Kind="reward_refresh";c.TargetId=0;var f=Frame();f.Surfaces[0].Type=surface;
   Check(LivePolicy.Gate(c,f,now,false)=="","missions refresh in place: "+surface);
   f.Surfaces=[f.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true,Order=99}];
   Check(LivePolicy.Gate(c,f,now,false)!="","unknown confirmation blocks query: "+surface);
   f.Surfaces=[f.Surfaces[0],new(){Id=45,Type="BattleUI_PVP"}];
   Check(LivePolicy.Gate(c,f,now,false)!="","underlying battle blocks query: "+surface);
  }
  {var c=Command();c.Kind="reward_refresh";c.TargetId=0;var f=Frame();f.Surfaces[0].Type="HuntOrAirwayUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","hunt refreshes server missions without leaving its page");
   f.Surfaces=[f.Surfaces[0],new(){Id=44,Type="HuntDispatchPopupUI",Popup=true,Order=99}];
   Check(LivePolicy.Gate(c,f,now,false)!="","hunt query cannot overlap an open spend preview");
  }
  {var c=Command();c.Kind="pass_select";c.TargetId=0;c.Value=131;var f=Frame();f.Surfaces[0].Type="PassUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","select active return pass by native id");
   foreach(var edit in new Action<Frame>[] {v=>v.BridgeVersion=73,v=>v.Surfaces[0].Type="MenuUI",v=>v.Surfaces[0].InputReady=false,v=>v.Surfaces=[v.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true,Order=99}]}){
    var v=Frame();v.Surfaces[0].Type="PassUI";edit(v);Check(LivePolicy.Gate(c,v,now,false)!="","pass selection respects version and modal ownership");
   }
   c.Value=0;Check(LivePolicy.Gate(c,f,now,false)!="","pass id must be positive");
  }
  {var c=Command();c.Kind="pass_init";c.TargetId=0;var f=Frame();f.Surfaces[0].Type="PassUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","native pass shortcut initialization");
   foreach(var edit in new Action<Frame>[] {v=>v.BridgeVersion=9,v=>v.Surfaces[0].Type="ShopUI",v=>v.Surfaces[0].InputReady=false,v=>v.Surfaces=[v.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true,Order=99}]}){
    var v=Frame();v.Surfaces[0].Type="PassUI";edit(v);Check(LivePolicy.Gate(c,v,now,false)!="","pass initialization scoped");
   }
   c.TargetId=43;Check(LivePolicy.Gate(c,f,now,false)!="","pass initialization cannot target arbitrary button");}
  foreach(var kind in new[]{"equipment_upgrade_preview","equipment_break_preview","equipment_refine_preview"}){
   var c=Command();c.Kind=kind;c.TargetId=0;c.Items=new long[]{1};var f=Frame();f.Surfaces[0].Type="InventoryManageUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","bounded equipment preview "+kind);
   foreach(var ids in new[]{Array.Empty<long>(),new long[]{0},new long[]{1,1},Enumerable.Range(1,31).Select(x=>(long)x).ToArray()}){c.Items=ids;Check(LivePolicy.Gate(c,f,now,false)!="","invalid equipment selection");}
   c.Items=new long[]{1};f.Surfaces[0].Type="ShopUI";Check(LivePolicy.Gate(c,f,now,false)!="","equipment preview surface scoped");
  }
  {var c=Command();c.Kind="mail_tab";c.TargetId=0;var f=Frame();f.Surfaces[0].Type="MailUI";
   foreach(var tab in new[]{0,1}){c.Value=tab;Check(LivePolicy.Gate(c,f,now,false)=="","mail normal/cash only");}
   c.Value=2;Check(LivePolicy.Gate(c,f,now,false)!="","cannot select mail history as collection");}
  {var c=Command();c.Kind="square_ranking_nav";c.TargetId=0;c.Value=1;c.Scene="Map3009_003";var f=Frame();f.Scene=c.Scene;f.Surfaces[0].Type="GameFieldDefaultUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","observed square field navigation");f.Scene=c.Scene="Map1001_001";Check(LivePolicy.Gate(c,f,now,false)!="","square commands reject other fields");}
  {var c=Command();c.Kind="dispatch_collect_all";c.TargetId=0;c.Value=-123;var f=Frame();f.Surfaces[0].Type="GameFieldDefaultUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","field messenger collection");
   foreach(var change in new Action<Frame>[] {v=>v.BridgeVersion=14,v=>v.Surfaces[0].Type="DispatchSelectUI",v=>v.Surfaces[0].InputReady=false,v=>v.Surfaces=[v.Surfaces[0],new(){Id=44,Type="MenuUI"}]}){var v=Frame();v.Surfaces[0].Type="GameFieldDefaultUI";change(v);Check(LivePolicy.Gate(c,v,now,false)!="","global claim requires observed field");}
   c.Value=0;Check(LivePolicy.Gate(c,f,now,false)!="","global claim requires messenger instance");c.Value=-123;c.TargetId=43;Check(LivePolicy.Gate(c,f,now,false)!="","global claim rejects UI target");
  }
  {
   var f=Frame();f.BridgeVersion=16;f.Surfaces[0].Type="PackListUI";var c=Command();c.Kind="mainline_pack";c.TargetId=0;
   foreach(var chapter in new[]{1,8}){c.Value=chapter;Check(LivePolicy.Gate(c,f,now,false)=="","mainline owned chapter scope");}
   foreach(var chapter in new[]{0,9,14,18,1001,3001}){c.Value=chapter;Check(LivePolicy.Gate(c,f,now,false)!="","mainline ends at eight");}
   f.BridgeVersion=26;
   foreach(var chapter in new[]{9,14,15}){c.Value=chapter;Check(LivePolicy.Gate(c,f,now,false)=="","extended owned chapter scope");}
   foreach(var chapter in new[]{0,16,18}){c.Value=chapter;Check(LivePolicy.Gate(c,f,now,false)!="","reject beyond requested route");}
   f.BridgeVersion=84;
   foreach(var chapter in new[]{20,1008,1009,1010}){c.Value=chapter;Check(LivePolicy.Gate(c,f,now,false)=="","new story IDs pass to native ownership/type verification");}
   foreach(var chapter in new[]{0,2009,3001,20000}){c.Value=chapter;Check(LivePolicy.Gate(c,f,now,false)!="","unrelated cartridge ranges remain blocked");}
   f.BridgeVersion=26;
   c.Kind="mainline_menu";f.Surfaces[0].Type="GameFieldDefaultUI";
   foreach(var talent in new[]{2,3,4,6,15,17,20}){c.Value=talent;Check(LivePolicy.Gate(c,f,now,false)=="","mainline gathering talents");}
   c.Value=18;Check(LivePolicy.Gate(c,f,now,false)!="","reject unrelated talent");
  }
  {
   var c=Command();c.Kind="mainline_talent";c.TargetId=0;c.Value=123;var f=Frame();f.BridgeVersion=21;f.Surfaces[0].Type="QuickMenuUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","observed native mainline talent");
   foreach(var change in new Action<Frame>[] {v=>v.BridgeVersion=20,v=>v.Surfaces[0].Type="ShopUI",v=>v.Surfaces[0].InputReady=false,v=>v.Surfaces=[v.Surfaces[0],new(){Id=44,Type="MessagePopupUI",Popup=true,Order=99}]}){
    var v=Frame();v.BridgeVersion=21;v.Surfaces[0].Type="QuickMenuUI";change(v);Check(LivePolicy.Gate(c,v,now,false)!="","native talent readiness and context");
   }
   c.Value=0;Check(LivePolicy.Gate(c,f,now,false)!="","talent instance required");
  }
  {
   var f=Frame();f.BridgeVersion=25;f.Surfaces[0].Type="ShopUI";var c=Command();c.Kind="trade_favorite_preview";c.TargetId=0;c.Value=40;c.Items=new long[]{1,2,10,4};
   Check(LivePolicy.Gate(c,f,now,false)=="","bounded native favorite preview");
   foreach(var a in new[]{new long[0],new long[]{1,2,10},new long[]{1,2,-1,4}}){c.Items=a;Check(LivePolicy.Gate(c,f,now,false)!="","invalid favorite selection");}
   c.Items=new long[]{1,2,10,4};f.BridgeVersion=24;Check(LivePolicy.Gate(c,f,now,false)!="","trade requires current bridge");f.BridgeVersion=25;
   f.Surfaces[0].Type="InventoryManageUI";Check(LivePolicy.Gate(c,f,now,false)!="","trade cannot sell equipment inventory");
   f.Surfaces[0].Type="ShopPopupUI";c.Kind="trade_sell_confirm";c.Items=new long[]{1,1001,123,10,4};Check(LivePolicy.Gate(c,f,now,false)=="","food sale confirmation");
   c.Items=new long[]{1,1001,123,10};Check(LivePolicy.Gate(c,f,now,false)!="","sale stack and quote required");
   c.Kind="trade_menu";c.Items=new long[0];f.Surfaces[0].Type="GameFieldDefaultUI";c.Value=7;Check(LivePolicy.Gate(c,f,now,false)=="","cooking talent menu");c.Value=16;Check(LivePolicy.Gate(c,f,now,false)!="","bargain requires real merchant menu");
  }
  foreach(var kind in new[]{"square_shop_nav","square_shop_interact"}){
   var c=Command();c.Kind=kind;c.TargetId=0;c.Value=0;c.Scene="Map3009_003";var f=Frame();f.Scene=c.Scene;f.BridgeVersion=35;f.Surfaces[0].Type="GameFieldDefaultUI";
   Check(LivePolicy.Gate(c,f,now,false)=="","merchant entry in square");
   f.BridgeVersion=32;Check(LivePolicy.Gate(c,f,now,false)!="","merchant requires new bridge");f.BridgeVersion=35;
   c.Value=1;Check(LivePolicy.Gate(c,f,now,false)!="","merchant cannot target arbitrary NPC");c.Value=0;
   f.Scene=c.Scene="Map1003_003";Check(LivePolicy.Gate(c,f,now,false)!="","merchant outside square rejected");
  }
  {
   foreach(var pair in new[]{("quiz_hub","MenuUI"),("quiz_list","MiniEventMainUI"),("quiz_play","MiniEventQuizUI"),("quiz_talk","BalloonScriptUI"),("quiz_leave","MiniEventMainUI")}){
    var f=Frame();f.Surfaces[0].Type=pair.Item2;var c=Command();c.Kind=pair.Item1;c.TargetId=0;c.Value=1;
    c.Items=pair.Item1=="quiz_play"||pair.Item1=="quiz_talk"?new long[]{32,3,4}:new long[0];
    Check(LivePolicy.Gate(c,f,now,false)=="","quiz scoped native action");
    f.BridgeVersion=46;Check(LivePolicy.Gate(c,f,now,false)!="","quiz requires updated bridge");f.BridgeVersion=47;
    f.Surfaces[0].Type="ShopUI";Check(LivePolicy.Gate(c,f,now,false)!="","quiz wrong surface refused");
    f.Surfaces[0].Type=pair.Item2;c.Items=new long[]{0};Check(LivePolicy.Gate(c,f,now,false)!="","quiz malformed identity refused");
   }
   var qf=Frame();qf.Surfaces[0].Type="BalloonScriptUI";
   var toolbar=LiveProtocol.StoryToolbar(77,"BalloonScriptUI/StorySkipUI",qf.Surfaces[0].Order,true,new[]{new Target{Field="_objSkipButton",Enabled=false}});
   var qc=Command();qc.Kind="quiz_talk";qc.TargetId=0;qc.Items=new long[]{32,3,14};qc.Value=512272;
   qf.Surfaces=new[]{qf.Surfaces[0],toolbar};
   Check(!toolbar.Popup&&LivePolicy.Gate(qc,qf,now,false)=="","embedded disabled quiz toolbar does not block answer");
   var popup=new Surface{Id=78,Type="StoryPopupUI",Popup=true,Order=qf.Surfaces[0].Order+1};
   qf.Surfaces=new[]{qf.Surfaces[0],toolbar,popup};Check(LivePolicy.Gate(qc,qf,now,false)=="quiz_popup_blocked","real story confirmation still blocks quiz");
   qf.Surfaces=new[]{qf.Surfaces[0],toolbar};qf.Surfaces[0].Type="ScriptUI";qf.Surfaces[0].NativeContext="story_dialogue";qf.Surfaces[0].Targets=new[]{new Target{Field="_objButtonTouch",Enabled=true}};
   qc.Kind="story_advance";qc.Value=0;qc.Items=new long[0];
   Check(LivePolicy.Gate(qc,qf,now,false)=="","disabled toolbar permits native story touch");
   qf.Surfaces=new[]{qf.Surfaces[0],toolbar,popup};Check(LivePolicy.Gate(qc,qf,now,false)=="story_popup_blocked","native story touch preserves confirmation");
   var df=Frame();df.Surfaces[0].Type="EventUI";var dc=Command();dc.Kind="reward_action";dc.TargetId=0;dc.Value=6;dc.Items=new long[]{560};
   dc.Value=8;Check(LivePolicy.Gate(dc,df,now,false)=="","roulette explicit single draw");df.BridgeVersion=104;Check(LivePolicy.Gate(dc,df,now,false)!="","old bridge refuses explicit single draw");df.BridgeVersion=LiveProtocol.BridgeVersion;dc.Value=6;
   dc.Value=9;Check(LivePolicy.Gate(dc,df,now,false)=="","puzzle bulk open uses current scoped event");df.BridgeVersion=112;Check(LivePolicy.Gate(dc,df,now,false)!="","old bridge refuses puzzle batch");df.BridgeVersion=LiveProtocol.BridgeVersion;dc.Value=10;Check(LivePolicy.Gate(dc,df,now,false)!="","manual puzzle renewal is not exposed");dc.Value=6;
   Check(LivePolicy.Gate(dc,df,now,false)=="","dice scoped start");df.BridgeVersion=46;Check(LivePolicy.Gate(dc,df,now,false)!="","old bridge refuses dice command");
  }
  {
   foreach(var type in new[]{"NewsPopupEventUI","PackagePopupUI"}){
    var f=Frame();f.Surfaces[0].Type=type;f.Surfaces[0].NoticeSuppression="unchecked";
    var c=Command();c.Kind="notice_suppress";c.TargetId=0;
    Check(LivePolicy.Gate(c,f,now,false)=="","native seven-day checkbox");
    c.Kind="back";Check(LivePolicy.Gate(c,f,now,false)=="notice_checkbox_not_confirmed","cannot close before selection");c.Kind="notice_suppress";
    foreach(var state in new[]{"loading","hidden","other_period","checked","none"}){f.Surfaces[0].NoticeSuppression=state;Check(LivePolicy.Gate(c,f,now,false)!="","no blind checkbox toggles");}
    f.Surfaces[0].NoticeSuppression="unchecked";f.Surfaces[0].InputReady=false;Check(LivePolicy.Gate(c,f,now,false)!="","notice animation gate");f.Surfaces[0].InputReady=true;
    f.Surfaces=[f.Surfaces[0],new(){Id=55,Type="MessagePopupUI",Popup=true,Order=9999}];Check(LivePolicy.Gate(c,f,now,false)!="","unrelated confirmation preserved");
    f.Surfaces=[f.Surfaces[0]];f.Surfaces[0].Type="ShopUI";Check(LivePolicy.Gate(c,f,now,false)!="","no arbitrary shop action");
    f.Surfaces[0].Type=type;f.BridgeVersion=66;Check(LivePolicy.Gate(c,f,now,false)!="","old bridge rejected");
   }
  }
  {
   var f=Frame();f.BridgeVersion=73;f.Surfaces[0].Type="MenuUI";
   var c=Command();c.Kind="weekly_book_open";c.TargetId=0;c.Value=0;c.Items=new long[0];
   Check(LivePolicy.Gate(c,f,now,false)=="","weekly book native entry");
   f.BridgeVersion=72;Check(LivePolicy.Gate(c,f,now,false)!="","book rejects old bridge");f.BridgeVersion=73;
   f.Surfaces[0].Type="ShopUI";Check(LivePolicy.Gate(c,f,now,false)!="","book rejects wrong surface");f.Surfaces[0].Type="MenuUI";
   c.Value=1;Check(LivePolicy.Gate(c,f,now,false)!="","book cannot select arbitrary shortcut");c.Value=0;
   f.Surfaces=[f.Surfaces[0],new(){Id=55,Type="MessagePopupUI",Popup=true,Order=9999}];Check(LivePolicy.Gate(c,f,now,false)!="","book preserves unrelated popup");
  }
  {
   foreach(var item in new[]{("weekly_steal_talk","BalloonScriptUI",new long[0]),("weekly_steal_menu","GameFieldDefaultUI",new long[0]),("weekly_steal_talent","QuickMenuUI",new long[]{123,101}),("weekly_steal_confirm","StealInfoPopupUI",new long[]{101}),("collection_query_steal","GameFieldDefaultUI",new long[]{1,2,3})}){
    var f=Frame();f.BridgeVersion=98;f.Surfaces[0].Type=item.Item2;
    var c=Command();c.Kind=item.Item1;c.TargetId=0;c.Value=123;c.Items=item.Item3;
    Check(LivePolicy.Gate(c,f,now,false)=="","weekly steal scoped command");
    f.BridgeVersion=97;Check(LivePolicy.Gate(c,f,now,false)!="","weekly steal rejects old bridge");f.BridgeVersion=98;
    f.Surfaces[0].Type="ShopUI";Check(LivePolicy.Gate(c,f,now,false)!="","weekly steal rejects different surface");f.Surfaces[0].Type=item.Item2;
    c.Items=new long[]{-1};Check(LivePolicy.Gate(c,f,now,false)!="","weekly steal rejects invalid identity");c.Items=item.Item3;
    f.Surfaces=[f.Surfaces[0],new(){Id=55,Type="MessagePopupUI",Popup=true,Order=9999}];Check(LivePolicy.Gate(c,f,now,false)!="","weekly steal preserves unrelated popup");
   }
  }
  string Scalar(object v)=>System.Text.Json.JsonSerializer.Serialize(v);
  object Member(object v,string name)=>((Dictionary<string,object>)v)[name];
  var projectionRows=Enumerable.Range(0,597).Select(i=>(object)new Dictionary<string,object>{{"id",i},{"nested",new Dictionary<string,object>{{"value",i+1}}}}).ToArray();
  string projected=ObservationProjection.Capture(projectionRows,"$self",new[]{"id","nested.value"},1000,Member,Scalar);
  using(var data=System.Text.Json.JsonDocument.Parse(projected)){Check(data.RootElement.GetArrayLength()==597,"projection must include rows after 300");Check(data.RootElement[596].GetProperty("nested.value").GetInt32()==597,"projection final value");}
  foreach(var limit in new[]{0,596,10001}){
   bool rejected=false;try{ObservationProjection.Capture(projectionRows,"$self",new[]{"id"},limit,Member,Scalar);}catch(InvalidOperationException){rejected=true;}Check(rejected,"projection must reject partial/invalid cap");
  }
  {bool rejected=false;try{ObservationProjection.Capture(new object[]{null!},"$self",new[]{"id"},1,Member,Scalar);}catch(InvalidOperationException){rejected=true;}Check(rejected,"null projection row");}
  foreach(string kind in new[]{"weekly_npc_query","weekly_npc_accept","weekly_npc_nav","weekly_npc_talk","weekly_npc_auto","weekly_npc_board_nav","weekly_npc_board_open","weekly_npc_board_scroll","weekly_npc_board_select"}){
   var f=Frame();var c=Command();c.Kind=kind;c.TargetId=0;c.Value=100001;
   f.Surfaces[0].Type=kind=="weekly_npc_accept"?"QuestPopupUI":kind=="weekly_npc_board_scroll"||kind=="weekly_npc_board_select"?"QuestBoardUI":kind=="weekly_npc_talk"?"BalloonScriptUI":kind=="weekly_npc_auto"?"BattleUI_FieldBattle":"GameFieldDefaultUI";
   c.Items=kind=="weekly_npc_accept"||kind=="weekly_npc_nav"||kind=="weekly_npc_auto"?[0]:[];
   if(kind=="weekly_npc_accept"){f.Surfaces[0].NativeContext="weekly_npc_accept:"+c.Value;f.Surfaces=f.Surfaces.Append(new Surface{Id=98,Type="QuestBoardUI",NativeContext="weekly_npc_board"}).ToArray();}
   Check(LivePolicy.Gate(c,f,now,false)=="","current NPC command scoped "+kind);
   f.BridgeVersion=99;Check(LivePolicy.Gate(c,f,now,false)!="","old bridge cannot execute new NPC actions "+kind);f.BridgeVersion=100;
   f.Surfaces=f.Surfaces.Append(new Surface{Id=99,Type="MessagePopupUI",Popup=true}).ToArray();Check(LivePolicy.Gate(c,f,now,false)!="","popup blocks NPC input "+kind);f.Surfaces=f.Surfaces.Take(1).ToArray();
   c.Items=[2];Check(LivePolicy.Gate(c,f,now,false)!="","invalid hunt consent rejected "+kind);
   c.Items=[];c.AccountKey="another";Check(LivePolicy.Gate(c,f,now,false)!="","NPC account guard "+kind);
  }
  {
   Check(LivePolicy.HiddenPickupReady(true,2),"valid hidden pickup");
   Check(!LivePolicy.HiddenPickupReady(false,180),"stale cached time cannot activate research");
   foreach(double remaining in new[]{-1d,0d,1d,double.NaN,double.PositiveInfinity})Check(!LivePolicy.HiddenPickupReady(true,remaining),"invalid/expiring research not picked up");
   var f=Frame();var c=Command();var popup=f.Surfaces[0];popup.Type="MessagePopupUI";popup.Popup=true;popup.Order=10000;popup.NativeContext="talent_inactive_ack_only";c.Kind="talent_error_ack";c.TargetId=0;c.Value=100005;
   Check(LivePolicy.Gate(c,f,now,false)=="","known callback-free talent error acknowledged");
   popup.NativeContext="";Check(LivePolicy.Gate(c,f,now,false)!="","unclassified popup not acknowledged");popup.NativeContext="talent_inactive_ack_only";
   c.Value=100004;Check(LivePolicy.Gate(c,f,now,false)!="","other error not acknowledged");c.Value=100005;
   f.Surfaces=[popup,new(){Id=100,Type="PurchasePopupUI",Popup=true,Order=10001}];Check(LivePolicy.Gate(c,f,now,false)!="","higher purchase popup preserved");f.Surfaces=[popup];
   popup.InputReady=false;Check(LivePolicy.Gate(c,f,now,false)!="","unready error waits");popup.InputReady=true;
   f.BridgeVersion=100;Check(LivePolicy.Gate(c,f,now,false)!="","old bridge rejected for typed recovery");
  }
  Console.WriteLine($"PASS: {cases} live command scope / expiry / screen / disabled-target checks");
 }
}
