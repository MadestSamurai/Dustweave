namespace BD2Equipment.Live;
public static class PolicyChecks {
 public static string Run(){int checks=0;void Check(bool value){checks++;if(!value)throw new Exception("Connection policy check "+checks);}
 long now=DateTime.UtcNow.Ticks;var f=new Frame{ProcessId=1,ProcessStartTicks=2,AtUtcTicks=now,Instance="i",AccountKey="a",PlayerKey="p",Scene="home",UiToken="u",Surfaces=new[]{new Surface{Id=3,Type="EquipmentUpgradeUI",Path="ui/equipment"}}};
 var c=new Command{Id=Guid.NewGuid().ToString("N"),Kind="equipment_refine_batch",ProcessId=1,ProcessStartTicks=2,ObservedUtcTicks=now,ExpiresUtcTicks=now+TimeSpan.FromSeconds(10).Ticks,Instance="i",AccountKey="a",PlayerKey="p",Scene="home",UiToken="u",SurfaceId=3,Value=22,Items=new long[]{7,5000,400000,150000}};
 string Gate()=>LivePolicy.Gate(c,f,now,false);Check(Gate()=="");c.Items[1]=5001;Check(Gate()!="");c.Items[1]=5000;c.Value=25;Check(Gate()!="");c.Value=22;c.Items[2]=-1;Check(Gate()!="");c.Items[2]=400000;
 c.AccountKey="other";Check(Gate()=="identity_changed");c.AccountKey="a";c.UiToken="old";Check(Gate()=="screen_changed");c.UiToken="u";f.AtUtcTicks=now-TimeSpan.FromSeconds(4).Ticks;Check(Gate()=="stale_frame");f.AtUtcTicks=now;
 Check(LivePolicy.Gate(c,f,now,true)=="another_operation_owns_game");c.Kind="trade_buy_confirm";Check(Gate()=="unsupported_command");c.Kind="equipment_refine_batch";f.Surfaces[0].InputReady=false;Check(Gate()=="ui_not_ready");f.Surfaces[0].InputReady=true;
 f.Surfaces=f.Surfaces.Concat(new[]{new Surface{Id=4,Type="MessagePopupUI",Popup=true,Order=2,Path="ui/message"}}).ToArray();Check(Gate()=="foreground_popup");f.Surfaces=f.Surfaces.Take(1).ToArray();c.Kind="powder_options";c.Items=new long[]{100};c.Value=7;f.Surfaces[0].Type="EquipmentUpgradePopupUI";Check(Gate()=="");c.Value=0;Check(Gate()!="");c.Value=7;c.Items[0]=0;Check(Gate()!="");
 Check(LiveProtocol.Ready(f,1,2,now));Check(!LiveProtocol.Ready(f,1,3,now));
 c.Kind="back";f.Surfaces=new[]{new Surface{Id=3,Type="ItemGetPopupUI",Path="ui/item",Popup=true,Order=1000},new Surface{Id=4,Type="EquipmentInfoPopupUI",Path="ui/item/card",Popup=true,Order=1000}};
 Check(Gate()=="");f.Surfaces[1].Path="ui/other";Check(Gate()=="foreground_popup");
return "Equipment connection checks: "+checks;
 }
}