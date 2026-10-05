using System.Diagnostics;
namespace BD2SecretVision;

// The runtime still owns the six-second fail-safe. Recovery is permitted only
// while the previous lease is valid; a terminated pump never silently resumes.
public sealed class ControlHeartbeat(Action<DateTimeOffset> renew, Action revoke, Action<string,Exception> diagnose) : IDisposable
{
    private readonly CancellationTokenSource stop=new(), failed=new();
    private Task? pump;
    private Exception? failure;
    private long lastPublished;
    public Exception? Failure => Volatile.Read(ref failure);
    public CancellationToken FailureToken => failed.Token;
    public void Start()
    {
        if(pump!=null) throw new InvalidOperationException("heartbeat-already-started");
        Renew(); // No game command is sent until the first publication succeeds.
        pump=Task.Run(Pump);
    }
    private void Renew()
    {
        long began=Stopwatch.GetTimestamp();
        renew(DateTimeOffset.UtcNow.AddSeconds(6));
        lastPublished=began; // Publication time counts against the lease, too.
    }
    private void Fail(Exception error)
    {
        Interlocked.CompareExchange(ref failure,error,null);
        diagnose("heartbeat-failed",error);
        failed.Cancel();
    }
    private async Task Pump()
    {
        Exception? firstFailure=null;
        int delay=1000;
        try {
            while(true) {
                await Task.Delay(delay,stop.Token);
                if(Stopwatch.GetElapsedTime(lastPublished)>=TimeSpan.FromSeconds(5)) {
                    Fail(firstFailure??new IOException("control-heartbeat-expired")); return;
                }
                try {
                    Renew();
                    if(firstFailure!=null) diagnose("heartbeat-recovered",firstFailure);
                    firstFailure=null; delay=1000;
                } catch(Exception error) when(Files.IsFileError(error)) {
                    if(firstFailure==null) {firstFailure=error;diagnose("heartbeat-retrying",error);}
                    delay=200;
                }
            }
        } catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception error) {Fail(error);}
    }
    public async Task<Exception?> Stop()
    {
        stop.Cancel();
        if(pump!=null) await pump;
        try {revoke();return null;}
        catch(Exception error) {diagnose("heartbeat-revoke-failed",error);return error;}
    }
    public void Dispose(){stop.Dispose();failed.Dispose();}
}
