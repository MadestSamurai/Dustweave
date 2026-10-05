namespace BD2Sichuan;

// Independent of WPF rendering and solving. Stop is serialized with renewal so no late timer can re-arm it.
public sealed class SichuanControlLink : IDisposable
{
    private readonly SichuanLiveFiles files;
    private readonly object sync=new();
    private readonly Timer timer;
    private bool enabled,disposed;
    private string owner="";
    public string Error {get;private set;}="";
    public SichuanControlLink(SichuanLiveFiles files){this.files=files;timer=new(_=>Pulse(),null,250,250);}
    public void Enable(bool value)
    {
        lock(sync){enabled=value;if(!value)Stop("capture_disabled");files.Lease(value);}
    }
    public void Start(SichuanRunCommand command)
    {
        lock(sync)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SichuanControlLink));
            enabled=true;files.Lease(true);owner=command.OwnerId;
            try{files.ExecutionLease(owner,true);files.Submit(command);Error="";}
            catch{Stop("run_submit_failed");throw;}
        }
    }
    public void SetInterval(int value)
    {
        if(!SichuanRunEngine.ValidInterval(value))throw new ArgumentOutOfRangeException(nameof(value));
        lock(sync){files.IntervalMilliseconds=value;if(owner.Length>0)files.ExecutionLease(owner,true);}
    }
    public void Stop(string reason)
    {
        lock(sync){var previous=owner;owner="";if(previous.Length>0)try{files.ExecutionLease(previous,false,reason);}catch(Exception e){Error=e.Message;}}
    }
    private void Pulse()
    {
        lock(sync)
        {
            if(disposed||!enabled)return;
            try{files.Lease(true);if(owner.Length>0)files.ExecutionLease(owner,true);Error="";}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){Error=e.Message;}
        }
    }
    public void Dispose()
    {
        lock(sync){if(disposed)return;Stop("window_closed");disposed=true;enabled=false;timer.Dispose();try{files.Lease(false);}catch{}}
    }
}
