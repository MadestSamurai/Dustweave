using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Google.Protobuf;
namespace BD2Equipment.Live {
 internal static class NativeCatalog {
  [DataContract] sealed class Request { [DataMember] public string Id; [DataMember] public long ExpiresUtcTicks; [DataMember] public int Part; }
  static readonly string[] Tables={"EquipmentTable","EquipmentMakingTable","EquipmentGrowthTable","EquipmentGradeTable","EquipmentExpectTable","EquipmentRankTable","EquipmentOptionTable","RandomBoxTable","RewardGroupTable","TalentSkillTable","ResourceTable","CharTable","NameTextTable","LocalTextTable"};
  static readonly MethodInfo ReadTable=NativeTableReader.Resolve(typeof(RawDataManager));
  static object Manager(){
   if(ReadTable.IsStatic)return null;
   var properties=new List<PropertyInfo>();for(var t=typeof(RawDataManager);t!=null;t=t.BaseType)properties.AddRange(t.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.DeclaredOnly).Where(p=>p.PropertyType==typeof(RawDataManager)&&p.GetIndexParameters().Length==0&&p.GetGetMethod(true)!=null));
   var value=properties.Single().GetValue(null,null);if(value==null)throw new InvalidOperationException("Current game tables are not loaded");return value;
  }
  static readonly Dictionary<string,string> rows=new Dictionary<string,string>();
  static byte[] payload;static int sentPart=-1;
  static CatalogProjection projection=new CatalogProjection();
  static string active="",completed="",actor="";static int next;
  internal static IEnumerable<T> Table<T>(string name){
   return ((IEnumerable)ReadTable.MakeGenericMethod(typeof(T)).Invoke(Manager(),NativeTableReader.Arguments(ReadTable,name))).Cast<T>();
  }
  internal static bool IsNormalRecipe(int id){
   var recipe=Table<Proto.Design.common.EquipmentMakingTable>("EquipmentMakingTable").Single(r=>r.Id==id);
   if(recipe.ResultItemType!=9||recipe.ResultItemCount!=1)return false;
   var box=Table<Proto.Design.common.RandomBoxTable>("RandomBoxTable").Single(b=>b.Id==recipe.ResultItemId);
   var reward=Table<Proto.Design.common.RewardGroupTable>("RewardGroupTable").Single(r=>r.Id==box.RewardGroupId);
   var equipment=Table<Proto.Design.common.EquipmentTable>("EquipmentTable").ToDictionary(g=>g.Id);
   return reward.ItemId.Count>0&&reward.ItemId.Count==reward.ItemType.Count&&reward.ItemType.All(t=>t==10)&&reward.ItemId.All(i=>equipment.ContainsKey(i)&&equipment[i].Grade==1);
  }
  static string Quote(string text){using(var s=new MemoryStream()){new DataContractJsonSerializer(typeof(string)).WriteObject(s,text);return Encoding.UTF8.GetString(s.ToArray());}}
  static void Save(string path,string text){if(BD2.LocalIpc.RuntimeFiles.Write(path,Encoding.UTF8.GetBytes(text)))return;var tmp=path+".tmp";File.WriteAllText(tmp,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
  internal static void Tick(string root,Frame frame){
   var path=Path.Combine(root,"catalog-request.json");var bytes=BD2.LocalIpc.RuntimeFiles.Read(path);if(bytes==null){payload=null;return;}if(!LivePolicy.GameplayReady(frame))return;
   Request request;using(var f=new MemoryStream(bytes))request=(Request)new DataContractJsonSerializer(typeof(Request)).ReadObject(f);
   if(request==null||request.ExpiresUtcTicks<DateTime.UtcNow.Ticks){payload=null;return;}
   if(request.Id==completed){
    if(payload!=null&&request.Part>=0&&request.Part!=sentPart){
     try{var part=CatalogTransfer.Part(payload,request.Part);Save(Path.Combine(root,"catalog-part.json"),"{\"id\":"+Quote(request.Id)+",\"index\":"+request.Part+",\"sha256\":"+Quote(CatalogTransfer.Hash(part))+",\"data\":"+Quote(Convert.ToBase64String(part))+"}");sentPart=request.Part;}
     catch(Exception e){Save(Path.Combine(root,"catalog-part.json"),"{\"id\":"+Quote(request.Id)+",\"index\":"+request.Part+",\"error\":"+Quote(e.GetBaseException().Message)+"}");}
    }return;
   }
   Guid id;if(!Guid.TryParseExact(request.Id,"N",out id))return;
   var identity=frame.ProcessId+"|"+frame.ProcessStartTicks+"|"+frame.Instance+"|"+frame.AccountKey+"|"+frame.PlayerKey;
   if(active!=request.Id){active=request.Id;actor=identity;rows.Clear();next=0;payload=null;sentPart=-1;projection=new CatalogProjection();}
   string error="";
   try {
    if(identity!=actor)throw new InvalidOperationException("Account changed during catalog capture");
    var name=Tables[next];var type=typeof(UIBase).Assembly.GetType("Proto.Design.common."+name,true);
    var table=((IEnumerable)ReadTable.MakeGenericMethod(type).Invoke(Manager(),NativeTableReader.Arguments(ReadTable,name))).Cast<IMessage>().Where(m=>projection.Include(name,m)).ToArray();
    if(table.Length==0)throw new InvalidOperationException("Native table unavailable: "+name);
    foreach(var row in table)projection.Remember(name,row);
    rows[name]="["+string.Join(",",table.Select(m=>JsonFormatter.Default.Format(m)).ToArray())+"]";
    next++;if(next<Tables.Length)return; // One table per observation tick, only on explicit refresh.
   }catch(Exception e){error=e.GetBaseException().ToString();}
   try{
    if(error.Length>0)throw new InvalidOperationException(error);
    payload=Encoding.UTF8.GetBytes("{\"tables\":{"+string.Join(",",rows.Select(p=>Quote(p.Key)+":"+p.Value).ToArray())+"}}");
    int parts=CatalogTransfer.Parts(payload.Length);
    Save(Path.Combine(root,"catalog.json"),"{\"format\":2,\"id\":"+Quote(active)+",\"clientMvid\":"+Quote(ClientNames.Mvid)+",\"actor\":"+Quote(actor)+",\"bytes\":"+payload.Length+",\"parts\":"+parts+",\"partBytes\":"+CatalogTransfer.PartBytes+",\"sha256\":"+Quote(CatalogTransfer.Hash(payload))+"}");
   }catch(Exception e){payload=null;Save(Path.Combine(root,"catalog.json"),"{\"id\":"+Quote(active)+",\"actor\":"+Quote(actor)+",\"error\":"+Quote(e.GetBaseException().Message)+"}");}
   rows.Clear();
   completed=active;
  }
 }
}
