using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dustweave.Ota;

sealed record Blob(string FileName, long Bytes, string Sha256);
sealed record Request(int Schema, string Product, string Version, string ExpectedFeedSha256, string FeedSha256,
    string EvidenceSha256, string Nonce, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc, Blob[] Files);
sealed record Envelope(int Schema, string KeyId, string Algorithm, string Payload, string Signature);
sealed record Header(Envelope Request, string Feed, string Evidence);
sealed record Config(string State, string Public, Dictionary<string,string> Keys, int Layout = 1, string? MirrorBaseUrl = null, bool Capable = false);
sealed record Asset(string Flavor, string FileName, long Bytes, string Sha256);
sealed record Delta(string Flavor, string FromVersion, string FileName, long Bytes, string Sha256, string Algorithm);
sealed record Release(string Version, string? PreviousVersion, int Layout, Dictionary<string,string[]> Notes, Asset[] Assets, Delta[]? Deltas, string? RuntimeVersion = null, int InstallerProtocol = 1);
sealed record Feed(int Schema, string Product, string Channel, Release[] Releases, Delta[]? BaselineDeltas = null);

static class Rules
{
    public static string FeedPath(Config c, bool writing = false)
    {
        string path = Path.Combine(c.Public, c.Capable ? "updates-v3.json" : "updates.json");
        // The first capable publication is compare-and-swapped against the
        // legacy feed. It never writes that feed or changes its assets.
        return writing || File.Exists(path) ? path : Path.Combine(c.Public, "updates.json");
    }
    public static Config Capability(Config c)
    {
        Need(c.Layout == 2, "Capability feed requires layout 2");
        return c with { State = Path.Combine(c.State, "capable"), Capable = true };
    }
    public static Delta[] Patches(Feed feed) => (feed.Releases[0].Deltas??[]).Concat(feed.BaselineDeltas??[]).ToArray();
    public const string Domain = "Dustweave-OTA-Publish-v1\n";
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T Read<T>(byte[] data) => JsonSerializer.Deserialize<T>(data, Json) ?? throw new InvalidDataException("Empty JSON");
    public static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static string HashFile(string path) { using var file=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    public static bool IsHash(string? v) => v is { Length:64 } && v.All(Uri.IsHexDigit);
    public static bool IsNonce(string? v) => v is { Length:32 } && v.All(c => char.IsAsciiHexDigit(c) && !char.IsUpper(c));
    public static void Need(bool ok, string message) { if(!ok) throw new InvalidDataException(message); }
    public static void NoLinks(string path)
    {
        for(string? p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))
            Need(!File.Exists(p) && !Directory.Exists(p) || (File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"Linked path rejected");
    }
    public static byte[] Verify(Envelope e, Config config, string domain)
    {
        Need(e.Schema==1 && e.Algorithm=="ECDSA-P256-SHA256","Unknown signature format");
        Need(config.Keys.ContainsKey(e.KeyId),"Untrusted signing key");
        var payload=Convert.FromBase64String(e.Payload);
        Need(payload.Length is >0 and <=1048576,"Payload size rejected");
        using var key=ECDsa.Create(); key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(config.Keys[e.KeyId]),out _);
        Need(key.VerifyData(Encoding.UTF8.GetBytes(domain).Concat(payload).ToArray(),Convert.FromBase64String(e.Signature),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation),"Signature rejected");
        return payload;
    }
    public static Feed VerifyFeed(byte[] bytes, Config config)
    {
        var feed=Read<Feed>(Verify(Read<Envelope>(bytes),config,""));
        Need((feed.Schema==config.Layout+1 || config.Capable && feed.Schema==4) && feed.Product=="Dustweave" && feed.Channel=="stable" && feed.Releases.Length is >0 and <=300,"Invalid feed");
        var versions=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<feed.Releases.Length;i++)
        {
            var r=feed.Releases[i]; var version=DailyVersion.Parse(r.Version);
            Need(versions.Add(r.Version) && r.Layout==config.Layout && (r.Layout == 1 || System.Version.TryParse(r.RuntimeVersion,out _)),"Duplicate release or invalid layout");
            Need(r.InstallerProtocol is 1 or 2 && (r.InstallerProtocol == 1 || feed.Schema == 4 && config.Capable), "Unsupported installer protocol");
            Need(r.Assets.Length==2 && r.Assets.Select(a=>a.Flavor).Order().SequenceEqual(new[]{"Lite","Portable"}),"Both flavors required");
            Need(new[]{"zh-CN","zh-TW","en-US"}.All(l=>r.Notes.TryGetValue(l,out var n) && n.Length is >0 and <=100 && n.All(s=>s.Length is >0 and <=4000)),"Missing localized notes");
            if(i+1<feed.Releases.Length) Need(r.PreviousVersion==feed.Releases[i+1].Version && version>DailyVersion.Parse(feed.Releases[i+1].Version),"Broken release chain");
            foreach(var a in r.Assets) Need(a.FileName==$"Dustweave-{r.Version}-{a.Flavor}-win-x64.zip" && a.Bytes is >0 and <=629145600 && IsHash(a.Sha256),"Invalid asset");
            var deltas=r.Deltas??[];
            Need(deltas.Length<=(config.Layout == 1 ? 12 : 20) && deltas.Select(d=>(d.Flavor,d.FromVersion)).Distinct().Count()==deltas.Length,"Too many or duplicate patches");
            foreach(var d in deltas) Need(d.Flavor is "Lite" or "Portable" && (d.Algorithm==(config.Layout == 1 ? "dustweave-cdc-v1" : "dustweave-copy-v2") || r.InstallerProtocol == 2 && d.Algorithm == "dustweave-copy-v3") && DailyVersion.Parse(d.FromVersion)<version &&
                d.FileName==$"Dustweave-{r.Version}-{d.Flavor}-from-{d.FromVersion}-win-x64.delta.zip" && d.Bytes > 0 &&
                d.Bytes <= (config.Layout == 1 || r.InstallerProtocol == 2 && d.Algorithm == "dustweave-copy-v3" ? 629145600 : 8388608) &&
                (r.InstallerProtocol != 2 || d.Bytes < r.Assets.Single(a=>a.Flavor==d.Flavor).Bytes) && IsHash(d.Sha256),"Invalid patch");
        }
        var top=feed.Releases[0];var extended=feed.BaselineDeltas??[];
        Need(extended.Length<=feed.Releases.Length*2 && (extended.Length==0 || config.Layout==2),"Invalid baseline patch count");
        Need(Patches(feed).Select(d=>(d.Flavor,d.FromVersion)).Distinct().Count()==Patches(feed).Length,"Duplicate baseline patch");
        foreach(var d in extended) Need(d.Flavor is "Lite" or "Portable" && (d.Algorithm=="dustweave-copy-v2" || top.InstallerProtocol == 2 && d.Algorithm=="dustweave-copy-v3") &&
            DailyVersion.Parse(d.FromVersion)>=DailyVersion.Parse("1.4.7") && DailyVersion.Parse(d.FromVersion)<DailyVersion.Parse(top.Version) &&
            feed.Releases.Any(r=>r.Version==d.FromVersion && r.Layout==top.Layout && (top.InstallerProtocol==2 || r.RuntimeVersion==top.RuntimeVersion)) &&
            d.FileName==$"Dustweave-{top.Version}-{d.Flavor}-from-{d.FromVersion}-win-x64.delta.zip" && d.Bytes is >0 and <=10485760 && IsHash(d.Sha256),"Invalid long-term baseline patch");
        return feed;
    }
    public static (Request Request, byte[] Feed, byte[] Evidence) Validate(Header h,Config config,DateTimeOffset now)
    {
        var r=Read<Request>(Verify(h.Request,config,Domain));
        Need(r.Schema==1 && r.Product=="Dustweave" && IsNonce(r.Nonce),"Invalid request identity");
        Need(r.IssuedUtc<=now.AddSeconds(60) && r.IssuedUtc>=now.AddMinutes(-15) && r.ExpiresUtc>now && r.ExpiresUtc<=r.IssuedUtc.AddMinutes(15),"Expired request");
        Need(IsHash(r.ExpectedFeedSha256) && IsHash(r.FeedSha256) && IsHash(r.EvidenceSha256),"Invalid digest");
        var feedBytes=Convert.FromBase64String(h.Feed); var evidence=Convert.FromBase64String(h.Evidence);
        Need(feedBytes.Length<=1048576 && evidence.Length<=262144,"Metadata too large");
        Need(Hash(feedBytes)==r.FeedSha256 && Hash(evidence)==r.EvidenceSha256,"Metadata changed");
        var feed=VerifyFeed(feedBytes,config);
        Need(config.Capable ? feed.Schema == 4 && feed.Releases[0].InstallerProtocol == 2 : feed.Releases[0].InstallerProtocol == 1, "Incorrect publication index");
        Need(feed.Releases[0].Version==r.Version && feed.Releases.All(v=>DailyVersion.Parse(v.Version)>=DailyVersion.Parse("1.0.0-beta")),"Only public release chain supported");
        var top=feed.Releases[0];
        foreach(var d in Patches(feed))
        {
            int index=Array.FindIndex(feed.Releases,b=>b.Version==d.FromVersion);
            Need(index>0,"Missing patch baseline");
            if(config.Layout>=2) Need((index<=10 || DailyVersion.Parse(d.FromVersion)>=DailyVersion.Parse("1.4.7")) && (top.InstallerProtocol==2 || feed.Releases[index].RuntimeVersion==top.RuntimeVersion) && (DailyVersion.Parse(d.FromVersion)>=DailyVersion.Parse("1.4.7") || d.Bytes<top.Assets.Single(a=>a.Flavor==d.Flavor).Bytes*0.8),"Patch exceeds automatic update policy");
        }
        var expected=(config.Layout == 1 ? top.Assets : []).Select(a=>new Blob(a.FileName,a.Bytes,a.Sha256.ToLowerInvariant())).Concat(Patches(feed).Select(d=>new Blob(d.FileName,d.Bytes,d.Sha256.ToLowerInvariant()))).OrderBy(f=>f.FileName,StringComparer.Ordinal).ToArray();
        Need(r.Files.SequenceEqual(expected) && r.Files.Sum(f=>f.Bytes)<=1073741824,"Archive inventory differs from signed feed");
        using var doc=JsonDocument.Parse(evidence); var root=doc.RootElement;
        Need(root.GetProperty("status").GetString()=="passed","Package acceptance did not pass");
        var cases=root.GetProperty("cases").EnumerateArray().ToArray();
        foreach(var name in new[]{"upgrade-Lite","upgrade-Portable","startup-failure","bootstrap-recovery"})
            Need(cases.Any(c=>c.GetProperty("case").GetString()==name && c.GetProperty("status").GetString()=="passed" &&
                (!name.StartsWith("upgrade-") || c.GetProperty("to").GetString()==r.Version && c.GetProperty("baselineHelper").GetBoolean())),"Missing packaged acceptance: "+name);
        if(top.InstallerProtocol==2)
            foreach(var flavor in new[]{"Lite","Portable"})
                Need(cases.Any(c=>c.GetProperty("case").GetString()=="native-repair-"+flavor && c.GetProperty("status").GetString()=="passed" && c.TryGetProperty("to",out var repairVersion) && repairVersion.GetString()==r.Version),"Missing native repair acceptance: "+flavor);
        return (r,feedBytes,evidence);
    }
    public static void CheckArchive(string path,Blob blob)
    {
        NoLinks(path); Need(new FileInfo(path).Length==blob.Bytes && HashFile(path)==blob.Sha256,"Archive hash mismatch: "+blob.FileName);
    }
    public static void Atomic(string path,byte[] bytes)
    {
        NoLinks(path);var temporary=path+".new-"+Guid.NewGuid().ToString("N");
        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(bytes);stream.Flush(true); }
        if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead|UnixFileMode.OtherRead);
        File.Move(temporary,path,true);
    }
}
