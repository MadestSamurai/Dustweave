using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyToolInventory
{
    public static async Task<JsonObject> ReadAsync(string root, IGameHost host, GameInstance game, DailySnapshot expected, CancellationToken token)
    {
        var request = new ToolInventoryRequest { Id = Guid.NewGuid().ToString("N"), ProcessId = game.ProcessId, ProcessStartTicks = game.StartTicks,
            InstanceId = expected.InstanceId, AccountKey = expected.AccountKey, PlayerKey = expected.PlayerKey, ExpiresUtcTicks = DateTime.UtcNow.AddSeconds(20).Ticks };
        if (expected.State != "identified" || string.IsNullOrEmpty(expected.AccountKey) || string.IsNullOrEmpty(expected.PlayerKey)) throw new IOException("plugins.tool_identity");
        DailyJson.Write(Path.Combine(root, "tool-inventory-request.json"), request);
        try
        {
            for (int i = 0; i < 100; i++)
            {
                token.ThrowIfCancellationRequested();
                if (!host.IsRunning(game)) throw new IOException("plugins.tool_identity");
                var response = DailyJson.TryRead<ToolInventoryResponse>(Path.Combine(root, "tool-inventory-response.json"));
                if (response?.Id == request.Id)
                {
                    var now = host.ReadSnapshot(game);
                    if (response.AccountKey != expected.AccountKey || response.PlayerKey != expected.PlayerKey || response.InstanceId != expected.InstanceId ||
                        response.ProcessId != game.ProcessId || response.ProcessStartTicks != game.StartTicks || now?.State != "identified" || now.AccountKey != expected.AccountKey || now.PlayerKey != expected.PlayerKey || now.InstanceId != expected.InstanceId)
                        throw new IOException("plugins.tool_identity");
                    if (response.Error.Length != 0) throw new IOException("plugins.tool_inventory_failed", new IOException(response.Error));
                    var data = JsonNode.Parse(response.Json)?.AsObject() ?? throw new InvalidDataException("plugins.tool_inventory_failed");
                    return new JsonObject { ["inventory"] = data, ["accountName"] = response.PlayerName,
                        ["accountKey"] = DailyIdentity.Hash(expected.AccountKey + "|" + expected.PlayerKey), ["capturedAt"] = DateTimeOffset.UtcNow.ToString("O") };
                }
                await Task.Delay(200, token);
            }
            throw new IOException("plugins.tool_inventory_timeout");
        }
        finally
        {
            request.ExpiresUtcTicks = 0; DailyJson.Write(Path.Combine(root, "tool-inventory-request.json"), request);
            // The runtime owns the response and clears it after this request expires.
        }
    }
}
