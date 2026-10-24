using System.Security.Cryptography;

namespace Dustweave;

// Compatibility mode uses normal copy-on-write. Retire ONLY known shipped
// program overlays, so a host launcher update is visible without resetting the
// writable Addressable cache, login registry, LocalLow or launcher.settings.
internal static class DailySandboxProgramSync
{
    internal static bool IsProgram(string relative)
    {
        relative=relative.Replace('/','\\');
        return System.Text.RegularExpressions.Regex.IsMatch(relative,@"^BrownDust II_Data\\(?:level\d+(?:\.resS)?|sharedassets\d+\.assets(?:\.resS)?|resources\.assets(?:\.resS)?|globalgamemanagers\.assets\.resS)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || DailySandboxGameFiles.ProgramPaths.Any(p=>p.EndsWith("\\*",StringComparison.Ordinal)
            ? relative.StartsWith(p[..^1],StringComparison.OrdinalIgnoreCase)
            : relative.Equals(p,StringComparison.OrdinalIgnoreCase));
    }
    internal static string Overlay(string root,string executable)
    {
        string host=DailySandboxGameFiles.GameRoot(executable);
        if(Path.GetPathRoot(host)?.Length!=3)throw new InvalidDataException("client_update.sync_path");
        return Path.Combine(Path.GetFullPath(root),"drive",host[..1].ToLowerInvariant(),host[3..]);
    }
    internal static async Task<int> SynchronizeAsync(string executable,string root,string backup,Func<Task<bool>> idle,
        Action<string> progress,CancellationToken token)
    {
        string host=DailySandboxGameFiles.GameRoot(executable),overlay=Overlay(root,executable);
        DailyCacheStorage.NoLinks(overlay);DailyCacheStorage.NoLinks(backup);
        if(!Directory.Exists(overlay))return 0;
        // Do not traverse Addressable (potentially many gigabytes) or unknown directories.
        var files=new List<string>();
        string data=Path.Combine(overlay,"BrownDust II_Data");DailyCacheStorage.NoLinks(data);
        if(Directory.Exists(data))files.AddRange(Directory.EnumerateFiles(data).Where(p=>IsProgram(Path.GetRelativePath(overlay,p))));
        foreach(string program in DailySandboxGameFiles.ProgramPaths) {
            string path=Path.Combine(overlay,program.TrimEnd('*').TrimEnd('\\'));
            DailyCacheStorage.NoLinks(path);
            if(program.EndsWith("\\*",StringComparison.Ordinal)) {
                if(Directory.Exists(path)) Walk(path,files,token);
            } else if(File.Exists(path))files.Add(path);
        }
        int changed=0;
        foreach(string target in files.Distinct(StringComparer.OrdinalIgnoreCase)) {
            token.ThrowIfCancellationRequested();
            string relative=Path.GetRelativePath(overlay,target),source=Path.Combine(host,relative);
            DailyCacheStorage.NoLinks(target);DailyCacheStorage.NoLinks(source);
            string old=Hash(target);
            if(File.Exists(source)&&Hash(source)==old)continue;
            if(!await idle())throw new InvalidOperationException("client_update.sync_busy");
            if(changed==0)progress("client_update.sync_programs");
            // A durable copy precedes removal. Interrupted retirement can be retried.
            string saved=Path.Combine(backup,relative);
            DailyCacheStorage.NoLinks(saved);Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
            string temporary=saved+"."+Guid.NewGuid().ToString("N");
            try {
                File.Copy(target,temporary);
                if(Hash(temporary)!=old || Hash(target)!=old || !await idle())throw new IOException("client_update.sync_changed");
                File.Move(temporary,saved,true);
                token.ThrowIfCancellationRequested();
                File.Delete(target);changed++;
            } finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        if(changed>0)DailyJson.Write(Path.Combine(backup,"last-sync.json"),new {atUtc=DateTimeOffset.UtcNow,changed,credentialsTouched=false,cacheTouched=false});
        return changed;
    }
    private static string Hash(string path) {using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    private static void Walk(string path,List<string> files,CancellationToken token)
    {
        DailyCacheStorage.NoLinks(path);
        foreach(string child in Directory.EnumerateFileSystemEntries(path)) {
            token.ThrowIfCancellationRequested();DailyCacheStorage.NoLinks(child);
            if(Directory.Exists(child))Walk(child,files,token);else files.Add(child);
        }
    }
}
