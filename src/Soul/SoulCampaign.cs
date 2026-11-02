using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dustweave.Soul;

// Fresh Start replays all five. Resume explicitly adopts current game progress.
public sealed class SoulCampaign
{
    public string AccountKey { get; set; } = "";
    public string PlayerKey { get; set; } = "";
    public string StartedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public int[] Dungeons { get; set; } = [];
    public int[] Waves { get; set; } = [];
    public List<int> Completed { get; set; } = [];
    public int ResumeDungeon { get; set; }
    public int Season { get; set; }
    public int Target => Dungeons.Contains(ResumeDungeon)&&!Completed.Contains(ResumeDungeon)?ResumeDungeon:Dungeons.FirstOrDefault(d=>!Completed.Contains(d));
    public int Battles { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int PendingDungeon { get; set; }
    public int PendingWave { get; set; }
    public int PendingLife { get; set; } = -1;
    public int PendingSeason { get; set; }
    public long PendingSince { get; set; }
    public int UnconfirmedBattles { get; set; }
    public string Status { get; set; } = "ready";
    public int LastDungeon { get; set; }
    public int LastWave { get; set; }
    public Dictionary<int,int> Restarts { get; set; } = [];
    // Reporting only. Neither this counter nor battle outcomes are planner
    // features, weights or search seeds.
    public Dictionary<int,int> FailedAttempts { get; set; } = [];
    public int LastDefeatBattle { get; set; } = -1;
    internal void RetryDefeatedDungeon()
    {
        if(Status!="defeated"||PendingDungeon!=0||Target==0||LastDungeon!=Target||Battles<=0)
            throw new InvalidDataException("Only a settled defeated dungeon may be retried.");
        if(LastDefeatBattle!=Battles){FailedAttempts[Target]=FailedAttempts.GetValueOrDefault(Target)+1;LastDefeatBattle=Battles;}
        Status="retrying";
    }
    public void Initialize(JsonObject state,string account,string player)
    {
        int[] Ids(string key)=>state["progress"]![key]!.AsArray().Select(n=>int.Parse(n!.ToString())).ToArray();
        if(Dungeons.Length==0){Dungeons=Ids("SeasonDungeons");Waves=Ids("WaveCounts");AccountKey=account;PlayerKey=player;Season=state["progress"]?["RegularSeason"]?.GetValue<int>()??0;}
        if(AccountKey!=account||PlayerKey!=player)throw new InvalidDataException("Challenge belongs to another account.");
        if(Dungeons.Length!=5||Waves.Length!=5||Dungeons.Distinct().Count()!=5||Waves.Any(n=>n<=0))throw new InvalidDataException("Five-dungeon season is unavailable.");
        var ids=Ids("SeasonDungeons");if(ids.Length>0&&!ids.SequenceEqual(Dungeons))throw new InvalidDataException("Season changed during challenge.");
    }
    public void BeginBattle(int dungeon,int wave,JsonObject? before=null)
    {
        if(PendingDungeon!=0||dungeon!=Target||wave<1||wave>Waves[Array.IndexOf(Dungeons,dungeon)])throw new InvalidDataException("Unresolved or mismatched battle.");
        PendingDungeon=dungeon;PendingWave=wave;LastDungeon=dungeon;LastWave=wave;Status="running";
        PendingLife=before?["play"]?["life"]?.GetValue<int>()??-1;
        PendingSeason=before?["play"]?["season"]?.GetValue<int>()??0;
        PendingSince=DateTime.UtcNow.Ticks;
    }
    public void Settle(bool won)
    {
        if(PendingDungeon==0)throw new InvalidDataException("No matching battle to settle.");
        Battles++;if(won)Wins++;else Losses++;
        if(won&&PendingWave==Waves[Array.IndexOf(Dungeons,PendingDungeon)])Completed.Add(PendingDungeon);
        PendingDungeon=0;PendingWave=0;
        PendingLife=-1;PendingSeason=0;PendingSince=0;
        if(Target==0)Status="complete";
    }
    // Called only with a newly accepted IbMainInfo response, never with the
    // client cache alone. A journal remembers an uncertainty; it is not a lock.
    internal string Reconcile(JsonObject response,JsonObject? settlement=null)
    {
        if(PendingDungeon==0)return "none";
        var body=SoulResume.Response(response,"soul.IbMainInfo");
        // The native MainInfo callback explicitly clears Play when the server
        // omits PlayInfo. This is the normal no-active-dungeon response.
        var play=body["playInfo"]??new JsonObject();
        int N(JsonNode? n)=>n is null?0:int.Parse(n.ToString());
        int season=N(body["regularSeason"]),dungeon=N(play["dungeonId"]),wave=N(play["stageId"]),life=N(play["life"]);
        if(PendingSeason>0&&season!=PendingSeason)throw new InvalidDataException("赛季已变化，请从第一关开始本赛季挑战。 / Season changed; start this season's challenge.");
        if(dungeon==PendingDungeon&&play["isStageEnter"]?.GetValue<bool>()==true)return "active";
        bool? won=null;
        // Time-bound to this attempt: an old successful receipt must never
        // finish a later replay of an already cleared dungeon.
        if(settlement is not null&&PendingSince>0&&(settlement["AtUtcTicks"]?.GetValue<long>()??0)>=PendingSince
            &&settlement["Accepted"]?.GetValue<bool>()==true&&settlement["Role"]?.ToString()=="soul.IbStageEnd"){
            var end=SoulResume.Response(settlement,"soul.IbStageEnd");
            if(N(end["isDungeonCleared"])==1||N(end["nextStageId"])>PendingWave)won=true;
            else if(PendingLife>0&&N(end["life"])<PendingLife)won=false;
        }
        if(won is null&&dungeon==PendingDungeon){
            if(wave>PendingWave)won=true;
            else if(wave==PendingWave&&PendingLife>0&&life<PendingLife)won=false;
        }
        if(won.HasValue){Settle(won.Value);Status=Target==0?"complete":"running";return won.Value?"won":"lost";}
        // The server now confirms no active battle. Resume from its current
        // progress, without inventing a win/loss or replaying the old command.
        // Historical clears alone cannot prove that THIS attempt was won.
        UnconfirmedBattles++;PendingDungeon=0;PendingWave=0;PendingLife=-1;PendingSeason=0;PendingSince=0;Status="running";
        return "current_progress";
    }
    internal SoulCampaign ResumeFromGame(JsonObject state,JsonObject response,string account,string player,JsonObject? settlement=null)
    {
        if(AccountKey.Length>0&&(AccountKey!=account||PlayerKey!=player))throw new InvalidDataException("Saved challenge belongs to another account.");
        var body=SoulResume.Response(response,"soul.IbMainInfo");
        int N(JsonNode? n)=>n is null?0:int.Parse(n.ToString());
        int season=N(body["regularSeason"]);
        var play=body["playInfo"]??new JsonObject();int active=N(play["dungeonId"]);
        var ids=state["progress"]!["SeasonDungeons"]!.AsArray().Select(n=>N(n)).ToArray();
        bool newSeason=Season>0&&Season!=season||Dungeons.Length>0&&!Dungeons.SequenceEqual(ids)
            ||PendingSeason>0&&PendingSeason!=season;
        var result=newSeason?new SoulCampaign():JsonSerializer.Deserialize<SoulCampaign>(JsonSerializer.Serialize(this))!;
        result.Initialize(state,account,player);result.Season=season;
        if(active!=0&&!result.Dungeons.Contains(active))throw new InvalidDataException("Current dungeon is outside the active season.");
        if(result.PendingDungeon!=0)result.Reconcile(response,settlement);
        // The game owns completion, including manual work after a tool pause.
        // An explicitly active rechallenge takes priority even if its original
        // clear is also in the game's season history.
        var cleared=body["clearedDungeonId"]?.AsArray().Select(n=>N(n)).ToHashSet()??[];
        result.Completed=result.Dungeons.Where(d=>d!=active&&cleared.Contains(d)).ToList();
        result.ResumeDungeon=active;
        if(active>0&&play["isStageEnter"]?.GetValue<bool>()==true&&result.PendingDungeon==0)
            result.BeginBattle(active,N(play["stageId"]),new JsonObject{["play"]=play.DeepClone()});
        result.Status=result.Target==0?"complete":"running";
        return result;
    }
    internal void Save(string folder)
    {
        string path=Path.Combine(folder,"campaign.json");File.WriteAllText(path+".pending",JsonSerializer.Serialize(this));File.Move(path+".pending",path,true);
    }
}
