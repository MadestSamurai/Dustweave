extern alias liveUtility;
using System.IO;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave.Desktop;

internal sealed class DailyBargainSession(string root, string account, IAccountSessions? accountSessions = null)
{
    volatile bool stop;
    CancellationTokenSource restartCancellation = new();
    readonly DailyPracticeRestartClock restartClock = new();
    string SettingsPath => Path.Combine(root,"tools","bargain-training","accounts",DailyProfiles.ValidKey(account)?account:throw new InvalidDataException("bargain.account_changed"),"settings.json");
    public int RestartMinutes { get=>restartClock.Minutes; set {var next=new DailyPracticeSettings(DailyPracticeRestartClock.Choices.Contains(value)?value:0);DailyJson.Write(SettingsPath,next);restartClock.Minutes=next.RestartMinutes;} }
    public void LoadSettings()=>restartClock.Minutes=DailyJson.TryRead<DailyPracticeSettings>(SettingsPath)?.RestartMinutes??0;
    public Action<string> Report { get; set; } = _ => { };
    public Action<DailyBargainProgress> Progress { get; set; } = _ => { };
    public Action<DailyPracticeSnapshot> Snapshot { get; set; } = _ => { };
    public void Stop() { stop = true;restartCancellation.Cancel(); }
    public void Begin() { stop = false;restartCancellation.Dispose();restartCancellation=new();restartClock.Reset(); }
    public void RecordFailure(Exception error)
    {
        try { DailyJson.Write(Path.Combine(root, "tools", "bargain-training", "logs", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json"), new { atUtc = DateTimeOffset.UtcNow, account, error = error.ToString() }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    public async Task<(DailyBargainCharacter[] Characters, long Potions, string Player, DailyPracticeTalent[] Talents,DailyPracticeAchievement[] Achievements)> Read()
        => await Execute(false, async w =>
        {
            var e = await w.Evidence(DailyTalentPracticeProof.ReadId);
            var chars = DailyTalentPracticeProof.Characters(e).OrderBy(c => c.Full).ThenBy(c => c.Name).ToArray();
            return (chars, N(DailyTalentPracticeProof.State(e)["Catalyst"]), (await w.Observe()).Daily?.PlayerName ?? "", DailyTalentPracticeProof.Talents(e),DailyPracticeAchievement.Read(DailyTalentPracticeProof.State(e)));
        });
    string QueuePath=>Path.Combine(root,"tools","bargain-training","accounts",DailyProfiles.ValidKey(account)?account:throw new InvalidDataException("bargain.account_changed"),"queue.json");
    public List<DailyPracticeJob> LoadQueue()=>(DailyJson.TryRead<DailyPracticeJob[]>(QueuePath)??[])
        .Where(j=>Guid.TryParseExact(j.Id,"N",out _)&&(j.Kind==16||DailyTalentPracticeProof.FieldKind(j.Kind))&&(j.Achievement||j.Character>0))
        .DistinctBy(j=>j.Id).Select(j=>j.State=="running"?j with{State="pending"}:j).ToList();
    public void SaveQueue(IEnumerable<DailyPracticeJob> jobs)=>DailyJson.Write(QueuePath,jobs.ToArray());
    public async Task TrainQueue(IList<DailyPracticeJob> jobs,Action changed)
    {
        int completedRounds=0;long completedSpent=0;
        await Execute(true,async w=>
        {
            DailyBargainProgress? last=null;
            void OnProgress(DailyBargainProgress p){last=p;Progress(p with{Rounds=completedRounds+p.Rounds,Spent=completedSpent+p.Spent});}
            async Task<string> RunCharacter(DailyBargainCharacter c,DailyPracticeAchievement? goal,bool reset=false)
            {
                Checkpoint(w);
                last=null;
                try
                {
                    if(c.Kind==16){fieldTrainer=null;trainer=new(w,()=>stop,Report,OnProgress){Checkpoint=()=>Checkpoint(w)};return await trainer.Run(c,goal);}
                    trainer=null;fieldTrainer??=new(w,()=>stop,Report,OnProgress);return await fieldTrainer.RunRound(c,goal,reset);
                }
                finally {if(last!=null){completedRounds+=last.Rounds;completedSpent+=last.Spent;}}
            }
            await DailyPracticeQueue.RunByMap(jobs,async()=>{
                var state=await DailyTalentPractice.ReadSnapshot(w);
                Snapshot(state);return state;
            },(c,goal)=>RunCharacter(c,goal),RunCharacter,()=>stop,()=>{SaveQueue(jobs);changed();});
            return true;
        });
    }
    void Checkpoint(DailyWorkflow w)
    {
        if(!restartClock.Due)return;
        restartClock.Checkpoint(w.Business.Records(w.Context).Any(DailyManagedBusiness.Pending));
    }
    public async Task<string> Train(DailyBargainCharacter approved, int maximumRounds = int.MaxValue)
        => await Execute(true, async w =>
        {
            if (approved.Kind != 16)
            {
                fieldTrainer = new(w, () => stop, Report, Progress);
                return await fieldTrainer.Run(approved, maximumRounds);
            }
            trainer = new(w, () => stop, Report, Progress){Checkpoint=()=>Checkpoint(w)};
            return await trainer.Run(approved);
        });
    DailyBargainTraining? trainer;
    DailyTalentPractice? fieldTrainer;
    async Task<T> Execute<T>(bool training, Func<DailyWorkflow, Task<T>> action)
    {
        // Acquired only while reading/running; an idle dialog owns no controller.
        using var lease = DailyToolControl.Acquire(root);
        JsonObject? context = null;
        bool firstRun=true,networkRecovered=false;
        while(true)
        {
            try { return await ExecuteOnce(training, action, first =>
            {
                context = first.Context.DeepClone().AsObject();
                if (training&&firstRun) DailyNetworkRecovery.BeginManualRun(root, first);
                firstRun=false;
            }); }
            catch(DailyPracticeRestartDue) when(training&&context!=null)
            {
                if(stop)throw new StageHostException("stopped","bargain.stopped");
                using var ownAccounts=accountSessions==null?new AccountSessions():null;
                await DailyPracticeRestart.RestartAsync(root,context,accountSessions??ownAccounts!,restartCancellation.Token,Report);
                restartClock.Reset();
            }
            catch (Exception error) when (training && !stop && context != null && error is StageHostException { Kind: "network" })
            {
                RecordFailure(error);
                if(networkRecovered)throw new StageHostException("network", DailyNetworkRecovery.Manual) { HoldQueue = true, Display = DailyText.Of("network.recovery_manual", DailyNetworkRecovery.Manual) };
                networkRecovered=true;
                if (!await DailyNetworkRecovery.RunAsync(root, context, () => stop, Report)) throw;
                restartClock.Reset();
            }
        }
    }
    async Task<T> ExecuteOnce<T>(bool training, Func<DailyWorkflow, Task<T>> action, Action<DailyStageFrame> observed)
    {
        trainer = null; fieldTrainer = null;
        var host = new DailyGameHost(root);
        var game = host.Find() ?? throw new DailyBargainFault("bargain.open_game");
        Report("bargain.connecting");
        using var accounts = new AccountSessions();
        await CurrentGameObservation.ConnectAsync(accounts, host, _ => { }, CancellationToken.None);
        var snapshot = host.ReadSnapshot();
        if (host.Find() != game || snapshot?.AccountKey != account || !DailyIdentityGuard.Ready(snapshot, game, DateTimeOffset.UtcNow))
            throw new DailyBargainFault("bargain.account_changed");
        await DailySuite.ActivateAsync(host, root, "daily", _ => { }, CancellationToken.None);
        await liveUtility::Dustweave.Connection.LiveEntry.RunAsync(["ready"], false,
            args => liveUtility::Dustweave.Connection.LiveEntry.RunAsync(args, false), "talent-practice-evidence-spec.json");
        if (host.Find() != game) throw new DailyBargainFault("bargain.account_changed");
        if (stop) throw new StageHostException("stopped", "bargain.stopped");
        bool StopBetweenRounds() => stop && trainer?.Settling != true && fieldTrainer?.Settling != true;
        var observation = DailyStageObservation.Attach(root, account, StopBetweenRounds);
        var first = await observation.ReadFrameAsync();
        observed(first);
        using var driver = new DailyCommandDriver(root, new DailyPipeMailbox(root, game), observation.ReadFrameAsync, StopBetweenRounds);
        driver.EnsureBound(first.Context);
        var proofs = new[] { DailyBargainProof.Definition, DailyTalentPracticeProof.Definition };
        using var business = new DailyManagedBusiness(root, driver, proofs, StopBetweenRounds);
        var w = new DailyWorkflow(root, AppContext.BaseDirectory, first.Context, driver, business,
            new DailyStageNavigation(observation.ReadFrameAsync, StopBetweenRounds), proofs, StopBetweenRounds,
            (operation, _, request) => operation == "navigation_step"
                ? driver.NavigationAsync(request!["action"]!.AsObject())
                : throw new InvalidOperationException("bargain.unexpected_stage"));
        if (!training) driver.SubmissionGuard = () => throw new InvalidOperationException("bargain.read_only");
        try
        {
            if (training) await new DailyManagedBootstrap(root, driver, observation.ReadFrameAsync, () => stop).BeginAsync(first.Context);
            else driver.Acquire("live");
            return await action(w);
        }
        finally
        {
            if (driver.HasControl)
            {
                try { await driver.HandleAsync(O(("operation", "write"), ("channel", "live"), ("name", "pause"), ("value", ""))); }
                catch (Exception e) when (e is IOException or StageHostException or TimeoutException) { }
            }
        }
    }
}
