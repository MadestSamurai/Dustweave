using System;
using System.IO;
using System.Security.Cryptography;
namespace BD2Equipment.Live {
 public static class CatalogTransfer {
  public const int PartBytes=1024*1024,MaximumBytes=128*1024*1024;
  public static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
  public static int Parts(int bytes){if(bytes<=0||bytes>MaximumBytes)throw new InvalidDataException("Equipment catalog size is out of range");return (bytes+PartBytes-1)/PartBytes;}
  public static byte[] Part(byte[] payload,int index){int count=Parts(payload.Length);if(index<0||index>=count)throw new InvalidDataException("Invalid catalog part");int offset=index*PartBytes;var result=new byte[Math.Min(PartBytes,payload.Length-offset)];Buffer.BlockCopy(payload,offset,result,0,result.Length);return result;}
 }
 public sealed class CatalogDownload {
  readonly string id,hash;readonly byte[] buffer;int next;
  public readonly int Count;
  public CatalogDownload(string request,int bytes,int parts,int partBytes,string sha){Count=CatalogTransfer.Parts(bytes);if(Count!=parts||partBytes!=CatalogTransfer.PartBytes||string.IsNullOrEmpty(request)||sha==null||sha.Length!=64)throw new InvalidDataException("Invalid equipment catalog manifest");id=request;hash=sha;buffer=new byte[bytes];}
  public void Add(string request,int index,byte[] part,string sha){if(request!=id||index!=next||index>=Count||part==null||part.Length!=Math.Min(CatalogTransfer.PartBytes,buffer.Length-index*CatalogTransfer.PartBytes)||CatalogTransfer.Hash(part)!=sha)throw new InvalidDataException("Equipment catalog part identity, length or checksum mismatch");Buffer.BlockCopy(part,0,buffer,index*CatalogTransfer.PartBytes,part.Length);next++;}
  public byte[] Complete(){if(next!=Count||CatalogTransfer.Hash(buffer)!=hash)throw new InvalidDataException("Equipment catalog is incomplete or checksum differs");return buffer;}
 }
}
