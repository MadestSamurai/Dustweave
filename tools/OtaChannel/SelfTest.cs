using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave.Ota;
static class SelfTest
{
    public static async Task Run()
    {
        var repo=new DirectoryInfo(AppContext.BaseDirectory);
        while(repo!=null && !File.Exists(Path.Combine(repo.FullName,"Dustweave.slnx")))repo=repo.Parent;
        var folder=Path.Combine(repo?.FullName??Environment.CurrentDirectory,"artifacts/ota-channel-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);using var signer=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys=new Dictionary<string,string>{{"test",Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())}};
        var checks=new List<string>();
        void Pass(string name,bool ok=true) { Rules.Need(ok,"Test failed: "+name);checks.Add(name); }
        void Reject(string name,Action action) { try { action(); }catch { checks.Add(name);return; }throw new Exception("Not rejected: "+name); }
        Pass("legacy-account-fixed-config",Channel.ConfigPathForAccount("dustweave-ota")=="/etc/dustweave-ota-channel/config.json");
        Pass("v2-account-separate-config",Channel.ConfigPathForAccount("dustweave-ota-v2")=="/etc/dustweave-ota-channel-v2/config.json");
        async Task RejectAsync(string name,Func<Task> action) { try { await action(); }catch { checks.Add(name);return; }throw new Exception("Not rejected: "+name); }
        Envelope Sign<T>(T value,string domain="") { var data=Rules.Bytes(value);return new(1,"test","ECDSA-P256-SHA256",Convert.ToBase64String(data),Convert.ToBase64String(signer.SignData(Encoding.UTF8.GetBytes(domain).Concat(data).ToArray(),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))); }
        var notes=new Dictionary<string,string[]>{{"zh-CN",["test"]},{"zh-TW",["test"]},{"en-US",["test"]}};
        Release ReleaseOf(string version,string? previous) => new(version,previous,1,notes,new[]{"Lite","Portable"}.Select(f=>new Asset(f,$"Dustweave-{version}-{f}-win-x64.zip",4,Rules.Hash([1,2,3,4]))).ToArray(),[]);
        var baseline=ReleaseOf("1.0.0-beta",null);var current=ReleaseOf("1.0.1-beta",baseline.Version);
        var old=Rules.Bytes(Sign(new Feed(2,"Dustweave","stable",[baseline])));
        var feed=Rules.Bytes(Sign(new Feed(2,"Dustweave","stable",[current,baseline])));
        var evidence=Rules.Bytes(new{status="passed",cases=new[]{"upgrade-Lite","upgrade-Portable","startup-failure","bootstrap-recovery"}.Select(name=>new{ @case=name,status="passed",to=current.Version,baselineHelper=true,delta=true}).ToArray()});
        Config Setup(string name) { var dir=Path.Combine(folder,name);var c=new Config(Path.Combine(dir,"state"),Path.Combine(dir,"public"),keys);Directory.CreateDirectory(c.State);Directory.CreateDirectory(c.Public);File.WriteAllBytes(Path.Combine(c.Public,"updates.json"),old);return c; }
        Request NewRequest() => new(1,"Dustweave",current.Version,Rules.Hash(old),Rules.Hash(feed),Rules.Hash(evidence),Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(10),current.Assets.Select(a=>new Blob(a.FileName,a.Bytes,a.Sha256)).ToArray());
        Header HeaderOf(Request r) => new(Sign(r,Rules.Domain),Convert.ToBase64String(feed),Convert.ToBase64String(evidence));
        MemoryStream Frame(Header h,bool truncate=false,bool trailing=false) { var bytes=Rules.Bytes(h);var m=new MemoryStream();byte[] p=new byte[4];BinaryPrimitives.WriteInt32BigEndian(p,bytes.Length);m.Write(p);m.Write(bytes);m.Write(new byte[]{1,2,3,4,1,2,3,4},0,truncate?5:8);if(trailing)m.WriteByte(0);m.Position=0;return m; }
        var c=Setup("normal");var r=NewRequest();var h=HeaderOf(r);
        Pass("valid-signed-request",Rules.Validate(h,c,DateTimeOffset.UtcNow).Request==r || Rules.Validate(h,c,DateTimeOffset.UtcNow).Request.Nonce==r.Nonce);
        Reject("tampered-signature",()=>Rules.Validate(h with{Request=h.Request with{Signature=Convert.ToBase64String(new byte[64])}},c,DateTimeOffset.UtcNow));
        Reject("wrong-domain",()=>Rules.Validate(h with{Request=Sign(r)},c,DateTimeOffset.UtcNow));
        Reject("untrusted-key",()=>Rules.Validate(h with{Request=h.Request with{KeyId="other"}},c,DateTimeOffset.UtcNow));
        Reject("expired-request",()=>Rules.Validate(HeaderOf(r with{IssuedUtc=DateTimeOffset.UtcNow.AddHours(-1),ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(-50)}),c,DateTimeOffset.UtcNow));
        Reject("future-request",()=>Rules.Validate(HeaderOf(r with{IssuedUtc=DateTimeOffset.UtcNow.AddHours(1),ExpiresUtc=DateTimeOffset.UtcNow.AddHours(1).AddMinutes(10)}),c,DateTimeOffset.UtcNow));
        Reject("overlong-ttl",()=>Rules.Validate(HeaderOf(r with{ExpiresUtc=r.IssuedUtc.AddHours(1)}),c,DateTimeOffset.UtcNow));
        Reject("metadata-digest",()=>Rules.Validate(HeaderOf(r with{FeedSha256=new string('0',64)}),c,DateTimeOffset.UtcNow));
        Reject("evidence-digest",()=>Rules.Validate(HeaderOf(r with{EvidenceSha256=new string('0',64)}),c,DateTimeOffset.UtcNow));
        Reject("path-traversal",()=>Rules.Validate(HeaderOf(r with{Files=[r.Files[0] with{FileName="../../evil"},r.Files[1]]}),c,DateTimeOffset.UtcNow));
        Reject("version-path",()=>Rules.Validate(HeaderOf(r with{Version="../../evil"}),c,DateTimeOffset.UtcNow));
        Reject("extra-file",()=>Rules.Validate(HeaderOf(r with{Files=[..r.Files,new("evil",1,new string('0',64))]}),c,DateTimeOffset.UtcNow));
        foreach(var command in new[]{"sh","dustweave-ota status; id","dustweave-ota submit foo","dustweave-ota status ../x","dustweave-ota worker "+r.Nonce}) Reject("command-"+command,()=>Channel.ParseCommand(command));
        Pass("status-command",Channel.ParseCommand("dustweave-ota status "+r.Nonce).EndsWith(r.Nonce));
        byte[] tooLarge=new byte[4];BinaryPrimitives.WriteInt32BigEndian(tooLarge,2097153);
        await RejectAsync("frame-size-limit",async()=>{await Channel.Submit(c,new MemoryStream(tooLarge),false);});
        var badEvidence=Rules.Bytes(new{status="failed",cases=Array.Empty<object>()});
        var er=r with{EvidenceSha256=Rules.Hash(badEvidence)};
        Reject("failed-evidence",()=>Rules.Validate(new(Sign(er,Rules.Domain),h.Feed,Convert.ToBase64String(badEvidence)),c,DateTimeOffset.UtcNow));
        var changedBaseline=baseline with{Notes=new Dictionary<string,string[]>{{"zh-CN",["changed"]},{"zh-TW",["changed"]},{"en-US",["changed"]}}};
        var changedFeed=Rules.Bytes(Sign(new Feed(2,"Dustweave","stable",[current,changedBaseline])));
        var changedRequest=NewRequest() with{FeedSha256=Rules.Hash(changedFeed)};
        await RejectAsync("published-history-immutable",async()=>{await Channel.Submit(c,Frame(new(Sign(changedRequest,Rules.Domain),Convert.ToBase64String(changedFeed),h.Evidence)),false);});
        var legacy=ReleaseOf("0.9.13",null);var legacyFeed=Rules.Bytes(Sign(new Feed(2,"Dustweave","stable",[current with{PreviousVersion=legacy.Version},legacy])));
        var legacyRequest=NewRequest() with{FeedSha256=Rules.Hash(legacyFeed)};
        Reject("legacy-chain-rejected",()=>Rules.Validate(new(Sign(legacyRequest,Rules.Domain),Convert.ToBase64String(legacyFeed),h.Evidence),c,DateTimeOffset.UtcNow));
        await Channel.Submit(c,Frame(h),false);Pass("upload-accepted");
        await RejectAsync("nonce-replay",async()=>{await Channel.Submit(c,Frame(h),false);});
        var bad=NewRequest();await RejectAsync("truncated-upload",async()=>{await Channel.Submit(c,Frame(HeaderOf(bad),true),false);});
        Pass("truncated-upload-recorded",JsonSerializer.Serialize(Channel.Status(c,bad.Nonce)).Contains("failed"));
        await RejectAsync("trailing-data",async()=>{await Channel.Submit(c,Frame(HeaderOf(NewRequest()),trailing:true),false);});
        await RejectAsync("changed-live-feed",async()=>{await Channel.Submit(c,Frame(HeaderOf(NewRequest() with{ExpectedFeedSha256=new string('0',64)})),false);});
        Pass("failed-uploads-do-not-switch-feed",Rules.HashFile(Path.Combine(c.Public,"updates.json"))==Rules.Hash(old));
        Channel.Worker(c,r.Nonce);Pass("worker-publishes",Rules.HashFile(Path.Combine(c.Public,"updates.json"))==Rules.Hash(feed));
        Pass("completed-receipt",JsonSerializer.Serialize(Channel.Status(c,r.Nonce)).Contains("completed"));
        Pass("previous-feed-preserved",Rules.HashFile(Path.Combine(c.State,"operations",r.Nonce,"previous-updates.json"))==Rules.Hash(old));
        Channel.Worker(c,r.Nonce);Pass("completed-reconciliation-idempotent");
        await RejectAsync("stale-current",async()=>{await Channel.Submit(c,Frame(HeaderOf(NewRequest())),false);});
        var crash=Setup("crash");var cr=NewRequest();await Channel.Submit(crash,Frame(HeaderOf(cr)),false);
        Reject("fault-before-feed",()=>Channel.Worker(crash,cr.Nonce,phase=>{if(phase=="version-ready")throw new IOException("injected");}));
        Pass("fault-retains-old-feed",Rules.HashFile(Path.Combine(crash.Public,"updates.json"))==Rules.Hash(old));
        Channel.Worker(crash,cr.Nonce);Pass("resume-immutable-version");
        var lost=Setup("lost-receipt");var lr=NewRequest();await Channel.Submit(lost,Frame(HeaderOf(lr)),false);
        Channel.Worker(lost,lr.Nonce,_=>{if(_=="feed-switched")throw new IOException("lost receipt");});
        Pass("lost-receipt-recovers-completed",JsonSerializer.Serialize(Channel.Status(lost,lr.Nonce)).Contains("completed"));
        var changed=Setup("archive-tamper");var ar=NewRequest();await Channel.Submit(changed,Frame(HeaderOf(ar)),false);
        File.WriteAllBytes(Path.Combine(changed.State,"operations",ar.Nonce,"upload",ar.Files[0].FileName),[5,6,7,8]);
        Reject("worker-rehashes-archives",()=>Channel.Worker(changed,ar.Nonce));
        Pass("bad-archive-retains-feed",Rules.HashFile(Path.Combine(changed.Public,"updates.json"))==Rules.Hash(old));
        var stale=Setup("stalled");var sr=NewRequest();await Channel.Submit(stale,Frame(HeaderOf(sr)),false);
        var statePath=Path.Combine(stale.State,"operations",sr.Nonce,"status.json");
        Rules.Atomic(statePath,Rules.Bytes(new Operation("accepted",sr.Nonce,sr.Version,DateTimeOffset.UtcNow.AddMinutes(-30))));
        Pass("stalled-operations-stop-polling",JsonSerializer.Serialize(Channel.Status(stale,sr.Nonce)).Contains("needs_attention"));
        var locked=Setup("locked");var kr=NewRequest();await Channel.Submit(locked,Frame(HeaderOf(kr)),false);
        using(var gate=new FileStream(Path.Combine(locked.State,"publish.lock"),FileMode.Create,FileAccess.ReadWrite,FileShare.None))
            Reject("concurrent-publish-lock",()=>Channel.Worker(locked,kr.Nonce));
        Pass("busy-worker-records-failure",JsonSerializer.Serialize(Channel.Status(locked,kr.Nonce)).Contains("failed"));
        // Layout 2 hosts patches only and remains isolated from the legacy lane.
        var layoutBase=ReleaseOf("1.3.0",null) with{Layout=2,RuntimeVersion="8.0.30"};
        var layoutNext=ReleaseOf("1.3.1","1.3.0") with{Layout=2,RuntimeVersion="8.0.30"};
        layoutNext=layoutNext with{Assets=layoutNext.Assets.Select(a=>a with{Bytes=400}).ToArray(),Deltas=layoutNext.Assets.Select(a=>new Delta(a.Flavor,"1.3.0",$"Dustweave-1.3.1-{a.Flavor}-from-1.3.0-win-x64.delta.zip",4,Rules.Hash([1,2,3,4]),"dustweave-copy-v2")).ToArray()};
        var layoutConfig=Setup("layout2") with{Layout=2};
        var layoutOld=Rules.Bytes(Sign(new Feed(3,"Dustweave","stable",[layoutBase])));
        File.WriteAllBytes(Path.Combine(layoutConfig.Public,"updates.json"),layoutOld);
        var layoutEvidence=Rules.Bytes(new{status="passed",cases=new[]{"upgrade-Lite","upgrade-Portable","startup-failure","bootstrap-recovery"}.Select(name=>new{@case=name,status="passed",to=layoutNext.Version,baselineHelper=true,delta=true}).ToArray()});
        Header LayoutHeader(Release next,Release? prior=null,bool full=false)
        {
            var bytes=Rules.Bytes(Sign(new Feed(3,"Dustweave","stable",[next,prior??layoutBase])));
            var blobs=(full?next.Assets.Select(a=>new Blob(a.FileName,a.Bytes,a.Sha256)):next.Deltas!.Select(d=>new Blob(d.FileName,d.Bytes,d.Sha256))).OrderBy(b=>b.FileName,StringComparer.Ordinal).ToArray();
            var request=new Request(1,"Dustweave",next.Version,Rules.Hash(layoutOld),Rules.Hash(bytes),Rules.Hash(layoutEvidence),Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(10),blobs);
            return new(Sign(request,Rules.Domain),Convert.ToBase64String(bytes),Convert.ToBase64String(layoutEvidence));
        }
        var layoutHeader=LayoutHeader(layoutNext);
        Pass("layout2-patch-only-accepted",Rules.Validate(layoutHeader,layoutConfig,DateTimeOffset.UtcNow).Request.Files.Length==2);
        Reject("layout2-rejects-full-uploads",()=>Rules.Validate(LayoutHeader(layoutNext,full:true),layoutConfig,DateTimeOffset.UtcNow));
        Reject("layout2-cannot-target-legacy-lane",()=>Rules.Validate(layoutHeader,c,DateTimeOffset.UtcNow));
        Reject("legacy-cannot-target-layout2-lane",()=>Rules.VerifyFeed(old,layoutConfig));
        Reject("layout2-runtime-crossing-rejected",()=>Rules.Validate(LayoutHeader(layoutNext with{RuntimeVersion="9.0.0"}),layoutConfig,DateTimeOffset.UtcNow));
        Reject("layout2-oversized-delta-rejected",()=>Rules.Validate(LayoutHeader(layoutNext with{Deltas=[layoutNext.Deltas![0] with{Bytes=8388609},layoutNext.Deltas![1]]}),layoutConfig,DateTimeOffset.UtcNow));
        Reject("layout2-poor-ratio-rejected",()=>Rules.Validate(LayoutHeader(layoutNext with{Deltas=[layoutNext.Deltas![0] with{Bytes=320},layoutNext.Deltas![1]]}),layoutConfig,DateTimeOffset.UtcNow));
        var longHistory=Enumerable.Range(7,24).Reverse().Select(i=>ReleaseOf($"1.4.{i}",i==7?null:$"1.4.{i-1}") with{Layout=2,RuntimeVersion="8.0.30",Deltas=[]}).ToArray();
        var longPatch=new Delta("Lite","1.4.7","Dustweave-1.4.30-Lite-from-1.4.7-win-x64.delta.zip",10485760,new string('a',64),"dustweave-copy-v2");
        var longFeed=new Feed(3,"Dustweave","stable",longHistory,[longPatch]);
        Pass("long-baseline-ten-MiB-beyond-ten-releases",Rules.VerifyFeed(Rules.Bytes(Sign(longFeed)),layoutConfig).BaselineDeltas!.Length==1);
        var longBytes=Rules.Bytes(Sign(longFeed));
        var longEvidence=Rules.Bytes(new{status="passed",cases=new[]{"upgrade-Lite","upgrade-Portable","startup-failure","bootstrap-recovery"}.Select(name=>new{@case=name,status="passed",to="1.4.30",baselineHelper=true,delta=true}).ToArray()});
        var longRequest=new Request(1,"Dustweave","1.4.30",Rules.Hash(layoutOld),Rules.Hash(longBytes),Rules.Hash(longEvidence),Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(10),[new(longPatch.FileName,longPatch.Bytes,longPatch.Sha256)]);
        var longHeader=new Header(Sign(longRequest,Rules.Domain),Convert.ToBase64String(longBytes),Convert.ToBase64String(longEvidence));
        Pass("long-baseline-upload-inventory",Rules.Validate(longHeader,layoutConfig,DateTimeOffset.UtcNow).Request.Files.Single().Bytes==10485760);
        Reject("long-baseline-upload-cannot-omit-patch",()=>Rules.Validate(longHeader with{Request=Sign(longRequest with{Files=[]},Rules.Domain)},layoutConfig,DateTimeOffset.UtcNow));
        Reject("long-baseline-size-limit",()=>Rules.VerifyFeed(Rules.Bytes(Sign(longFeed with{BaselineDeltas=[longPatch with{Bytes=10485761}]})),layoutConfig));
        Reject("long-baseline-legacy-excluded",()=>Rules.VerifyFeed(Rules.Bytes(Sign(longFeed with{BaselineDeltas=[longPatch with{FromVersion="1.4.6"}]})),layoutConfig));
        Reject("long-baseline-runtime-mismatch",()=>Rules.VerifyFeed(Rules.Bytes(Sign(longFeed with{Releases=[longHistory[0] with{RuntimeVersion="9.0.0"},..longHistory.Skip(1)]})),layoutConfig));
        Reject("long-baseline-duplicate",()=>Rules.VerifyFeed(Rules.Bytes(Sign(longFeed with{Releases=[longHistory[0] with{Deltas=[longPatch with{Bytes=4}]},..longHistory.Skip(1)]})),layoutConfig));
        await Channel.Submit(layoutConfig,Frame(layoutHeader),false);
        var layoutRequest=Rules.Read<Request>(Rules.Verify(layoutHeader.Request,layoutConfig,Rules.Domain));
        Channel.Worker(layoutConfig,layoutRequest.Nonce);
        Pass("layout2-publishes-patches-only",Directory.GetFiles(Path.Combine(layoutConfig.Public,"v1.3.1")).All(p=>p.EndsWith(".delta.zip",StringComparison.Ordinal)));
        Pass("layout2-feed-switched",Rules.VerifyFeed(File.ReadAllBytes(Path.Combine(layoutConfig.Public,"updates.json")),layoutConfig).Releases[0].Version=="1.3.1");
        var report=new{status="passed",checks=checks.Count,cases=checks,gameTouched=false,serverTouched=false};
        File.WriteAllBytes(Path.Combine(folder,"results.json"),Rules.Bytes(report));Console.WriteLine($"{checks.Count} OTA channel checks passed: {folder}");
    }
}
