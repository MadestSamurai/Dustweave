using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed record DailyPracticeTalent(int Kind, DailyDisplayName Names);

// Manual training only. Normal game cartridge entry resets the effect; never
// call the game's force-stop/reset functions or change experience directly.
public static class DailyTalentPracticeProof
{
    public const string ReadId = "mainline.talent_training", Role = "talent.practice";
    public static bool FieldKind(int kind) => kind is 2 or 6 or 12 or 13 or 17;
    public static DailyPracticeTalent[] Talents(JsonObject e) => Rows(State(e)["Talents"] as JsonArray ?? new JsonArray())
        .Select(r => new DailyPracticeTalent(I(r["Kind"]), new(S(r["Names"]?["Cn"]), S(r["Names"]?["Tw"]), S(r["Names"]?["En"])))).ToArray();
    public static JsonObject State(JsonObject e) => DailyData.State(e, ReadId, "$self");
    public static DailyBargainCharacter[] Characters(JsonObject e) => Rows(State(e)["Characters"]).Select(DailyBargainCharacter.Parse).ToArray();
    public static DailyBargainCharacter Character(JsonObject e, long id) => Characters(e).Single(c => c.Character == id);
    public static DailyBusinessProof Definition => new(Role, "talent_practice", [ReadId], Verify, ["dispatch.start"]);
    public static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after)
    {
        var before = op["before"]!.AsObject();
        var a = Character(before, N(op["scope"]?["character"]));
        var b = Character(after, a.Character); a.AssertSameTier(b);
        bool achievement=DailyPracticeAchievement.Authorized(op,before,ReadId,a.Kind);
        Require(FieldKind(a.Kind) && (achievement||!a.Full), "practice.skill_changed");
        var response = Response("dispatch.start", DailyFieldTalentProof.MatchingSkillEvents(op, events, a.Group), before, after)!;
        Require(B(response["IsSuccess"]), "practice.cast_failed");
        if (response["TalentSkillInfo"] != null) Require(N(response["TalentSkillInfo"]?["groupId"]) == a.Group, "bargain.response_changed");
        long gain = N(response["AddTalentExp"]);
        Require(gain >= 0 && gain <= a.PerUse && b.Experience - a.Experience == Math.Min(gain, a.Remaining), "bargain.experience_changed");
        Require(N(State(before)["Catalyst"]) - N(State(after)["Catalyst"]) == a.Cost, "bargain.cost_changed");
        return O(("character", a.Character), ("kind", a.Kind), ("gain", b.Experience - a.Experience), ("cost", a.Cost), ("experience", b.Experience), ("full", b.Full));
    }
    internal static bool Restarted(JsonObject before, JsonObject after)
    {
        var a = before["Frame"]!; var b = after["Frame"]!;
        return S(a["AccountKey"]) == S(b["AccountKey"]) && S(a["PlayerKey"]) == S(b["PlayerKey"])
            && S(b["AccountKey"]).Length > 0 && N(a["ProcessStartTicks"]) > 0 && N(b["ProcessStartTicks"]) > 0
            && (N(a["ProcessId"]) != N(b["ProcessId"]) || N(a["ProcessStartTicks"]) != N(b["ProcessStartTicks"]));
    }
    internal static bool ProgressAdvanced(JsonObject op, JsonObject after)
    {
        var before = op["before"]!.AsObject();
        var oldFrame = before["Frame"]!.AsObject(); var frame = after["Frame"]!.AsObject();
        if (S(frame["AccountKey"]).Length == 0 || S(frame["PlayerKey"]).Length == 0
            || new[] { "ProcessId", "ProcessStartTicks", "AccountKey", "PlayerKey" }.Any(k => !JsonNode.DeepEquals(oldFrame[k], frame[k]))) return false;
        var a = Character(before, N(op["scope"]?["character"]));
        var b = Character(after, a.Character);
        if (a.Kind != b.Kind || a.Id != b.Id || a.Group != b.Group || b.Level < a.Level) return false;
        if (b.Level == a.Level) a.AssertSameTier(b);
        // Adopt authoritative progress, including manual play, without claiming
        // that a missing response succeeded and without replaying its command.
        if(b.Level > a.Level || b.Experience > a.Experience)return true;
        if(B(op["scope"]?["achievement"]))
        {
            try{return DailyPracticeAchievement.From(after,ReadId,a.Kind).Current>DailyPracticeAchievement.From(before,ReadId,a.Kind).Current;}
            catch(DailyBargainFault){return false;}
        }
        return false;
    }
    internal static JsonObject? IdleUnchanged(JsonObject op, JsonObject after)
    {
        var before = op["before"]!.AsObject();
        var oldFrame = before["Frame"]!.AsObject(); var frame = after["Frame"]!.AsObject();
        if (S(frame["AccountKey"]).Length == 0 || S(frame["PlayerKey"]).Length == 0
            || new[] { "ProcessId", "ProcessStartTicks", "AccountKey", "PlayerKey" }.Any(k => !JsonNode.DeepEquals(oldFrame[k], frame[k]))
            || State(after)["NetworkBusy"] is not JsonValue busy || !busy.TryGetValue<bool>(out bool requesting) || requesting
            || N(after["AtUtcTicks"]) - N(op["at"]) < TimeSpan.FromSeconds(30).Ticks) return null;
        var a = Character(before, N(op["scope"]?["character"])); var b = Character(after, a.Character);
        if (a.Kind != b.Kind || a.Id != b.Id || a.Group != b.Group || a.Level != b.Level || a.Skill != b.Skill
            || a.Cost != b.Cost || a.Experience != b.Experience || b.Active
            || N(State(before)["Catalyst"]) != N(State(after)["Catalyst"])) return null;
        // A constructor tap is not proof of an outstanding native request. Only
        // retire this attempt after fresh, stable native idle + unchanged state.
        // Never manufacture a success receipt or replay its old UI instance.
        return O(("character", b), ("potions", N(State(after)["Catalyst"])));
    }
    internal static bool Safe(JsonObject e)
    {
        var map = DailyFieldRoute.Map(e); var travel = DailyFieldRoute.Travel(e);
        // The generic proto snapshot omits zero-valued fields such as mapType.
        // Native travel metadata explicitly reports outdoor/safety semantics.
        return N(map["packId"]) is 1 or 2 && N(travel["Pack"]) == N(map["packId"]) && N(travel["Map"]) == N(map["id"])
            && Rows(travel["Maps"]).Any(m => N(m["Id"]) == N(map["id"]) && OutdoorSafe(m));
    }
    internal static bool OutdoorSafe(JsonObject m) => m["MapType"] != null && N(m["MapType"]) == 0
        && m["Inside"] != null && !B(m["Inside"]) && !B(m["Hunting"]) && !B(m["EnvironmentalHazard"]);
}

