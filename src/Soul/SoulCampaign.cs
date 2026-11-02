using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dustweave.Soul;

// Progress belongs to this invocation, never to the account's historical clears.
public sealed class SoulCampaign
{
    public string AccountKey { get; set; } = "";
    public string PlayerKey { get; set; } = "";
    public string StartedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public int[] Dungeons { get; set; } = [];
    public int[] Waves { get; set; } = [];
    public List<int> Completed { get; set; } = [];
    public int Target => Dungeons.FirstOrDefault(d=>!Completed.Contains(d));
    public int Battles { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int PendingDungeon { get; set; }
    public int PendingWave { get; set; }
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
        if(Dungeons.Length==0){Dungeons=Ids("SeasonDungeons");Waves=Ids("WaveCounts");AccountKey=account;PlayerKey=player;}
        if(AccountKey!=account||PlayerKey!=player)throw new InvalidDataException("Challenge belongs to another account.");
        if(Dungeons.Length!=5||Waves.Length!=5||Dungeons.Distinct().Count()!=5||Waves.Any(n=>n<=0))throw new InvalidDataException("Five-dungeon season is unavailable.");
        var ids=Ids("SeasonDungeons");if(ids.Length>0&&!ids.SequenceEqual(Dungeons))throw new InvalidDataException("Season changed during challenge.");
    }
    public void BeginBattle(int dungeon,int wave)
    {
        if(PendingDungeon!=0||dungeon!=Target||wave<1||wave>Waves[Array.IndexOf(Dungeons,dungeon)])throw new InvalidDataException("Unresolved or mismatched battle.");
        PendingDungeon=dungeon;PendingWave=wave;LastDungeon=dungeon;LastWave=wave;Status="running";
    }
    public void Settle(bool won)
    {
        if(PendingDungeon==0)throw new InvalidDataException("No matching battle to settle.");
        Battles++;if(won)Wins++;else Losses++;
        if(won&&PendingWave==Waves[Array.IndexOf(Dungeons,PendingDungeon)])Completed.Add(PendingDungeon);
        PendingDungeon=0;PendingWave=0;
        if(Target==0)Status="complete";
    }
    internal void Save(string folder)
    {
        string path=Path.Combine(folder,"campaign.json");File.WriteAllText(path+".pending",JsonSerializer.Serialize(this));File.Move(path+".pending",path,true);
    }
}
