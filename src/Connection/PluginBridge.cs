using System;
using System.Linq;
namespace BD2Daily.Live {
 // Versioned extension SDK. Only implementations compiled into this module are eligible.
 internal interface IConnectionExtension {
  int ApiVersion {get;}
  void Execute(Command command,object surface);
  object Capture(string id);
 }
 internal static class PluginHost {
  static readonly IConnectionExtension extension=Load();
  static IConnectionExtension Load(){
   var types=typeof(PluginHost).Assembly.GetTypes().Where(t=>t.IsClass&&!t.IsAbstract&&typeof(IConnectionExtension).IsAssignableFrom(t)).ToArray();
   if(types.Length>1)throw new InvalidOperationException("Multiple connection extensions are not supported");
   var type=types.SingleOrDefault();
   if(type==null)return null;
   var value=(IConnectionExtension)Activator.CreateInstance(type,true);
   if(value.ApiVersion!=1)throw new InvalidOperationException("Plugin bridge API mismatch");
   return value;
  }
  internal static bool Available {get{return extension!=null;}}
  internal static void Execute(Command c,object ui){
   if(extension==null)throw new InvalidOperationException("Plugin is unavailable");
   extension.Execute(c,ui);
  }
  internal static object Capture(string id){
   if(extension==null)throw new InvalidOperationException("Plugin is unavailable");
   return extension.Capture(id);
  }
 }
}
