using System.Diagnostics;

namespace Dustweave;

public sealed record DailySandboxProcess(int Id,string Name);

internal static class DailySandboxProcesses
{
    internal static int[] Parse(string text)
    {
        string[] lines=text.Trim('\uFEFF','\0',' ','\r','\n','\t').Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(lines.Length==0 || !int.TryParse(lines[0],out int count) || count<0 || count>4096 || lines.Length!=count+1)
            throw new IOException("sandbox.manage.process_unknown");
        var pids=new List<int>();
        foreach(string line in lines.Skip(1)) {
            if(!int.TryParse(line,out int pid)||pid<=0||pids.Contains(pid))throw new IOException("sandbox.manage.process_unknown");
            pids.Add(pid);
        }
        return pids.ToArray();
    }
    internal static async Task<DailySandboxProcess[]> ReadAsync(string installation,string box,CancellationToken token)
    {
        if(!DailySandbox.ValidBoxName(box))throw new InvalidDataException("sandbox.repair.required");
        int[] pids=await Task.Run(()=>Accounts.SandboxConfiguration.Processes(installation,box),token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var result=new List<DailySandboxProcess>();
        foreach(int pid in pids) {
            try { using var process=Process.GetProcessById(pid); if(!process.HasExited)result.Add(new(pid,process.ProcessName)); }
            catch(ArgumentException) { } // Already exited: do not turn a stale enumeration into a permanent block.
            catch(InvalidOperationException) { }
            catch(System.ComponentModel.Win32Exception) { result.Add(new(pid,"")); }
        }
        return result.ToArray();
    }
    internal static async Task WaitIdleAsync(string installation,string box,string state,CancellationToken token)
    {
        // Start /wait can exit just before its sandboxed launcher is reaped.
        // Permit a bounded settling interval; never kill another process or wait forever.
        var watch=Stopwatch.StartNew();
        DailySandboxProcess[] rows;
        do {
            rows=await ReadAsync(installation,box,token).ConfigureAwait(false);
            if(rows.Length==0)return;
            if(watch.Elapsed>=TimeSpan.FromSeconds(6))break;
            await Task.Delay(250,token).ConfigureAwait(false);
        } while(true);
        try { DailyJson.Write(Path.Combine(state,"sandbox-processes.json"),new {atUtc=DateTimeOffset.UtcNow,box,processes=rows}); } catch(IOException) { } catch(UnauthorizedAccessException) { }
        throw new InvalidOperationException("sandbox.manage.running");
    }
}
