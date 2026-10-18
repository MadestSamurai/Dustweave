using System;
using System.IO;
using System.Runtime.Serialization.Json;
namespace BD2InfiniteGacha.Runtime
{
    internal static class Storage
    {
        [System.Runtime.Serialization.DataContract] private class EventRecord
        {
            [System.Runtime.Serialization.DataMember] public string At,State,Message,Stage;
            [System.Runtime.Serialization.DataMember] public int Rolls,Skips;
        }
        internal static void Event(string state,string message,Snapshot snapshot=null)
        {
            try{Directory.CreateDirectory(Identity.Root);var path=Path.Combine(Identity.Root,"runtime-events.jsonl");if(File.Exists(path)&&new FileInfo(path).Length>1024*1024){var previous=path+".previous";if(File.Exists(previous))File.Delete(previous);File.Move(path,previous);}
                var value=new EventRecord{At=DateTime.UtcNow.ToString("o"),State=state,Message=message,Stage=snapshot==null?"":snapshot.AnimationStage,Rolls=snapshot==null?0:snapshot.Rolls,Skips=snapshot==null?0:snapshot.SkipCount};
                using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(EventRecord)).WriteObject(stream,value);File.AppendAllText(path,System.Text.Encoding.UTF8.GetString(stream.ToArray())+Environment.NewLine);}
            }catch{}
        }
        internal static T Read<T>(string name) where T:class
        {try{using(var f=new MemoryStream(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(Identity.Root,name))??new byte[0]))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(f);}catch{return null;}}
        internal static void Write(string name,object value)
        {using(var buffer=new MemoryStream()){new DataContractJsonSerializer(value.GetType()).WriteObject(buffer,value);if(BD2.LocalIpc.RuntimeFiles.Write(Path.Combine(Identity.Root,name),buffer.ToArray()))return;}Directory.CreateDirectory(Identity.Root);var path=Path.Combine(Identity.Root,name);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var f=File.Create(temp))new DataContractJsonSerializer(value.GetType()).WriteObject(f,value);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
}
