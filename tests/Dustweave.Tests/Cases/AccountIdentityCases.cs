using System.Text;
using System.Text.Json;
using Dustweave.Accounts;
using Dustweave;

internal static class AccountIdentityCases
{
    internal static void Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("account identity: " + name); }
        void Reject(Action action, string name) { try { action(); } catch (InvalidOperationException) { Check(true, name); return; } catch (SessionManagerException) { Check(true, name); return; } throw new Exception("Not rejected: " + name); }
        var key = DailyIdentity.MemberKey("1000001");
        var other = DailyIdentity.MemberKey("2000001");
        DailyAccount A(int slot, string k, string name = "Original") => new(slot, name, k, "ID …000001", true, false, "");
        var raw = new DailyAccountCatalog([A(1,key),A(2,other),A(13,key,"Accidental copy")], key, 13, true, false, false, true);
        var catalog = DailyAccountIdentity.Normalize(raw);
        Check(catalog.Accounts.Count == 2 && catalog.CurrentSlot == 1, "duplicate complete identity keeps original slot");
        Check(catalog.Accounts.Count(a => a.IsCurrent) == 1 && catalog.Accounts[0].IsCurrent, "current badge uses complete identity");
        Check(catalog.OccupiedSlots.SequenceEqual(new[]{1,2,13}), "hidden duplicate slot remains reserved");
        Check(catalog.Accounts.Any(a => a.AccountKey == other), "same masked suffix does not merge different members");
        Check(DailyAccountIdentity.SavePlan(catalog) == new DailyAccountSavePlan(1,key,"Original",true), "save updates original name and slot");
        Check(DailyAccountIdentity.SavePlan(catalog with { CurrentKey = DailyIdentity.MemberKey("3") }).SlotNumber == 3, "new member uses genuinely empty slot");
        var full = catalog with { OccupiedSlots = Enumerable.Range(1,100).ToArray() };
        Check(DailyAccountIdentity.SavePlan(full).SlotNumber == 1, "full vault still permits updating existing member");
        Reject(()=>DailyAccountIdentity.SavePlan(full with {CurrentKey=DailyIdentity.MemberKey("3")}),"full vault blocks new account");
        Reject(()=>DailyAccountIdentity.SavePlan(catalog with {CurrentKey=""}),"unknown identity is not saved");
        Reject(()=>DailyAccountIdentity.SavePlan(catalog with {SessionComplete=false}),"partial session is not saved");
        Reject(()=>DailyAccountIdentity.SavePlan(catalog with {GameRunning=true}),"running game credentials are not captured");
        Reject(()=>DailyAccountIdentity.SavePlan(catalog with {StarterRunning=true}),"running starter credentials are not captured");
        Check(DailyAccountIdentity.SameCatalog(catalog,DailyAccountIdentity.Normalize(raw)),"unchanged directory does not reset edits");
        Check(!DailyAccountIdentity.SameCatalog(catalog,DailyAccountIdentity.Normalize(raw with {CurrentKey=other})),"external account switch refreshes catalog");
        Check(!DailyAccountIdentity.SameCatalog(catalog,catalog with {GameRunning=true}),"game lifecycle refreshes account display");
        Check(!DailyAccountIdentity.SameCatalog(catalog,catalog with {Accounts=[A(1,key,"Renamed"),A(2,other)]}),"renamed account refreshes display");
        var profile=new DailyAccountProfile {AccountKey=key,SlotNumber=1,Order=4,Selected=true,PlayerName="Original player"};
        var damaged=catalog with {Accounts=[A(1,"","Unreadable") with {Valid=false},A(2,other)],OccupiedSlots=Enumerable.Range(1,100).ToArray()};
        var hinted=DailyAccountIdentity.WithRecoveryHints(damaged,[profile]);
        var repair=DailyAccountIdentity.SavePlan(hinted);
        Check(repair.SlotNumber==1 && repair.ReplacesUnreadable && repair.AccountKey==key && repair.Name==profile.PlayerName,"reinstall recovery restores the original slot even when the vault is full");
        Check(!DailyAccountIdentity.Normalize(hinted).Accounts[0].Valid && !DailyAccountIdentity.Normalize(hinted).Accounts[0].IsCurrent,"profile hints never authorize launching unreadable credentials");
        Check(DailyAccountIdentity.WithRecoveryHints(damaged,[profile,new DailyAccountProfile{AccountKey=other,SlotNumber=1}]).Accounts[0].AccountKey=="","conflicting profile hints cannot select an old identity");
        var store=new DailyProfiles(Path.Combine(root,"identity-profile"));store.Update(profile);
        Check(DailyAccountOrder.ProfileFor(catalog.Accounts[0],store.Read()).Order == 4 && store.Read().Single().Selected,"dedup preserves account preferences and manual order");
        Check(DailyAccountOrder.ProfileFor(catalog.Accounts[0],store.Read()).PlayerName == "Original player","dedup preserves verified player");
        SessionSlot S(string member, string token, int hour) => new(1,"Original",DateTimeOffset.Parse("2026-10-03T00:00:00Z").AddHours(hour),"fixture",[
            new("neon_auth_member_h1293550423","Binary",Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {member_id=member})))),
            new("neon_access_token_h1862384816","Binary",Convert.ToBase64String(Encoding.UTF8.GetBytes(token)))]);
        var old=S("1000001","old-token",1);var current=S("1000001","refreshed-token",3);
        var slots=new Dictionary<int,SessionSlot>{{1,old},{2,S("2000001","other-token",6)},{13,current with {Alias="Accidental copy"}}};
        int writes=0;bool replaced=false;int written=0;
        SessionSlot? Load(int slot)=>slots.GetValueOrDefault(slot);
        SlotSummary Save(int slot,SessionSlot s,string name,bool replace){writes++;written=slot;replaced=replace;slots[slot]=s with {Alias=name};return new(name,s.CapturedAtUtc,"fixture","fixture",true,null);}
        SessionService.SaveCapturedToSlot(current,14,"New copy","1000001",Load,Save);
        Check(writes==1 && written==1 && replaced && !slots.ContainsKey(14),"service prevents duplicate even when caller requests empty slot");
        Check(slots[1].Alias == "Original" && slots[1].Entries.SequenceEqual(current.Entries),"same member refreshes credentials without losing alias");
        SessionService.SaveCapturedToSlot(current,1,"Chosen alias","1000001",Load,Save);
        Check(writes==2 && slots[1].Alias=="Chosen alias", "explicit original-slot save honors chosen alias");
        int savedWrites=writes;
        Reject(()=>SessionService.SaveCapturedToSlot(S("3","x",4),2,"Overwrite",null,Load,Save),"different identity cannot overwrite occupied slot");
        Reject(()=>SessionService.SaveCapturedToSlot(S("3","x",4),14,"Race","1000001",Load,Save),"identity change during save is rejected");
        Reject(()=>SessionService.SaveCapturedToSlot(S("bad","x",4),14,"Invalid",null,Load,Save),"malformed member cannot save");
        Check(writes==savedWrites,"rejected saves have no writes");
        SessionService.SaveCapturedToSlot(S("3","new",5),14,"New account","3",Load,Save);
        Check(written==14 && !replaced && slots.Count==4,"new identity creates exactly one slot");
        Check(SessionIdentity.GetMemberId(S("0001000001","x",1))=="1000001","numeric and string identities normalize identically");
        var latest=SessionService.LatestSameAccount(old,Load);
        var identityOnly = old with { Entries = old.Entries.Where(e => e.Name == "neon_auth_member_h1293550423").ToArray() };
        Check(SessionIdentity.GetMemberId(identityOnly) == SessionIdentity.GetMemberId(old), "member identity is readable without token or auto-login flags");
        Check(latest.Entries.SequenceEqual(current.Entries) && latest.Alias==old.Alias,"launch uses newest same-account credentials but original alias");
        Check(SessionIdentity.GetMemberId(latest)!="2000001","newer other account never selected for launch");
        Check(slots[13].Alias=="Accidental copy","legacy duplicate file retained, no destructive migration");
        SessionSlot? Broken(int slot) => slot==5 ? throw new System.ComponentModel.Win32Exception(unchecked((int)0x8009000B),"DPAPI") : null;
        bool refused=false;try{SessionService.SaveCapturedToSlot(current,5,"Recovered","1000001",Broken,Save);}catch(System.ComponentModel.Win32Exception){refused=true;}
        Check(refused,"unreadable slots require explicit replacement intent");
        SessionService.SaveCapturedToSlot(current,5,"Recovered","1000001",Broken,Save,true);
        Check(written==5 && replaced && slots[5].Alias=="Recovered","verified new login replaces an undecryptable slot atomically through the normal vault writer");
        Reject(()=>SessionService.SaveCapturedToSlot(current,2,"Wrong","1000001",n=>n==2?S("2000001","other",1):null,Save,true),"recovery flag cannot overwrite readable other-account credentials");
        Check(DailyIssues.Classify("Windows DPAPI 解密失败").Code=="credentials-unreadable","reinstall errors explain how to save a fresh login");

        // Real encrypted files in a temporary vault only: removal cannot require
        // a working DPAPI key, current registry login or a different saved account.
        var vault = new SessionVault(Path.Combine(root, "unreadable-removal"));
        var service = new SessionService(vault);
        string SlotPath(int number) => Path.Combine(vault.RootDirectory, SessionConstants.SlotDirectoryName,
            $"slot-{number}{SessionConstants.SlotExtension}");
        var validSession = new SessionSlot(1, "Saved", DateTimeOffset.UtcNow, SessionConstants.RegistrySubKey,
            SessionConstants.RequiredRegistryValues.Select(d => new RegistryEntrySnapshot(d.RegistryName, d.ExpectedKind.ToString(),
                Convert.ToBase64String(d.MustBeEnabled ? BitConverter.GetBytes(1) : Encoding.UTF8.GetBytes(
                    d.RegistryName.Contains("member") ? "{\"member_id\":\"1000001\"}" : "synthetic-sign-in")))).ToArray());
        vault.SaveFixedSlot(1, validSession, "Saved", false);
        vault.SaveFixedSlot(2, validSession, "Other record", false);
        byte[] untouched = File.ReadAllBytes(SlotPath(2));
        string healthyRevision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(SlotPath(1))));
        Check(vault.UnreadableFixedSlotRevision(1) == "", "healthy slot never exposes an unreadable removal token");
        Reject(() => service.DeleteUnreadableSlot(1, healthyRevision), "even a matching hash cannot bypass healthy-account deletion guards");
        byte[] broken = File.ReadAllBytes(SlotPath(1)); broken[^1] ^= 0x5a; File.WriteAllBytes(SlotPath(1), broken);
        string revision = vault.UnreadableFixedSlotRevision(1);
        Check(revision.Length == 64, "DPAPI failure has an exact encrypted-file removal token");
        File.WriteAllText(SlotPath(1), "changed broken record");
        Reject(() => service.DeleteUnreadableSlot(1, revision), "changed unreadable record rejects stale confirmation");
        Check(File.Exists(SlotPath(1)), "rejected removal leaves changed record intact");
        revision = vault.UnreadableFixedSlotRevision(1);
        vault.SaveFixedSlot(1, validSession, "Repaired", true);
        Reject(() => service.DeleteUnreadableSlot(1, revision), "saving repaired credentials invalidates old delete confirmation");
        Check(vault.LoadFixedSlot(1).Alias == "Repaired", "repaired account survives a stale remove action");
        File.WriteAllText(SlotPath(1), "malformed old slot");
        service.DeleteUnreadableSlot(1, vault.UnreadableFixedSlotRevision(1));
        Check(!vault.FixedSlotExists(1), "unparseable record can be removed without reading current login or launching the game");
        Check(File.ReadAllBytes(SlotPath(2)).SequenceEqual(untouched), "unreadable removal does not touch other saved accounts");
        vault.SaveFixedSlot(1, validSession, "Undecryptable", false);
        broken = File.ReadAllBytes(SlotPath(1)); broken[^1] ^= 0x5a; File.WriteAllBytes(SlotPath(1), broken);
        service.DeleteUnreadableSlot(1, vault.UnreadableFixedSlotRevision(1));
        Check(!vault.FixedSlotExists(1), "undecryptable record is removable independently of a complete session");
        byte[] incomplete = DpapiProtector.Protect(Encoding.UTF8.GetBytes("{\"SchemaVersion\":1,\"Alias\":null,\"Entries\":null}"));
        File.WriteAllBytes(SlotPath(1), SessionConstants.FileMagic.Concat(incomplete).ToArray());
        service.DeleteUnreadableSlot(1, vault.UnreadableFixedSlotRevision(1));
        Check(!vault.FixedSlotExists(1), "structurally incomplete decrypted JSON can also be removed");
        Reject(() => service.DeleteUnreadableSlot(2, ""), "missing removal token is rejected");
        Reject(() => service.DeleteUnreadableSlot(101, new string('a', 64)), "unreadable removal retains slot boundaries");
        Check(store.Read().Single().PlayerName == "Original player", "removing credentials keeps per-account daily preferences");
    }
}