public sealed class DailyTalentPractice(DailyWorkflow w, Func<bool> stopped, Action<string> report, Action<DailyBargainProgress> progress)
{
    internal const double TravelSeconds = 180;
    public bool Settling { get; private set; }
    Task<JsonObject> Read(params string[] more) => w.Evidence([DailyTalentPracticeProof.ReadId, ..more]);
    async Task Reconcile()
    {
        foreach (var op in w.Business.Records(w.Context, DailyTalentPracticeProof.Role).Where(DailyManagedBusiness.Pending))
        {
            if (S(op["state"]) == "prepared") { op["state"] = "rejected"; w.Business.Save(op); continue; }
            if (!await Recover(op))
            {
                if (stopped()) return;
                throw new DailyBargainFault("practice.pending");
            }
        }
    }
    async Task<bool> Recover(JsonObject op)
    {
        report("practice.recovering");
        double end = w.Time + 8;
        long quietSince = 0;
        JsonObject? quietState = null;
        long previousRead = 0;
        while (!stopped() && w.Time < end)
        {
            var after = await Read();
            var events = w.Business.Events(op);
            op["recovery_observation"] = Copy(after);
            try
            {
                op["result"] = DailyTalentPracticeProof.Verify(op, events, after);
                op["after"] = Copy(after); op["state"] = "completed"; w.Business.Save(op);
                return true;
            }
            catch (InvalidDataException)
            {
                // A new authenticated game cannot retain the previous process's
                // in-flight command. Keep its outcome unknown; do not replay it.
                bool restarted = DailyTalentPracticeProof.Restarted(op["before"]!.AsObject(), after);
                bool advanced = DailyTalentPracticeProof.ProgressAdvanced(op, after);
                var idle = DailyTalentPracticeProof.IdleUnchanged(op, after);
                long readAt = N(after["AtUtcTicks"]);
                bool fresh = readAt > previousRead;
                if (idle == null || readAt < previousRead) { quietState = null; quietSince = 0; }
                else if (fresh && !JsonNode.DeepEquals(quietState, idle)) { quietState = idle; quietSince = readAt; }
                previousRead = readAt;
                // Cached observations neither establish idle time nor erase it.
                // Require two actual game captures spanning the quiet window.
                if (restarted || advanced || fresh && quietState != null && readAt - quietSince >= TimeSpan.FromSeconds(2).Ticks)
                {
                    _ = DailyTalentPracticeProof.Character(after, N(op["scope"]?["character"]));
                    op["state"] = "superseded";
                    op["reconciliation"] = O(("method", restarted ? "new_game_current_training_state" : advanced ? "current_training_progress" : "native_idle_unchanged_training_state"),
                        ("actions", 0), ("original_success_claimed", false), ("saved_action_replayed", false), ("at", w.Driver.UtcTicks), ("current", after));
                    op["events"] = Copy(events);
                    w.Business.Save(op);
                    w.Driver.Diagnostics.Event("talent_practice_recovered", O(("id", op["id"]), ("method", op["reconciliation"]!["method"])));
                    return true;
                }
            }
            await w.Delay(250);
        }
        w.Business.Save(op);
        return false;
    }
    async Task SafeArea(DailyFieldRoute route, long pack)
    {
        report("practice.travel");
        // One shared loading budget covers dispatch, identity refresh, target
        // arrival and the safe landing. Readiness returns immediately; this is
        // never a fixed sleep or permission to send another switch request.
        using var waiting = DailyObservationWindow.Begin(TravelSeconds, () => w.Time);
        double started = w.Time;
        try
        {
            await route.Enter(pack, leaveInterior: false, transitionSeconds: TravelSeconds);
            var e = await Read("mainline.map", "mainline.travel", "route.quick");
            if (!DailyTalentPracticeProof.Safe(e))
            {
                var safe = Rows(DailyFieldRoute.Travel(e)["Maps"]).Where(DailyTalentPracticeProof.OutdoorSafe).Select(m => N(m["Id"])).ToHashSet();
                var target = DailyQuickTravel.Targets(e).FirstOrDefault(t => safe.Contains(N(t["Map"])));
                if (target == null) throw new DailyBargainFault("practice.safe_area_unavailable");
                await DailyQuickTravel.TryMove(w, N(target["Npc"]), N(target["Map"]), TravelSeconds);
                await route.FieldReady(timeout: waiting.Remaining, settle: 0);
                e = await Read("mainline.map", "mainline.travel");
            }
            if (!DailyTalentPracticeProof.Safe(e) || N(DailyFieldRoute.Map(e)["packId"]) != pack)
            {
                w.Driver.Diagnostics.Event("talent_practice_landing_unready", O(("requested_pack", pack), ("map", DailyFieldRoute.Map(e))));
                throw new DailyBargainFault("practice.safe_area_unavailable");
            }
            w.Driver.Diagnostics.Event("talent_practice_safe_area", O(("pack", pack), ("map", DailyFieldRoute.Map(e)["id"])));
        }
        catch (StageHostException error) when (error.ObservationTimedOut || error.Kind == "adapter" && w.Time - started >= TravelSeconds)
        {
            w.Driver.Diagnostics.Failure("talent_practice_travel_timeout", O(("pack", pack), ("elapsed", w.Time - started), ("error", error.Message)));
            throw new DailyBargainFault("practice.travel_timeout");
        }
    }
    public const string MapResetRequired = "practice.map_reset_required";
    public static async Task<DailyPracticeSnapshot> ReadSnapshot(DailyWorkflow workflow)
    {
        using var waiting=DailyObservationWindow.Begin(30,()=>workflow.Time);
        var evidence=await workflow.Evidence(DailyTalentPracticeProof.ReadId);
        var state=DailyTalentPracticeProof.State(evidence);
        return new(DailyTalentPracticeProof.Characters(evidence),N(state["Catalyst"]),DailyPracticeAchievement.Read(state));
    }
    bool batchReconciled;
    public async Task<string> RunRound(DailyBargainCharacter approved,DailyPracticeAchievement? achievement,bool reset)
    {
        using var waiting=DailyObservationWindow.Begin(TravelSeconds+90,()=>w.Time);
        try
        {
            var result=await Run(approved,1,achievement,allowMapReset:false,forceMapReset:reset,reconcile:!batchReconciled);
            batchReconciled=true;return result;
        }
        catch{batchReconciled=false;throw;}
    }
    public async Task<string> Run(DailyBargainCharacter approved, int maximumRounds = int.MaxValue,DailyPracticeAchievement? achievement=null,
        bool allowMapReset = true, bool forceMapReset = false,bool reconcile=true)
    {
        if (!DailyTalentPracticeProof.FieldKind(approved.Kind) || maximumRounds <= 0) throw new ArgumentOutOfRangeException(nameof(approved));
        if(reconcile)await Reconcile();
        if (stopped()) return "bargain.stopped";
        var e = await Read(); var current = DailyTalentPracticeProof.Character(e, approved.Character);
        approved.AssertSameTier(current);
        Require(current.Experience >= approved.Experience, "bargain.preview_changed");
        achievement=achievement?.Refresh(e,DailyTalentPracticeProof.ReadId);
        if(achievement?.Remaining==0)return "practice.achievement_complete";
        if (achievement==null&&current.Full) return "bargain.upgrade";
        if(achievement==null)new DailyBargainPlan(current, N(DailyTalentPracticeProof.State(e)["Catalyst"])).Validate();
        long budget = achievement==null?current.Estimate:checked(achievement.Remaining*current.Cost), spent = 0; int rounds = 0, attempts = 0, recoveries = 0;
        progress(new(current, N(DailyTalentPracticeProof.State(e)["Catalyst"]), rounds, spent){Achievement=achievement,Achievements=DailyPracticeAchievement.Read(DailyTalentPracticeProof.State(e))});
        var route = new DailyFieldRoute(w);
        if (await w.Has("QuickMenuUI"))
            await w.Step("QuickMenuUI", operation: "back", absent: "QuickMenuUI");
        if (!DailyFieldRoute.FieldInputReady((await w.Observe()).Frame))
            await w.Home("weekly_mainline");
        // Require both cartridges up front so a locked second cartridge cannot
        // strand a user after spending medicine on the first cast.
        var owned = await w.Evidence("mainline.owned_packs", "mainline.packs");
        if (!route.Owned(owned, 1) || !route.Owned(owned, 2)) throw new DailyBargainFault("practice.packs_required");
        // Continuing after a slow arrival adopts its actual safe cartridge;
        // do not immediately travel back to Chapter 1 just to start the tool.
        // A main-menu start has no field data yet and must enter normally.
        long pack = 1;
        if (DailyFieldRoute.FieldInputReady((await w.Observe()).Frame))
        {
            var location = await Read("mainline.map", "mainline.travel");
            if (DailyTalentPracticeProof.Safe(location)) pack = N(DailyFieldRoute.Map(location)["packId"]);
        }
        await SafeArea(route, pack);
        if(forceMapReset&&!stopped()){pack=pack==1?2:1;await SafeArea(route,pack);}
        while (!stopped() && attempts < maximumRounds)
        {
            e = await Read(); current = DailyTalentPracticeProof.Character(e, approved.Character); approved.AssertSameTier(current);
            if(achievement!=null){achievement=achievement.Refresh(e,DailyTalentPracticeProof.ReadId);if(achievement.Remaining==0)return "practice.achievement_complete";}
            else if (current.Full) return "bargain.upgrade";
            if (current.Active)
            {
                if(!allowMapReset)return MapResetRequired;
                pack = pack == 1 ? 2 : 1;
                await SafeArea(route, pack);
                e = await Read(); current = DailyTalentPracticeProof.Character(e, approved.Character);
                if (current.Active) throw new DailyBargainFault("practice.effect_not_reset");
            }
            await w.Step("GameFieldDefaultUI", operation: "mainline_menu", value: current.Kind, expect: "QuickMenuUI");
            JsonObject? row = null; double end = w.Time + 35;
            while (!stopped() && w.Time < end)
            {
                e = await Read("mainline.talent_rows", "mainline.map", "mainline.travel");
                if (!DailyTalentPracticeProof.Safe(e)) throw new DailyBargainFault("practice.safe_area_unavailable");
                current = DailyTalentPracticeProof.Character(e, approved.Character); approved.AssertSameTier(current);
                if(achievement!=null){achievement=achievement.Refresh(e,DailyTalentPracticeProof.ReadId);if(achievement.Remaining==0)return "practice.achievement_complete";}
                else if (current.Full) return "bargain.upgrade";
                var matches = DailyFieldRoute.Talents(e).Where(r => N(r["Character"]) == current.Character && N(r["Kind"]) == current.Kind && N(r["Group"]) == current.Group).ToArray();
                if (matches.Length != 1 || S(matches[0]["Reason"]) != "") throw new DailyBargainFault("bargain.character_unavailable");
                string gate = S(matches[0]["Gate"]);
                if (gate == "") { row = matches[0]; break; }
                if(!allowMapReset&&gate is "talent_wait:duration_active" or "talent_wait:research_active")
                    return MapResetRequired;
                if (!gate.StartsWith("talent_wait:", StringComparison.Ordinal)) throw new DailyBargainFault("bargain.character_unavailable");
                await w.Delay(150);
            }
            if (stopped()) break;
            if (row == null) throw new DailyBargainFault("bargain.surface_timeout");
            Require(spent + current.Cost <= budget, "bargain.budget_reached");
            Require(N(DailyTalentPracticeProof.State(e)["Catalyst"]) >= current.Cost, "bargain.potions_changed");
            Settling = true;
            try
            {
                report("practice.casting");
                attempts++;
                var op = await w.TransactObserved(DailyTalentPracticeProof.Role,
                    O(("character", current.Character), ("group", current.Group), ("kind", current.Kind),("achievement",achievement!=null),("achievementTarget",achievement?.Target??0)),
                    O(("ui", "QuickMenuUI"), ("operation", "mainline_talent"), ("value", row["Instance"])), e, 30);
                rounds++; spent += N(op["result"]?["cost"]);
                var after = op["after"]!.AsObject(); current = DailyTalentPracticeProof.Character(after, approved.Character);
                await route.FieldReady(settle: 0);
                if(achievement!=null)achievement=await achievement.AfterUse(w,DailyTalentPracticeProof.ReadId);
                progress(new(current, N(DailyTalentPracticeProof.State(after)["Catalyst"]), rounds, spent){Achievement=achievement,Achievements=DailyPracticeAchievement.Read(DailyTalentPracticeProof.State(after))});
                if(achievement?.Remaining==0)return "practice.achievement_complete";
                if (achievement==null&&current.Full) return "bargain.upgrade";
                if (achievement==null&&N(op["result"]?["gain"]) == 0) return "bargain.no_growth";
            }
            catch (StageHostException error) when (error.Kind == "pending")
            {
                var op = w.Business.Records(w.Context, DailyTalentPracticeProof.Role).SingleOrDefault(DailyManagedBusiness.Pending);
                if (op == null || !await Recover(op))
                {
                    if (stopped()) return "bargain.stopped";
                    throw new DailyBargainFault("practice.pending");
                }
                var after = await Read(); current = DailyTalentPracticeProof.Character(after, approved.Character);
                long used = Math.Max(0, N(DailyTalentPracticeProof.State(e)["Catalyst"]) - N(DailyTalentPracticeProof.State(after)["Catalyst"]));
                spent += used;
                if(achievement!=null){var next=achievement.Refresh(after,DailyTalentPracticeProof.ReadId);if(next.Current>achievement.Current)rounds++;achievement=next;}
                else if (current.Experience > DailyTalentPracticeProof.Character(e, approved.Character).Experience) rounds++;
                progress(new(current, N(DailyTalentPracticeProof.State(after)["Catalyst"]), rounds, spent){Achievement=achievement,Achievements=DailyPracticeAchievement.Read(DailyTalentPracticeProof.State(after))});
                if(achievement?.Remaining==0)return "practice.achievement_complete";
                if (achievement==null&&current.Full) return "bargain.upgrade";
                // Bound recovery even when repeated native calls have no effect.
                // The settled record stays recoverable on a later explicit run.
                if (++recoveries >= 2) throw new DailyBargainFault("practice.retry_exhausted");
            }
            finally { Settling = false; }
            // Switch even if a short-lived animation has ended before its data
            // refresh: the next round always begins in the other safe cartridge.
            if (!stopped() && attempts < maximumRounds) { pack = pack == 1 ? 2 : 1; await SafeArea(route, pack); }
        }
        return stopped() ? "bargain.stopped" : "practice.test_complete";
    }
}
