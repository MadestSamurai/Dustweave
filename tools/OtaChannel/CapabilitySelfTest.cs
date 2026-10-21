using System.Buffers.Binary;
using System.Security.Cryptography;
using Dustweave;
namespace Dustweave.Ota;

static class CapabilitySelfTest
{
    public static async Task Run(string folder,List<string> checks)
    {
        void Pass(string name,bool ok=true){if(!ok)throw new Exception(name);checks.Add(name);}
        void Reject(string name,Action action){try{action();}catch(Exception e)when(e is IOException or InvalidDataException){Pass(name);return;}throw new Exception(name);}
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys=new Dictionary<string,string>{{"test",Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}};
        Envelope Sign<T>(T value,string domain="") { var bytes=Rules.Bytes(value);return new(1,"test","ECDSA-P256-SHA256",Convert.ToBase64String(bytes),
            Convert.ToBase64String(key.SignData(System.Text.Encoding.UTF8.GetBytes(domain).Concat(bytes).ToArray(),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))); }
        var path=Path.Combine(folder,"capability");Directory.CreateDirectory(path);
        var legacy=new Config(Path.Combine(path,"state"),Path.Combine(path,"public"),keys,2);Directory.CreateDirectory(legacy.State);Directory.CreateDirectory(legacy.Public);
        var capable=Rules.Capability(legacy);Directory.CreateDirectory(capable.State);
        Release ReleaseOf(string version,string? previous,int protocol=1)=>new(version,previous,2,
            new(){{"zh-CN",["更新"]},{"zh-TW",["更新"]},{"en-US",["Update"]}},
            new[]{"Lite","Portable"}.Select(f=>new Asset(f,$"Dustweave-{version}-{f}-win-x64.zip",10000,new('a',64))).ToArray(),[],"8.0.30",protocol);
        var original=ReleaseOf("1.4.14",null);var bridge=ReleaseOf("1.4.15-bridge.1",original.Version);var final=ReleaseOf("1.4.15",bridge.Version,2);
        byte[] old=Rules.Bytes(Sign(new Feed(3,"Dustweave","stable",[original])));
        File.WriteAllBytes(Rules.FeedPath(legacy,true),old);
        var feed=new Feed(4,"Dustweave","stable",[final,bridge,original]);byte[] signed=Rules.Bytes(Sign(feed));
        Pass("capability initial read is anchored to existing feed",Rules.FeedPath(capable)==Rules.FeedPath(legacy));
        Pass("capability write never selects legacy feed",Rules.FeedPath(capable,true)!=Rules.FeedPath(legacy,true));
        Pass("capability operations have a separate state directory",capable.State!=legacy.State && capable.Public==legacy.Public);
        Reject("legacy endpoint refuses the new signed schema",()=>Rules.VerifyFeed(signed,legacy));
        Pass("capable endpoint accepts new installer protocol",Rules.VerifyFeed(signed,capable).Releases[0].InstallerProtocol==2);
        var largePatch=new Delta("Lite",bridge.Version,$"Dustweave-{final.Version}-Lite-from-{bridge.Version}-win-x64.delta.zip",20*1024*1024,new('b',64),"dustweave-copy-v3");
        var largeRelease=final with{Assets=final.Assets.Select(a=>a with{Bytes=40*1024*1024}).ToArray(),Deltas=[largePatch]};
        var largeFeed=feed with{Releases=[largeRelease,bridge,original]};
        Pass("capable patch may exceed the old size limit",Rules.VerifyFeed(Rules.Bytes(Sign(largeFeed)),capable).Releases[0].Deltas![0].Bytes==largePatch.Bytes);
        Reject("legacy patch does not inherit capable size limit",()=>Rules.VerifyFeed(Rules.Bytes(Sign(largeFeed with{Releases=[largeRelease with{InstallerProtocol=1,Deltas=[largePatch with{Algorithm="dustweave-copy-v2"}]},bridge,original]})),capable));
        Reject("capable patch cannot exceed full archive size",()=>Rules.VerifyFeed(Rules.Bytes(Sign(largeFeed with{Releases=[largeRelease with{Deltas=[largePatch with{Bytes=41*1024*1024}]},bridge,original]})),capable));
        var mirrorCapable=capable with{MirrorBaseUrl="https://dustweave-test.cdn.7caiyun.com/dustweave/ota/v2"};
        Pass("capable mirror accepts signed larger patches",Mirror.FileUri(mirrorCapable,final.Version,new(largePatch.FileName,largePatch.Bytes,largePatch.Sha256)).Scheme=="https");
        Reject("legacy mirror retains its patch size limit",()=>Mirror.FileUri(mirrorCapable with{Capable=false},final.Version,new(largePatch.FileName,largePatch.Bytes,largePatch.Sha256)));
        Reject("unknown installer protocol is rejected",()=>Rules.VerifyFeed(Rules.Bytes(Sign(feed with{Releases=[final with{InstallerProtocol=3},bridge,original]})),capable));
        Reject("bridge cannot be exposed before final feed",()=>Channel.CheckPreparedInventory(legacy,new Feed(3,"Dustweave","stable",[bridge,original])));
        foreach(var command in new[]{"submit","status","repair","repair-index","status "+new string('a',32)})Pass("capable restricted command "+command,Channel.ParseCommand("dustweave-ota capable "+command).EndsWith(command));
        foreach(var command in new[]{"capable status","status; id","repair ../../else","submit other"})Reject("capable command blocks "+command,()=>Channel.ParseCommand("dustweave-ota capable "+command));
        var evidence=Rules.Bytes(new{status="passed",cases=new[]{"upgrade-Lite","upgrade-Portable","startup-failure","bootstrap-recovery","native-repair-Lite","native-repair-Portable"}.Select(n=>new{@case=n,status="passed",to=final.Version,baselineHelper=true}).ToArray()});
        var r=new Request(1,"Dustweave",final.Version,Rules.Hash(old),Rules.Hash(signed),Rules.Hash(evidence),Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(10),[]);
        var h=new Header(Sign(r,Rules.Domain),Convert.ToBase64String(signed),Convert.ToBase64String(evidence));
        var missingRepair=Rules.Bytes(new{status="passed",cases=new[]{"upgrade-Lite","upgrade-Portable","startup-failure","bootstrap-recovery"}.Select(n=>new{@case=n,status="passed",to=final.Version,baselineHelper=true}).ToArray()});
        var incomplete=h with{Request=Sign(r with{EvidenceSha256=Rules.Hash(missingRepair)},Rules.Domain),Evidence=Convert.ToBase64String(missingRepair)};
        Reject("capable publication requires independent repair acceptance",()=>Rules.Validate(incomplete,capable,DateTimeOffset.UtcNow));
        var frame=new MemoryStream();byte[] head=Rules.Bytes(h),prefix=new byte[4];BinaryPrimitives.WriteInt32BigEndian(prefix,head.Length);frame.Write(prefix);frame.Write(head);frame.Position=0;
        await Channel.Submit(capable,frame,false);
        Reject("publication waits for complete repair inventory",()=>Channel.Worker(capable,r.Nonce));
        Pass("failed capability publication leaves legacy bytes intact",Rules.HashFile(Rules.FeedPath(legacy))==Rules.Hash(old));
        void Repair(Release release)
        {
            byte[] body=[4,5,6];string hash=DailyUpdateRepairManifest.Hash(body);
            string objects=Path.Combine(legacy.Public,"repair","objects");Directory.CreateDirectory(objects);File.WriteAllBytes(Path.Combine(objects,hash+".bin"),body);
            foreach(var asset in release.Assets)
            {
                var files=new[]{"Dustweave.exe","update-package.json","app/support/Dustweave.Updater.exe"}.Select(n=>new DailyRepairFile(n,3,hash,[new(hash,3)])).ToArray();
                var manifest=new DailyRepairManifest(1,"Dustweave",release.Version,asset.Flavor,2,release.RuntimeVersion!,asset.Sha256,files);
                string target=Path.Combine(legacy.Public,"repair","v"+release.Version);Directory.CreateDirectory(target);
                File.WriteAllBytes(Path.Combine(target,DailyUpdateRepairManifest.Name(release.Version,asset.Flavor)),Rules.Bytes(DailyUpdateRepairManifest.Sign(manifest,"test",key)));
            }
        }
        Repair(final);Channel.Worker(capable,r.Nonce);
        Pass("capable feed published without touching legacy",Rules.HashFile(Rules.FeedPath(capable))==Rules.Hash(signed) && Rules.HashFile(Rules.FeedPath(legacy))==Rules.Hash(old));
        Repair(bridge);Channel.CheckPreparedInventory(legacy,new Feed(3,"Dustweave","stable",[bridge,original]));Pass("bridge becomes eligible only when final and source inventories exist");
        Reject("different bridge cannot inherit final readiness",()=>Channel.CheckPreparedInventory(legacy,new Feed(3,"Dustweave","stable",[bridge with{RuntimeVersion="8.0.31"},original])));
        Channel.Worker(capable,r.Nonce);Pass("capable reconciliation is idempotent",Rules.HashFile(Rules.FeedPath(capable))==Rules.Hash(signed));
    }
}
