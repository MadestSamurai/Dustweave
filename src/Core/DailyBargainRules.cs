using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed record DailyBargainCharacter(long Character,int Id,string Name,int Level,int MaxLevel,int Group,int Skill,long Experience,long Previous,long Target,int PerUse,int Cost)
{
    public bool Full => Level>=MaxLevel || Experience>=Target;
    public long Remaining => Full?0:Math.Max(0,Target-Experience);
    public long Uses => Remaining==0?0:PerUse>0?checked((Remaining+PerUse-1)/PerUse):throw new InvalidDataException("bargain.no_growth");
    public long Estimate => checked(Uses*Cost);
    public string Label => $"{Name} · 天赋 Lv{Level}";
    public long BarValue => Math.Clamp(Experience-Previous,0,Math.Max(0,Target-Previous));
    public long BarMax => Math.Max(0,Target-Previous);
    public static DailyBargainCharacter Parse(JsonObject r)
    {
        var c=new DailyBargainCharacter(N(r["Character"]),I(r["Id"]),S(r["Name"]),I(r["Level"]),I(r["MaxLevel"]),I(r["Group"]),I(r["Skill"]),N(r["Experience"]),N(r["Previous"]),N(r["Target"]),I(r["PerUse"]),I(r["Cost"]));
        Require(c.Character>0&&c.Level>0&&c.MaxLevel>=c.Level&&c.Group>0&&c.Skill>0&&c.Experience>=c.Previous&&c.Target>=c.Previous&&c.PerUse>=0&&c.Cost>0,"bargain.data_incomplete");
        return c;
    }
    public void AssertSameTier(DailyBargainCharacter other)
    {
        Require(Character==other.Character&&Id==other.Id&&Level==other.Level&&MaxLevel==other.MaxLevel&&Group==other.Group&&Skill==other.Skill&&Previous==other.Previous&&Target==other.Target&&Cost==other.Cost&&PerUse==other.PerUse,"bargain.preview_changed");
    }
}

public static class DailyBargainProof
{
    public const string Role="bargain.training";
    public const string ReadId="mainline.bargain_training";
    public static JsonObject State(JsonObject e)=>DailyData.State(e,ReadId,"$self");
    public static DailyBargainCharacter[] Characters(JsonObject e)=>Rows(State(e)["Characters"]).Select(DailyBargainCharacter.Parse).ToArray();
    public static DailyBargainCharacter Character(JsonObject e,long id)=>Characters(e).Single(c=>c.Character==id);
    public static DailyBusinessProof Definition=>new(Role,"bargain_training",[ReadId,"trade.currency"],Verify,["dispatch.start"],PreviewOwner:OwnsPreview);
    private static bool OwnsPreview(JsonObject op,DailyStageFrame f)=>op["preview_frame"] is JsonObject original&&S(original["Scene"])==S(f.Frame["Scene"])&&JsonNode.DeepEquals(original["UiToken"],f.Frame["UiToken"])&&DailyNavigationDecision.Types(f.Frame).Contains("DiscountPopupUI");
    public static JsonObject Verify(JsonObject op,JsonArray events,JsonObject after)
    {
        var before=op["before"]!.AsObject();var scope=op["scope"]!.AsObject();
        var response=Response("dispatch.start",events,before,after)!;
        long id=N(scope["character"]);
        var a=Character(before,id);var b=Character(after,id);a.AssertSameTier(b);
        Require(a.Level<a.MaxLevel&&a.Experience<a.Target,"bargain.upgrade");
        Require(N(response["TalentSkillInfo"]?["groupId"])==a.Group,"bargain.response_changed");
        long gain=N(response["AddTalentExp"]),actual=b.Experience-a.Experience;
        Require(gain>=0&&gain<=a.PerUse&&actual>=0&&actual==Math.Min(gain,Math.Max(0,a.Target-a.Experience)),"bargain.experience_changed");
        Require(N(State(before)["Catalyst"])-N(State(after)["Catalyst"])==a.Cost,"bargain.cost_changed");
        Require(N(State(before)["Gold"])==N(State(after)["Gold"]),"bargain.gold_changed");
        // A failed bargain can still grant experience; training tracks experience, not discount success.
        Require(response["IsSuccess"] is JsonValue flag&&flag.TryGetValue<bool>(out _),"bargain.result_missing");
        return O(("character",id),("gain",actual),("reported_gain",gain),("cost",a.Cost),("experience",b.Experience),("full",b.Full),("success",response["IsSuccess"]));
    }
}
