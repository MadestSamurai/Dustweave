using BD2Daily;
using BD2Daily.Desktop;
using System.Text.Json;
internal static class GuildCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool v, string m)
        {
            if (!v)
                throw new Exception(m);
        }
        void Case(string name, Action fn)
        {
            fn();
            cases.Add("guild: " + name);
        }
        async Task Async(string name, Func<Task> fn)
        {
            await fn();
            cases.Add("guild: " + name);
        }
        void Throws(Action fn)
        {
            try
            {
                fn();
            }
            catch (Exception e) when (e is not NullReferenceException) { return; }
            throw new Exception("Expected rejection");
        }
        async Task Reject(Func<Task> fn)
        {
            try
            {
                await fn();
            }
            catch (InvalidOperationException) { return; }
            catch (InvalidDataException) { return; }
            throw new Exception("Expected session rejection");
        }
        string NewRoot() => Path.Combine(root, "guild-" + Guid.NewGuid().ToString("N"));
        DailySnapshot Snap()
        {
            var env = new DemoEnvironment(NewRoot());
            return env.ReadSnapshot()!;
        }
        GuildReceipt Intent(DailySnapshot s) => GuildStore.Intent(Guid.NewGuid().ToString("N"), s);
        GuildTrace Flow()
        {
            var s = Snap();
            var r = Intent(s);
            r.MayHaveDispatched = true;
            r.RequestCount = 1;
            r.ResponseSeen = true;
            r.CallbackMatched = true;
            r.MemberMatched = true;
            r.CacheMatched = true;
            r.AttendanceGranted = true;
            r.State = "response_received";
            return new()
            {
                Origin = "synthetic",
                Event = "decision",
                Snapshot = s,
                Receipt = r,
                NowUtcTicks = DateTime.UtcNow.Ticks,
                ResponseAtTicks = DateTime.UtcNow.AddSeconds(-3).Ticks,
                HasControl = true
            };
        }
        Case("fresh home allows one entry", () => { var s = Snap(); Check(GuildPolicy.Gate(s, null!, DateTime.UtcNow.Ticks) == "", "fresh blocked"); });
        foreach (var condition in new[] { "unsupported", "no-guild", "server-missing", "boundary", "popup", "not-menu", "already-open", "old-frame" })
            Case("entry rejects " + condition, () =>
            {
                var s = Snap();
                switch (condition)
                {
                    case "unsupported":
                        s.Guild.Supported = false;
                        break;
                    case "no-guild":
                        s.Guild.InGuild = false;
                        break;
                    case "server-missing":
                        s.Guild.ServerKey = "";
                        break;
                    case "boundary":
                        s.Guild.ResetTicks = s.Guild.ServerTicks + TimeSpan.FromSeconds(30).Ticks;
                        break;
                    case "popup":
                        s.Guild.BlockReason = "unexpected modal";
                        break;
                    case "not-menu":
                        s.Guild.MenuVisible = false;
                        break;
                    case "already-open":
                        s.Guild.GuildVisible = true;
                        break;
                    case "old-frame":
                        s.FrameUtcTicks -= TimeSpan.FromMinutes(1).Ticks;
                        break;
                }
                Check(GuildPolicy.Gate(s, null!, DateTime.UtcNow.Ticks) != "", condition);
            });
        foreach (var state in new[] { "completed", "confirmed_cleanup_pending", "checked_no_grant", "prepared", "waiting_response", "unknown" })
            Case("journal blocks retry of " + state, () => { var s = Snap(); var r = Intent(s); r.State = state; r.MayHaveDispatched = state != "prepared"; var store = new GuildStore(NewRoot()); store.Save(r); var reopened = new GuildStore(store.Root); Check(GuildPolicy.Gate(s, reopened.Prior(s), DateTime.UtcNow.Ticks) != "", "retry allowed"); });
        Case("unresolved survives a reset and guild change", () => { var s = Snap(); var r = Intent(s); r.State = "unknown"; r.MayHaveDispatched = true; var store = new GuildStore(NewRoot()); store.Save(r); s.Guild.CycleKey += "1"; s.Guild.GuildKey = DailyIdentity.Hash("other-guild"); Check(store.Prior(s)?.Id == r.Id, "unresolved erased"); });
        Case("completed opens at the next game cycle", () => { var s = Snap(); var r = Intent(s); r.State = "completed"; var store = new GuildStore(NewRoot()); store.Save(r); s.Guild.CycleKey += "1"; Check(store.Prior(s) == null, "completed leaks cycle"); });
        Case("pending is isolated by account player server", () => { var s = Snap(); var r = Intent(s); var store = new GuildStore(NewRoot()); store.Save(r); s.AccountKey = DailyIdentity.MemberKey("987"); Check(store.Prior(s) == null, "account leaked"); s.AccountKey = r.AccountKey; s.PlayerKey = DailyIdentity.PlayerKey(r.AccountKey, 999); Check(store.Prior(s) == null, "player leaked"); s.PlayerKey = r.PlayerKey; s.Guild.ServerKey = DailyIdentity.Hash("other"); Check(store.Prior(s) == null, "server leaked"); });
        Case("command matches exact instance and bounded lease", () =>
        {
            var s = Snap();
            var now = DateTime.UtcNow.Ticks;
            var l = new DailyLease { Owner = "owner", AccountKey = s.AccountKey, ProcessId = s.ProcessId, ProcessStartTicks = s.ProcessStartTicks, ExpiresUtcTicks = now + TimeSpan.FromSeconds(10).Ticks };
            var c = new GuildCommand { AccountKey = s.AccountKey, PlayerKey = s.PlayerKey, ProcessId = s.ProcessId, ProcessStartTicks = s.ProcessStartTicks, InstanceId = s.InstanceId, ServerKey = s.Guild.ServerKey, GuildKey = s.Guild.GuildKey, CycleKey = s.Guild.CycleKey, Owner = l.Owner, ExpiresUtcTicks = now + TimeSpan.FromSeconds(8).Ticks };
            Check(GuildPolicy.Matches(c, s, l, now), "valid command rejected");
            c.InstanceId = "different";
            Check(!GuildPolicy.Matches(c, s, l, now), "wrong instance accepted");
            c.InstanceId = s.InstanceId;
            c.ExpiresUtcTicks = now;
            Check(!GuildPolicy.Matches(c, s, l, now), "expired accepted");
            c.ExpiresUtcTicks = now + TimeSpan.FromSeconds(8).Ticks;
            c.CycleKey += "1";
            Check(!GuildPolicy.Matches(c, s, l, now), "changed reset accepted");
            c.CycleKey = s.Guild.CycleKey;
            l.Owner = "other";
            Check(!GuildPolicy.Matches(c, s, l, now), "other controller accepted");
        });
        foreach (var field in new[] { "no-response", "callback", "error", "member", "cache", "duplicate" })
            Case("response proof rejects " + field, () => { var t = Flow(); switch (field) { case "no-response": t.Receipt.ResponseSeen = false; break; case "callback": t.Receipt.CallbackMatched = false; break; case "error": t.Receipt.ErrorCode = 1; break; case "member": t.Receipt.MemberMatched = false; break; case "cache": t.Receipt.CacheMatched = false; break; case "duplicate": t.Receipt.RequestCount = 2; break; } Check(GuildPolicy.Outcome(t.Receipt) == "unknown", field); });
        Case("false attendance is never called complete", () => { var t = Flow(); t.Receipt.AttendanceGranted = false; Check(GuildPolicy.Outcome(t.Receipt) == "checked_no_grant", "false positive"); });
        var scenarios = new Dictionary<string, Action<GuildTrace>>
        {
            ["identity_changed"] = t => t.Snapshot.AccountKey = "changed",
            ["wait_response"] = t => t.Receipt.ResponseSeen = false,
            ["timeout"] = t => { t.Receipt.ResponseSeen = false; t.Receipt.StartedUtcTicks -= TimeSpan.FromSeconds(30).Ticks; },
            ["unknown"] = t => t.Receipt.RequestCount = 2,
            ["release"] = t => t.HasControl = false,
            ["wait_ui"] = t => t.Snapshot.Guild.BlockReason = "network popup",
            ["close_attendance"] = t => t.Snapshot.Guild.AttendancePopup = true,
            ["return_menu"] = t => t.Snapshot.Guild.GuildVisible = true,
            ["finish"] = t => { t.BackClicked = true; t.ObservedGuild = true; },
            ["cleanup_timeout"] = t => t.ResponseAtTicks -= TimeSpan.FromSeconds(30).Ticks
        };
        var traceStore = new GuildStore(NewRoot());
        var traceId = Guid.NewGuid().ToString("N");
        foreach (var item in scenarios)
            Case("production decision " + item.Key, () => { var t = Flow(); item.Value(t); t.Decision = item.Key; Check(GuildPolicy.Next(t) == item.Key, item.Key); traceStore.Append(traceId, t); });
        Case("same production policy replays every synthetic decision", () => { var rows = GuildReplay.Run(traceStore.TracePath(traceId)); Check(rows.Count == scenarios.Count && rows.All(r => r.Match), "replay diverged"); });
        Case("popup click not repeated after animation delay", () => { var t = Flow(); t.Snapshot.Guild.AttendancePopup = true; t.PopupClicked = true; Check(GuildPolicy.Next(t) == "wait_ui", "clicked twice"); });
        Case("back click not repeated after animation delay", () => { var t = Flow(); t.Snapshot.Guild.GuildVisible = true; t.BackClicked = true; Check(GuildPolicy.Next(t) == "wait_ui", "back twice"); });
        Case("missing guild transition does not fabricate return", () => { var t = Flow(); t.BackClicked = true; Check(GuildPolicy.Next(t) != "finish", "false return"); });
        Case("corrupt journal is not absent", () => { var s = Snap(); var r = Intent(s); var store = new GuildStore(NewRoot()); store.Save(r); File.WriteAllText(store.ReceiptPath(r.Id), "{broken"); Throws(() => store.Prior(s)); Check(File.ReadAllText(store.ReceiptPath(r.Id)) == "{broken", "overwritten"); });
        Case("locked journal blocks without resetting", () => { var s = Snap(); var r = Intent(s); var store = new GuildStore(NewRoot()); store.Save(r); using var locked = new FileStream(store.ReceiptPath(r.Id), FileMode.Open, FileAccess.ReadWrite, FileShare.None); Throws(() => store.Prior(s)); });
        Case("unconsumed command cannot be replaced", () => { var file = Path.Combine(NewRoot(), "command.json"); var c = new GuildCommand { Id = "first" }; GuildStore.Write(file, c, false); Throws(() => GuildStore.Write(file, new GuildCommand { Id = "second" }, false)); Check(GuildStore.Read<GuildCommand>(file)!.Id == "first", "overwritten command"); });
        Case("journal rejects traversal and invalid DTO", () => { var store = new GuildStore(NewRoot()); Throws(() => store.Receipt("../../escape")); var r = Intent(Snap()); r.PlayerKey = ""; Throws(() => store.Save(r)); });
        async Task<(DemoEnvironment env, GuildSession session, string path)> Fixture()
        {
            var path = NewRoot();
            var env = new DemoEnvironment(path);
            var c = new DailyCoordinator(env, env, path, new()
            {
                PollInterval = TimeSpan.FromMilliseconds(2),
                LoginTimeout = TimeSpan.FromSeconds(1)
            });
            await c.ConnectCurrentAsync();
            return (env, new GuildSession(env, env, path, TimeSpan.FromMilliseconds(2)), path);
        }
        await Async("observer never sends command or invokes game", async () => { var f = await Fixture(); var before = f.env.Calls.Count; var pending = f.session.ObserveAsync(); await Task.Delay(30); f.session.Stop(); await pending; Check(!File.Exists(Path.Combine(f.path, "guild-command.json")), "observer command"); Check(f.env.Calls.Skip(before).All(c => c == "control"), "observer executed"); Check(File.Exists(f.session.LastTrace), "missing observation"); });
        foreach (var grant in new[] { true, false })
            await Async("one-shot and persistent duplicate guard grant=" + grant, async () => { var f = await Fixture(); f.env.HandleGuildCommands = true; f.env.GuildGrant = grant; await f.session.SingleAsync(); Check(f.env.Calls.Count(c => c == "guild.entry") == 1, "wrong entry count"); var next = new GuildSession(f.env, f.env, f.path, TimeSpan.FromMilliseconds(2)); await Reject(next.SingleAsync); Check(f.env.Calls.Count(c => c == "guild.entry") == 1, "repeated after reopening"); Check(GuildStore.Read<DailyLease>(Path.Combine(f.path, "lease.json"))!.Owner == "", "lease retained"); });
        await Async("stop during pending submission keeps intent and revokes", async () => { var f = await Fixture(); var pending = f.session.SingleAsync(); var until = DateTime.UtcNow.AddSeconds(1); while (!File.Exists(Path.Combine(f.path, "guild-command.json")) && DateTime.UtcNow < until) await Task.Delay(2); f.session.Stop(); await pending; Check(new GuildStore(f.path).Prior(f.env.ReadSnapshot()!)?.State == "prepared", "intent lost"); Check(GuildStore.Read<DailyLease>(Path.Combine(f.path, "lease.json"))!.Owner == "", "lease retained"); await Reject(new GuildSession(f.env, f.env, f.path, TimeSpan.FromMilliseconds(2)).SingleAsync); });
        await Async("wrong current account blocks before emission", async () => { var f = await Fixture(); f.env.ForcedKey = DailyIdentity.MemberKey("991"); await Reject(f.session.SingleAsync); Check(!File.Exists(Path.Combine(f.path, "guild-command.json")), "wrong account emitted"); });
        await Async("concurrent stop and task-exit cannot race lease writes", async () => { for (int attempt = 0; attempt < 30; attempt++) { var f = await Fixture(); var task = f.session.SingleAsync(); await Task.Delay(8); f.session.Stop(); await task; Check(GuildStore.Read<DailyLease>(Path.Combine(f.path, "lease.json"))!.Owner == "", "lease survived concurrent stop"); } });
        File.Copy(traceStore.TracePath(traceId), Path.Combine(root, "guild-synthetic-decisions.jsonl"), true);
    }
}
