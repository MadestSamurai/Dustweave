using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class BargainTrainingCases
{
    static readonly DailyBargainCharacter Sample = new(10, 100, "Silvia", 2, 5, 605, 6052, 155, 100, 170, 5, 10);
    static JsonObject Native(DailyBargainCharacter c, long potions) => O(("Characters", new[] { c }), ("Catalyst", potions), ("Gold", 1000));
    sealed class Fixture : IDisposable
    {
        public readonly WorkflowHarness H;
        public readonly DailyBargainTraining Trainer;
        public DailyBargainCharacter Character = Sample;
        public long Potions = 100;
        public bool Discount, Stop, MissingResponse, Success = true, StopAtPreview, StopAtCommit;
        public int Attempts, Closed, Gain = 5;
        public Fixture(string root)
        {
            H = new(root, [DailyBargainProof.Definition]);
            H.Frame["Scene"] = "Map3009_001";
            H.Page("ShopUI", "_objBackButton");
            Trainer = new(H.Workflow, () => Stop, _ => { }, _ => { });
            H.Readings = () => [
                WorkflowCases.Reading(DailyBargainProof.ReadId, ("$self", Native(Character, Potions))),
                WorkflowCases.Reading("trade.currency", ("Catalyst", Potions), ("Gold", 1000)),
                WorkflowCases.Reading("trade.shop_ui", ("ὩὤὬὪὤὦὧὭὣὥὠ.ὯὫὪὡὭὤὪὮὫὬὣ", 50), ("ὯὣὡὫὨὫὡὧὩὬὠ", "Buy")),
                WorkflowCases.Reading("trade.quickmenu", ("ὬὣὨὤὯὪὪὮὠὥὨ.ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ", 50)),
                WorkflowCases.Reading("trade.bargain", ("$self", Discount)),
                WorkflowCases.Reading("trade.merchant", ("ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ", 50), ("$items", new[] { O(("ὫὠὫὯὠὢὥὧὢὥὥ", "Shop"), ("ὭὩὪὯὯὬὧὠὠὬὠ", 3002)) })),
                WorkflowCases.Reading("mainline.talent_rows", ("$self", new[] { O(("Kind", 16), ("Character", 10), ("Group", 605), ("Instance", 20), ("Gate", ""), ("Reason", "")) })),
                WorkflowCases.Reading("trade.discount_popup", ("ὤὣὤὥὨὧὮὨὣὤὤ", 10), ("ὣὮὡὮὣὥὫὬὨὣὬ", 50), ("ὣὡὪὭὤὨὭὣὪὧὩ", O(("id", 6052), ("classType", 16), ("catalystValue", 10))))
            ];
            H.OnCommand = c =>
            {
                H.Time += .01; // Native input and its reply cannot occur at an identical clock instant across rounds.
                string ui = S(c["UiType"]), kind = S(c["Kind"]);
                // The command's observed token identifies the originating native UI.
                ui = S(c["UiToken"]);
                if (kind == "trade_talent")
                {
                    H.Page("DiscountPopupUI", "_objButtonOk", "_objButtonCancel");
                    if (StopAtPreview) Stop = true;
                }
                else if (ui == "ShopUI")
                {
                    Closed++;
                    if (Discount) H.Page("DiscountCancelPopupUI", "_objButtonOk"); else H.Page("QuickMenuUI");
                }
                else if (ui == "DiscountCancelPopupUI") H.Page("QuickMenuUI"); // cached discount intentionally stays true
                else if (ui == "DiscountPopupUI" && N(c["TargetId"]) == 11) H.Page("QuickMenuUI");
                else if (ui == "DiscountPopupUI")
                {
                    Attempts++; H.Event("dispatch.start", "request");
                    Character = Character with { Experience = Math.Min(Character.Target, Character.Experience + Gain) }; Potions -= Character.Cost;
                    if (!MissingResponse) H.Event("dispatch.start", "response", ("TalentSkillInfo", O(("groupId", 605))), ("AddTalentExp", Gain), ("IsSuccess", Success));
                    Discount = Success; H.Page("ShopUI", "_objBackButton");
                    if (StopAtCommit) Stop = true;
                }
                else throw new Exception("Unexpected training navigation: " + c.ToJsonString());
            };
        }
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        ProofCases(checks);
        Check(Sample.Estimate == 30 && Sample.Uses == 3 && Sample.BarValue == 55, "bargain cumulative experience and current-tier cost");
        Check((Sample with { Experience = 168 }).Estimate == 10, "partial final XP still costs a full attempt");
        Check((Sample with { Experience = 170 }).Estimate == 0 && (Sample with { Level = 5 }).Full, "cap and maximum tier never train");
        Check(!new DailyBargainPlan(Sample, 29).CanStart && new DailyBargainPlan(Sample, 29).Shortage == 1, "whole-tier shortage explained before navigation");
        Check(new DailyBargainPlan(Sample, 30).CanStart, "exact tier budget is accepted");
        Check(!new DailyBargainPlan(Sample with { PerUse = 0 }, 100).CanStart, "zero XP talent cannot start");
        foreach (var success in new[] { true, false })
        using (var f = new Fixture(Path.Combine(root, "bargain-full-" + success)) { Success = success })
        {
            Check(await f.Trainer.Run(Sample) == "bargain.upgrade", "full tier gives book reminder: " + success);
            Check(f.Attempts == 3 && f.Potions == 70 && f.Closed == 4, "one close per result, no ordinary shop reopen: " + success);
            Check(!f.H.Box.Commands.Any(c => S(c["Kind"]) == "square_shop_interact"), "reuse merchant menu without travel loop: " + success);
        }
        using (var f = new Fixture(Path.Combine(root, "bargain-short")) { Potions = 29 })
        {
            bool shortage = false; try { await f.Trainer.Run(Sample); } catch (DailyBargainFault e) { shortage = e.Code == "bargain.shortage"; }
            Check(shortage && f.H.Box.Commands.Count == 0, "insufficient full-tier budget performs no game actions");
        }
        using (var f = new Fixture(Path.Combine(root, "bargain-stop-preview")) { StopAtPreview = true })
        {
            Check(await f.Trainer.Run(Sample) == "bargain.stopped" && f.Attempts == 0, "stop cancels own preview before spending");
        }
        using (var f = new Fixture(Path.Combine(root, "bargain-no-growth")) { Gain = 0 })
        {
            Check(await f.Trainer.Run(Sample) == "bargain.no_growth" && f.Attempts == 1, "settled zero-XP round stops without waste");
        }
        using (var f = new Fixture(Path.Combine(root, "bargain-stop-commit")) { StopAtCommit = true })
        {
            Check(await f.Trainer.Run(Sample) == "bargain.stopped" && f.Attempts == 1 && f.Closed == 2, "stop after submission settles one round and closes only its own shop");
            Check(!f.H.Business.Records(f.H.Context, DailyBargainProof.Role).Any(DailyManagedBusiness.Pending), "graceful stop leaves no false pending consumption");
        }
        using (var f = new Fixture(Path.Combine(root, "bargain-lost-response")) { MissingResponse = true })
        {
            try { await f.Trainer.Run(Sample); throw new Exception("Missing response accepted"); } catch (StageHostException) { }
            Check(f.Attempts == 1 && f.H.Business.Records(f.H.Context, DailyBargainProof.Role).Any(DailyManagedBusiness.Pending), "missing response remains pending, not automatically repeated");
            try { await f.Trainer.Run(Sample); throw new Exception("Unconfirmed round replayed"); } catch (StageHostException) { }
            Check(f.Attempts == 1, "restart preserves unresolved consuming operation");
        }
        using (var f = new Fixture(Path.Combine(root, "bargain-changed-tier")))
        {
            f.Character = Sample with { Level = 3 };
            try { await f.Trainer.Run(Sample); throw new Exception("Changed tier accepted"); } catch (InvalidDataException) { }
            Check(f.H.Box.Commands.Count == 0, "book upgrade requires new preview");
        }
        Check(DailyDiagnosticExport.Allowed("tools/bargain-training/logs/error.json"), "manual training failures included in diagnostic export");
    }
    static void ProofCases(List<string> checks)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label) { try { action(); } catch (InvalidDataException) { checks.Add(label); return; } throw new Exception(label); }
            var c=new DailyBargainCharacter(10,100,"角色",2,5,605,6052,155,100,300,5,10);
            Check(c.BarValue==55&&c.BarMax==200,"cumulative tier progress");
            Check(!c.Full&&c.Remaining==145&&c.Uses==29&&c.Estimate==290,"remaining cost");
            Check((c with{Experience=300}).Full&&(c with{Experience=300}).Estimate==0,"exact full cap");
            Check((c with{Experience=301}).Full,"overflow bar stops");
            Check((c with{Level=5}).Full,"max tier stops");
            Reject(()=>_=(c with{PerUse=0}).Uses,"zero experience cannot train");
            Reject(()=>c.AssertSameTier(c with{Level=3}),"tier change");
            Reject(()=>c.AssertSameTier(c with{Character=20}),"character change");
            Reject(()=>c.AssertSameTier(c with{Cost=20}),"cost change");
            var frame=O(("ProcessId",1),("ProcessStartTicks",2),("Instance","connection"),("AccountKey","account"),("PlayerKey","player"));
            JsonObject E(DailyBargainCharacter ch,long potions)=>O(("Config","same"),("Frame",frame),("Readings",new[]{O(("Id",DailyBargainProof.ReadId),("Error",""),("Values",new[]{O(("Path","$self"),("Error",""),("Json",O(("Catalyst",potions),("Gold",1000),("Characters",new[]{O(("Character",ch.Character),("Id",ch.Id),("Name",ch.Name),("Level",ch.Level),("MaxLevel",ch.MaxLevel),("Group",ch.Group),("Skill",ch.Skill),("Experience",ch.Experience),("Previous",ch.Previous),("Target",ch.Target),("PerUse",ch.PerUse),("Cost",ch.Cost))})).ToJsonString()))}))}));
            JsonArray Events(long gain=5,bool success=true,int group=605)=>new(O(("Role","dispatch.start"),("Kind","request"),("Sequence",1),("Frame",frame)),O(("Role","dispatch.start"),("Kind","response"),("Sequence",2),("Frame",frame),("Accepted",true),("ErrorCode",0),("Error",""),("Values",new[]{O(("Path","TalentSkillInfo"),("Error",""),("Json",O(("groupId",group)).ToJsonString())),O(("Path","AddTalentExp"),("Error",""),("Json",gain.ToString())),O(("Path","IsSuccess"),("Error",""),("Json",success?"true":"false"))})));
            var before=E(c,1000);var op=O(("before",before),("scope",O(("character",10))));var after=E(c with{Experience=160},990);
            Check(N(DailyBargainProof.Verify(op,Events(),after)["gain"])==5,"response plus character exp and catalyst proof");
            Check(N(DailyBargainProof.Verify(op,Events(5,false),after)["gain"])==5,"failed discount with experience continues");
            Check(N(DailyBargainProof.Verify(op,Events(0,false),E(c,990))["gain"])==0,"no growth is settled and stops");
            Reject(()=>DailyBargainProof.Verify(op,Events(group:606),after),"wrong skill response");
            Reject(()=>DailyBargainProof.Verify(op,Events(),E(c,990)),"another character gaining exp cannot count");
            Reject(()=>DailyBargainProof.Verify(op,Events(),E(c with{Experience=160},980)),"unexpected catalyst cost");
            var twice=Events();foreach(var item in Events())twice.Add(item!.DeepClone());Reject(()=>DailyBargainProof.Verify(op,twice,after),"duplicate consuming request");
            var a2=after.DeepClone().AsObject();a2["Frame"]!["PlayerKey"]="other";Reject(()=>DailyBargainProof.Verify(op,Events(),a2),"account identity changed");
            var a3=after.DeepClone().AsObject();a3["Config"]="other";Reject(()=>DailyBargainProof.Verify(op,Events(),a3),"evidence configuration changed");
            var near=c with{Experience=298};var capOp=O(("before",E(near,1000)),("scope",O(("character",10))));Check(N(DailyBargainProof.Verify(capOp,Events(),E(c with{Experience=300},990))["gain"])==2,"final use clamps to current cap");
            var noGold=E(c with{Experience=160},990);var native=DailyBargainProof.State(noGold);native["Gold"]=999;
            // Reading values are serialized strings, so write the changed payload back.
            noGold["Readings"]![0]!["Values"]![0]!["Json"]=native.ToJsonString();Reject(()=>DailyBargainProof.Verify(op,Events(),noGold),"unrelated gold consumption");
            JsonObject Closing(params JsonObject[] surfaces)=>O(("Surfaces",surfaces));
            var cancel=O(("Type","DiscountCancelPopupUI"),("Popup",true),("InputReady",true),("Targets",new[]{O(("Field","_objButtonOk"),("Enabled",true))}));
            Check(DailyTradeClose.Next(Closing(cancel),true,false)=="confirm","owned bargain cancellation");
            Check(DailyTradeClose.Next(Closing(cancel),true,true)=="wait","cancel is never replayed");
            bool rejected=false;try{DailyTradeClose.Next(Closing(cancel),false,false);}catch(DailyStepException){rejected=true;}Check(rejected,"unowned cancellation is preserved");
            rejected=false;try{DailyTradeClose.Next(Closing(O(("Type","MessagePopupUI"),("Popup",true))),true,false);}catch(DailyStepException){rejected=true;}Check(rejected,"unknown popup is preserved");
            Check(DailyTradeClose.Next(Closing(),true,true)=="settling","native stale bargain flag does not stall exit");

    }
}
