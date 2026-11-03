using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;
namespace BD2Equipment.Compatibility;

// Resolve only the small public hunt workflow's structural anchors. New hunt
// support does not add unrelated obfuscated symbols to the equipment contract.
internal static class SupplyBindings
{
 internal static string Source(ModuleDefinition module){
  try{return Resolve(module);}catch(InvalidOperationException ex){
   // A new optional sweep interface must not disable existing craft/refine.
   return "namespace BD2Equipment.Live { internal static class SupplyNames { internal const string Error="+JsonSerializer.Serialize(ex.Message)+"; internal const string PopupTable=\"\",SelectionTable=\"\",Count=\"\",State=\"\",Bonus=\"\",LoaderType=\"\",Loader=\"\"; } }";
  }
 }
 static string Resolve(ModuleDefinition module){
  var popup=module.Types.Single(t=>t.FullName=="HuntDispatchPopupUI");
  var selection=module.Types.Single(t=>t.FullName=="HuntOrAirwayUI");
  string Table(TypeDefinition t)=>t.Fields.Single(f=>f.FieldType.FullName=="Proto.Design.common.HuntDispatchTable").Name;
  var count=popup.Methods.Single(m=>m.Name=="OnValueChanged"&&m.Parameters.Count==1).Body.Instructions.Where(i=>i.OpCode.Code==Code.Stfld&&i.Operand is FieldReference f&&f.DeclaringType==popup&&f.FieldType.FullName=="System.Int32").Select(i=>((FieldReference)i.Operand).Name).Distinct().Single();
  var loaders=popup.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>().Where(m=>!m.HasThis&&m.ReturnType.FullName=="System.Void"&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"System.Int32","System.Action"})).GroupBy(m=>m.FullName).Select(g=>g.First()).ToArray();
  if(loaders.Length!=1)throw new InvalidOperationException("Hunt pack loader is ambiguous");
  var bonus=popup.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.Operand is MethodReference mr&&mr.DeclaringType.FullName=="Proto.Net.SkyWayScheduleDBInfo"&&mr.Name=="get_BonusRate").Select(i=>i.Next?.Operand as FieldReference).Where(f=>f!=null&&f.DeclaringType==popup).Select(f=>f!.Name).Distinct().Single();
  var state=selection.Fields.Single(p=>p.FieldType.Scope==module&&p.FieldType.Resolve()?.IsEnum==true&&p.FieldType.Resolve().Fields.Any(f=>f.Name=="ABLE"));
  string Q(string s)=>JsonSerializer.Serialize(s);
  return "namespace BD2Equipment.Live { internal static class SupplyNames { internal const string Error=\"\"; internal const string PopupTable="+Q(Table(popup))+", SelectionTable="+Q(Table(selection))+", Count="+Q(count)+", State="+Q(state.Name)+", Bonus="+Q(bonus)+", LoaderType="+Q(loaders[0].DeclaringType.FullName)+", Loader="+Q(loaders[0].Name)+"; } }";
 }
}
