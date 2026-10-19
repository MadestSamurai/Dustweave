using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace BD2Sichuan.Runtime
{
    internal static class SichuanBindings
    {
        private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        private static readonly Dictionary<string,MemberInfo> cache=new Dictionary<string,MemberInfo>();
        internal static Type Type(string role)=>typeof(SichuanBoardUI).Assembly.GetType(SichuanClient.TypeNames[role],true);
        internal static MemberInfo Api(string role)
        {
            if(cache.TryGetValue(role,out var known))return known;
            var entry=SichuanClient.Apis[role];var type=typeof(SichuanBoardUI).Assembly.GetType(entry[0],true);
            return cache[role]=entry[2]=="method"?(MemberInfo)type.GetMethods(Flags).Single(m=>m.MetadataToken==int.Parse(entry[1])):
                ((MemberInfo)type.GetField(entry[1],Flags)??type.GetProperty(entry[1],Flags)??throw new MissingMemberException(role));
        }
        internal static object Read(string role,object owner)
        {var m=Api(role);return m is FieldInfo f?f.GetValue(owner):((PropertyInfo)m).GetValue(owner,null);}
        internal static object Call(string role,object owner,params object[] args)=>((MethodInfo)Api(role)).Invoke(owner,args);
        internal static int Int(string role,object owner)=>Convert.ToInt32(Read(role,owner));
        internal static float Num(string role,object owner)=>Convert.ToSingle(Read(role,owner));
        internal static bool Flag(string role,object owner)=>(bool)Read(role,owner);
        internal static IList List(string role,object owner)=>(IList)Read(role,owner);
        internal static bool TryBoard(out object ui)
        {object[] args={null};bool active=(bool)Call("UI.Active",null,args);ui=args[0];return active&&ui!=null;}
        internal static Delegate SubscribePair(object model,Action<int,int> callback)
        {var handler=SichuanClient.CreatePairHandler(callback);Call("Model.PairAdd",model,handler);return handler;}
        internal static void UnsubscribePair(object model,Delegate handler)=>Call("Model.PairRemove",model,handler);
        internal static int Validate()
        {
            if(typeof(SichuanBoardUI).Module.ModuleVersionId.ToString()!=SichuanClient.CompiledMvid)
                throw new InvalidOperationException("游戏运行中客户端文件发生变化，请正常重启游戏后重新连接。");
            foreach(var role in SichuanClient.Apis.Keys)if(Api(role)==null)throw new MissingMemberException(role);
            return SichuanClient.Apis.Count;
        }
    }
}
