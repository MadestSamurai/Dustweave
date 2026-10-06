using System.Reflection;
namespace Dustweave.Desktop;
// Product build label is independent of the stable game protocol and integrated tool versions.
internal static class DailyProductVersion
{
    internal static string Current => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? DailyIdentity.Version;
}
