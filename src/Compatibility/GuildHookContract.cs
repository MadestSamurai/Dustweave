using Mono.Cecil;
using System.Text.Json;
namespace Dustweave.Compatibility;
public static class GuildHookContract {
 public static BindingContract Generate(string managed){
  using var i=new MetadataIndex(Path.Combine(managed,"Assembly-CSharp.dll"));
  var rules=new[]{
   ("Guild.Member","ὨὬὣὫὩὯὩὩὣὠὧ","ὦὬὯὫὥὬὪὨὭὡὮ"),
   ("Guild.Base","ὨὬὣὫὩὯὩὩὣὠὧ","ὦὨὩὠὪὫὤὬὢὥὫ"),
   ("Guild.Info","ὠὢὮὤὡὩὮὯὠὧὭ","ὡὩὮὢὥὫὪὤὥὮὤ"),
   ("Guild.Response","ὠὢὮὤὡὩὮὯὠὧὭ/ὦὫὥὣὠὩὣὩὣὭὧ","ὨὨὮὯὦὢὣὩὩὯὮ"),
   ("Guild.Callback","ὠὢὮὤὡὩὮὯὠὧὭ/ὦὫὥὣὠὩὣὩὣὭὧ","onRefreshGuildInfo"),
   ("Services.Timer","ὧὥὢὯὯὣὩὧὡὠὨ","ὤὬὯὣὡὭὪὤὥὧὧ"),
   ("Services.App","ὧὥὢὯὯὣὩὧὡὠὨ","ὦὨὪὥὯὮὡὠὧὡὤ"),
   ("Services.Network","ὧὥὢὯὯὣὩὧὡὠὨ","ὢὪὢὦὩὬὪὤὢὫὯ"),
   ("Network.Server","BDNetwork.NetworkManager","ὫὪὢὦὮὪὣὫὤὫὬ"),
   ("Menu.Guild","MenuUI","_buttonGuild"),("Popup.Button","GuildJoinAttendancePopupUI","_buttonBackground"),
   ("Popup.Attendance","GuildJoinAttendancePopupUI","_objAttendanceText"),("UI.Popup","UIBase","_isPopupUI"),("UI.Back","UIBase","_objBackButton"),
   ("UI.IsHud","ὨὧὠὯὪὦὩὣὤὢὡ","ὩὠὮὥὫὧὢὣὯὯὫ")
  };
  var entries=rules.Select(r=>(Role:r.Item1,Type:i.Find(r.Item2),Member:MetadataIndex.Members(i.Find(r.Item2)).Single(m=>m.Name==r.Item3))).ToArray();
  var types=entries.GroupBy(e=>e.Type).Select(g=>new TypeContract(g.Key.FullName,i.Shape(g.Key),g.Key.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Count>=10).OrderByDescending(m=>m.Body.Instructions.Count).Take(8).Select(i.Body).ToArray(),g.Select(e=>e.Member).Distinct().Select(m=>new MemberContract(m.Name,MetadataIndex.Signature(m),i.MemberBody(m),i.Uses(m))).ToArray())).ToArray();
  return new(1,types,entries.Select(e=>new ApiContract(e.Role,e.Type.FullName,e.Member.Name,-1,MetadataIndex.Signature(e.Member))).ToArray(),new());
 }
 public static string Source(MetadataIndex index,out BindingReport report){
  var contract=JsonSerializer.Deserialize<BindingContract>(DailyHookCompiler.Resource("BD2Daily.GuildContract.json"))!;
  var r=BindingResolver.Resolve(index,contract);report=r.Report;
  if(r.Report.Status!="compatible")return "namespace BD2Daily.Runtime { internal static class GuildBindings { internal const bool Supported=false; internal const string Mvid=\"\"; internal static System.Reflection.MemberInfo Get(string role){throw new System.NotSupportedException(\"Guild interface unsupported\");} } }";
  var bindings=contract.Apis.Select(a=>{var m=BindingResolver.Api(r,a);return "case "+JsonSerializer.Serialize(a.Role)+":return typeof(Proto.Net.UserDBInfo).Module.ResolveMember("+(m is PropertyDefinition p?p.GetMethod.MetadataToken:m.MetadataToken).ToInt32()+");";});
  return "namespace BD2Daily.Runtime { internal static class GuildBindings { internal const bool Supported=true; internal const string Mvid="+JsonSerializer.Serialize(index.Module.Mvid.ToString())+"; internal static System.Reflection.MemberInfo Get(string role){if(typeof(Proto.Net.UserDBInfo).Module.ModuleVersionId.ToString()!=Mvid)throw new System.InvalidOperationException(\"Client changed\");switch(role){"+string.Join("",bindings)+"default:throw new System.ArgumentException(role);}} } }";
 }
}
