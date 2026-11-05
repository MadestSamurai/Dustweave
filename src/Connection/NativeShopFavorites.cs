using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BD2Daily.Live {
 // Read this client's native shop representation. Never migrate local
 // favorites, send a save request or invent an empty list on missing data.
 internal static class NativeShopFavorites {
  internal static long[][] Read(Type store){
   var reader=store.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)
    .Single(m=>m.GetParameters().Length==0&&m.ReturnType==typeof(Dictionary<int,HashSet<int>>));
   var current=(Dictionary<int,HashSet<int>>)reader.Invoke(null,null);
   if(current==null)throw new InvalidOperationException("Shop favorites have not loaded yet.");
   return current.SelectMany(s=>s.Value.Select(p=>new long[]{s.Key,p})).ToArray();
  }
 }
}
