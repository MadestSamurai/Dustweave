using System.IO;
namespace BD2Daily.Desktop;
// Isolated fixtures for packaged UI smoke checks. Never constructs the real session service or injector.
public sealed class DemoEnvironment : IAccountSessions, IGameHost
{
    public readonly List<DailyAccount> Accounts = new(); public readonly List<string> Calls = new();
    public string CurrentKey; public GameInstance? Game; public string? ForcedKey; public string? FailConnectKey;
    public bool StartupRecoverySucceeds; public int StartupRecoveryCalls;
    public Task<bool> RecoverStartupAsync(string account, Action<string> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        StartupRecoveryCalls++;
        if (StartupRecoverySucceeds)
            TitleBlock = "";
        return Task.FromResult(StartupRecoverySucceeds);
    }
    public bool TitleVisible; public bool HoldTitle; public bool TitleClickEnabled = true; public int TitleClicks; public string TitleBlock = "";
    public bool HandleGuildCommands; public bool GuildGrant = true; public bool Ready = true; public bool DenyControl; private readonly string root; private long sequence;
    public DemoEnvironment(string root)
    {
        this.root = root;
        for (int i = 1; i <= 3; i++)
            Accounts.Add(new(i, new[] { "主账号", "测试账号", "第三个账号" }[i - 1], DailyIdentity.MemberKey((1000 + i).ToString()), $"***{1000 + i}", true, i == 1, ""));
        CurrentKey = Accounts[0].AccountKey;
        Game = new(100, DateTimeOffset.UtcNow.AddSeconds(-30).UtcTicks, "demo-game.exe");
    }
    public DailyAccountCatalog Read() => new(Accounts.Select(a => a with { IsCurrent = a.AccountKey == CurrentKey }).ToArray(), CurrentKey, Accounts.FirstOrDefault(a => a.AccountKey == CurrentKey)?.SlotNumber, true, Game != null, false, true);
    public void EnsureControl()
    {
        Calls.Add("control");
        if (DenyControl)
            throw new InvalidOperationException("旧账号工具正在运行");
    }
    public void ActivateAndLaunch(int slot, string expectedKey)
    {
        Calls.Add("launch:" + slot);
        var a = Accounts.Single(a => a.SlotNumber == slot);
        if (a.AccountKey != expectedKey)
            throw new Exception("slot changed");
        CurrentKey = expectedKey;
        Game = new(100 + slot, DateTimeOffset.UtcNow.UtcTicks, "demo-game.exe");
    }
    public void Save(int slot, string name, string expectedKey) { if (expectedKey != CurrentKey) throw new InvalidOperationException("Account changed"); Calls.Add("save:" + slot); }
    public void Rename(int slot, string name, string expectedKey) => Calls.Add("rename:" + slot);
    public void Delete(int slot, string expectedKey) => Calls.Add("delete:" + slot);
    public void LoginNew() => Calls.Add("new"); public void Recover() => Calls.Add("recover");
    public GameInstance? Find() => Game;
    public Task CloseAsync(GameInstance instance, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Calls.Add("close");
        Game = null;
        return Task.CompletedTask;
    }
    public Task ConnectAsync(GameInstance instance, Action<string> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Calls.Add("connect");
        if (CurrentKey == FailConnectKey)
            throw new InvalidOperationException("test connection failed");
        return Task.CompletedTask;
    }
    public DailyRuntimeStatus? ReadStatus() => null;
    public DailySnapshot? ReadSnapshot()
    {
        if (Game == null || !Ready)
            return null;
        var key = ForcedKey ?? CurrentKey;
        var s = new DailySnapshot { ProcessId = Game.ProcessId, ProcessStartTicks = Game.StartTicks, AccountKey = key, PlayerKey = DailyIdentity.PlayerKey(key, 500), PlayerName = Accounts.FirstOrDefault(a => a.AccountKey == key)?.Name + "的角色", InstanceId = "fixture-" + Game.ProcessId, Sequence = ++sequence, FrameUtcTicks = DateTimeOffset.UtcNow.UtcTicks, State = "identified", Scene = "演示主城" };
        s.Guild = new GuildObservation { Supported = true, InGuild = true, MenuVisible = true, GuildName = "演示公会", GuildKey = DailyIdentity.Hash("fixture-guild"), ServerKey = DailyIdentity.Hash("fixture-server"), ServerTicks = DateTime.UtcNow.Ticks, ResetTicks = DateTime.UtcNow.Date.AddDays(1).Ticks, CycleKey = DateTime.UtcNow.Date.AddDays(1).Ticks.ToString(), ClientMvid = "fixture" };
        s.Capabilities = ["account.identity", "guild.observe", "guild.single"];
        var lease = DailyJson.TryRead<DailyLease>(Path.Combine(root, "lease.json"));
        if (lease != null && lease.Matches(s, DateTimeOffset.UtcNow.UtcTicks))
            s.LeaseOwner = lease.Owner;
        s.Startup = new StartupObservation { Supported = true, Visible = TitleVisible, Ready = TitleVisible, ClickEnabled = TitleClickEnabled, TitleInstanceId = 88, Attempted = TitleClicks > 0, Stage = TitleVisible ? "IntroWorkFinished" : "", BlockReason = TitleBlock };
        if (TitleVisible)
        {
            s.State = "waiting_start";
            var permit = DailyJson.TryRead<StartupPermit>(Path.Combine(root, "startup-permit.json"));
            if (StartupPolicy.Decide(s, permit!, DateTime.UtcNow.Ticks) == "click_start")
            {
                TitleClicks++;
                Calls.Add("startup.click");
                s.Startup.Attempted = true;
                if (!HoldTitle)
                    TitleVisible = false;
            }
        }
        if (HandleGuildCommands)
        {
            var path = Path.Combine(root, "guild-command.json");
            var c = GuildStore.Read<GuildCommand>(path);
            if (c != null)
            {
                var store = new GuildStore(root);
                var r = store.Receipt(c.Id)!;
                File.Move(path, path + "." + c.Id + ".fixture-consumed");
                if (GuildPolicy.Matches(c, s, lease!, DateTime.UtcNow.Ticks) && GuildPolicy.Gate(s, store.Prior(s, c.Id), DateTime.UtcNow.Ticks) == "")
                {
                    Calls.Add("guild.entry");
                    r.MayHaveDispatched = true;
                    r.RequestCount = 1;
                    r.ResponseSeen = true;
                    r.CallbackMatched = true;
                    r.AttendanceGranted = GuildGrant;
                    r.MemberMatched = true;
                    r.CacheMatched = true;
                    r.State = GuildPolicy.Outcome(r);
                    r.ReturnedHome = true;
                    r.Message = "隔离替身响应";
                }
                else
                {
                    r.State = "rejected";
                    r.Message = "隔离替身拒绝";
                }
                store.Save(r);
            }
        }
        return s;
    }
}
