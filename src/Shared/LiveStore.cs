using System;
namespace BD2Daily {
 public static class LiveStore {
  public static Func<string,bool> Handles;
  public static Func<string,byte[]> Read;
  public static Func<string,byte[],bool,bool> Write;
 }
}
