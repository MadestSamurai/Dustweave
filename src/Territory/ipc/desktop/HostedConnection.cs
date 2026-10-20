using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace BD2.LocalIpc {
 // Optional host contract. Standalone launches have no environment descriptor and keep their own connection.
 public static class HostedConnection {
  public static bool Enabled=>!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL"));
  // The parent already selected a game inside its own Sandboxie scope. Never
  // rediscover all games or fall back to another account when that game exits.
  // The caller owns the returned Process and must dispose it.
  public static Process? FindGame(Func<string,bool> isGame,string multipleError){
   if(Enabled){
    const string changed="游戏会话已变化，请返回日常助手重新连接。";
    if(!int.TryParse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_PID"),out int pid)||pid<=0
       ||!long.TryParse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_START"),out long start)||start<=0)
     throw new InvalidOperationException(changed);
    Process? game=null;
    try{
     game=Process.GetProcessById(pid);
     if(game.HasExited||game.StartTime.ToUniversalTime().Ticks!=start||!isGame(game.ProcessName))
      throw new InvalidOperationException(changed);
     return game;
    }catch(ArgumentException error){game?.Dispose();throw new InvalidOperationException(changed,error);}
     catch{game?.Dispose();throw;}
   }
   var games=new List<Process>();
   foreach(var game in Process.GetProcesses()){
    bool keep=false;
    try{keep=isGame(game.ProcessName);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
    if(keep)games.Add(game);else game.Dispose();
   }
   if(games.Count==1)return games[0];
   foreach(var game in games)game.Dispose();
   if(games.Count>1)throw new InvalidOperationException(multipleError);
   return null;
  }
  public static bool TryOpen(PipeClient pipe,int pid,long start){
   if(!Enabled)return false;
   string tool=Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL")!;
   string owner=Environment.GetEnvironmentVariable("BD2_DAILY_SUITE_OWNER")!;
   string root=Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT")!;
   if(!int.TryParse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_PID"),out int expectedPid)||!long.TryParse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_START"),out long expectedStart)||pid!=expectedPid||start!=expectedStart)
    throw new InvalidOperationException("游戏会话已变化，请返回日常助手重新连接。");
   var control=new PipeClient(Path.Combine(root,"suite"),pid,start);
   using var state=JsonDocument.Parse(control.Read("status.json")??throw new IOException("统一会话未就绪。"));
   var s=state.RootElement;
   if(s.GetProperty("State").GetString()!="ready"||s.GetProperty("Tool").GetString()!=tool||s.GetProperty("Owner").GetString()!=owner||s.GetProperty("At").GetInt64()<DateTime.UtcNow.AddSeconds(-5).Ticks)
    throw new InvalidOperationException("此功能的统一会话已停止，请返回日常助手。");
   string fingerprint=Environment.GetEnvironmentVariable("BD2_DAILY_TOOL_FINGERPRINT")??"";
   if(string.IsNullOrEmpty(fingerprint)||pipe.Fingerprint()!=fingerprint)throw new InvalidOperationException("功能组件与统一会话不一致，请返回日常助手。");
   pipe.Open(fingerprint);return true;
  }
 }
}
