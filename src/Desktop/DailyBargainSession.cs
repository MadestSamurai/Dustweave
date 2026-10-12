extern alias liveUtility;
using System.IO;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave.Desktop;

internal sealed class DailyBargainSession(string root, string account)
{
    volatile bool stop;
    public Action<string> Report { get; set; } = _ => { };
    public Action<DailyBargainProgress> Progress { get; set; } = _ => { };
    public void Stop() => stop = true;
    public void Begin() => stop = false;
    public void RecordFailure(Exception error)
    {
        try { DailyJson.Write(Path.Combine(root, "tools", "bargain-training", "logs", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json"), new { atUtc = DateTimeOffset.UtcNow, account, error = error.ToString() }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    public async Task<(DailyBargainCharacter[] Characters, long Potions, string Player)> Read()
        => await Execute(false, async w =>
        {
            var e = await w.Evidence(DailyBargainProof.ReadId);
            var chars = DailyBargainProof.Characters(e).OrderBy(c => c.Full).ThenBy(c => c.Name).ToArray();
            return (chars, N(DailyBargainProof.State(e)["Catalyst"]), (await w.Observe()).Daily?.PlayerName ?? "");
        });
    public async Task<string> Train(DailyBargainCharacter approved)
        => await Execute(true, async w =>
        {
            trainer = new(w, () => stop, Report, Progress);
            return await trainer.Run(approved);
        });
    DailyBargainTraining? trainer;
    async Task<T> Execute<T>(bool training, Func<DailyWorkflow, Task<T>> action)
    {
        trainer = null;
        // Acquired only while reading/running; an idle dialog owns no controller.
        using var lease = DailyToolControl.Acquire(root);
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
            args => liveUtility::Dustweave.Connection.LiveEntry.RunAsync(args, false), "bargain-evidence-spec.json");
        if (host.Find() != game) throw new DailyBargainFault("bargain.account_changed");
        if (stop) throw new StageHostException("stopped", "bargain.stopped");
        var observation = DailyStageObservation.Attach(root, account, () => false);
        var first = await observation.ReadFrameAsync();
        using var driver = new DailyCommandDriver(root, new DailyPipeMailbox(root, game), observation.ReadFrameAsync, () => false);
        driver.EnsureBound(first.Context);
        var proofs = new[] { DailyBargainProof.Definition };
        var business = new DailyManagedBusiness(root, driver, proofs, () => false);
        bool StopBetweenRounds() => stop && trainer?.Settling != true;
        var w = new DailyWorkflow(root, AppContext.BaseDirectory, first.Context, driver, business,
            new DailyStageNavigation(observation.ReadFrameAsync, StopBetweenRounds), proofs, StopBetweenRounds,
            (_, _, _) => throw new InvalidOperationException("bargain.unexpected_stage"));
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
