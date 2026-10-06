using Dustweave;
using System.Text.Json;
static class StorageCases
{
    public static async Task Run(string output, List<string> cases)
    {
        string folder = Path.Combine(output, "storage");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "result.json");
        void Check(bool ok, string name)
        {
            if (!ok)
                throw new Exception(name);
            cases.Add(name);
        }
        DailyJson.Write(path, new
        {
            state = "running"
        });
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var writer = Task.Run(() => DailyJson.Write(path, new { state = "completed" }));
            await Task.Delay(160);
            Check(!writer.IsCompleted, "journal write waits for non-delete-sharing reader");
            reader.Dispose();
            await writer;
        }
        using (var doc = DailyJson.TryRead<JsonDocument>(path))
            Check(doc?.RootElement.GetProperty("state").GetString() == "completed", "journal replacement commits after sharing lock releases");
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            DailyJson.Write(path, new
            {
                state = "latest"
            });
            using var old = JsonDocument.Parse(reader);
            Check(old.RootElement.GetProperty("state").GetString() == "completed", "atomic replace succeeds with shared reader still open");
        }
        var poll = Task.Run(() => { for (int i = 0; i < 300; i++) { using var value = DailyJson.TryRead<JsonDocument>(path); if (value == null) throw new Exception("Reader saw torn/missing record"); } });
        for (int i = 0; i < 80; i++)
            DailyJson.Write(path, new
            {
                sequence = i
            });
        await poll;
        Check(Directory.GetFiles(folder, "*.tmp").Length == 0, "concurrent progress reads and atomic writes leave no temporary files");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false;
            try
            {
                await Task.Run(() => DailyJson.Write(path, new { sequence = 999 }));
            }
            catch (IOException) { failed = true; }
            Check(failed, "persistent journal lock raises bounded failure");
        }
        using (var doc = DailyJson.TryRead<JsonDocument>(path))
            Check(doc?.RootElement.GetProperty("sequence").GetInt32() == 79, "persistent commit failure preserves previous valid record");
        var pending = Directory.GetFiles(folder, "*.tmp");
        using (var doc = DailyJson.TryRead<JsonDocument>(pending.Single()))
            Check(doc?.RootElement.GetProperty("sequence").GetInt32() == 999, "persistent commit failure retains flushed result for diagnosis");
    }
}
