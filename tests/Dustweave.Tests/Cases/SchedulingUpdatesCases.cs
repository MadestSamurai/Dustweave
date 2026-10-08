using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Dustweave;

internal static class SchedulingUpdatesCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(string name, bool condition) { if (!condition) throw new Exception(name); cases.Add(name); }
        void Reject(string name, Action action) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        string data = Path.Combine(root, "schedule-update"); Directory.CreateDirectory(data);
        var now = new DateTimeOffset(2026, 10, 7, 1, 0, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var plan = new DailySchedulePlan { Enabled = true, Accounts = [new string('a', 64), new string('b', 64)], Hour = 9, SavedUtc = now.AddHours(-1) };
        plan.Validate();
        Check("schedule becomes due at local time", DailyScheduleClock.Due(plan, now, zone) == now);
        Check("schedule accepts short startup delay", DailyScheduleClock.Due(plan, now.AddMinutes(14), zone) == now);
        Check("schedule never catches up hours of old work", DailyScheduleClock.Due(plan, now.AddMinutes(16), zone) == null);
        Check("schedule enabling after the time does not start old work", DailyScheduleClock.Due(plan with { SavedUtc = now.AddSeconds(1) }, now.AddMinutes(1), zone) == null);
        Check("disabled schedule never runs", DailyScheduleClock.Due(plan with { Enabled = false }, now, zone) == null);
        Check("legacy weekday schedule stays constrained before migration", DailyScheduleClock.Due(plan with { Days = [1] }, now, zone) == null);
        Check("legacy next weekday is computed before migration", DailyScheduleClock.Next(plan with { Days = [1] }, now, zone)?.Day == 12);
        foreach (int offset in Enumerable.Range(0, 7))
            Check("daily schedule covers day " + offset, DailyScheduleClock.Due(plan, now.AddDays(offset), zone) == now.AddDays(offset));
        Check("next daily run is tomorrow", DailyScheduleClock.Next(plan, now, zone) == now.AddDays(1));
        Check("midnight grace checks previous day", DailyScheduleClock.Due(plan with { Hour = 23, Minute = 55 }, now.AddHours(15).AddMinutes(5), zone) == now.AddHours(14).AddMinutes(55));
        var dst = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Check("spring missing local time is skipped", DailyScheduleClock.Occurrence(plan with { Hour = 2, Minute = 30 }, new(2026, 3, 8), dst) == null);
        var autumn = DailyScheduleClock.Occurrence(plan with { Hour = 1, Minute = 30 }, new(2026, 11, 1), dst);
        Check("autumn repeat uses only first occurrence", autumn?.Offset == TimeSpan.FromHours(-4));
        Reject("schedule rejects invalid account keys", () => (plan with { Accounts = ["current"] }).Validate());
        Reject("schedule rejects no selected days", () => (plan with { Days = [] }).Validate());
        Reject("schedule rejects invalid times", () => (plan with { Hour = 24 }).Validate());
        Reject("schedule rejects duplicate accounts", () => (plan with { Accounts = [new string('a', 64), new string('a', 64)] }).Validate());
        var store = new DailyScheduleStore(data); store.Save(plan);
        Check("schedule preserves fixed identities and order", store.Read().Accounts.SequenceEqual(plan.Accounts));
        Check("first occurrence can claim", store.Claim(now, now));
        Check("duplicate launch cannot claim same occurrence", !new DailyScheduleStore(data).Claim(now, now));
        Check("claim survives process restart", new DailyScheduleStore(data).IsClaimed(now));
        Check("next scheduled day may claim", store.Claim(now.AddDays(1), now.AddDays(1)));
        var migrationStore = new DailyScheduleStore(Path.Combine(data, "migration"));
        var legacy = plan with { Days = [1, 3, 5] };
        migrationStore.Save(legacy);
        Check("legacy schedule requires daily migration", !migrationStore.Read().IsDaily);
        int registrations = 0;
        var migrationTime = now.AddMinutes(1);
        Check("weekly schedule migration executes", await migrationStore.MigrateToDailyAsync(p =>
        {
            registrations++;
            Check("migration disables execution before Windows registration", !migrationStore.Read().Enabled);
            Check("Windows migration receives all days and original account order", p.IsDaily && p.Enabled && p.Accounts.SequenceEqual(legacy.Accounts));
            return Task.CompletedTask;
        }, migrationTime));
        var migrated = migrationStore.Read();
        Check("migration preserves time enabled state and account order", migrated.IsDaily && migrated.Enabled && migrated.Hour == legacy.Hour && migrated.Minute == legacy.Minute && migrated.Accounts.SequenceEqual(legacy.Accounts));
        Check("migration does not catch up the earlier time", migrated.SavedUtc == migrationTime && DailyScheduleClock.Due(migrated, now.AddMinutes(2), zone) == null);
        Check("migration runs tomorrow even if formerly excluded", DailyScheduleClock.Due(migrated, now.AddDays(1), zone) == now.AddDays(1));
        Check("migration is idempotent", !await migrationStore.MigrateToDailyAsync(_ => { registrations++; return Task.CompletedTask; }, now.AddMinutes(2)) && registrations == 1);
        var failedStore = new DailyScheduleStore(Path.Combine(data, "failed-migration"));
        failedStore.Save(legacy);
        bool failed = false;
        try { await failedStore.MigrateToDailyAsync(_ => throw new IOException("synthetic registration failure"), migrationTime); }
        catch (IOException) { failed = true; }
        Check("failed registration retains disabled daily schedule", failed && !failedStore.Read().Enabled && failedStore.Read().IsDaily && failedStore.Read().Accounts.SequenceEqual(legacy.Accounts));
        Check("failed migration cannot run the next day", DailyScheduleClock.Due(failedStore.Read(), now.AddDays(1), zone) == null);
        Check("failed migration does not endlessly retry registration", !await failedStore.MigrateToDailyAsync(_ => throw new Exception("unexpected registration retry"), now.AddMinutes(2)));
        var disabledStore = new DailyScheduleStore(Path.Combine(data, "disabled-migration"));
        disabledStore.Save(legacy with { Enabled = false, Accounts = [] });
        Check("disabled weekly draft migrates without registering Windows task", await disabledStore.MigrateToDailyAsync(_ => throw new Exception("disabled task registered"), migrationTime) && !disabledStore.Read().Enabled && disabledStore.Read().IsDaily);
        var newStore = new DailyScheduleStore(Path.Combine(data, "fresh-schedule"));
        Check("new installs stay disabled without writing a schedule", !await newStore.MigrateToDailyAsync(_ => throw new Exception("fresh task registered"), now) && !File.Exists(newStore.PlanPath));
        Reject("Windows rejects unconverted legacy weekday plans", () => DailyWindowsSchedule.Register(legacy, Environment.ProcessPath!, validateOnly: true));
        var xml = XDocument.Parse(DailyWindowsSchedule.Register(plan, Environment.ProcessPath!, validateOnly: true));
        var ns = xml.Root!.Name.Namespace;
        Check("Windows task repeats daily", xml.Descendants(ns + "ScheduleByDay").Single().Element(ns + "DaysInterval")?.Value == "1");
        Check("Windows task contains no weekday mask", !xml.Descendants(ns + "ScheduleByWeek").Any() && !xml.Descendants(ns + "DaysOfWeek").Any());
        Check("Windows task uses interactive user only", xml.Descendants(ns + "LogonType").Single().Value == "InteractiveToken");
        Check("Windows task does not require admin", xml.Descendants(ns + "RunLevel").Single().Value == "LeastPrivilege");
        Check("Windows task has the scheduled entry point", xml.Descendants(ns + "Arguments").Single().Value == "--scheduled");
        Check("Windows task does not kill a long queue", xml.Descendants(ns + "ExecutionTimeLimit").Single().Value == "PT0S");

        var notes = new Dictionary<string, string[]> { ["zh-CN"] = ["更新"], ["zh-TW"] = ["更新"], ["en-US"] = ["Update"] };
        string package = Path.Combine(data, "package.zip");
        void WriteZip(params (string Name, string Text)[] files)
        {
            using var archive = ZipFile.Open(package, ZipArchiveMode.Create);
            foreach (var f in files) { using var writer = new StreamWriter(archive.CreateEntry(f.Name).Open()); writer.Write(f.Text); }
        }
        WriteZip(("Dustweave.exe", "new executable"), ("update-package.json", JsonSerializer.Serialize(new DailyUpdatePackage("0.9.1", "Portable"))), ("data/table.json", "{}"));
        DailyUpdateAsset Asset() => new("Portable", "Dustweave-0.9.1-Portable-win-x64.zip", new FileInfo(package).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))));
        var asset = Asset(); var release = new DailyUpdateRelease("0.9.1", "0.9.0", 1, notes, [asset]);
        var older = new DailyUpdateRelease("0.9.0", null, 1, notes, [asset with { FileName = asset.FileName.Replace("0.9.1", "0.9.0") }]);
        var feed = new DailyUpdateFeed(2, "Dustweave", "stable", [release, older]);
        Check("release chain selects highest version", DailyUpdates.Latest(feed, "0.9.0") == release);
        Check("OTA cannot downgrade", DailyUpdates.Latest(feed, "0.9.2") == null);
        Reject("OTA rejects foreign download origin", () => DailyUpdates.Validate(feed with { Releases = [release with { Assets = [asset with { FileName = "https://evil.example/update.zip" }] }] }));
        Reject("OTA rejects broken release chain", () => DailyUpdates.Validate(feed with { Releases = [release with { PreviousVersion = "0.8.0" }, older] }));
        Reject("OTA rejects duplicate releases", () => DailyUpdates.Validate(feed with { Releases = [release, release] }));
        Reject("OTA rejects unsupported package layout", () => DailyUpdates.Validate(feed with { Releases = [release with { Layout = 2 }] }));
        Check("OTA verifies length and SHA256", DailyUpdates.Verify(package, asset) && !DailyUpdates.Verify(package, asset with { Sha256 = new string('0', 64) }));
        foreach (string name in new[] { "../bad", "/absolute", "data/../../account.json", "data\\bad.json", "data/ads:stream.json", "plugins/payload.dll", "accounts.json", "Dustweave.exe.", "C:/bad.exe" })
            Check("OTA rejects protected/path traversal " + name, !DailyUpdates.AllowedFile(name));
        string staging = Path.Combine(data, "staging"), target = Path.Combine(data, "install"), backup = Path.Combine(data, "backup");
        var names = DailyUpdates.Extract(package, staging, release, asset);
        Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "Dustweave.exe"), "old executable");
        Directory.CreateDirectory(Path.Combine(target, "plugins")); File.WriteAllText(Path.Combine(target, "plugins", "local.txt"), "kept");
        File.WriteAllText(Path.Combine(target, "accounts.json"), "user accounts");
        var journal = DailyUpdateTransaction.Prepare(staging, target, backup, names);
        try { DailyUpdateTransaction.Apply(journal, i => { if (i == 2) throw new IOException("injected failure"); }); } catch (IOException) { }
        DailyUpdateTransaction.Restore(DailyUpdateTransaction.Read(backup, target));
        Check("failed replacement restores previous executable", File.ReadAllText(Path.Combine(target, "Dustweave.exe")) == "old executable");
        Check("failed replacement removes only newly installed files", !File.Exists(Path.Combine(target, "data/table.json")) && !File.Exists(Path.Combine(target, "update-package.json")));
        var retry = DailyUpdateTransaction.Prepare(staging, target, backup + "-retry", names);
        DailyUpdateTransaction.Apply(retry);
        Check("valid package installs", File.ReadAllText(Path.Combine(target, "Dustweave.exe")) == "new executable");
        Check("OTA preserves user accounts", File.ReadAllText(Path.Combine(target, "accounts.json")) == "user accounts");
        Check("OTA preserves local plugins", File.ReadAllText(Path.Combine(target, "plugins/local.txt")) == "kept");
        DailyUpdateTransaction.Restore(retry);
        Check("new-version startup failure can restore old files", File.ReadAllText(Path.Combine(target, "Dustweave.exe")) == "old executable");
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trust = new DailyUpdateTrust(new() { ["test"] = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()) }, [new("cn", DailyUpdates.FeedUrl, "https://bd2.madsam.work/updates/dustweave")]);
        var signed = DailyUpdateSignatures.Sign(feed, "test", signer);
        var transport = new DailyUpdateTransport(trust);
        using var client = new HttpClient(new MemoryHandler(File.ReadAllBytes(package), JsonSerializer.SerializeToUtf8Bytes(signed)));
        var fetched = await transport.FetchAsync(client, data, default);
        Check("update feed transport is parsed and validated", fetched.Feed.Releases.Length == 2);
        string download = await transport.DownloadAsync(client, fetched, release.Version, asset.Flavor, data, null, default);
        Check("downloaded archive is verified before ready", DailyUpdates.Verify(Path.Combine(download, "package.zip"), asset));
        File.Delete(package); WriteZip(("../escape.txt", "bad"), ("update-package.json", "{}"));
        Reject("ZIP path traversal rejected before extraction", () => DailyUpdates.Extract(package, staging, release, Asset()));
        Check("ZIP traversal creates no outside file", !File.Exists(Path.Combine(data, "escape.txt")));
    }
    private sealed class MemoryHandler(byte[] archive, byte[] feed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.AbsoluteUri == DailyUpdates.FeedUrl ? feed : archive) });
    }
}
