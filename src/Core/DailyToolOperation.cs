using System.Text.Json.Nodes;
namespace Dustweave;

/// <summary>The host owns the control lease and authenticates the game. Private
/// plugins implement only their declared operations inside the current module.</summary>
public static class DailyToolOperation
{
    public static async Task<JsonObject> RunAsync(string root,IGameHost host,GameInstance game,DailySnapshot expected,string plugin,string operation,JsonObject? payload,CancellationToken token)
    {
        string live=Path.Combine(root,"live"),id=Guid.NewGuid().ToString("N");
        var frame=DailyJson.TryRead<JsonObject>(Path.Combine(live,"snapshot.json"));
        string instance=frame?["Instance"]?.GetValue<string>()??"";
        if(instance.Length==0||frame?["AccountKey"]?.GetValue<string>()!=expected.AccountKey||frame?["PlayerKey"]?.GetValue<string>()!=expected.PlayerKey||
           frame?["ProcessId"]?.GetValue<int>()!=game.ProcessId||frame?["ProcessStartTicks"]?.GetValue<long>()!=game.StartTicks||frame?["AtUtcTicks"]?.GetValue<long>()<DateTime.UtcNow.AddSeconds(-3).Ticks)
            throw new IOException("plugins.tool_identity");
        token.ThrowIfCancellationRequested();
        DailyJson.Write(Path.Combine(live,"plugin-tool-request.json"),new{Id=id,PluginId=plugin,Operation=operation,Payload=payload?.ToJsonString()??"{}",AccountKey=expected.AccountKey,PlayerKey=expected.PlayerKey,Instance=instance,ProcessId=game.ProcessId,ProcessStartTicks=game.StartTicks,ExpiresUtcTicks=DateTime.UtcNow.AddSeconds(75).Ticks});
        // Once sent, retain the lease until a receipt or the bounded deadline.
        // Cancellation must not enable another save while this one may settle.
        var until=DateTime.UtcNow.AddSeconds(75);
        while(DateTime.UtcNow<until)
        {
            if(!host.IsRunning(game))throw new IOException("plugins.tool_identity");
            var response=DailyJson.TryRead<JsonObject>(Path.Combine(live,"plugin-tool-response.json"));
            if(response?["Id"]?.GetValue<string>()==id&&response["Complete"]?.GetValue<bool>()==true)
            {
                var now=host.ReadSnapshot(game);
                if(response["Instance"]?.GetValue<string>()!=instance||
                   !string.IsNullOrEmpty(now?.AccountKey)&&now.AccountKey!=expected.AccountKey||
                   !string.IsNullOrEmpty(now?.PlayerKey)&&now.PlayerKey!=expected.PlayerKey)throw new IOException("plugins.tool_identity");
                if(string.IsNullOrEmpty(now?.AccountKey)||string.IsNullOrEmpty(now?.PlayerKey)){await Task.Delay(150);continue;}
                string error=response["Error"]?.GetValue<string>()??"";if(error.Length>0)throw new IOException(error);
                var result=JsonNode.Parse(response["Result"]!.GetValue<string>())!.AsObject();
                if(result["backup"] is JsonObject backup)
                {
                    if(!DailyPlugin.ValidId(plugin))throw new IOException("plugins.tool_request_invalid");
                    DailyJson.Write(Path.Combine(root,"tool-plugins","backups",plugin,id+".json"),new {atUtc=DateTimeOffset.UtcNow,account=DailyIdentity.Hash(expected.AccountKey+"|"+expected.PlayerKey),operation,backup});
                }
                return result;
            }
            await Task.Delay(150);
        }
        throw new TimeoutException("插件操作未取得回执；请重新读取当前状态，再决定是否重试。未自动重复提交。");
    }
}
