using System;
using System.IO;
using System.Runtime.Serialization.Json;
namespace BD2Daily.Runtime
{
    internal static class LocalStorage
    {
        internal static string Root {get{return DailyIdentity.DataRoot;}}
        internal static T Read<T>(string name) where T:class
        {try{var path=Path.Combine(Root,name);if(BD2.LocalIpc.RuntimeFiles.Handles(path)){var bytes=BD2.LocalIpc.RuntimeFiles.Read(path);if(bytes==null)return null;using(var memory=new MemoryStream(bytes))return(T)new DataContractJsonSerializer(typeof(T)).ReadObject(memory);}using(var s=new FileStream(Path.Combine(Root,name),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s);}catch{return null;}}
        internal static void Write(string name,object data)
        {var path=Path.Combine(Root,name);using(var memory=new MemoryStream()){new DataContractJsonSerializer(data.GetType()).WriteObject(memory,data);if(BD2.LocalIpc.RuntimeFiles.Write(path,memory.ToArray()))return;}Directory.CreateDirectory(Root);var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var s=File.Create(tmp))new DataContractJsonSerializer(data.GetType()).WriteObject(s,data);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
    }
}
