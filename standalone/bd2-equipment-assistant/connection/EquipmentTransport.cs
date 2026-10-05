using System.Diagnostics;
using BD2.LocalIpc;
namespace BD2Equipment.Live;
public static class EquipmentTransport {
 public static void Configure()=>DesktopFiles.Configure(LiveEntry.Root,LiveProtocol.LiveEntries);
 public static PipeClient Connect(){Configure();var games=Process.GetProcessesByName("BrownDust II");try{
 if(games.Length!=1)throw new IOException("Open exactly one game instance before connecting");var g=games[0];return DesktopFiles.Connect(LiveEntry.Root,g.Id,g.StartTime.ToUniversalTime().Ticks);
 }finally{foreach(var g in games)g.Dispose();}}
 public static void Open(){var pipe=Connect();if(pipe.Fingerprint()!=LiveEntry.Fingerprint)throw new IOException("Reconnect to update the equipment component");pipe.Open(LiveEntry.Fingerprint);}
}
