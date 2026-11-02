using System.Text.Json.Nodes;
using Dustweave.Soul;
internal static class SoulCases
{
    internal static void Run(string root,List<string> cases)
    {
        SoulChecks.Run(Path.Combine(root,"soul"));
        cases.Add("soul: five full dungeons and 55 wins independent of historical clears");
        cases.Add("soul: failed wave retains its dungeon and next transition");
        cases.Add("soul: repeated defeated-dungeon retries preserve clears, resume once and advance only after clearing");
        cases.Add("soul: account/player binding and unresolved battle prevent replay");
        cases.Add("soul: layout validity and stale economic state guards");
        cases.Add("soul: duplicate/missing owned views, duplicate shop slots and orphan shop views reject battle start");
        cases.Add("soul: bounded dispatch recovery distinguishes explicit rejection from unknown outcome");
        cases.Add("soul: growth route memory cannot cross dungeon or acquired inventory identity");
        cases.Add("soul: finite-life continuation matches exhaustive outcomes, timing and terminal cases");
        cases.Add("soul: weak boards continue funded shopping beyond two refreshes and preserve measured improvements");
        cases.Add("soul: existing dungeon resumes from matching read receipts without requiring a fresh dungeon transaction");
        cases.Add("soul: confirmed native sales synchronize the stale wallet locally with exact item identity and actual refund; invalid receipts cannot synchronize or replay a sale");
        cases.Add("soul: routine reads keep one current raw/parsed pair and one copy per configuration; action checkpoints remain complete and immutable");
        var session=new SoulSession(Path.Combine(root,"soul-unused"));
        session.Pause();session.Pause();session.StopAsync().GetAwaiter().GetResult();
        if(session.Running||session.Status.State!="ready")throw new Exception("Idle stop must be harmless and cannot connect to the game.");
        cases.Add("soul: repeated idle stop never starts or connects to the game");
    }
}
