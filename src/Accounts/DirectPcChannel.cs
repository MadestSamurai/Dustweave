using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BD2AccountSessionManager;

// AppManager.TryLoadLauncherSetting supports bd2 (official PC) and bd2_gpg
// (Google Play). A direct launch cannot supply Google's per-launch context.
internal static class DirectPcChannel
{
    internal sealed record Plan(string Path, byte[] Original, byte[]? Replacement);

    internal static Plan Inspect(string executable)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(executable))!, "launcher.settings");
        if (!File.Exists(path)) return new(path, [], null); // Game's default direct-PC path.
        if (new FileInfo(path).Length > 65536)
            throw new SessionManagerException("游戏启动渠道配置异常：launcher.settings 过大，未修改。");
        byte[] original = File.ReadAllBytes(path);
        try
        {
            string text = Encoding.UTF8.GetString(original).TrimStart('\uFEFF');
            var data = JsonNode.Parse(text) as JsonObject ?? throw new JsonException();
            string? service = data["service"]?.GetValue<string>();
            if (string.Equals(service, "bd2", StringComparison.OrdinalIgnoreCase)) return new(path, original, null);
            if (!string.Equals(service, "bd2_gpg", StringComparison.OrdinalIgnoreCase) || data["ver"]?.GetValue<int>() != 1)
                throw new SessionManagerException("无法识别游戏启动渠道或配置版本，未修改 launcher.settings；请先使用官方 PC 启动器确认安装。");
            data["service"] = "bd2";
            return new(path, original, Encoding.UTF8.GetBytes(data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new SessionManagerException("游戏启动渠道配置无法解析，未修改 launcher.settings。");
        }
    }

    internal static void Prepare(string executable, string backupDirectory)
    {
        Plan plan = Inspect(executable);
        if (plan.Replacement == null) return;
        // Keep the exact previous settings, independent of account/session backups.
        Directory.CreateDirectory(backupDirectory);
        string backup = System.IO.Path.Combine(backupDirectory, Convert.ToHexString(SHA256.HashData(plan.Original)) + ".launcher.settings.json");
        if (File.Exists(backup))
        {
            if (!File.ReadAllBytes(backup).AsSpan().SequenceEqual(plan.Original))
                throw new SessionManagerException("启动渠道备份校验失败，未修改游戏配置。");
        }
        else
        {
            using var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(plan.Original); stream.Flush(true);
        }
        string temporary = plan.Path + ".bd2-account-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, plan.Replacement);
            if (!File.ReadAllBytes(plan.Path).AsSpan().SequenceEqual(plan.Original))
                throw new SessionManagerException("启动渠道配置被其他程序改动，请关闭官方启动器后重试。");
            File.Move(temporary, plan.Path, overwrite: true);
            if (!File.ReadAllBytes(plan.Path).AsSpan().SequenceEqual(plan.Replacement))
                throw new SessionManagerException("官方 PC 启动渠道写入后校验失败，已保留原配置备份。");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
