using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace BD2Daily.Live {
 // Cache only immutable CLR metadata, never a Unity object, getter result or
 // account value. Type identity includes its assembly; renamed client aliases
 // must be resolved before lookup. Getters still run on every observation.
 internal sealed class ObservationMetadata {
  const BindingFlags InstanceFlags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
  readonly Type fieldStop;
  readonly ConcurrentDictionary<Type,FieldInfo[]> hierarchy=new ConcurrentDictionary<Type,FieldInfo[]>();
  readonly ConcurrentDictionary<Type,FieldInfo[]> instanceFields=new ConcurrentDictionary<Type,FieldInfo[]>();
  readonly ConcurrentDictionary<Type,Members> types=new ConcurrentDictionary<Type,Members>();
  internal ObservationMetadata(Type fieldStop=null){this.fieldStop=fieldStop;}
  internal FieldInfo[] Fields(Type type){FieldInfo[] fields;return hierarchy.TryGetValue(type,out fields)?fields:hierarchy.GetOrAdd(type,CollectFields);}
  FieldInfo[] CollectFields(Type type){
   var result=new List<FieldInfo>();
   for(var t=type;t!=null&&t!=fieldStop;t=t.BaseType)
    result.AddRange(t.GetFields(InstanceFlags|BindingFlags.DeclaredOnly));
   return result.ToArray();
  }
  internal FieldInfo[] InstanceFields(Type type){FieldInfo[] fields;return instanceFields.TryGetValue(type,out fields)?fields:instanceFields.GetOrAdd(type,CollectInstanceFields);}
  static FieldInfo[] CollectInstanceFields(Type type){return type.GetFields(InstanceFlags);}
  static Members CreateMembers(Type type){return new Members(type);}
  internal object Read(object instance,Type type,string name,bool isStatic=false){
   var member=types.GetOrAdd(type,CreateMembers).Value(name,isStatic);
   var property=member as PropertyInfo;
   return property!=null?property.GetValue(instance,null):((FieldInfo)member).GetValue(instance);
  }
  internal MethodInfo Method(Type type,string name,bool isStatic=false){return types.GetOrAdd(type,CreateMembers).Method(name,isStatic);}
  sealed class Members {
   readonly Type type;
   readonly ConcurrentDictionary<string,MemberInfo> instanceValues=new ConcurrentDictionary<string,MemberInfo>(StringComparer.Ordinal);
   readonly ConcurrentDictionary<string,MemberInfo> staticValues=new ConcurrentDictionary<string,MemberInfo>(StringComparer.Ordinal);
   readonly ConcurrentDictionary<string,MethodInfo> instanceMethods=new ConcurrentDictionary<string,MethodInfo>(StringComparer.Ordinal);
   readonly ConcurrentDictionary<string,MethodInfo> staticMethods=new ConcurrentDictionary<string,MethodInfo>(StringComparer.Ordinal);
   internal Members(Type type){this.type=type;}
   internal MemberInfo Value(string name,bool isStatic){
    var cache=isStatic?staticValues:instanceValues;MemberInfo value;
    if(cache.TryGetValue(name,out value))return value;
    var flags=BindingFlags.Public|BindingFlags.NonPublic|(isStatic?BindingFlags.Static:BindingFlags.Instance);
    for(var t=type;t!=null;t=t.BaseType){
     var p=t.GetProperty(name,flags|BindingFlags.DeclaredOnly);
     if(p!=null&&p.GetIndexParameters().Length==0){cache.TryAdd(name,p);return p;}
     var f=t.GetField(name,flags|BindingFlags.DeclaredOnly);
     if(f!=null){cache.TryAdd(name,f);return f;}
    }
    throw new MissingMemberException(type.FullName,name);
   }
   internal MethodInfo Method(string name,bool isStatic){
    var cache=isStatic?staticMethods:instanceMethods;MethodInfo value;
    if(cache.TryGetValue(name,out value))return value;
    var flags=BindingFlags.Public|BindingFlags.NonPublic|(isStatic?BindingFlags.Static:BindingFlags.Instance);
    value=type.GetMethod(name,flags,null,Type.EmptyTypes,null);
    if(value!=null)cache.TryAdd(name,value);
    return value;
   }
  }
 }
}
