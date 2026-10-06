using Dustweave;
using System.Text.Json.Nodes;
static class ManagedInputsCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        async Task Reject(Func<Task> action, string kind, string label)
        {
            string found = "";
            try
            {
                await action();
            }
            catch (StageHostException e) { found = e.Kind; }
            Check(found == kind, label + " (" + found + ")");
        }
        JsonObject Page(string name, string field = "_objBackButton")
        {
            var f = CommandDriverCases.Frame();
            f["BridgeVersion"] = 104;
            f["Surfaces"]![0]!["Type"] = name;
            f["Surfaces"]![0]!["InputReady"] = true;
            f["Surfaces"]![0]!["Targets"]![0]!["Field"] = field;
            return f;
        }
        var policy = DailyNavigationPolicy.Load();
        JsonObject context = CommandDriverCases.Context(), current = Page("MailUI");
        double time = 0;
        string path = Path.Combine(output, "managed-inputs");
        var box = new CommandDriverCases.Mailbox();
        Func<Task<DailyStageFrame>> read = () => Task.FromResult(new DailyStageFrame(current.DeepClone().AsObject(), context.DeepClone().AsObject()));
        using (var driver = new DailyCommandDriver(path, box, read, () => false, () => 100000000, () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; }))
        {
            driver.Bind(context);
            driver.Acquire("live");
            box.AfterCommand = _ => current = Page("MenuUI", "_buttonMail");
            var close = DailyNavigationDecision.CloseAction(current, policy)!;
            var result = await driver.NavigationAsync(close);
            Check(result["state"]!.GetValue<string>() == "observed_expected_ui" && box.Commands.Count == 1, "managed navigation closes a known page and observes its absence");
            await Reject(() => driver.NavigationAsync(close), "navigation_changed", "stale close action is refused without another input");
            Check(box.Commands.Count == 1, "changed navigation sends zero replacement commands");
            current = Page("MailUI");
            box.State = "rejected";
            box.Dispatched = false;
            await Reject(() => driver.NavigationAsync(DailyNavigationDecision.CloseAction(current, policy)!), "navigation_changed", "managed non-dispatch navigation rejection permits bounded outer replan");
            box.State = "unknown";
            box.Dispatched = true;
            await Reject(() => driver.NavigationAsync(DailyNavigationDecision.CloseAction(current, policy)!), "pending", "managed uncertain navigation cannot be replanned");
            box.State = "observed_after_dispatch";
            box.Dispatched = true;
            await Reject(() => driver.NavigationAsync(new() { ["ui"] = "MenuUI", ["field"] = "_buttonMail", ["allow_guild"] = true }), "protocol", "navigation SDK rejects extra spending authorization");
            int before = box.Commands.Count;
            current = Page("MailUI");
            box.AfterCommand = null;
            await Reject(() => driver.NavigationAsync(DailyNavigationDecision.CloseAction(current, policy)!), "pending", "missing expected UI retains the original dispatched receipt");
            Check(box.Commands.Count == before + 1, "expected-UI timeout has no automatic repeat");
        }
        time = 0;
        current = Page("MessagePopupUI", "_buttonOK");
        current["Surfaces"]![0]!["NativeContext"] = "talent_inactive_ack_only";
        current["Surfaces"]![0]!["Popup"] = true;
        box = new();
        string talentPath = Path.Combine(output, "managed-talent-inputs");
        using (var driver = new DailyCommandDriver(talentPath, box, read, () => false, () => 100000000, () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; }))
        {
            driver.Bind(context);
            driver.Acquire("live");
            box.AfterCommand = _ => current = Page("MenuUI", "_buttonMail");
            for (int i = 0; i < 4; i++)
            {
                current = Page("MessagePopupUI", "_buttonOK");
                current["Surfaces"]![0]!["NativeContext"] = "talent_inactive_ack_only";
                current["Surfaces"]![0]!["Popup"] = true;
                await driver.NavigationAsync(DailyNavigationDecision.TalentAction(current, policy)!);
            }
            Check(box.Commands.Count == 4 && Directory.GetFiles(Path.Combine(talentPath, "live", "popup-recovery"), "*.json").All(p => DailyJson.TryRead<JsonObject>(p)!["business_replayed"]!.GetValue<bool>() == false), "managed talent acknowledgements retain no-business-replay diagnostics");
            current = Page("MessagePopupUI", "_buttonOK");
            current["Surfaces"]![0]!["NativeContext"] = "talent_inactive_ack_only";
            current["Surfaces"]![0]!["Popup"] = true;
            await Reject(() => driver.NavigationAsync(DailyNavigationDecision.TalentAction(current, policy)!), "adapter", "managed talent recovery retains original four-per-minute bound");
            Check(box.Commands.Count == 4, "repeated talent popup does not send a fifth acknowledgement");
        }
        foreach (string scenario in new[] { "resume-in-delay", "resume-in-read", "still-missing", "changed-account", "stopped", "unknown-receipt" })
        {
            double elapsed = 0;
            bool submitted = false, pause = false;
            var frame = Page("MenuUI", "_buttonMail");
            var actor = CommandDriverCases.Context();
            var delayedBox = new CommandDriverCases.Mailbox { Error = "" };
            if (scenario == "unknown-receipt") delayedBox.State = "unknown";
            delayedBox.AfterCommand = _ => submitted = true;
            using var delayed = new DailyCommandDriver(Path.Combine(output, "observed-resume-" + scenario), delayedBox,
                () =>
                {
                    if (submitted && scenario == "resume-in-read") { elapsed = 120; frame = Page("MailUI"); }
                    frame["AtUtcTicks"] = 100000000 + (long)(elapsed * TimeSpan.TicksPerSecond);
                    return Task.FromResult(new DailyStageFrame(frame.DeepClone().AsObject(), actor.DeepClone().AsObject()));
                }, () => pause, () => 100000000 + (long)(elapsed * TimeSpan.TicksPerSecond), () => elapsed, t =>
                {
                    elapsed += submitted ? 120 : t.TotalSeconds;
                    if (submitted && scenario == "resume-in-delay") frame = Page("MailUI");
                    if (scenario == "changed-account") actor["actor"]![3] = new string('d', 64);
                    if (scenario == "stopped") pause = true;
                    return Task.CompletedTask;
                });
            delayed.Bind(CommandDriverCases.Context());
            JsonObject? result = null;
            string failure = "";
            try { result = await delayed.SendObservedAsync(CommandDriverCases.Action()); }
            catch (DailyStepException e) { failure = e.Kind; }
            Check(scenario.StartsWith("resume-", StringComparison.Ordinal)
                ? result?["state"]?.GetValue<string>() == "observed_expected_ui"
                : failure == "pending", "UI input reobserves after delay without masking " + scenario);
            Check(delayedBox.Commands.Count == 1, "UI input " + scenario + " never replays dispatched command");
        }
        current = Page("MenuUI", "_buttonGuild");
        time = 0;
        box = new();
        string guildPath = Path.Combine(output, "managed-guild-inputs");
        string guildKey = new('c', 64);
        DailySnapshot daily = new()
        {
            Runtime = DailyIdentity.RuntimeName,
            State = "identified",
            ProcessId = 7,
            ProcessStartTicks = 10,
            AccountKey = new('a', 64),
            PlayerKey = new('b', 64),
            FrameUtcTicks = 100000000,
            Guild = new()
            {
                GuildKey = guildKey,
                ServerKey = "test",
                CycleKey = "today",
                Supported = true,
                InGuild = true,
                MenuVisible = true,
                ServerTicks = 100000000,
                ResetTicks = 100000000 + 100 * TimeSpan.TicksPerSecond
            }
        };
        Func<Task<DailyStageFrame>> guildRead = () => Task.FromResult(new DailyStageFrame(current.DeepClone().AsObject(), context.DeepClone().AsObject(), daily));
        using (var driver = new DailyCommandDriver(guildPath, box, guildRead, () => false, () => 100000000, () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; }))
        {
            driver.Bind(context);
            driver.Acquire("live");
            box.AfterCommand = _ => current = Page("GuildUI");
            var entry = DailyGuildStage.LoadRecipe()["rules"]!.AsArray().Single(r => r!["id"]!.GetValue<string>() == "enter-guild-once")!["action"]!.DeepClone().AsObject();
            var done = await driver.GuildAsync(entry, guildKey);
            Check(box.Commands.Count == 1 && box.Commands[0]["Reason"]!.GetValue<string>().StartsWith("guild-once|today|"), "managed guild input preserves once-per-cycle native reason");
            current = Page("MenuUI", "_buttonGuild");
            await Reject(() => driver.GuildAsync(entry, guildKey), "guild_rejected", "existing dispatched guild receipt prevents a second entry");
            Check(box.Commands.Count == 1, "guild duplicate guard reaches no native input");
            await Reject(() => driver.GuildAsync(entry, new string('d', 64)), "identity", "guild identity change prevents managed input");
        }
    }
}
