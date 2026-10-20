namespace BD2Territory;

/// <summary>Keep the previous complete settings when readers or scanners briefly hold the file.</summary>
public static class AtomicSettingsFile
{
 public static void Write(string path,byte[] contents)
 {
  var directory=Path.GetDirectoryName(Path.GetFullPath(path))!;
  Directory.CreateDirectory(directory);
  var temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
  try
  {
   File.WriteAllBytes(temporary,contents);
   var elapsed=System.Diagnostics.Stopwatch.StartNew();
   while(true)
   {
    try
    {
     File.Move(temporary,path,true);
     return;
    }
    catch(Exception error) when(error is IOException or UnauthorizedAccessException)
    {
     int code=error.HResult&0xffff;
     bool retry=code is 5 or 32 or 33 or 80 or 183;
     if(!retry||elapsed.ElapsedMilliseconds>=500||Directory.Exists(path)||
       (File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReadOnly)!=0))throw;
     Thread.Sleep(20);
    }
   }
  }
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
}
