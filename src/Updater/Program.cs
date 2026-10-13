using Dustweave;

if (args.Length == 2 && args[0] is "--apply-update" or "--recover-update")
    return DailyUpdateEngine.Run(args[1], args[0] == "--recover-update");
if (args.Length == 2 && args[0] == "--identity")
{
    File.WriteAllText(args[1], "Dustweave independent updater 1\n");
    return 0;
}
return 2;
