using Dustweave;
using BD2.LocalIpc;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
static class CommandDriverCases
{
    internal sealed class Mailbox : IDailyCommandMailbox
    {
        public Dictionary<string, byte[]> Values = new(); public List<JsonObject> Commands = []; public HashSet<string> Opened = [];
        public Action<JsonObject>? AfterCommand; public Action<string, string, byte[]>? AfterWrite;
        public string State = "observed_after_dispatch", Error = "screen_changed"; public bool? Dispatched = true; public bool Revoked, ThrowCreate, Tamper;
        public void Acquire(string channel)
        {
            if (Revoked)
                throw new LeaseRevokedException();
            Opened.Add(channel);
        }
        public byte[]? Read(string channel, string name)
        {
            if (Revoked)
                throw new LeaseRevokedException();
            return Values.GetValueOrDefault(channel + ":" + name);
        }
        public void Write(string channel, string name, byte[] value, bool create = false)
        {
            if (Revoked)
                throw new LeaseRevokedException();
            string key = channel + ":" + name;
            if (create && Values.ContainsKey(key))
                throw new IOException("Already pending");
            if (name == "command.json")
            {
                var command = JsonNode.Parse(value)!.AsObject();
                Commands.Add(command);
                if (ThrowCreate)
                    throw new IOException("Reply lost after request bytes");
                var attached = command.DeepClone().AsObject();
                if (Tamper)
                    attached["AccountKey"] = "other";
                var receipt = new JsonObject { ["Command"] = attached, ["State"] = State, ["Error"] = Error };
                if (Dispatched != null)
                    receipt["MayHaveDispatched"] = Dispatched.Value;
                Values["live:receipts~" + command["Id"]!.GetValue<string>() + ".json"] = JsonSerializer.SerializeToUtf8Bytes(receipt);
                if (Dispatched == true)
                    AfterCommand?.Invoke(command);
                return;
            }
            Values[key] = value;
            AfterWrite?.Invoke(channel, name, value);
        }
        public void Delete(string channel, string name)
        {
            if (Revoked)
                throw new LeaseRevokedException();
            Values.Remove(channel + ":" + name);
        }
        public string[] List(string channel, string prefix) => Values.Keys.Where(k => k.StartsWith(channel + ":" + prefix)).Select(k => k[(channel.Length + 1)..]).ToArray();
    }
    internal static JsonObject Context() => new() { ["actor"] = new JsonArray(7, 10, "fixture", new string('a', 64), new string('b', 64)), ["server"] = "test", ["cycle"] = "today" };
    internal static JsonObject Frame() => new()
    {
        ["ProcessId"] = 7,
        ["ProcessStartTicks"] = 10,
        ["Instance"] = "fixture",
        ["AccountKey"] = new string('a', 64),
        ["PlayerKey"] = new string('b', 64),
        ["Scene"] = "Map",
        ["UiToken"] = "fixture",
        ["AtUtcTicks"] = 100000000,
        ["Surfaces"] = new JsonArray(new JsonObject { ["Id"] = 1, ["Type"] = "MenuUI", ["Popup"] = false, ["Order"] = 0, ["Targets"] = new JsonArray(new JsonObject { ["Id"] = 2, ["Field"] = "_buttonMail", ["Enabled"] = true, ["Route"] = "ui" }) })
    };
    internal static JsonObject Action() => new() { ["ui"] = "MenuUI", ["field"] = "_buttonMail", ["expect"] = "MailUI", ["reason"] = "test", ["timeout"] = 1 };
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        int n = 0;
        async Task Exercise(Action<Mailbox, JsonObject>? configure, string expected, string label, Action<Mailbox, JsonObject?, string>? inspect = null, Func<int, DailyStageFrame>? observe = null, bool stopped = false)
        {
            string path = Path.Combine(output, "driver-" + (++n));
            var box = new Mailbox();
            var action = Action();
            configure?.Invoke(box, action);
            double time = 0;
            int reads = 0;
            using var driver = new DailyCommandDriver(path, box, () => Task.FromResult(observe?.Invoke(++reads) ?? new DailyStageFrame(Frame(), Context())), () => stopped, () => 100000000, () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; });
            driver.Bind(Context());
            string kind = "";
            JsonObject? result = null;
            try
            {
                result = await driver.SubmitAsync(action);
            }
            catch (DailyStepException e) { kind = e.Kind; }
            catch (StageHostException e) { kind = e.Kind; }
            Check(kind == expected, label);
            inspect?.Invoke(box, result, path);
        }
        await Exercise(null, "", "managed command receives the matching native receipt", (b, r, p) =>
        {
            Check(b.Commands.Count == 1 && b.Opened.SetEquals(["live"]), "managed step owns one lease and submits exactly once");
            Check(File.Exists(Path.Combine(p, "live", "steps", r!["id"]!.GetValue<string>(), "transport.json")), "managed driver keeps durable submit and receipt evidence");
            var c = b.Commands.Single();
            Check(c["ObservedUtcTicks"]!.GetValue<long>() == 100000000 && c["ExpiresUtcTicks"]!.GetValue<long>() == 200000000 && c["Kind"]!.GetValue<string>() == "click", "managed command retains observed time, expiry and native kind");
        });
        await Exercise((b, a) => { b.State = "rejected"; b.Dispatched = false; }, "rejected", "only explicit non-dispatch native rejection permits replan", (b, r, p) => Check(b.Commands.Count == 1, "native rejection never resubmits inside driver"));
        await Exercise((b, a) => { b.State = "rejected"; b.Dispatched = true; }, "pending", "rejected receipt with dispatch evidence remains pending");
        await Exercise((b, a) => { b.State = "rejected"; b.Dispatched = null; }, "pending", "missing dispatch flag never proves an undispatched rejection");
        await Exercise((b, a) => { b.State = "unknown"; }, "pending", "unknown receipt is preserved without replay");
        await Exercise((b, a) => { b.State = "dispatching"; }, "pending", "receipt deadline leaves dispatched command pending", (b, r, p) => Check(b.Commands.Count == 1, "timeout does not resubmit a command"));
        await Exercise((b, a) => { b.State = "observed_after_dispatch"; b.Dispatched = false; }, "pending", "observed receipt must contain dispatch evidence");
        await Exercise((b, a) => b.Tamper = true, "pending", "receipt for another actor is rejected without replay");
        await Exercise((b, a) => b.ThrowCreate = true, "pending", "lost create response is uncertain and cannot be retried", (b, r, p) => Check(b.Commands.Count == 1, "ambiguous mailbox create has one original intent"));
        await Exercise((b, a) => b.Values["live:command.json"] = Encoding.UTF8.GetBytes("{}"), "rejected", "existing pending mailbox command is never overwritten", (b, r, p) => Check(b.Commands.Count == 0, "pending command guard submits nothing"));
        await Exercise(null, "rejected", "operator stop prevents ownership and dispatch", (b, r, p) => Check(b.Opened.Count == 0 && b.Commands.Count == 0, "stopped queue takes no writer lease"), stopped: true);
        await Exercise((b, a) => b.Values["live:pause"] = [], "rejected", "paused mailbox is not silently cleared by a step");
        await Exercise((b, a) => a["startup"] = true, "rejected", "identified queue driver cannot initiate startup control");
        await Exercise((b, a) => a["allow_guild"] = false, "", "ordinary command does not require guild authorization");
        await Exercise((b, a) => { a["field"] = "_buttonGuild"; }, "rejected", "guild entry cannot use an ordinary step");
        await Exercise((b, a) => a["unknown"] = 1, "protocol", "managed step refuses unknown action fields");
        await Exercise(null, "rejected", "changing surface between intent and dispatch submits nothing", (b, r, p) => Check(b.Commands.Count == 0, "stale surface never reaches mailbox"), i => { var f = Frame(); if (i > 1) f["Surfaces"]![0]!["Id"] = 99; return new(f, Context()); });
        await Exercise(null, "rejected", "changing scene before dispatch submits nothing", null, i => { var f = Frame(); if (i > 1) f["Scene"] = "Other"; return new(f, Context()); });
        await Exercise(null, "identity", "reset change rejects the original queue context", null, i => { var c = Context(); if (i > 1) c["cycle"] = "tomorrow"; return new(Frame(), c); });
        await Exercise(null, "rejected", "foreground unknown popup blocks step", null, i => { var f = Frame(); f["Surfaces"]!.AsArray().Add(new JsonObject { ["Id"] = 9, ["Type"] = "UnknownPopupUI", ["Popup"] = true, ["Order"] = 100 }); return new(f, Context()); });
        await Exercise(null, "rejected", "disabled observed target receives no input", null, i => { var f = Frame(); f["Surfaces"]![0]!["Targets"]![0]!["Enabled"] = false; return new(f, Context()); });
        foreach (var kind in new[] { "pointer", "native_click", "back", "mirror_ready", "dispatch_menu", "pass_init", "story_advance" })
        {
            var a = Action();
            string route = "ui";
            if (kind == "pointer")
                route = "pointer";
            else if (kind == "story_advance")
                a["operation"] = kind;
            else
                a[kind switch
                {
                    "native_click" => "native",
                    "mirror_ready" => "mirror_entry",
                    "dispatch_menu" => "dispatch_entry",
                    _ => kind
                }] = true;
            var f = Frame();
            var command = DailyCommandDriver.BuildCommand(f, f["Surfaces"]![0]!.AsObject(), 2, a, 100000000, "test", "reason", route);
            Check(command["Kind"]!.GetValue<string>() == kind, "managed command retains kind " + kind);
        }
        string ioPath = Path.Combine(output, "driver-mailbox");
        var ioBox = new Mailbox();
        using (var driver = new DailyCommandDriver(ioPath, ioBox, () => Task.FromResult(new DailyStageFrame(Frame(), Context())), () => false))
        {
            driver.Bind(Context());
            await driver.HandleAsync(new()
            {
                ["operation"] = "open",
                ["channel"] = "live"
            });
            async Task Reject(JsonObject request, string label)
            {
                string kind = "";
                try
                {
                    await driver.HandleAsync(request);
                }
                catch (StageHostException e) { kind = e.Kind; }
                Check(kind == "protocol", label);
            }
            await Reject(new()
            {
                ["operation"] = "read",
                ["channel"] = "live",
                ["name"] = "../secret"
            }, "managed mailbox refuses path traversal");
            await Reject(new()
            {
                ["operation"] = "read",
                ["channel"] = "live",
                ["name"] = "accounts.json"
            }, "managed mailbox exposes only registered live entries");
            await Reject(new()
            {
                ["operation"] = "read",
                ["channel"] = "other",
                ["name"] = "snapshot.json"
            }, "managed mailbox rejects foreign channels");
            await Reject(new()
            {
                ["operation"] = "write",
                ["channel"] = "live",
                ["name"] = "command.json",
                ["value"] = "e30="
            }, "managed SDK cannot bypass submit through raw command write");
            await Reject(new()
            {
                ["operation"] = "delete",
                ["channel"] = "live",
                ["name"] = "command.json"
            }, "managed SDK cannot remove an uncertain native command");
            await driver.HandleAsync(new()
            {
                ["operation"] = "write",
                ["channel"] = "live",
                ["name"] = "observation-request.json",
                ["value"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("日常😀"))
            });
            var result = await driver.HandleAsync(new()
            {
                ["operation"] = "read",
                ["channel"] = "live",
                ["name"] = "observation-request.json"
            });
            Check(Encoding.UTF8.GetString(Convert.FromBase64String(result["value"]!.GetValue<string>())) == "日常😀", "managed mailbox preserves Unicode bytes");
            ioBox.Revoked = true;
            string fault = "";
            try
            {
                await driver.HandleAsync(new()
                {
                    ["operation"] = "open",
                    ["channel"] = "live"
                });
            }
            catch (LeaseRevokedException) { fault = "revoked"; }
            Check(fault == "revoked", "revoked driver never retakes control");
        }
        string lockPath = Path.Combine(output, "driver-lock");
        using (var control = new DailyControlOwner(lockPath))
        {
            control.Acquire();
            var repo = Environment.CurrentDirectory;
            async Task<bool> ManagedLock()
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("--control-lock-child");
                start.ArgumentList.Add(lockPath);
                using var p = Process.Start(start)!;
                var read = p.StandardOutput.ReadToEndAsync();
                var error = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0)
                    throw new Exception(await error);
                return (await read).Trim() == "owned";
            }
            Check(!await ManagedLock(), "one byte-range controller lock excludes another process");
            control.Dispose();
            Check(await ManagedLock(), "shutdown releases the cross-process controller lock");
        }
    }
}

