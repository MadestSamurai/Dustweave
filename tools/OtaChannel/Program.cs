using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave.Ota;
sealed record Operation(string State,string Nonce,string Version,DateTimeOffset UpdatedUtc,string? Error=null,int? WorkerPid=null);

static class Channel
{
    // The OS account fixes the lane; SSH requests cannot select another configuration.
    public static string ConfigPathForAccount(string account) => account == "dustweave-ota-v2"
        ? "/etc/dustweave-ota-channel-v2/config.json" : account == "dustweave-ota-aio"
        ? "/etc/dustweave-ota-channel-aio/config.json" : "/etc/dustweave-ota-channel/config.json";
    static string Folder(Config c,string nonce) { Rules.Need(Rules.IsNonce(nonce),"Invalid operation ID"); return Path.Combine(c.State,"operations",nonce); }
    static void Record(string folder,Operation state) => Rules.Atomic(Path.Combine(folder,"status.json"),Rules.Bytes(state));
    static void CheckCurrent(Config c,Request r,byte[] nextFeed)
    {
        var path=Rules.FeedPath(c); Rules.NoLinks(path);
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
        if (command.StartsWith("dustweave-ota capable ", StringComparison.Ordinal))
        {
            string suffix = command["dustweave-ota capable ".Length..];
            Rules.Need(!suffix.StartsWith("capable ", StringComparison.Ordinal), "Nested capability selection rejected");
            _ = ParseCommand("dustweave-ota " + suffix); return command;
        }
        if(command is "dustweave-ota submit" or "dustweave-ota status" or "dustweave-ota repair" or "dustweave-ota repair-index") return command;
        if(command.StartsWith("dustweave-ota status ",StringComparison.Ordinal) && Rules.IsNonce(command["dustweave-ota status ".Length..])) return command;
        throw new InvalidDataException("Only dustweave-ota submit/repair/status are permitted");
    }
    public static object Status(Config c,string? nonce=null)
    {
        if(nonce!=null)
        {
            var repairResult=Path.Combine(c.State,"repairs",nonce,"result.json");
            if(File.Exists(repairResult)) { Rules.NoLinks(repairResult);return Rules.Read<JsonElement>(File.ReadAllBytes(repairResult)); }
            var repairHeader=Path.Combine(c.State,"repairs",nonce,"header.json");
            if(File.Exists(repairHeader))
            {
                Rules.NoLinks(repairHeader);
                if(File.GetLastWriteTimeUtc(repairHeader)<DateTime.UtcNow.AddMinutes(-20))
                    return new {state="needs_attention",nonce,error="Repair publication stopped reporting. Inspect the saved receipt before preparing another request."};
                return new {state="receiving",nonce};
            }
            var folder=Folder(c,nonce); var status=Path.Combine(folder,"status.json");
            if(!File.Exists(status)) return new {state=File.Exists(Path.Combine(c.State,"claims",nonce))?"receiving":"not_found",nonce};
            var state=Rules.Read<Operation>(File.ReadAllBytes(status));
            if(state.State is "receiving" or "accepted" or "publishing" && state.UpdatedUtc<DateTimeOffset.UtcNow.AddMinutes(-20))
            {
                var h=Rules.Read<Header>(File.ReadAllBytes(Path.Combine(folder,"header.json")));
                var r=Rules.Read<Request>(Rules.Verify(h.Request,c,Rules.Domain));
                if(Rules.HashFile(Rules.FeedPath(c))==r.FeedSha256)
                { CheckPublished(Path.Combine(c.Public,"v"+r.Version),r);state=state with{State="completed",UpdatedUtc=DateTimeOffset.UtcNow};Record(folder,state); }
                else return state with{State="needs_attention",Error="The operation stopped reporting. Inspect the saved receipt; do not blindly resubmit."};
            }
            return state;
        }
        var feedPath=Rules.FeedPath(c);
        var feed=Rules.VerifyFeed(File.ReadAllBytes(feedPath),c);
        return new {product="Dustweave",version=feed.Releases[0].Version,feedSha256=Rules.HashFile(feedPath),protocol=1,layout=c.Layout,mirror=Mirror.Status(c)};
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
                info.ArgumentList.Add(c.Capable ? "worker-capable" : "worker");info.ArgumentList.Add(request.Nonce);
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
            var liveFeed=Rules.FeedPath(c);var target=Path.Combine(c.Public,"v"+r.Version);
            if(Rules.HashFile(liveFeed)==r.FeedSha256) { CheckPublished(target,r);Record(folder,new("completed",nonce,r.Version,DateTimeOffset.UtcNow));return; }
            CheckCurrent(c,r,feed);
            CheckPreparedInventory(c, Rules.VerifyFeed(feed,c));
            var stage=Path.Combine(folder,"upload");
            if(Directory.Exists(stage)) foreach(var blob in r.Files) Rules.CheckArchive(Path.Combine(stage,blob.FileName),blob);
            else CheckPublished(target,r);
            Rules.Atomic(Path.Combine(folder,"previous-updates.json"),File.ReadAllBytes(liveFeed));
            Rules.NoLinks(target);
            if(Directory.Exists(target)) CheckPublished(target,r); else Directory.Move(stage,target);
            fault?.Invoke("version-ready");
            // Version contents are immutable; the only mutable public file is switched last.
            CheckCurrent(c,r,feed); Rules.Atomic(Rules.FeedPath(c, writing:true),feed);fault?.Invoke("feed-switched");
            CheckPublished(target,r);Rules.Need(Rules.HashFile(Rules.FeedPath(c))==r.FeedSha256,"Public feed verification failed");
            // Mirror setup never invalidates a healthy signed publication. Until
            // all CDN files verify, the unchanged origin URLs serve local bytes.
            Mirror.TryPrepareAndProbe(c,feed);
            Record(folder,new("completed",nonce,r.Version,DateTimeOffset.UtcNow));
        }
        catch(Exception e)
        {
            // A lost receipt after the atomic switch must not turn a completed deployment into a retry.
            if(Rules.HashFile(Rules.FeedPath(c))==r.FeedSha256)
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
    internal static void CheckPreparedInventory(Config c, Feed candidate)
    {
        var top = candidate.Releases[0];
        if (top.Version == "1.4.15-bridge.1")
        {
            var capable = Rules.Capability(c);
            string finalPath = Rules.FeedPath(capable, writing:true);
            Rules.Need(File.Exists(finalPath), "Publish and verify the final capability feed before exposing the bridge");
            var final = Rules.VerifyFeed(File.ReadAllBytes(finalPath), capable);
            Rules.Need(final.Releases.Any(r => r.Version == "1.4.15" && r.InstallerProtocol == 2) &&
                final.Releases.Any(r => r.Version == top.Version && Rules.Hash(Rules.Bytes(r)) == Rules.Hash(Rules.Bytes(top))), "Bridge does not match the ready final release");
        }
        if (top.InstallerProtocol != 2 && top.Version != "1.4.15-bridge.1") return;
        foreach (var asset in top.Assets)
        {
            string path = Path.Combine(c.Public, "repair", "v" + top.Version, Dustweave.DailyUpdateRepairManifest.Name(top.Version, asset.Flavor));
            Rules.NoLinks(path); Rules.Need(new FileInfo(path).Length <= Dustweave.DailyUpdateRepairManifest.MaxEnvelopeBytes, "Repair inventory too large");
            var manifest = Dustweave.DailyUpdateRepairManifest.Verify(File.ReadAllBytes(path), c.Keys);
            Rules.Need(manifest.Version == top.Version && manifest.Flavor == asset.Flavor && manifest.RuntimeVersion == top.RuntimeVersion &&
                manifest.ArchiveSha256.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase), "Prepared repair inventory differs from target release");
            foreach (var block in manifest.Files.SelectMany(f => f.Blocks).Distinct())
                Rules.CheckArchive(Path.Combine(c.Public,"repair","objects",Dustweave.DailyUpdateRepairManifest.ObjectName(block)),
                    new(Dustweave.DailyUpdateRepairManifest.ObjectName(block),block.Bytes,block.Sha256));
        }
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
            if(args.Length==4 && args[0]=="mirror-nginx")
            {
                var c=Rules.Read<Config>(File.ReadAllBytes(args[3]));
                File.WriteAllText(args[2],MirrorNginx.Patch(File.ReadAllText(args[1]),c));return 0;
            }
            if(args.Length==4 && args[0]=="bundle-nginx")
            {
                var c=Rules.Read<Config>(File.ReadAllBytes(args[3]));
                File.WriteAllText(args[2],MirrorNginx.BundlePatch(File.ReadAllText(args[1]),c));return 0;
            }
            if(args.Length==4 && args[0]=="verify-initial-bundle")
            {
                var c=Rules.Read<Config>(File.ReadAllBytes(args[1]));
                var feed=Rules.VerifyFeed(File.ReadAllBytes(Path.Combine(args[2],"updates.json")),c);
                Rules.Need(c.Layout==3 && feed.Releases.Length==1 && feed.Releases[0].Version=="1.5.0","Expected initial public bundle");
                foreach(var a in feed.Releases[0].Assets) Rules.CheckArchive(Path.Combine(args[2],a.FileName),new(a.FileName,a.Bytes,a.Sha256));
                using var proof=JsonDocument.Parse(File.ReadAllBytes(args[3]));
                Rules.Need(proof.RootElement.GetProperty("status").GetString()=="passed","Initial package acceptance failed");
                foreach(var flavor in new[]{"Lite","Portable"}) foreach(var scenario in new[]{"upgrade-","rollback-","interrupt-host-","interrupt-helper-","repair-"})
                    Rules.Need(proof.RootElement.GetProperty("cases").EnumerateArray().Any(x=>x.GetProperty("case").GetString()==scenario+flavor && x.GetProperty("status").GetString()=="passed" && x.GetProperty("to").GetString()=="1.5.0"),"Missing initial bundle acceptance");
                Console.WriteLine("Initial bundle verified");return 0;
            }
            if(args.Length==4 && args[0]=="mirror-config")
            {
                var c=Rules.Read<Config>(File.ReadAllBytes(args[1])) with { MirrorBaseUrl=args[3] };
                _=Mirror.BaseUrl(c);
                Rules.Atomic(args[2],Rules.Bytes(c));return 0;
            }
            var configPath=Channel.ConfigPathForAccount(Environment.UserName);
            Rules.NoLinks(configPath);var config=Rules.Read<Config>(File.ReadAllBytes(configPath));
            Rules.NoLinks(config.State);Rules.NoLinks(config.Public);
            if(args.SequenceEqual(new[]{"mirror-prepare"}))
            {
                using var gate=new FileStream(Path.Combine(config.State,"publish.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                using var client=Mirror.Client();
                await Mirror.Prepare(config,File.ReadAllBytes(Rules.FeedPath(config)),client);
                Rules.Need(await Mirror.Probe(config,client),"Mirror health verification failed");
                Console.WriteLine(JsonSerializer.Serialize(Mirror.Status(config),Rules.Json));return 0;
            }
            if(args.SequenceEqual(new[]{"mirror-health"}))
            {
                using var client=Mirror.Client();
                await Mirror.Probe(config,client);
                Console.WriteLine(JsonSerializer.Serialize(Mirror.Status(config),Rules.Json));return 0;
            }
            if(args.Length==2 && args[0]=="worker") { Channel.Worker(config,args[1]);return 0; }
            if(args.Length==2 && args[0]=="worker-capable") { Channel.Worker(Rules.Capability(config),args[1]);return 0; }
            Rules.Need(args.SequenceEqual(new[]{"serve"}),"Invalid invocation");
            var command=Channel.ParseCommand(Environment.GetEnvironmentVariable("SSH_ORIGINAL_COMMAND")??"");
            if (command.StartsWith("dustweave-ota capable ", StringComparison.Ordinal))
            {
                config = Rules.Capability(config); Rules.NoLinks(config.State); Directory.CreateDirectory(config.State);
                command = "dustweave-ota " + command["dustweave-ota capable ".Length..];
            }
            object result=command=="dustweave-ota repair-index" ? RepairChannel.Index(config) :
                command=="dustweave-ota repair" ? await RepairChannel.Submit(config,Console.OpenStandardInput()) :
                command=="dustweave-ota submit" ? await Channel.Submit(config,Console.OpenStandardInput()) :
                Channel.Status(config,command.Length>"dustweave-ota status ".Length?command["dustweave-ota status ".Length..]:null);
            Console.WriteLine(JsonSerializer.Serialize(result,Rules.Json));return 0;
        }
        catch(Exception error)
        { Console.WriteLine(JsonSerializer.Serialize(new{state="error",error=error.Message},Rules.Json));return 1; }
    }
}
