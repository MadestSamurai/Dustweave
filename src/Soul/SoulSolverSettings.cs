namespace Dustweave.Soul;

public sealed record SoulSolverSettings
{
    public const int DefaultCandidates = 3000;
    public static IReadOnlyList<int> CandidateOptions { get; } = Array.AsReadOnly(new[] { 1000, 3000, 10000, 30000 });
    public int WorkerLimit { get; init; } = 3;
    static int DefaultForChapter(int index) => index == 3 ? 10000 : DefaultCandidates;
    public int[] ChapterCandidates { get; init; } = Enumerable.Range(0, 5).Select(DefaultForChapter).ToArray();
    [System.Text.Json.Serialization.JsonIgnore]
    public int Workers => Math.Clamp(WorkerLimit, 1, Math.Min(3, Math.Max(1, Environment.ProcessorCount)));
    public SoulSolverSettings Normalize() => new()
    {
        WorkerLimit = WorkerLimit is >= 1 and <= 3 ? WorkerLimit : 3,
        ChapterCandidates = Enumerable.Range(0, 5).Select(i => ChapterCandidates is not null && i < ChapterCandidates.Length
            && CandidateOptions.Contains(ChapterCandidates[i]) ? ChapterCandidates[i] : DefaultForChapter(i)).ToArray()
    };
    public int ForChapter(int index) => Normalize().ChapterCandidates[Math.Clamp(index, 0, 4)];
    static string PathFor(string root) => Path.Combine(root, "tools", "soul", "settings.json");
    public static SoulSolverSettings Load(string root) => (DailyJson.TryRead<SoulSolverSettings>(PathFor(root)) ?? new()).Normalize();
    public void Save(string root) => DailyJson.Write(PathFor(root), Normalize());
}

// Concurrency controls scheduling, never the number or identity of search branches.
internal static class SoulSearchWork
{
    internal static int Share(int total, int branch, int branches) => total / branches + (branch < total % branches ? 1 : 0);
    internal static int NativeBatchLimit(int performance) => performance switch { 1 => 1, 2 => 4, _ => 0 };
    internal static T[] Run<T>(int branches, int workers, Func<int, CancellationToken, T> compute, CancellationToken cancellation, Action progress)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var slots = new SemaphoreSlim(Math.Clamp(workers, 1, branches));
        var tasks = Enumerable.Range(0, branches).Select(i => Task.Run(async () =>
        {
            await slots.WaitAsync(stop.Token).ConfigureAwait(false);
            try { stop.Token.ThrowIfCancellationRequested(); return compute(i, stop.Token); }
            catch { stop.Cancel(); throw; }
            finally { slots.Release(); }
        }, stop.Token)).ToArray();
        var completion = Task.WhenAll(tasks);
        try { while (!completion.IsCompleted) { Thread.Sleep(100); progress(); } }
        catch {
            stop.Cancel();
            // Drain owned jobs before their semaphore or native contexts can be disposed.
            try { completion.GetAwaiter().GetResult(); } catch { }
            throw;
        }
        return completion.GetAwaiter().GetResult();
    }
}
