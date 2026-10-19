using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class FriendshipStageCases
{
    sealed class Fixture : IDisposable
    {
        public readonly WorkflowHarness F;
        public readonly DailyFriendshipStage Stage;
        public readonly DailyPreferences Settings = new();
        public bool QuickAvailable;
        public int Free = 3, Selected = 1, Claims, Backs;
        public bool Pending, Gift, Freeze, MissingPending;
        public double GiftDelay = 4, Due = double.PositiveInfinity, ReadyAt;
        public readonly int[] Remaining = [1, 1, 1];
        public Fixture(string root, string scenario = "same")
        {
            F = new(root, [DailyFriendshipProof.Definition()], configureIdentity: (frame, context) =>
            {
                if (scenario == "wrong-process") { frame["ProcessStartTicks"] = 20; context["actor"]![1] = 20; }
                if (scenario == "wrong-cycle") context["cycle"] = "tomorrow";
                if (scenario == "reconnected") { frame["Instance"] = "new-bridge"; context["actor"]![2] = "new-bridge"; }
            });
            F.Readings = () => [WorkflowCases.Reading("friendship.native", ("$self", State())),
                WorkflowCases.Reading("reward.presentation", ("ὣὤὥὦὯὦὩὤὨὠὪ", F.Time >= ReadyAt), ("ὪὯὣὥὬὫὬὩὠὮὠ", true), ("ὮὬὧὦὣὠὠὤὮὦὧ", true), ("ὦὡὮὫὧὠὮὡὭὭὬ", false))];
            F.OnDelay = () =>
            {
                if (!Freeze && Pending && F.Time >= Due) { Pending = false; Gift = true; ReadyAt = F.Time + .3; Page(); }
            };
            F.OnCommand = c =>
            {
                switch (S(c["Kind"]))
                {
                    case "friendship_select":
                        if (Pending || Gift) throw new Exception("Selected another character before gift completion");
                        Selected = checked((int)I(c["Value"])); Page(); break;
                    case "friendship_complete":
                        if (Pending || Gift || Free <= 0) throw new Exception("Repeated counseling");
                        F.Event(DailyFriendshipProof.Role, "request");
                        Free--; Remaining[Selected - 1]--; Claims++;
                        Pending = GiftDelay >= 0; Due = F.Time + GiftDelay;
                        F.Event(DailyFriendshipProof.Role, "response", ("GainedExp", 100));
                        break;
                    case "back":
                        if (!Gift || F.Time < ReadyAt) throw new Exception("Premature reward close");
                        Gift = false; Backs++; Page(); break;
                    default: throw new Exception("Unexpected command " + c.ToJsonString());
                }
            };
            var nav = new DailyStageNavigation(() => Task.FromResult(new DailyStageFrame(F.Frame, F.Context)), () => F.Stopped, clock: () => F.Time,
                delay: t => { F.Time += t.TotalSeconds; return Task.CompletedTask; });
            Stage = new(root, F.Driver, F.Business, nav, () => F.Stopped, () => F.Time,
                t => { F.Time += t.TotalSeconds; return Task.CompletedTask; }, _ => Settings);
            Page();
        }
        JsonObject State()
        {
            var s = O(("Available", true), ("Free", Free), ("DailyLimit", 3), ("Selected", Selected), ("NoticePending", Pending),
                ("Candidates", Array(Enumerable.Range(1, 3).Select(id => O(("Id", id), ("Costume", 100 + id), ("Level", id + (Remaining[id - 1] == 0 ? 1 : 0)), ("Remaining", Remaining[id - 1]), ("Max", false), ("Quick", QuickAvailable))))));
            if (MissingPending) s.Remove("NoticePending");
            return s;
        }
        public void Page()
        {
            F.Page("FriendshipManageUI");
            if (Gift)
            {
                F.Frame["Surfaces"]!.AsArray().Add(O(("Id", 77), ("Type", "RewardReceivePopupUI"), ("Popup", true), ("Order", 9), ("InputReady", true), ("Targets", new JsonArray())));
                F.Frame["UiToken"] = "gift"; F.Publish();
            }
        }
        public Task<JsonObject> Run() => Stage.ExecuteAsync(F.Context, (operation, stage, args) =>
        {
            if (Pending || Gift) throw new Exception("Left consultation before its gift");
            F.Page("MenuUI");
            return Task.FromResult(O(("state", "completed")));
        });
        public void Dispose() => F.Dispose();
    }
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        async Task<string> Error(Func<Task<JsonObject>> run) { try { await run(); return ""; } catch (StageHostException e) { return e.Kind; } }
        int index = 0;
        Fixture New() => new(Path.Combine(root, "friendship-" + ++index));
        using (var f = New())
        {
            f.Settings.Friendship.Quick = true;
            f.QuickAvailable = true;
            await f.Run();
            Check(f.Claims == 3 && f.F.Box.Commands.Count(c => S(c["Kind"]) == "friendship_complete") == 3 &&
                f.F.Business.Records(f.F.Context, DailyFriendshipProof.Role).All(o => o["preview_action"] == null),
                "legacy quick preference and quick-capable characters still use three normal dialogues");
        }
        foreach (double latency in new[] { .1, 4, 12 })
        using (var f = New())
        {
            f.GiftDelay = latency;
            var result = await f.Run();
            Check(S(result["state"]) == "completed" && f.Claims == 3 && f.Backs == 3 && f.Free == 0, "all three counseling gifts finish before next selection at delay " + latency);
            Check(f.F.Business.Records(f.F.Context, DailyFriendshipProof.Role).All(o => B(o["presentation_closed"])), "presentation journal closes only after native pending gift drains " + latency);
        }
        using (var f = New())
        {
            f.GiftDelay = -1;
            await f.Run();
            Check(f.Claims == 3 && f.Backs == 0 && f.F.Time < 10, "no level-up gift completes without waiting for a nonexistent popup");
        }
        using (var f = New())
        {
            f.Freeze = true;
            Check(await Error(f.Run) == "adapter" && f.Claims == 1 && f.Pending, "native pending reward timeout never submits second counseling");
            f.Freeze = false;
            await f.Run();
            Check(f.Claims == 3 && f.Backs == 3, "pending gift can finish on rerun without repeating confirmed counseling");
        }
        foreach (string scenario in new[] { "same", "reconnected", "wrong-target", "wrong-process", "wrong-cycle" })
        using (var f = New())
        {
            f.Freeze = true;
            Check(await Error(f.Run) == "adapter" && f.Claims == 1, "interrupted gift preserves confirmed receipt " + scenario);
            f.Freeze = false;
            var op = f.F.Business.Records(f.F.Context, DailyFriendshipProof.Role).Single();
            // Reproduce the old-version record: parent visible, presentation marked closed,
            // then the level reward loads after the operation was saved.
            op["presentation_closed"] = true; op.Remove("presentation_frame"); f.F.Business.Save(op);
            f.Dispose();
            using var resumed = new Fixture(f.F.Root, scenario) { Free = f.Free, Claims = f.Claims, Selected = scenario == "wrong-target" ? 2 : f.Selected, Gift = true };
            f.Remaining.CopyTo(resumed.Remaining, 0);
            resumed.Page();
            Check(resumed.Stage.CanResume(new(resumed.F.Frame, resumed.F.Context)) == (scenario is not ("wrong-process" or "wrong-cycle")), "late old-version gift scopes recovery " + scenario);
            if (scenario.StartsWith("wrong-"))
                Check(await Error(resumed.Run) == "adapter" && resumed.Claims == 1 && resumed.Backs == 0, "foreign live state cannot inherit old gift ownership " + scenario);
            else
            {
                await resumed.Run();
                Check(resumed.Claims == 3 && resumed.Backs == 3, "old prematurely closed gift resumes from current target and quota " + scenario);
            }
        }
        using (var f = New())
        {
            f.Gift = true; f.Page();
            Check(!f.Stage.CanResume(new(f.F.Frame, f.F.Context)), "unrelated reward has no counseling owner");
            Check(await Error(f.Run) == "adapter" && f.Backs == 0, "unrelated reward is preserved without blind dismissal");
        }
        using (var f = New())
        {
            f.GiftDelay = -1; f.MissingPending = true;
            Check(await Error(f.Run) == "adapter" && f.Claims == 1, "missing new native field cannot silently declare gift complete");
        }
    }
}
