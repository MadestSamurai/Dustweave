using System.Text.Json;
namespace BD2InfiniteGacha;
public static class Diagnostics
{
 public static void Write(string root,string source,string state,string message)
 {
  try{Directory.CreateDirectory(root);string path=Path.Combine(root,source+"-events.jsonl");
   if(File.Exists(path)&&new FileInfo(path).Length>1024*1024)File.Move(path,path+".previous",true);
   File.AppendAllText(path,JsonSerializer.Serialize(new{at=DateTime.UtcNow,version=ApplicationInfo.Version,state,message})+Environment.NewLine);
  }catch(IOException){}catch(UnauthorizedAccessException){}
 }
}
