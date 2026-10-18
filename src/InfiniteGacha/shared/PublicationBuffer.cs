#nullable disable
using System;
using System.IO;
namespace BD2InfiniteGacha
{
 public sealed class PublicationBuffer
 {
  private Snapshot pending;
  public bool Blocked{get{return pending!=null;}}
  public string Error{get;private set;}
  public bool Publish(Snapshot snapshot,Action<Snapshot> write)
  {
   try{write(snapshot);pending=null;Error="";return true;}
   catch(IOException e){pending=snapshot;Error=e.Message;return false;}
   catch(UnauthorizedAccessException e){pending=snapshot;Error=e.Message;return false;}
  }
  public bool Retry(Action<Snapshot> write){return pending==null||Publish(pending,write);}
 }
}
