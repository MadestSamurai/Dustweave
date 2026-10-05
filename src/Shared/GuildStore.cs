#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
namespace BD2Daily {
 // Both Mono and the desktop use the same strict journal. Failure never means "no prior operation".
 public sealed class GuildStore {
  public readonly string Root;
  public GuildStore(string root){Root=root;}
  public static bool ValidId(string id){Guid parsed;return id!=null&&id.Length==32&&Guid.TryParseExact(id,"N",out parsed);}
  public static T Read<T>(string path) where T:class {
   try{if(LiveStore.Handles!=null&&LiveStore.Handles(path)){var bytes=LiveStore.Read(path);if(bytes==null)return null;using(var memory=new MemoryStream(bytes))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(memory);}using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(f)??throw new InvalidDataException("Empty operation record");}
   catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
  }
  public static void Write(string path,object value,bool overwrite=true){
   if(LiveStore.Handles!=null&&LiveStore.Handles(path)){using(var memory=new MemoryStream()){new DataContractJsonSerializer(value.GetType()).WriteObject(memory,value);LiveStore.Write(path,memory.ToArray(),!overwrite);return;}}
   Directory.CreateDirectory(Path.GetDirectoryName(path));var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(value.GetType()).WriteObject(f,value);f.Flush(true);}if(overwrite&&File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
   finally{if(File.Exists(tmp))File.Delete(tmp);}
  }
  public string ReceiptPath(string id){if(!ValidId(id))throw new InvalidDataException("Invalid operation ID");return Path.Combine(Root,"guild","operations",id+".json");}
  public GuildReceipt Receipt(string id){var r=Read<GuildReceipt>(ReceiptPath(id));if(r!=null)Validate(r,id);return r;}
  public void Save(GuildReceipt r){Validate(r,r.Id);r.AtUtcTicks=DateTime.UtcNow.Ticks;Write(ReceiptPath(r.Id),r);}
  private static bool Key(string key){return key!=null&&key.Length==64&&key.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'));}
  private static void Validate(GuildReceipt r,string id){if(r.Id!=id||!ValidId(r.Id)||!Key(r.AccountKey)||!Key(r.PlayerKey)||!Key(r.ServerKey)||!Key(r.GuildKey)||string.IsNullOrEmpty(r.CycleKey)||r.StartedUtcTicks<=0||!new[]{"prepared","dispatching","waiting_response","response_received","completed","checked_no_grant","confirmed_cleanup_pending","skipped","rejected","unknown"}.Contains(r.State))throw new InvalidDataException("Invalid guild operation record");}
  public GuildReceipt Prior(DailySnapshot s,string ignoreId=""){
   var path=Path.Combine(Root,"guild","operations");if(!Directory.Exists(path))return null;GuildReceipt latest=null;
   foreach(var file in Directory.GetFiles(path,"*.json")){
    var id=Path.GetFileNameWithoutExtension(file);var r=Receipt(id);if(r==null||id==ignoreId||r.AccountKey!=s.AccountKey||r.PlayerKey!=s.PlayerKey||r.ServerKey!=s.Guild.ServerKey)continue;
    bool unresolved=r.State=="prepared"||r.State=="dispatching"||r.State=="waiting_response"||r.State=="response_received"||r.State=="unknown";
    if(unresolved)return r; // An ambiguous operation blocks retries even after a reset.
    if(r.CycleKey==s.Guild.CycleKey&&r.GuildKey==s.Guild.GuildKey&&(r.State=="completed"||r.State=="checked_no_grant"||r.State=="confirmed_cleanup_pending")&&(latest==null||r.StartedUtcTicks>latest.StartedUtcTicks))latest=r;
   }return latest;
  }
  public string TracePath(string id){if(!ValidId(id))throw new InvalidDataException("Invalid trace ID");return Path.Combine(Root,"guild","traces",id+".jsonl");}
  public void Append(string id,GuildTrace trace){var path=TracePath(id);Directory.CreateDirectory(Path.GetDirectoryName(path));using(var bytes=new MemoryStream()){new DataContractJsonSerializer(typeof(GuildTrace)).WriteObject(bytes,trace);using(var f=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read)){bytes.Position=0;bytes.CopyTo(f);f.WriteByte(10);f.Flush(true);}}}
  public static GuildReceipt Intent(string id,DailySnapshot s){return new GuildReceipt{Id=id,AccountKey=s.AccountKey,PlayerKey=s.PlayerKey,GuildKey=s.Guild.GuildKey,ServerKey=s.Guild.ServerKey,CycleKey=s.Guild.CycleKey,ClientMvid=s.Guild.ClientMvid,StartedUtcTicks=DateTime.UtcNow.Ticks};}
 }
}
