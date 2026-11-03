using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class EventPresetCases
{
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var old = JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();
        old["EventBattle"]!.AsObject().Remove("PresetSlot");
        Check(DailyPreferences.Parse(old.ToJsonString()).EventBattle.PresetSlot == 0, "Old event preferences preserve the current team");
        foreach (int slot in new[] { 0, 1, 6, 999 })
        {
            var settings = new DailyPreferences(); settings.EventBattle.PresetSlot = slot; settings.Validate();
            Check(JsonSerializer.Deserialize<DailyPreferences>(JsonSerializer.Serialize(settings))!.EventBattle.PresetSlot == slot,
                "Event preset persists its slot number: " + slot);
        }
        foreach (int slot in new[] { -1, 1000 })
        {
            var settings = new DailyPreferences(); settings.EventBattle.PresetSlot = slot;
            bool rejected = false; try { settings.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid event preset slot rejected: " + slot);
        }
        foreach (string scenario in new[] { "disabled", "unbound", "repeat", "empty", "cleared", "read-failed", "read-timeout", "apply-timeout", "stale", "account-change" })
        {
            using var h = new WorkflowHarness(Path.Combine(root, "event-preset-" + scenario), []);
            h.Workflow.Settings.EventBattle.Enabled = scenario != "disabled";
            h.Workflow.Settings.EventBattle.PresetSlot = scenario == "unbound" ? 0 : 6;
            h.Page("EventBattleUI");
            var state = O(("Revision", 10), ("Slot", 6), ("State", "applied"));
            h.Readings = () => [WorkflowCases.Reading("event.preset", ("$self", state))];
            int reads = 0, uses = 0, team = 13, saved = 17; bool recovered = false;
            h.OnCommand = c =>
            {
                Check(I(c["Value"]) == 6, "Preset command uses configured slot: " + scenario);
                if (S(c["Kind"]) == "event_preset_read")
                {
                    reads++;
                    if (scenario == "stale" && !recovered) return;
                    state["Revision"] = N(state["Revision"]) + 1;
                    state["State"] = !recovered && scenario == "read-timeout" ? "reading" :
                        !recovered && scenario == "read-failed" ? "failed" : scenario == "empty" ? "empty" : "ready";
                    if (scenario == "account-change" && !recovered) h.Frame["AccountKey"] = "other-account";
                }
                else if (S(c["Kind"]) == "event_preset_apply")
                {
                    Check(N(c["Items"]![0]) == N(state["Revision"]), "Application binds the fresh read: " + scenario);
                    uses++; state["State"] = !recovered && scenario == "apply-timeout" ? "applying" : scenario == "cleared" ? "empty" : "applied";
                    if (S(state["State"]) == "applied") team = saved;
                }
                else throw new Exception("Unexpected command: " + c.ToJsonString());
            };
            bool interrupted = false;
            try { await DailyEventPreset.Apply(h.Workflow); }
            catch (Exception) when (scenario is "read-failed" or "read-timeout" or "apply-timeout" or "stale" or "account-change") { interrupted = true; }
            if (scenario is "disabled" or "unbound") Check(reads == 0 && uses == 0 && team == 13, "Unset or disabled binding does no game work: " + scenario);
            else if (scenario is "empty" or "cleared") Check(team == 13 && uses == (scenario == "cleared" ? 1 : 0), "Empty slots preserve the current team: " + scenario);
            else if (scenario == "repeat")
            {
                Check(team == 17, "Selected slot is applied before the batch");
                team = 21; saved = 29;
                await DailyEventPreset.Apply(h.Workflow);
                Check(reads == 2 && uses == 2 && team == 29, "Same slot reapplies latest contents after manual team and preset changes");
            }
            else
            {
                Check(interrupted && reads == 1 && uses <= 1, "Failed preparation cannot silently start or loop: " + scenario);
                if (scenario != "account-change")
                {
                    recovered = true; await DailyEventPreset.Apply(h.Workflow);
                    Check(reads == 2 && team == 17, "Retry after recovery actually applies the fresh preset: " + scenario);
                }
                else Check(uses == 0, "Changed identity never receives a preset application");
            }
        }
        foreach (bool empty in new[]{false,true})
        {
            using var h = new WorkflowHarness(Path.Combine(root,"event-preset-batches-"+empty),[DailyEventNative.Definition(),DailyEventSweep.Definition()]);
            h.Workflow.Settings.EventBattle.Enabled=true; h.Workflow.Settings.EventBattle.Challenge=true; h.Workflow.Settings.EventBattle.PresetSlot=6;
            var lobby=EventTradeBoundaryCases.Lobby();
            var preset=O(("Revision",0),("Slot",6),("State","idle"));
            int free=2,reads=0,uses=0,battles=0; bool result=false;
            h.Readings=()=>[WorkflowCases.Reading("event.lobby",("$self",lobby)),
                WorkflowCases.Reading("event.currency",("EventApFree",free),("EventApStack",7)),
                WorkflowCases.Reading("event.preset",("$self",preset)),
                WorkflowCases.Reading("event.native",("$self",O(("Battle",result),("Result",result),("Continuing",false),("Lost",false),("Current",1),("Total",1)))),
                WorkflowCases.Reading("event.sweep.popup",("_buttonFreeOnly.IsEnable",true),(DailyEventSweep.Count,1))];
            void Lobby()=>h.Page("EventBattleUI","_buttonAutoBattle._button");
            Lobby();
            h.OnCommand=c=>
            {
                h.Time+=.01;
                string kind=S(c["Kind"]);
                var ui=Rows(h.Frame["Surfaces"]).Single(r=>N(r["Id"])==N(c["SurfaceId"]));
                string field=Rows(ui["Targets"]).Where(t=>N(t["Id"])==N(c["TargetId"])).Select(t=>S(t["Field"])).SingleOrDefault()??"";
                if(kind=="event_preset_read"){reads++;preset["Revision"]=reads;preset["State"]=empty?"empty":"ready";return;}
                if(kind=="event_preset_apply"){uses++;preset["State"]="applied";return;}
                if(kind=="event_native_select"){lobby["Selected"]=Copy(c["Items"]![2]);return;}
                if(field=="_buttonAutoBattle._button"){h.Page("BattleSkipPopupUI","_sliderAutoCount._objMinusMaxButton");return;}
                if(field=="_sliderAutoCount._objMinusMaxButton")return;
                if(kind=="event_native_confirm")
                {
                    Check(reads==battles+1 && uses==(empty?0:reads),"Every new event batch prepares the chosen preset before combat: "+empty);
                    battles++;free--;result=true;
                    lobby["Stages"]=Array(Rows(lobby["Stages"]).Select(r=>EventTradeBoundaryCases.Stage(I(r["Id"]),true)));
                    h.Page("BattleResultUI");return;
                }
                if(kind=="event_native_exit"){result=false;Lobby();return;}
                if(kind=="back"&&S(ui["Type"])=="EventBattleUI"){h.Page("EventMainUI","_buttonChallenge._button");return;}
                if(field=="_buttonChallenge._button")
                {
                    lobby["Category"]="Challenge";lobby["Latest"]=3;lobby["Stages"]=new JsonArray(EventTradeBoundaryCases.Stage(3,false));Lobby();return;
                }
                if(kind=="back"&&S(ui["Type"])=="EventMainUI"){h.Page("MenuUI");return;}
                throw new Exception("Unexpected event preset integration input: "+kind+"/"+field);
            };
            var done=await DailyEventNative.Run(h.Workflow);
            Check(S(done["state"])=="completed"&&battles==2&&free==0&&reads==2&&uses==(empty?0:2),
                "Normal and challenge batches complete with bound or empty presets: "+empty);
        }
    }
}
