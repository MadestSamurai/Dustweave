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
        public Action<string,string>? BeforeRead;
        public Action<string,string>? BeforeWrite;
        public JsonObject? Before, After;
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
            BeforeRead?.Invoke(channel,name);
            return Values.GetValueOrDefault(channel + ":" + name);
        }
        public void Write(string channel, string name, byte[] value, bool create = false)
        {
            if (Revoked)
                throw new LeaseRevokedException();
            BeforeWrite?.Invoke(channel,name);
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
                if (Before != null) receipt["Before"] = Before.DeepClone();
                if (After != null) receipt["After"] = After.DeepClone();
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
        async Task Exercise(Action<Mailbox, JsonObject>? configure, string expected, string label, Action<Mailbox, JsonObject?, string>? inspect = null, Func<int, DailyStageFrame>? observe = null, bool stopped = false, Func<bool>? stop = null)
        {
            string path = Path.Combine(output, "driver-" + (++n));
            var box = new Mailbox();
            var action = Action();
            configure?.Invoke(box, action);
            double time = 0;
            int reads = 0;
            using var driver = new DailyCommandDriver(path, box, () => Task.FromResult(observe?.Invoke(++reads) ?? new DailyStageFrame(Frame(), Context())), stop ?? (() => stopped), () => 100000000, () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; });
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
            string folder = Path.Combine(p, "live", "steps", r["id"]!.GetValue<string>());
            Check(!File.Exists(Path.Combine(folder,"before.json")) && JsonNode.DeepEquals(DailyJson.TryRead<JsonObject>(Path.Combine(folder,"transport.json"))!["before"],Frame()), "durable transport retains the exact preflight frame once instead of a second file");
            var c = b.Commands.Single();
            Check(c["ObservedUtcTicks"]!.GetValue<long>() == 100000000 && c["ExpiresUtcTicks"]!.GetValue<long>() == 200000000 && c["Kind"]!.GetValue<string>() == "click", "managed command retains observed time, expiry and native kind");
        });
        await Exercise((b,a) => b.AfterCommand = c => Directory.CreateDirectory(Path.Combine(output,"driver-"+n,"live","steps",c["Id"]!.ToString(),"transport.json")), "pending", "failed transport save retains the original pre-dispatch evidence", (b,r,p) =>
            Check(File.Exists(Path.Combine(p,"live","steps",b.Commands.Single()["Id"]!.ToString(),"before.json")), "preflight is not retired before the successor is durable"));
        FileStream? heldBefore = null;
        try
        {
            await Exercise((b,a) => b.AfterCommand = c => heldBefore = new FileStream(Path.Combine(output,"driver-"+n,"live","steps",c["Id"]!.ToString(),"before.json"),FileMode.Open,FileAccess.Read,FileShare.None), "", "busy duplicate evidence never turns a successful command into failure", (b,r,p) =>
                Check(File.Exists(Path.Combine(p,"live","steps",r!["id"]!.ToString(),"before.json")), "locked duplicate remains for later maintenance"));
        }
        finally { heldBefore?.Dispose(); }
        {
            string path=Path.Combine(output,"driver-canonical-result");double time=0;
            var before=Frame();var after=Frame();after["Surfaces"]![0]!["Type"]="MailUI";after["AtUtcTicks"]=100000001;
            var observed=before;var box=new Mailbox { Before=before,After=after };
            box.AfterCommand=_=>observed=after;
            using var driver=new DailyCommandDriver(path,box,()=>Task.FromResult(new DailyStageFrame(observed,Context())),()=>false,()=>100000000,()=>time,t=>{time+=t.TotalSeconds;return Task.CompletedTask;});
            driver.Bind(Context());var response=await driver.SendObservedAsync(Action());
            string folder=Path.Combine(path,"live","steps",response["id"]!.ToString());
            var saved=DailyJson.TryRead<JsonObject>(Path.Combine(folder,"result.json"))!;
            Check(Directory.GetFiles(folder).Select(Path.GetFileName).ToHashSet().SetEquals(["intent.json","result.json"]), "observed steps keep intent plus one complete final record");
            Check(JsonNode.DeepEquals(saved["before"],before)&&JsonNode.DeepEquals(saved["after"],after)&&JsonNode.DeepEquals(saved["receipt"],response["receipt"])&&JsonNode.DeepEquals(saved["command"],box.Commands.Single()), "canonical result preserves exact preflight, native receipt, command and final UI");
            Check(response["before"]==null&&saved["elapsed"]!=null, "storage consolidation does not change the caller result contract");
        }
        await Exercise((b, a) => { b.State = "rejected"; b.Dispatched = false; }, "rejected", "only explicit non-dispatch native rejection permits replan", (b, r, p) => Check(b.Commands.Count == 1, "native rejection never resubmits inside driver"));
        await Exercise((b, a) => { b.State = "rejected"; b.Dispatched = true; }, "pending", "rejected receipt with dispatch evidence remains pending");
        await Exercise((b, a) => { b.State = "rejected"; b.Dispatched = null; }, "pending", "missing dispatch flag never proves an undispatched rejection");
        await Exercise((b, a) => { b.State = "unknown"; }, "pending", "unknown receipt is preserved without replay");
        await Exercise((b, a) => { b.State = "dispatching"; }, "pending", "receipt deadline leaves dispatched command pending", (b, r, p) => Check(b.Commands.Count == 1, "timeout does not resubmit a command"));
        await Exercise((b, a) => { b.State = "observed_after_dispatch"; b.Dispatched = false; }, "pending", "observed receipt must contain dispatch evidence");
        await Exercise((b, a) => b.Tamper = true, "pending", "receipt for another actor is rejected without replay");
        await Exercise((b, a) => b.ThrowCreate = true, "pending", "lost create response is uncertain and cannot be retried", (b, r, p) => Check(b.Commands.Count == 1, "ambiguous mailbox create has one original intent"));
        {
            int attempts = 0;
            await Exercise((b,a) => b.BeforeWrite = (_,name) =>
            {
                if (name == "command.json" && ++attempts < 3)
                    throw new PipeConnectTimeoutException("create",name,new TimeoutException());
            }, "", "two proven unsent connection failures recover on the third attempt", (b,r,p) =>
            {
                Check(attempts == 3 && b.Commands.Count == 1 && b.Opened.Count == 1, "connection recovery sends one command without reacquiring the lease");
                Check(Directory.GetFiles(Path.Combine(p,"live","steps"),"result.json",SearchOption.AllDirectories).Count(f => DailyJson.TryRead<JsonObject>(f)?["state"]?.ToString() == "not_submitted") == 2, "unsent connection attempts retain explicit diagnostic evidence");
            });
            attempts = 0;
            await Exercise((b,a) => b.BeforeWrite = (_,name) =>
            {
                if (name == "command.json") { attempts++; throw new PipeConnectTimeoutException("create",name,new TimeoutException()); }
            }, "rejected", "persistent unsent connection failures stop after three attempts", (b,r,p) => Check(attempts == 3 && b.Commands.Count == 0, "unsent retry limit never fabricates a pending consuming operation"));
        }
        {
            bool missed = false;
            await Exercise((b,a) => b.BeforeWrite = (_,name) =>
            {
                if (name == "command.json" && !missed) { missed = true; throw new PipeConnectTimeoutException("create",name,new TimeoutException()); }
            }, "", "unsent retry refreshes the surface and target before sending", (b,r,p) =>
                Check(b.Commands.Single()["TargetId"]!.GetValue<int>() == 44 && b.Commands.Single()["SurfaceId"]!.GetValue<int>() == 33, "unsent retry does not replay stale UI identifiers"),
                observe: _ => { var f=Frame(); if(missed){ f["Surfaces"]![0]!["Id"]=33;f["Surfaces"]![0]!["Targets"]![0]!["Id"]=44; }return new(f,Context()); });
            missed = false;
            await Exercise((b,a) => b.BeforeWrite = (_,name) =>
            {
                if (name == "command.json" && !missed) { missed = true; throw new PipeConnectTimeoutException("create",name,new TimeoutException()); }
            }, "identity", "identity change during unsent recovery prevents the retry", (b,r,p) => Check(b.Commands.Count == 0,"changed account receives no recovered input"),
                observe: _ => { var c=Context(); if(missed)c["cycle"]="tomorrow";return new(Frame(),c); });
        }
        foreach (string entry in new[] { "pause", "command.json", "receipts~" })
        {
            int failures = 0;
            await Exercise((b,a) => b.BeforeRead = (_,name) =>
            {
                if (name.StartsWith(entry,StringComparison.Ordinal) && ++failures <= 2) throw new TimeoutException("temporary read failure");
            }, "", "transient mailbox timeout recovers: " + entry, (b,r,p) => Check(b.Commands.Count == 1 && failures >= 3,"read recovery submits exactly once: " + entry));
        }
        await Exercise((b,a) => b.AfterCommand = _ => throw new TimeoutException("accepted command reply lost"), "", "ambiguous write timeout resolves from the original receipt", (b,r,p) => Check(b.Commands.Count == 1,"accepted write timeout never resends the command"));
        {
            int failures = 0;
            await Exercise((b,a) => b.BeforeRead = (_,name) =>
            {
                if (name.StartsWith("receipts~",StringComparison.Ordinal)) { failures++;throw new TimeoutException("persistent receipt timeout"); }
            }, "pending", "persistent receipt timeout remains bounded and recoverable", (b,r,p) =>
            {
                Check(b.Commands.Count == 1 && failures <= 6, "receipt polling obeys the original deadline without repeating input");
                Check(DailyJson.TryRead<JsonObject>(Path.Combine(p,"live","steps",b.Commands.Single()["Id"]!.ToString(),"result.json"))?["state"]?.ToString() == "unknown_timeout", "receipt timeout persists the original command journal");
            });
        }
        await Exercise((b,a) => b.AfterCommand = c => { b.Values.Remove("live:receipts~"+c["Id"]+".json");throw new TimeoutException("ambiguous write without receipt"); }, "pending", "ambiguous write without receipt waits then preserves pending outcome", (b,r,p) => Check(b.Commands.Count == 1,"missing write reply never permits automatic replay"));
        {
            int failures=0;
            await Exercise((b,a) => b.BeforeRead=(_,name)=>
            {
                if(name.StartsWith("receipts~",StringComparison.Ordinal)){failures++;b.Revoked=true;throw new TimeoutException();}
            }, "pending", "revoked lease aborts receipt recovery without reacquisition", (b,r,p)=>Check(failures==1&&b.Commands.Count==1&&b.Opened.Count==1,"transport retry cannot reclaim a revoked writer"));
        }
        {
            bool stopNow = false;
            await Exercise((b,a) => b.BeforeRead = (_,name) =>
            {
                if (name.StartsWith("receipts~",StringComparison.Ordinal)) { stopNow = true;throw new TimeoutException(); }
            }, "pending", "operator stop interrupts receipt recovery and retains the pending command", (b,r,p) => Check(b.Commands.Count == 1,"stop during timeout recovery performs no further input"), stop: () => stopNow);
        }
        {
            using var process = Process.GetCurrentProcess();
            var client = new PipeClient(Path.Combine(output,"not-running-ipc"),process.Id,process.StartTime.ToUniversalTime().Ticks);
            PipeConnectTimeoutException? failure = null;
            try { client.Write("command.json",[],true); } catch(PipeConnectTimeoutException e) { failure=e; }
            Check(failure?.Operation == "create" && failure.Entry == "command.json" && failure.InnerException is TimeoutException,"real pipe connection timeout proves no command bytes were sent");
        }
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

