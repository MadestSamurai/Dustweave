using System.Windows;

namespace Dustweave.ToolPluginApi;

/// <summary>Native UI only. Game access stays in the per-launch request protocol.
/// The implementation and window live in a separate host child process.</summary>
public interface IToolPluginV2
{
    int UiApiVersion { get; }
    void Probe(string packageDirectory);
    Window CreateWindow(ToolPluginContext context, string[] arguments);
}

public abstract class ToolPluginContext
{
    public const int ApiVersion = 1;
    public abstract string PackageDirectory { get; }
    public abstract string Language { get; }
    public abstract string Appearance { get; }
    public abstract bool IsDark { get; }
    public abstract event EventHandler? PresentationChanged;
    public abstract void PrepareWindow(Window window);
    public abstract bool Confirm(Window owner, Func<string> title, Func<string> message, Func<string> accept);
}
