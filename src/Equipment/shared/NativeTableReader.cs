using System;
using System.Linq;
using System.Reflection;
namespace BD2Equipment.Live {
 public static class NativeTableReader {
  // The one-string overload takes SQL, not a table name. Use the native table API.
  public static MethodInfo Resolve(Type manager){
   var methods=manager.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance).Where(m=>{
    if(m.Name!="GetTableList"||!m.IsGenericMethodDefinition||m.GetGenericArguments().Length!=1)return false;
    var p=m.GetParameters();return p.Length==4&&p[0].ParameterType.IsEnum&&p[1].ParameterType==typeof(string)&&p[2].ParameterType==typeof(string)&&p[3].ParameterType.IsEnum&&Enum.IsDefined(p[0].ParameterType,"DB_COMMON")&&Enum.IsDefined(p[3].ParameterType,"ASC");
   }).ToArray();
   if(methods.Length!=1)throw new InvalidOperationException("Native table-name reader is unavailable or ambiguous: "+methods.Length);
   return methods[0];
  }
  public static object[] Arguments(MethodInfo method,string table){
   if(string.IsNullOrEmpty(table)||!table.EndsWith("Table",StringComparison.Ordinal)||!table.All(c=>char.IsLetterOrDigit(c)||c=='_'))throw new ArgumentException("Invalid equipment table name");
   var p=method.GetParameters();return new object[]{Enum.Parse(p[0].ParameterType,"DB_COMMON"),table,string.Empty,Enum.Parse(p[3].ParameterType,"ASC")};
  }
 }
}
