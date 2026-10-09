using System.Diagnostics;
using Dustweave;
using Dustweave.Accounts;

internal static class DesktopLaunchCases
{
    public static async Task Child(string file)
    {
        DailyJson.Write(file, DesktopGameLaunch.ReadToken(Environment.ProcessId));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(file + ".exit")) await Task.Delay(20, timeout.Token);
    }

    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); cases.Add("desktop launch: " + message); }
        void Reject(Action action, string message)
        {
            try { action(); } catch (SessionManagerException) { Check(true, message); return; }
            throw new Exception(message);
        }
        var limited = new DesktopGameLaunch.TokenState(false, "synthetic-user", 1);
        var elevated = limited with { Elevated = true };
        foreach (var caller in new[] { limited, elevated })
        foreach (var shell in new[] { limited, elevated })
        {
            var context = new DesktopGameLaunch.Context(42, caller, shell);
            Check(DesktopGameLaunch.Reject(caller, shell) == null, "same identity accepted regardless of desktop elevation");
            Check(DesktopGameLaunch.UseCallerToken(context) == shell.Elevated, "uses inherited token only when desktop cannot supply a limited one");
        }
        Check(DesktopGameLaunch.Reject(elevated, limited) == null && !DesktopGameLaunch.UseCallerToken(new(42, elevated, limited)), "elevated tool retains the ordinary desktop de-elevation route");
        foreach (var invalid in new[] { elevated with { User = "other-user" }, elevated with { Session = 2 } })
        {
            Check(DesktopGameLaunch.Reject(limited, invalid) != null, "different user or logon session remains rejected");
            Reject(() => DesktopGameLaunch.ValidateChild(limited, invalid), "child identity mismatch stops follow-up operations");
            Reject(() => DesktopGameLaunch.LaunchInherited(new(42, limited, invalid), "never-start.exe", "", output, true), "rejects mismatched context before process creation");
        }
        // Exercise the actual inherited-token path with this harmless test runner.
        // Simulate an elevated desktop; do not elevate or start the game.
        string folder = Path.Combine(output, "desktop-launch"); Directory.CreateDirectory(folder);
        string result = Path.Combine(folder, "child.json");
        var self = DesktopGameLaunch.ReadToken(Environment.ProcessId);
        int pid = DesktopGameLaunch.LaunchInherited(new(Environment.ProcessId, self, self with { Elevated = true }),
            Environment.ProcessPath!, "--desktop-launch-child " + DailyDesktopLaunch.Quote(result), Directory.GetCurrentDirectory(), true);
        using var child = Process.GetProcessById(pid);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(result)) await Task.Delay(20, timeout.Token);
            var actual = DailyJson.TryRead<DesktopGameLaunch.TokenState>(result)!;
            Check(actual.User == self.User && actual.Session == self.Session && actual.Elevated == self.Elevated,
                "actual child preserves caller user, logon session and elevation without changing desktop settings");
        }
        finally { File.WriteAllText(result + ".exit", "done"); }
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
        Check(child.ExitCode == 0, "isolated child exited normally without touching a game process");
    }
}
