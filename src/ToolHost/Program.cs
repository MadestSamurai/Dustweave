extern alias sichuan;
using BD2Fishing;
if(args.Length==2&&args[0]=="validate-sichuan"){
    foreach(bool daily in new[]{false,true}){
        var prepared=sichuan::BD2Sichuan.Compatibility.HookCompiler.Prepare(Path.GetFullPath(args[1]),daily:daily);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{daily,status=prepared.Report.Status,bytes=prepared.Payload.Length}));
    }
    return;
}
if(args.Length==1&&args[0]=="connect-sichuan"){
    sichuan::BD2.LocalIpc.DesktopFiles.Configure(sichuan::BD2Sichuan.SichuanIdentity.DataRoot,sichuan::BD2Sichuan.SichuanIdentity.LiveEntries);
    using var sichuanGame=sichuan::BD2Sichuan.SichuanConnection.FindGame();
    sichuan::BD2.LocalIpc.DesktopFiles.Connect(sichuan::BD2Sichuan.SichuanIdentity.DataRoot,sichuanGame.Id,sichuanGame.StartTime.ToUniversalTime().Ticks);
    var lease=sichuan::BD2Sichuan.SichuanJson.Read<sichuan::BD2Sichuan.SichuanActionLease>(Path.Combine(sichuan::BD2Sichuan.SichuanIdentity.DataRoot,"sichuan/execution-lease.json"));
    if(lease!=null&&lease.UntilUtcTicks>DateTime.UtcNow.Ticks)throw new InvalidOperationException("Sichuan already has an active controller");
    Console.WriteLine(new sichuan::BD2Sichuan.SichuanConnection().Connect(Console.WriteLine,daily:true));return;
}
if (args.Length != 1 || args[0] != "connect") throw new ArgumentException("connect | connect-sichuan");
BD2.LocalIpc.DesktopFiles.Configure(FishingIdentity.DataRoot,FishingIdentity.LiveEntries);
using var game=FishingConnection.FindGame();
BD2.LocalIpc.DesktopFiles.Connect(FishingIdentity.DataRoot,game.Id,game.StartTime.ToUniversalTime().Ticks);
var active = FishingJson.Read<FishingControl>(Path.Combine(FishingIdentity.DataRoot, "control.json"));
if (active is not null && active.Valid(DateTime.UtcNow.Ticks, active.ProcessId))
    throw new InvalidOperationException("Fishing already has an active controller; stop it before handing over to daily tasks.");
Console.WriteLine(new FishingConnection().Connect(Console.WriteLine,daily:true));
