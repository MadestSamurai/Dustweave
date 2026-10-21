using Dustweave;

if (args.Length == 2 && args[0] == "--repair-ui-smoke")
{
    try
    {
        var value = RepairDialog.Run("Dustweave repair · isolated UI check", async (progress, token) =>
        {
            for (int i = 0; i <= 4; i++) { progress.Report(new("checking", i, 4)); await Task.Delay(150, token); }
            return 4;
        });
        File.WriteAllText(args[1], value == 4 ? "passed" : "failed");
        return value == 4 ? 0 : 1;
    }
    catch (Exception error) { File.WriteAllText(args[1], error.ToString()); return 1; }
}
if (args.Length == 2 && args[0] == "--repair-install") return StandaloneRepair.Run(args[1]);
if (args.Length == 0 && new DirectoryInfo(AppContext.BaseDirectory) is { Name: "support", Parent: { Name: "app", Parent: { } installation } })
    return StandaloneRepair.Run(installation.FullName);
if (args.Length == 2 && args[0] is "--apply-update" or "--recover-update")
    return DailyUpdateEngine.Run(args[1], args[0] == "--recover-update");
if (args.Length == 2 && args[0] == "--identity")
{
    File.WriteAllText(args[1], "Dustweave independent updater 1\n");
    return 0;
}
return 2;
