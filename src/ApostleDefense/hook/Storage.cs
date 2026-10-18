using System;
using System.IO;
using System.Runtime.Serialization.Json;
namespace BD2ApostleDefense.Runtime
{
    internal static class Storage
    {
        internal static void AppendNetwork(NetworkEvidence value)
        {
            Directory.CreateDirectory(Identity.Root);var path=Path.Combine(Identity.Root,"network-current.jsonl");
            if(File.Exists(path)&&new FileInfo(path).Length>2*1024*1024){var previous=Path.Combine(Identity.Root,"network-previous.jsonl");if(File.Exists(previous))File.Delete(previous);File.Move(path,previous);}
            using(var f=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read)){new DataContractJsonSerializer(typeof(NetworkEvidence)).WriteObject(f,value);f.WriteByte(10);}
        }
        internal static void AppendFlow(FlowEvidence value)
        {
            Directory.CreateDirectory(Identity.Root);var path=Path.Combine(Identity.Root,"flow-current.jsonl");
            if(File.Exists(path)&&new FileInfo(path).Length>4*1024*1024){var previous=Path.Combine(Identity.Root,"flow-previous.jsonl");if(File.Exists(previous))File.Delete(previous);File.Move(path,previous);}
            using(var f=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read)){new DataContractJsonSerializer(typeof(FlowEvidence)).WriteObject(f,value);f.WriteByte(10);}
        }
        internal static T Read<T>(string name) where T:class
        {try{using(var f=new MemoryStream(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(Identity.Root,name))??new byte[0]))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(f);}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}catch(Exception e){IoDiagnostics.Record(Identity.Root,"read",Path.Combine(Identity.Root,name),e);return null;}}
        internal static void Write(string name,object value)
        {using(var buffer=new MemoryStream()){new DataContractJsonSerializer(value.GetType()).WriteObject(buffer,value);if(BD2.LocalIpc.RuntimeFiles.Write(Path.Combine(Identity.Root,name),buffer.ToArray()))return;}AtomicFiles.Write(Path.Combine(Identity.Root,name),f=>new DataContractJsonSerializer(value.GetType()).WriteObject(f,value));}
    }
}
