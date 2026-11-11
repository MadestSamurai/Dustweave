using System.Xml;
using System.Xml.Linq;

namespace Dustweave;

public sealed record DailySchedulePathResult(string State, string Previous = "", string Current = "", string Error = "")
{
    public bool NeedsAttention => State is "missing" or "disabled" or "review" or "failed";
}

/// <summary>Rebind an existing, enabled task without recreating its trigger or changing the saved plan.</summary>
public static class DailySchedulePathRepair
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static DailySchedulePathResult Repair(DailySchedulePlan plan, string executable, string userSid,
        Func<string?> read, Action<string, string> update)
    {
        if (!plan.Enabled || !plan.IsDaily) return new("skipped");
        string previous = "";
        try
        {
            executable = Path.GetFullPath(executable);
            if (!string.Equals(Path.GetFileName(executable), "Dustweave.exe", StringComparison.OrdinalIgnoreCase))
                return new("review", Current: executable);
            var original = read();
            if (original == null) return new("missing", Current: executable);
            var document = Parse(original);
            var task = document.Root!;
            if (task.Element(Ns + "Settings")?.Element(Ns + "Enabled") is { } enabled && !XmlConvert.ToBoolean(enabled.Value))
                return new("disabled", Current: executable);
            var principals = task.Element(Ns + "Principals")?.Elements().ToArray();
            var actions = task.Element(Ns + "Actions")?.Elements().ToArray();
            if (principals is not { Length: 1 } || principals[0].Element(Ns + "UserId")?.Value != userSid ||
                principals[0].Element(Ns + "LogonType")?.Value != "InteractiveToken" ||
                actions is not { Length: 1 } || actions[0].Name != Ns + "Exec" ||
                actions[0].Element(Ns + "Arguments")?.Value.Trim() != "--scheduled")
                return new("review", Current: executable);
            var action = actions[0];
            previous = action.Element(Ns + "Command")?.Value ?? "";
            if (!Path.IsPathFullyQualified(previous) || !string.Equals(Path.GetFileName(previous), "Dustweave.exe", StringComparison.OrdinalIgnoreCase))
                return new("review", previous, executable);
            string directory = Path.GetDirectoryName(executable)!;
            if (SamePath(previous, executable) && SamePath(action.Element(Ns + "WorkingDirectory")?.Value, directory))
                return new("unchanged", previous, executable);
            action.SetElementValue(Ns + "Command", executable);
            action.SetElementValue(Ns + "WorkingDirectory", directory);
            // The adapter compares the complete old XML again immediately before TASK_UPDATE.
            // A task deleted/edited in Windows must not be silently recreated/overwritten.
            update(original, document.ToString(SaveOptions.DisableFormatting));
            var after = read();
            var verified = after == null ? null : Parse(after).Root?.Element(Ns + "Actions")?.Element(Ns + "Exec");
            if (verified == null || !SamePath(verified.Element(Ns + "Command")?.Value, executable) ||
                !SamePath(verified.Element(Ns + "WorkingDirectory")?.Value, directory))
                return new("failed", previous, executable, "Windows did not retain the requested launch path.");
            return new("repaired", previous, executable);
        }
        catch (OperationCanceledException) { return new("skipped"); }
        catch (Exception error) { return new("failed", previous, executable, error.ToString()[..Math.Min(error.ToString().Length, 16000)]); }
    }

    private static XDocument Parse(string xml)
    {
        if (xml.Length > 262144) throw new InvalidDataException("Scheduled task definition is too large.");
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != Ns + "Task") throw new InvalidDataException("Unexpected scheduled task definition.");
        return document;
    }

    private static bool SamePath(string? left, string right) => !string.IsNullOrWhiteSpace(left) && Path.IsPathFullyQualified(left) &&
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
}
