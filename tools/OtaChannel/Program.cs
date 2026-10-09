using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave.Ota;
sealed record Operation(string State,string Nonce,string Version,DateTimeOffset UpdatedUtc,string? Error=null,int? WorkerPid=null);

static class Channel
{
    public const string ConfigPath="/etc/dustweave-ota-channel/config.json";
    static string Folder(Config c,string nonce) { Rules.Need(Rules.IsNonce(nonce),"Invalid operation ID"); return Path.Combine(c.State,"operations",nonce); }
    static void Record(string folder,Operation state) => Rules.Atomic(Path.Combine(folder,"status.json"),Rules.Bytes(state));
    static void CheckCurrent(Config c,Request r,byte[] nextFeed)
    {
        var path=Path.Combine(c.Public,"updates.json"); Rules.NoLinks(path);
        Rules.Need(Rules.HashFile(path)==r.ExpectedFeedSha256,"Current feed changed; query status before preparing another release");
        var prior=Rules.VerifyFeed(File.ReadAllBytes(path),c);
        var candidate=Rules.VerifyFeed(nextFeed,c);
        foreach(var published in prior.Releases.Where(v=>DailyVersion.Parse(v.Version)>=DailyVersion.Parse("1.0.0-beta")))
        {
            var kept=candidate.Releases.SingleOrDefault(v=>v.Version==published.Version);
            Rules.Need(kept!=null && Rules.Hash(Rules.Bytes(kept))==Rules.Hash(Rules.Bytes(published)),"Published public-version metadata cannot be removed or rewritten");
        }
        Rules.Need(DailyVersion.Parse(r.Version)>DailyVersion.Parse(prior.Releases[0].Version),"Release must advance the current version");
    }
    public static string ParseCommand(string command)
    {
        if(command is "dustweave-ota submit" or "dustweave-ota status") return command;
        if(command.StartsWith("dustweave-ota status ",StringComparison.Ordinal) && Rules.IsNonce(command["dustweave-ota status ".Length..])) return command;
        throw new InvalidDataException("Only dustweave-ota submit/status are permitted");
    }
    public static object Status(Config c,string? nonce=null)
    {
        if(nonce!=null)
        {
            var folder=Folder(c,nonce); var status=Path.Combine(folder,"status.json");
            if(!File.Exists(status)) return new {state=File.Exists(Path.Combine(c.State,"claims",nonce))?"receiving":"not_found",nonce};
            var state=Rules.Read<Operation>(File.ReadAllBytes(status));
            if(state.State is "receiving" or "accepted" or "publishing" && state.UpdatedUtc<DateTimeOffset.UtcNow.AddMinutes(-20))
            {
                var h=Rules.Read<Header>(File.ReadAllBytes(Path.Combine(folder,"header.json")));
                var r=Rules.Read<Request>(Rules.Verify(h.Request,c,Rules.Domain));
                if(Rules.HashFile(Path.Combine(c.Public,"updates.json"))==r.FeedSha256)
                { CheckPublished(Path.Combine(c.Public,"v"+r.Version),r);state=state with{State="completed",UpdatedUtc=DateTimeOffset.UtcNow};Record(folder,state); }
                else return state with{State="needs_attention",Error="The operation stopped reporting. Inspect the saved receipt; do not blindly resubmit."};
            }
            return state;
        }
        var feedPath=Path.Combine(c.Public,"updates.json");
        var feed=Rules.VerifyFeed(File.ReadAllBytes(feedPath),c);
        return new {product="Dustweave",version=feed.Releases[0].Version,feedSha256=Rules.HashFile(feedPath),protocol=1};
    }
    public static async Task<object> Submit(Config c,Stream input,bool startWorker=true)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15)); var token=timeout.Token;
        byte[] prefix=new byte[4]; await input.ReadExactlyAsync(prefix,token);
        int length=BinaryPrimitives.ReadInt32BigEndian(prefix); Rules.Need(length is >0 and <=2097152,"Invalid frame length");
        byte[] bytes=new byte[length];await input.ReadExactlyAsync(bytes,token);
        var header=Rules.Read<Header>(bytes);var (request,feed,evidence)=Rules.Validate(header,c,DateTimeOffset.UtcNow);
        CheckCurrent(c,request,feed);
        Directory.CreateDirectory(Path.Combine(c.State,"claims")); Directory.CreateDirectory(Path.Combine(c.State,"operations"));
        var claimPath=Path.Combine(c.State,"claims",request.Nonce); Rules.NoLinks(claimPath);
        using(var claim=new FileStream(claimPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { claim.Write(bytes);claim.Flush(true); }
        var folder=Folder(c,request.Nonce);Rules.NoLinks(folder);Directory.CreateDirectory(folder);
        Record(folder,new("receiving",request.Nonce,request.Version,DateTimeOffset.UtcNow));
        try
        {
            Rules.Atomic(Path.Combine(folder,"header.json"),bytes);
            Rules.Atomic(Path.Combine(folder,"updates.json"),feed); Rules.Atomic(Path.Combine(folder,"acceptance.json"),evidence);
            var upload=Path.Combine(folder,"upload"); Directory.CreateDirectory(upload);
            if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(upload,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute);
            byte[] buffer=new byte[131072];
            foreach(var blob in request.Files)
            {
                string path=Path.Combine(upload,blob.FileName);Rules.NoLinks(path);
                using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,FileOptions.Asynchronous);
                using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long left=blob.Bytes;
                while(left>0)
                {
                    int read=await input.ReadAsync(buffer.AsMemory(0,(int)Math.Min(left,buffer.Length)),token);
                    if(read==0)throw new EndOfStreamException("Interrupted upload; query this operation before retrying");
                    await output.WriteAsync(buffer.AsMemory(0,read),token);hash.AppendData(buffer,0,read);left-=read;
                }
                output.Flush(true);
                if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead|UnixFileMode.OtherRead);
                Rules.Need(Convert.ToHexString(hash.GetHashAndReset()).Equals(blob.Sha256,StringComparison.OrdinalIgnoreCase),"Upload digest mismatch");
            }
            Rules.Need(await input.ReadAsync(buffer.AsMemory(0,1),token)==0,"Unexpected trailing bytes");
            Record(folder,new("accepted",request.Nonce,request.Version,DateTimeOffset.UtcNow));
            if(startWorker)
            {
                // No inherited SSH handles: the accepted worker survives a disconnected publishing client.
                var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
                info.ArgumentList.Add("worker");info.ArgumentList.Add(request.Nonce);
                using var process=Process.Start(info)??throw new IOException("Cannot start publication worker");process.StandardInput.Close();
            }
            return new {state="accepted",nonce=request.Nonce,version=request.Version};
        }
        catch(Exception e) { Record(folder,new("failed",request.Nonce,request.Version,DateTimeOffset.UtcNow,e.Message));throw; }
    }
    public static void Worker(Config c,string nonce,Action<string>? fault=null)
    {
        string folder=Folder(c,nonce);var header=Rules.Read<Header>(File.ReadAllBytes(Path.Combine(folder,"header.json")));
        var signedRequest=Rules.Read<Request>(Rules.Verify(header.Request,c,Rules.Domain));
        // The deadline was checked before receiving bytes. Accepted uploads retain their signed identity.
        var (r,feed,_)=Rules.Validate(header,c,signedRequest.IssuedUtc);

        try
        {
            using var gate=new FileStream(Path.Combine(c.State,"publish.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            Record(folder,new("publishing",nonce,r.Version,DateTimeOffset.UtcNow,WorkerPid:Environment.ProcessId));
            var liveFeed=Path.Combine(c.Public,"updates.json");var target=Path.Combine(c.Public,"v"+r.Version);
            if(Rules.HashFile(liveFeed)==r.FeedSha256) { CheckPublished(target,r);Record(folder,new("completed",nonce,r.Version,DateTimeOffset.UtcNow));return; }
            CheckCurrent(c,r,feed);
            var stage=Path.Combine(folder,"upload");
            if(Directory.Exists(stage)) foreach(var blob in r.Files) Rules.CheckArchive(Path.Combine(stage,blob.FileName),blob);
            else CheckPublished(target,r);
            Rules.Atomic(Path.Combine(folder,"previous-updates.json"),File.ReadAllBytes(liveFeed));
            Rules.NoLinks(target);
            if(Directory.Exists(target)) CheckPublished(target,r); else Directory.Move(stage,target);
            fault?.Invoke("version-ready");
            // Version contents are immutable; the only mutable public file is switched last.
            CheckCurrent(c,r,feed); Rules.Atomic(liveFeed,feed);fault?.Invoke("feed-switched");
            CheckPublished(target,r);Rules.Need(Rules.HashFile(liveFeed)==r.FeedSha256,"Public feed verification failed");
            Record(folder,new("completed",nonce,r.Version,DateTimeOffset.UtcNow));
        }
        catch(Exception e)
        {
            // A lost receipt after the atomic switch must not turn a completed deployment into a retry.
            if(Rules.HashFile(Path.Combine(c.Public,"updates.json"))==r.FeedSha256)
            { CheckPublished(Path.Combine(c.Public,"v"+r.Version),r);Record(folder,new("completed",nonce,r.Version,DateTimeOffset.UtcNow));return; }
            Record(folder,new("failed",nonce,r.Version,DateTimeOffset.UtcNow,e.Message));throw;
        }
    }
    static void CheckPublished(string folder,Request r)
    {
        Rules.NoLinks(folder);
        Rules.Need(Directory.Exists(folder) && Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(r.Files.Select(f=>f.FileName).Order(StringComparer.Ordinal)),"Existing version has different contents");
        foreach(var blob in r.Files) Rules.CheckArchive(Path.Combine(folder,blob.FileName),blob);
    }
}

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.SequenceEqual(new[]{"self-test"})) { await SelfTest.Run();return 0; }
            if(args.Length==2 && args[0]=="verify")
            {
                var c=Rules.Read<Config>(File.ReadAllBytes(args[1])); Console.WriteLine(JsonSerializer.Serialize(Channel.Status(c),Rules.Json));return 0;
            }
            Rules.NoLinks(Channel.ConfigPath);var config=Rules.Read<Config>(File.ReadAllBytes(Channel.ConfigPath));
            Rules.NoLinks(config.State);Rules.NoLinks(config.Public);
            if(args.Length==2 && args[0]=="worker") { Channel.Worker(config,args[1]);return 0; }
            Rules.Need(args.SequenceEqual(new[]{"serve"}),"Invalid invocation");
            var command=Channel.ParseCommand(Environment.GetEnvironmentVariable("SSH_ORIGINAL_COMMAND")??"");
            object result=command=="dustweave-ota submit" ? await Channel.Submit(config,Console.OpenStandardInput()) :
                Channel.Status(config,command.Length>"dustweave-ota status ".Length?command["dustweave-ota status ".Length..]:null);
            Console.WriteLine(JsonSerializer.Serialize(result,Rules.Json));return 0;
        }
        catch(Exception error)
        { Console.WriteLine(JsonSerializer.Serialize(new{state="error",error=error.Message},Rules.Json));return 1; }
    }
}
