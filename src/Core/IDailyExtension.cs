using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
namespace Dustweave;

/// <summary>Optional extension; the host owns identity, transport and transaction journals.</summary>
public interface IDailyExtension
{
    int ApiVersion
    {
        get;
    }
    DailyBusinessProof[] ExtendProofs(DailyBusinessProof[] current);
    Task<JsonObject> ExecuteAsync(string stage, DailyWorkflow workflow);
    bool CanResume(string root, string stage, DailyStageFrame frame);
}
public static class DailyExtensionLoader
{
    public static IDailyExtension? Load(DailyPluginInfo info)
    {
        if (!info.Available)
            return null;
        // Recheck the complete manifest immediately before loading managed and native code.
        var verified = DailyPlugin.Inspect(info.Root);
        if (!verified.Available || verified.Fingerprint != info.Fingerprint)
            throw new StageHostException("adapter", "插件文件在加载前发生变化，请重新启动工具。");
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(verified.Root, verified.EntryAssembly));
        var type = assembly.GetType(verified.EntryType, throwOnError: true)!;
        if (Activator.CreateInstance(type, info.Root) is not IDailyExtension extension || extension.ApiVersion != DailyPlugin.ApiVersion)
            throw new StageHostException("adapter", "插件接口与主程序不一致。");
        return extension;
    }
}
