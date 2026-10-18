using System.Diagnostics;
using BD2.LocalIpc;
namespace BD2Equipment.Live;
public static class EquipmentTransport {
 public static void Configure()=>DesktopFiles.Configure(LiveEntry.Root,LiveProtocol.LiveEntries);
 public static PipeClient Connect(){Configure();using var g=HostedConnection.FindGame(n=>string.Equals(n,"BrownDust II",StringComparison.OrdinalIgnoreCase),"Open exactly one game instance before connecting")??throw new IOException("Open exactly one game instance before connecting");
 return DesktopFiles.Connect(LiveEntry.Root,g.Id,g.StartTime.ToUniversalTime().Ticks);}
 public static void Open(){var pipe=Connect();if(pipe.Fingerprint()!=LiveEntry.Fingerprint)throw new IOException("Reconnect to update the equipment component");pipe.Open(LiveEntry.Fingerprint);}
}
