using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Dustweave;

internal static class UpdateLayoutCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(string name, bool ok) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(string name, Action action) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        var notes = new Dictionary<string,string[]> { ["zh-CN"]=["更新"], ["zh-TW"]=["更新"], ["en-US"]=["Update"] };
        DailyUpdateRelease Release(int index) => new($"1.3.{index}", index == 0 ? null : $"1.3.{index-1}", 2, notes,
            [new("Lite", $"Dustweave-1.3.{index}-Lite-win-x64.zip", 1000000, new('a',64))],
            [new("Lite", "1.3.0", DailyUpdateDeltaPackage.Name($"1.3.{index}", "Lite", "1.3.0"), 100000, new('b',64), DailyUpdateDeltaPackage.Algorithm)], "8.0.30");
        var releases = Enumerable.Range(0,12).Reverse().Select(Release).ToArray();
        releases[^1] = releases[^1] with { Deltas = [] };
        var feed = new DailyUpdateFeed(3,"Dustweave","stable", releases); DailyUpdates.Validate(feed);
        var installed = new DailyUpdatePackage("1.3.0","Lite", Layout:2,RuntimeVersion:"8.0.30");
        Check("ten release steps permit an exact direct delta", DailyUpdates.ManualUpdateReason(feed, releases[1], installed) == null);
        Check("eleven release steps require manual migration", DailyUpdates.ManualUpdateReason(feed, releases[0], installed) == "updates.manual_distance");
        Check("old single-file layout never migrates automatically", DailyUpdates.ManualUpdateReason(feed, releases[1], installed with { Layout=1 }) == "updates.manual_layout");
        Check("runtime changes require full manual package", DailyUpdates.ManualUpdateReason(feed,releases[1] with { RuntimeVersion="8.0.31" },installed)=="updates.manual_runtime");
        Check("oversized delta cannot trigger background full download", DailyUpdates.ManualUpdateReason(feed,releases[1] with { Deltas=[releases[1].Deltas![0] with { Bytes=DailyUpdates.MaxAutomaticDelta+1 }] },installed)=="updates.manual_size");
        Check("missing direct base never chains intermediate downloads",DailyUpdates.ManualUpdateReason(feed,releases[1] with { Deltas=[] },installed)=="updates.manual_delta");
        var future=Enumerable.Range(7,24).Reverse().Select(i=>new DailyUpdateRelease($"1.4.{i}",i==7?null:$"1.4.{i-1}",2,notes,
            [new("Lite",$"Dustweave-1.4.{i}-Lite-win-x64.zip",24000000,new('a',64))],[],"8.0.30")).ToArray();
        var longPatch=new DailyUpdateDelta("Lite","1.4.7",DailyUpdateDeltaPackage.Name("1.4.30","Lite","1.4.7"),10*1024*1024,new('b',64),DailyUpdateDeltaPackage.Algorithm);
        var longFeed=new DailyUpdateFeed(3,"Dustweave","stable",future,[longPatch]);DailyUpdates.Validate(longFeed);
        var longInstalled=new DailyUpdatePackage("1.4.7","Lite",Layout:2,RuntimeVersion:"8.0.30");
        Check("1.4.7 accepts a direct ten MiB patch across 23 releases",DailyUpdates.ManualUpdateReason(longFeed,future[0],longInstalled)==null);
        Check("latest carries authenticated extended patches into the installer job",DailyUpdates.Latest(longFeed,"1.4.7")!.Deltas!.Single()==longPatch);
        Check("merging the current baseline index is idempotent",DailyUpdates.WithBaselines(longFeed,DailyUpdates.Latest(longFeed,"1.4.7")!).Deltas!.Length==1);
        Reject("long term payload above ten MiB is rejected",()=>DailyUpdates.Validate(longFeed with{BaselineDeltas=[longPatch with{Bytes=10*1024*1024+1}]}));
        Reject("extended patches cannot reuse a legacy baseline",()=>DailyUpdates.Validate(longFeed with{BaselineDeltas=[longPatch with{FromVersion="1.4.6"}]}));
        Reject("extended patches cannot redirect archive paths",()=>DailyUpdates.Validate(longFeed with{BaselineDeltas=[longPatch with{FileName="other.zip"}]}));
        Reject("duplicate ordinary and extended patch rejected",()=>DailyUpdates.Validate(longFeed with{Releases=[future[0] with{Deltas=[longPatch]},..future.Skip(1)]}));
        Check("long term baseline still rejects a different runtime",DailyUpdates.ManualUpdateReason(longFeed,future[0],longInstalled with{RuntimeVersion="8.0.29"})=="updates.manual_runtime");
        Check("split layout allows only package data, not arbitrary executables",DailyUpdates.AllowedFile("app/Dustweave.Core.dll") && DailyUpdates.AllowedFile("support/Dustweave.Updater.exe") && !DailyUpdates.AllowedFile("other.exe") && !DailyUpdates.AllowedFile("plugins/private.dll"));
        string folder=Path.Combine(root,"layout2");Directory.CreateDirectory(folder);
        Check("nested distribution limits payloads and keeps executable at root",DailyUpdates.AllowedFile("app/Dustweave.Core.dll") && DailyUpdates.AllowedFile("app/support/Dustweave.Updater.exe") && !DailyUpdates.AllowedFile("app/Dustweave.exe") && !DailyUpdates.AllowedFile("app/plugins/private.dll") && !DailyUpdates.AllowedFile("app/app/Dustweave.Core.dll") && !DailyUpdates.AllowedFile("app/../outside.dll"));
        string packageRoot=Path.Combine(folder,"安装 folder"), payloadRoot=Path.Combine(packageRoot,"app");Directory.CreateDirectory(payloadRoot);
        File.WriteAllBytes(Path.Combine(packageRoot,"Dustweave.exe"),[0]);
        File.WriteAllText(Path.Combine(payloadRoot,DailyTools.Marker),"{\"protocol\":1}");
        Check("nested utilities resolve from installation root",DailyTools.PackageDirectory(packageRoot)==payloadRoot);
        Check("nested utilities keep a single executable identity",DailyTools.Unified(payloadRoot) && DailyTools.StartInfo(payloadRoot,"live").FileName==Path.Combine(packageRoot,"Dustweave.exe") && DailyTools.StartInfo(packageRoot,"exporter").FileName==Path.Combine(packageRoot,"Dustweave.exe"));
        Check("nested evidence paths remain under app",DailyTools.EvidenceSpec(packageRoot,"policy.json")==Path.Combine(payloadRoot,"connection/specs","policy.json"));
        byte[] original = new byte[512000]; new Random(7391).NextBytes(original);
        var old = new Dictionary<string,byte[]> { ["Dustweave.exe"]=[1], ["app/Dustweave.Core.dll"]=original, ["app/coreclr.dll"]=original, ["docs/removed.md"]=[4] };
        var next = new Dictionary<string,byte[]> { ["Dustweave.exe"]=[1], ["app/Dustweave.Core.dll"]=original[..13001].Concat(new byte[]{9,2,3}).Concat(original[13001..]).ToArray(), ["app/coreclr.dll"]=original, ["docs/new.md"]=[8] };
        string Zip(string version, Dictionary<string,byte[]> files, int layout=2)
        {
            string path=Path.Combine(folder,version+".zip");
            files["update-package.json"]=JsonSerializer.SerializeToUtf8Bytes(installed with { Version=version, Files=files.Keys.Append("update-package.json").Distinct().Order().ToArray(), Layout=layout }, DailyUpdateJson.Options);
            using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
            foreach(var file in files){using var stream=zip.CreateEntry(file.Key).Open();stream.Write(file.Value);} return path;
        }
        string oldZip=Zip("1.3.0",old), targetZip=Zip("1.3.1",next), installation=Path.Combine(folder,"installed");
        ZipFile.ExtractToDirectory(oldZip,installation);
        var delta=DailyUpdateDeltaPackage.Create(oldZip,targetZip,Path.Combine(folder,"patch"));
        string patch=Path.Combine(folder,"patch",delta.FileName);
        var asset=new DailyUpdateAsset("Lite","Dustweave-1.3.1-Lite-win-x64.zip",new FileInfo(targetZip).Length,DailyUpdateTransaction.Hash(targetZip));
        var release=Release(1) with { Assets=[asset], Deltas=[delta] };
        var manifest=DailyUpdateDeltaPackage.Read(patch,release,asset,delta);
        string stage=Path.Combine(folder,"stage");
        var names=DailyUpdateDeltaPackage.Extract(patch,installation,stage,release,asset,delta,copyUnchanged:false);
        Check("sparse reconstruction writes changed files only",!File.Exists(Path.Combine(stage,"app/coreclr.dll")) && File.ReadAllBytes(Path.Combine(stage,"app/Dustweave.Core.dll")).SequenceEqual(next["app/Dustweave.Core.dll"]));
        var retained=manifest.Files.Where(f=>f.Mode=="copy").ToDictionary(f=>f.Name,f=>f.Sha256);
        using(var runtimeLock=new FileStream(Path.Combine(installation,"app/coreclr.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var journal=DailyUpdateTransaction.Prepare(stage,installation,Path.Combine(folder,"backup"),names,retained);
            Check("locked unchanged runtime is not backed up or replaced",journal.Files.All(f=>f.Name is not ("app/coreclr.dll" or "Dustweave.exe")));
            journal=DailyUpdateTransaction.Apply(journal);
            Check("split assembly update commits changed business files",File.ReadAllBytes(Path.Combine(installation,"app/Dustweave.Core.dll")).SequenceEqual(next["app/Dustweave.Core.dll"]));
            DailyUpdateTransaction.Restore(journal);
            Check("split assembly rollback restores deleted and replaced files",File.ReadAllBytes(Path.Combine(installation,"app/Dustweave.Core.dll")).SequenceEqual(original) && File.Exists(Path.Combine(installation,"docs/removed.md")) && !File.Exists(Path.Combine(installation,"docs/new.md")));
        }
        var signedFeed=new DailyUpdateFeed(3,"Dustweave","stable",[release,Release(0) with {Deltas=[]}]);
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trust=new DailyUpdateTrust(new(){["test"]=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())},[new("test","https://updates.test/feed.json","https://updates.test")]);
        var signed=DailyUpdateSignatures.Sign(signedFeed,"test",key);
        var handler=new Missing();using var client=new HttpClient(handler);
        try { await new DailyUpdateTransport(trust).DownloadAsync(client,new(signedFeed,signed,"test"),release.Version,"Lite",Path.Combine(folder,"download"),null,default,installation); throw new Exception("Missing patch accepted"); }
        catch(InvalidDataException e){Check("new layout missing patch reports manual recovery",e.Message=="updates.manual_delta");}
        Check("new layout never requests full archive on delta failure",handler.Paths.Count>0 && handler.Paths.All(p=>p.EndsWith(".delta.zip")));
        File.WriteAllBytes(Path.Combine(installation,"app/coreclr.dll"),[0]);
        Reject("retained file mutation between extraction and prepare is rejected",()=>DailyUpdateTransaction.Prepare(stage,installation,Path.Combine(folder,"mutated-backup"),names,retained));
        string bundleBaseZip = Zip("1.5.0", new() { ["Dustweave.exe"]=original, ["app/docs/test.md"]=[9] }, 3);
        var bundledBase = new DailyUpdateRelease("1.5.0", null, 3, notes,
            [new("Lite", "Dustweave-1.5.0-Lite-win-x64.zip", new FileInfo(bundleBaseZip).Length, DailyUpdateTransaction.Hash(bundleBaseZip))], [], "8.0.30");
        string bundleZip = Zip("1.5.1", new() { ["Dustweave.exe"]=original[..13001].Concat(new byte[]{3,4}).Concat(original[13001..]).ToArray(), ["app/docs/test.md"]=[9] }, 3);
        var bundleAsset = new DailyUpdateAsset("Lite", "Dustweave-1.5.1-Lite-win-x64.zip", new FileInfo(bundleZip).Length, DailyUpdateTransaction.Hash(bundleZip));
        var bundleDelta = DailyUpdateDeltaPackage.Create(bundleBaseZip,bundleZip,Path.Combine(folder,"bundle-patch"));
        var bundleNext = bundledBase with { Version="1.5.1", PreviousVersion="1.5.0", Assets=[bundleAsset], Deltas=[bundleDelta] };
        var bundleFeed = new DailyUpdateFeed(3,"Dustweave","stable",[bundleNext,bundledBase]);
        DailyUpdates.Validate(bundleFeed);
        var bundleInstalled = installed with {Layout=3,Version="1.5.0"};
        Check("bundled updates accept matching small deltas",DailyUpdates.ManualUpdateReason(bundleFeed,bundleNext,bundleInstalled)==null);
        Check("bundled runtime change requires manual download",DailyUpdates.ManualUpdateReason(bundleFeed,bundleNext,bundleInstalled with {RuntimeVersion="8.0.29"})=="updates.manual_runtime");
        Check("bundled missing delta requires manual download",DailyUpdates.ManualUpdateReason(bundleFeed,bundleNext with {Deltas=[]},bundleInstalled)=="updates.manual_delta");
        Check("bundled delta over ten MiB requires manual download",DailyUpdates.ManualUpdateReason(bundleFeed,bundleNext with {Deltas=[bundleDelta with {Bytes=DailyUpdates.MaxAutomaticDelta+1}]},bundleInstalled)=="updates.manual_size");
        Check("old installations cannot enter bundled feed automatically", DailyUpdates.ManualUpdateReason(bundleFeed,bundleNext,installed) == "updates.manual_layout");
        Reject("bundled feed cannot add block reconstruction protocol",()=>DailyUpdates.Validate(bundleFeed with {Releases=[bundleNext with {InstallerProtocol=2},bundledBase]}));
        string bundleInstall=Path.Combine(folder,"bundle-install"); ZipFile.ExtractToDirectory(bundleBaseZip,bundleInstall);
        var fullHandler = new FullArchive(File.ReadAllBytes(Path.Combine(folder,"bundle-patch",bundleDelta.FileName))); using var fullClient = new HttpClient(fullHandler);
        var fullSigned = DailyUpdateSignatures.Sign(bundleFeed,"test",key);
        var transport = new DailyUpdateTransport(trust);
        string downloaded = await transport.DownloadAsync(fullClient,new(bundleFeed,fullSigned,"test"),"1.5.1","Lite",Path.Combine(folder,"bundle-data"),null,default,bundleInstall);
        Check("bundled download requests only the signed patch",fullHandler.Paths.SequenceEqual(["/v1.5.1/"+bundleDelta.FileName]) && !File.Exists(Path.Combine(downloaded,"package.zip")));
        File.Copy(bundleZip,Path.Combine(downloaded,"package.zip")); // Stale full caches must not bypass the policy.
        File.WriteAllText(Path.Combine(bundleInstall,"user.keep"),"keep");
        foreach (var damage in new[]{"changed","missing","disabled"})
        {
            if(damage=="changed") File.WriteAllBytes(Path.Combine(bundleInstall,"Dustweave.exe"),new byte[original.Length]);
            if(damage=="missing") File.Delete(Path.Combine(bundleInstall,"Dustweave.exe"));
            try { await transport.DownloadAsync(fullClient,new(bundleFeed,fullSigned,"test"),"1.5.1","Lite",Path.Combine(folder,"bundle-data"),null,default,bundleInstall,allowDelta:damage!="disabled");throw new Exception("Damaged base accepted"); }
            catch(InvalidDataException e){Check("bundled "+damage+" base requires manual download",e.Message=="updates.manual_delta");}
        }
        Check("bundled failure never requests a full archive or edits user files",fullHandler.Paths.All(p=>p.EndsWith(".delta.zip")) && File.ReadAllText(Path.Combine(bundleInstall,"user.keep"))=="keep");
    }
    private sealed class Missing : HttpMessageHandler
    {
        public List<string> Paths {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        { Paths.Add(request.RequestUri!.AbsolutePath);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
    }
    private sealed class FullArchive(byte[] payload) : HttpMessageHandler
    {
        public List<string> Paths {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        { Paths.Add(request.RequestUri!.AbsolutePath); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)}); }
    }
}
