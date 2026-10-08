using Dustweave.Accounts;
using Dustweave;

static class SandboxCases
{
    public static Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); cases.Add(name); }
        void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { cases.Add(name); return; } catch (InvalidDataException) { cases.Add(name); return; } throw new InvalidOperationException(name); }
        string a = new('a', 64), b = new('b', 64);
        Check(DailySandbox.BoxName(a) == "Dustweave_" + new string('a', 20), "stable per-account sandbox name survives changes to account alias and slot");
        Check(DailySandbox.BoxName(a) != DailySandbox.BoxName(b), "different identities have separate sandbox registrations");
        foreach (var key in new[] { "", "../account", new string('z',64), a[..63] })
            Reject(() => DailySandbox.BoxName(key), "invalid account cannot choose a sandbox: " + key.Length);
        var valid = new DailySandboxRequest(DailySandbox.BoxName(a), 1, a, "Account", @"C:\Game\BrownDust II.exe", DateTimeOffset.UtcNow);
        DailySandbox.Validate(valid); cases.Add("valid sandbox request contains identity and path but no session bytes");
        Reject(() => DailySandbox.Validate(valid with { Box = DailySandbox.BoxName(b) }), "sandbox and account mismatch is rejected before session access");
        foreach (int slot in new[] { 0, 101 }) Reject(() => DailySandbox.Validate(valid with { Slot = slot }), "out-of-range sandbox slot rejected: " + slot);
        foreach (var path in new[] { "BrownDust II.exe", @"C:\Game\other.exe", @"..\BrownDust II.exe" })
            Reject(() => DailySandbox.Validate(valid with { GameExecutable = path }), "unexpected sandbox executable rejected: " + path);
        Reject(() => DailySandbox.Validate(valid with { Name = " " }), "unnamed sandbox request rejected");
        var imported = new DailySandboxBinding(valid.Box, a, "Account", 1) { ImportedSessionUtc = valid.CreatedUtc };
        Check(!DailySandbox.ShouldImportSession(imported, valid.CreatedUtc, true, a, a), "reopening preserves the isolated game's renewed session");
        Check(DailySandbox.ShouldImportSession(imported, valid.CreatedUtc.AddSeconds(1), true, a, a), "newly saved host sign-in replaces an expired isolated sign-in");
        Check(!DailySandbox.ShouldImportSession(imported, valid.CreatedUtc.AddSeconds(-1), true, a, a), "older saved sign-in cannot overwrite a renewed isolated session");
        Check(DailySandbox.ShouldImportSession(imported, valid.CreatedUtc, false, a, a), "incomplete isolated sign-in is repaired before launch");
        Check(DailySandbox.ShouldImportSession(null, valid.CreatedUtc, true, a, a), "new sandbox always imports the selected saved sign-in");
        var snapshot = new SessionSlot(1, "synthetic-sandbox", valid.CreatedUtc, SessionConstants.RegistrySubKey,
            SessionConstants.RequiredRegistryValues.Select(d => new RegistryEntrySnapshot(d.RegistryName, d.ExpectedKind.ToString(),
                Convert.ToBase64String(d.MustBeEnabled ? BitConverter.GetBytes(1) : System.Text.Encoding.UTF8.GetBytes("synthetic-sign-in-no-real-account")))).ToArray());
        string transfer = Path.Combine(output, "synthetic-transfer.bd2slot");
        var vault = new SessionVault();
        vault.WriteLaunchSnapshot(snapshot, transfer);
        Check(SessionRegistry.SessionsEqual(snapshot, vault.ReadLaunchSnapshot(transfer)), "encrypted launch snapshot round-trips without using the live vault");
        Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(transfer)).Contains("synthetic-sign-in"), "launch snapshot does not store plaintext sign-in values");
        var corrupt = File.ReadAllBytes(transfer); corrupt[^1] ^= 1; File.WriteAllBytes(transfer, corrupt);
        bool rejected = false; try { vault.ReadLaunchSnapshot(transfer); } catch { rejected = true; }
        Check(rejected, "damaged encrypted transfer is rejected before registry access");
        // Real DPAPI files, isolated from all user credentials. Reproduce a token
        // rotation in each context and transfer using the production merge code.
        SessionSlot Credentials(string token, int minute, string member = "1000001") => snapshot with
        {
            CapturedAtUtc = DateTimeOffset.Parse("2026-10-08T00:00:00Z").AddMinutes(minute),
            Entries = SessionConstants.RequiredRegistryValues.Select(d => new RegistryEntrySnapshot(d.RegistryName,d.ExpectedKind.ToString(),
                Convert.ToBase64String(d.MustBeEnabled ? BitConverter.GetBytes(1) : System.Text.Encoding.UTF8.GetBytes(
                    d.RegistryName.Contains("member") ? "{\"member_id\":\""+member+"\"}" : token)))).ToArray()
        };
        var original=Credentials("synthetic-original",0);
        var hostRefreshed=Credentials("synthetic-host-renewed",1);
        var boxRefreshed=Credentials("synthetic-box-renewed",2);
        var hostVault=new SessionVault(Path.Combine(output,"host-vault"));
        var boxVault=new SessionVault(Path.Combine(output,"box-vault"));
        var hostService=new SessionService(hostVault);
        var boxService=new SessionService(boxVault);
        hostVault.SaveFixedSlot(1,original,"Main",false);
        hostVault.SaveFixedSlot(13,hostRefreshed,"Duplicate",false);
        hostVault.RenameFixedSlot(1,"Renamed");
        Check(SessionRegistry.SessionsEqual(SessionService.LatestSameAccount(hostVault.LoadFixedSlot(1),hostVault.TryLoadFixedSlot),hostRefreshed),"renaming an old slot cannot supersede a renewed token");
        Check(hostVault.LoadFixedSlot(1).CapturedAtUtc==original.CapturedAtUtc,"rename preserves the credential capture time");
        hostService.SynchronizeCaptured(hostRefreshed);
        var outgoing=SessionService.LatestSameAccount(hostVault.LoadFixedSlot(1),hostVault.TryLoadFixedSlot);
        string handoff=Path.Combine(output,"roundtrip.bd2slot");
        hostVault.WriteLaunchSnapshot(outgoing,handoff);
        boxVault.SaveFixedSlot(1,boxVault.ReadLaunchSnapshot(handoff),"Isolated",false);
        boxVault.RememberObserved(outgoing);
        Check(SessionRegistry.SessionsEqual(boxVault.LoadFixedSlot(1),hostRefreshed),"host renewal is transferred before sandbox startup");
        Check(boxVault.LoadFixedSlot(1).CapturedAtUtc==hostRefreshed.CapturedAtUtc,"sandbox import preserves token age");
        boxService.SynchronizeCaptured(boxRefreshed);
        boxVault.WriteLaunchSnapshot(boxVault.LoadFixedSlot(1),handoff);
        hostService.AcceptTransferred(hostVault.ReadLaunchSnapshot(handoff),"1000001");
        Check(SessionRegistry.SessionsEqual(hostVault.LoadFixedSlot(1),boxRefreshed),"sandbox renewal returns to the original host slot");
        Check(hostVault.LoadFixedSlot(1).Alias=="Renamed","returning session preserves original host alias");
        new SessionService(new SessionVault(hostVault.RootDirectory)).SynchronizeCaptured(hostRefreshed with {CapturedAtUtc=DateTimeOffset.UtcNow});
        Check(SessionRegistry.SessionsEqual(hostVault.LoadFixedSlot(1),boxRefreshed),"unchanged host registry cannot overwrite the returned token after tool restart");
        boxService.AcceptTransferred(original with {Alias="Copied old"},"1000001");
        Check(SessionRegistry.SessionsEqual(boxVault.LoadFixedSlot(1),boxRefreshed),"stale host copy cannot roll back sandbox renewal");
        bool identityRejected=false;try{hostService.AcceptTransferred(Credentials("wrong-account",3,"2000001"),"1000001");}catch(SessionManagerException){identityRejected=true;}
        Check(identityRejected&&SessionRegistry.SessionsEqual(hostVault.LoadFixedSlot(1),boxRefreshed),"cross-account return cannot replace the selected slot");
        bool conflictRejected=false;try{hostService.AcceptTransferred(Credentials("ambiguous-token",2),"1000001");}catch(SessionManagerException){conflictRejected=true;}
        Check(conflictRejected,"equal-age conflicting credentials are preserved for manual login");
        var nextHost=Credentials("host-next-login",3);
        hostVault.RememberObserved(boxRefreshed);
        hostService.SynchronizeCaptured(nextHost);
        boxService.AcceptTransferred(hostVault.LoadFixedSlot(1),"1000001");
        Check(SessionRegistry.SessionsEqual(boxVault.LoadFixedSlot(1),nextHost),"second host renewal reaches the existing sandbox");
        new SessionService(new SessionVault(boxVault.RootDirectory)).SynchronizeCaptured(boxRefreshed with {CapturedAtUtc=DateTimeOffset.UtcNow});
        Check(SessionRegistry.SessionsEqual(boxVault.LoadFixedSlot(1),nextHost),"unchanged sandbox registry cannot roll back the next host renewal");
        Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(handoff)).Contains("synthetic-box-renewed"),"return handoff keeps token bytes encrypted");
        string registrationAccount=BD2Daily.DailyIdentity.MemberKey("1000001"), registrationBox=DailySandbox.BoxName(registrationAccount);
        string registrationState=Path.Combine(output,"registration",registrationBox), registrationRoot=Path.Combine(registrationState,"root");
        Directory.CreateDirectory(registrationState);
        string GamePath()=>@"C:\Game\BrownDust II.exe";
        Check(DailySandboxRegistration.Resolve(registrationAccount,registrationState,"",GamePath)==null,"unused sandbox folder does not block ordinary account launch");
        Reject(()=>DailySandboxRegistration.Resolve(registrationAccount,registrationState,registrationRoot,GamePath),"sandbox name alone is insufficient ownership proof");
        string registrationLocal=Path.Combine(registrationRoot,"user","current","AppData","Local");
        string bindingFile=Path.Combine(registrationLocal,"BD2DailyAssistant","sandbox-binding.json");
        var registrationBinding=new DailySandboxBinding(registrationBox,registrationAccount,"Kept alias",1);
        DailyJson.Write(bindingFile,registrationBinding);
        Reject(()=>DailySandboxRegistration.Resolve(registrationAccount,registrationState,registrationRoot,GamePath),"binding alone cannot restore missing host registration");
        var registrationVault=new SessionVault(Path.Combine(registrationLocal,SessionConstants.VaultDirectoryName));
        registrationVault.SaveFixedSlot(1,Credentials("identity-evidence",1,"2000001"),"Wrong",false);
        Reject(()=>DailySandboxRegistration.Resolve(registrationAccount,registrationState,registrationRoot,GamePath),"mismatching encrypted member cannot restore registration");
        registrationVault.SaveFixedSlot(1,Credentials("identity-evidence",1),"Saved",true);
        Reject(()=>DailySandboxRegistration.Resolve(registrationAccount,registrationState,Path.Combine(output,"foreign-root"),GamePath),"a different configured root cannot be adopted");
        var restoredRegistration=DailySandboxRegistration.Resolve(registrationAccount,registrationState,registrationRoot,GamePath);
        Check(restoredRegistration?.Account==registrationAccount && restoredRegistration.Box==registrationBox && restoredRegistration.Slot==1,"missing host index recovers from three matching independent records");
        string markerFile=Path.Combine(registrationState,"instance.json");
        var markerBytes=File.ReadAllBytes(markerFile);
        DailySandboxRegistration.Resolve(registrationAccount,registrationState,registrationRoot,GamePath);
        Check(File.ReadAllBytes(markerFile).SequenceEqual(markerBytes),"rechecking recovered registration is idempotent");
        Check(restoredRegistration!.Name=="Kept alias" && SessionRegistry.SessionsEqual(registrationVault.LoadFixedSlot(1),Credentials("identity-evidence",1)),"index recovery retains alias and encrypted credentials");
        DailyJson.Write(markerFile,restoredRegistration with {Account=b});
        Reject(()=>DailySandboxRegistration.Resolve(registrationAccount,registrationState,registrationRoot,GamePath),"explicit conflicting owner is not repaired over");
        Check(DailyJson.TryRead<DailySandboxRequest>(markerFile)?.Account==b,"conflicting ownership record stays unchanged");
        File.WriteAllText(markerFile,"invalid json");
        Reject(()=>DailySandboxRegistration.Resolve(registrationAccount,registrationState,registrationRoot,GamePath),"unreadable host index is distinct from a missing index");
        Check(File.ReadAllText(markerFile)=="invalid json","corrupt ownership evidence is preserved");
        DailyJson.Write(markerFile,restoredRegistration);
        DailyJson.Write(bindingFile,registrationBinding with {Account=b});
        Reject(()=>DailySandboxRegistration.Resolve(registrationAccount,registrationState,registrationRoot,GamePath),"valid host index cannot conceal conflicting inner binding");
        return Task.CompletedTask;
    }
}
