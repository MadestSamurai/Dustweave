#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
namespace BD2Equipment.Live {
 // Bounded, complete scalar projection. No silent truncation and no business calls.
 internal static class ObservationProjection {
  internal static object Resolve(object value,string path,Func<object,string,object> member){
   if(path=="$self")return value;
   foreach(var part in path.Split('.')){if(value==null)throw new InvalidOperationException("Null observation: "+path);value=member(value,part);}return value;
  }
  internal static string Capture(object root,string path,string[] fields,int limit,Func<object,string,object> member,Func<object,string> scalar){
   if(limit<1||limit>10000)throw new InvalidOperationException("Projection limit must be 1..10000");
   if(fields==null||fields.Length==0||fields.Length>32||fields.Distinct().Count()!=fields.Length)throw new InvalidOperationException("Invalid projection fields");
   var collection=Resolve(root,path,member) as IEnumerable;
   if(collection==null||collection is string)throw new InvalidOperationException("Expected observed collection");
   var rows=new List<string>();
   foreach(var item in collection){
    if(rows.Count==limit)throw new InvalidOperationException("Observed collection exceeds projection limit; no partial result");
    if(item==null)throw new InvalidOperationException("Null observation row");
    rows.Add("{"+string.Join(",",fields.Select(f=>scalar(f)+":"+scalar(Resolve(item,f,member))).ToArray())+"}");
   }
   return "["+string.Join(",",rows.ToArray())+"]";
  }
 }
}
