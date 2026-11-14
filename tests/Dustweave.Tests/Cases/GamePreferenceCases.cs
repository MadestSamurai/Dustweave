using Dustweave;
using Dustweave.Compatibility;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.InteropServices;

static class GamePreferenceCases
{
    public static Task Run(string root,List<string> cases)
    {
        void Check(bool condition,string name){if(!condition)throw new Exception(name);cases.Add(name);}
        void Reject(Action action,string name){try{action();}catch(Exception e)when(e is InvalidDataException or IOException){cases.Add(name);return;}throw new Exception(name);}
        RegistryRoundTrips(Check,Reject);
        var parts=GamePreferenceParts.Personal|GamePreferenceParts.Presentation|GamePreferenceParts.Gameplay;
        foreach(string name in new[]{"neon_access_token_h1862384816","neon_auth_member_h1293550423","IsAutoLogin_h601540819","StandaloneAutoLogin_h1463356620","LastUserOwnerIndex","StandalonePatchDataPath","LanguageGame_hbad","LanguageGame_extra","MicDeviceName"})
            Check(!DailyGamePreferenceStore.AllowedValue(name,parts,101),"Preferences exclude credential/device/unknown key: "+name);
        Check(DailyGamePreferenceStore.AllowedValue("LanguageGame_h123",parts,101),"Unity decimal suffix accepted for a known language key");
        Check(!DailyGamePreferenceStore.AllowedValue("auto_eat_food_flag_h123",GamePreferenceParts.Presentation,101),"Display selection cannot change food consumption");
        Check(DailyGamePreferenceStore.AllowedValue("is_auto_equipment_auto_mount_101_h12",parts,101)&&
            !DailyGamePreferenceStore.AllowedValue("is_auto_equipment_auto_mount_202_h12",parts,101),"Owner-suffixed settings remain account scoped");
        string directory=Path.Combine(root,"game-preferences");Directory.CreateDirectory(directory);
        string managed=Path.Combine(directory,"managed");Directory.CreateDirectory(managed);FixtureAssembly(managed);
        var codec=GamePreferenceCodec.Read(managed);
        string source=Path.Combine(directory,"source"),destination=Path.Combine(directory,"target"),work=Path.Combine(directory,"work");
        Directory.CreateDirectory(source);Directory.CreateDirectory(destination);Directory.CreateDirectory(work);
        string Make(string folder,bool incoming) {
            string file=Path.Combine(folder,"plain.db");File.WriteAllBytes(file,[]);
            using(var db=new DailyGamePreferenceStore.Sqlite(file,true)) {
                db.Execute("CREATE TABLE ClientDbVersionInfoTable(id INTEGER PRIMARY KEY, version INTEGER); INSERT INTO ClientDbVersionInfoTable VALUES(0,9);");
                db.Execute("CREATE TABLE ShopFavorite(ownerIndex REAL,saveId INTEGER,shopId INTEGER,productId INTEGER,PRIMARY KEY(ownerIndex,saveId));");
                db.Execute("CREATE TABLE EquipFilterPreset(ownerIndex REAL,slotIndex INTEGER,optionsJson TEXT,PRIMARY KEY(ownerIndex,slotIndex));");
                db.Execute(incoming?"INSERT INTO ShopFavorite VALUES(101,1001,1,1),(202,2002,2,2);":"INSERT INTO ShopFavorite VALUES(101,1009,1,9),(202,2008,2,8);");
                db.Execute("INSERT INTO EquipFilterPreset VALUES(101,1,"+DailyGamePreferenceStore.Sqlite.Literal(incoming?"测试'\"收藏":"old")+");");
                db.Execute("CREATE TABLE DayReset(ownerIndex REAL, ticks INTEGER); INSERT INTO DayReset VALUES(101,123),(202,456);");
            }
            byte[] bytes=File.ReadAllBytes(file);string encoded=Path.Combine(folder,codec.FileName);File.WriteAllBytes(encoded,codec.Encode(bytes));File.Delete(file);return encoded;
        }
        string src=Make(source,true),dst=Make(destination,false);
        Check(codec.Decode(codec.Encode(codec.Decode(File.ReadAllBytes(src)))).AsSpan().SequenceEqual(codec.Decode(File.ReadAllBytes(src))),"Game codec round-trips a real SQLite page layout");
        Reject(()=>codec.Decode(new byte[1024]),"Unknown/corrupt database codec rejected");
        string registryPath="Software\\Dustweave-PreferenceTest-"+Guid.NewGuid().ToString("N");
        string account=new('a',64),player=BD2Daily.DailyIdentity.PlayerKey(account,101);
        try {
            using var registry=Registry.CurrentUser.CreateSubKey(registryPath);
            registry.SetValue("LanguageGame_h123",3,RegistryValueKind.DWord);
            RawWrite(registry,"SoundMaster_h123",RegistryValueKind.DWord,BitConverter.GetBytes(0.75));
            registry.SetValue("neon_access_token_h1862384816","synthetic-token",RegistryValueKind.String);
            var snapshot=DailyGamePreferenceStore.Export(account,player,parts,managed,source,work,registry);
            Check(snapshot.Owner==101&&snapshot.Tables.All(t=>t.Rows.All(r=>r[Array.IndexOf(t.Columns,"ownerIndex")]=="101.0"||r[Array.IndexOf(t.Columns,"ownerIndex")]=="101")),"Export selects the verified account partition");
            Check(snapshot.Values.Length==2&&!snapshot.Tables.Any(t=>t.Name=="DayReset"),"Export includes preference whitelist only");
            byte[] original=File.ReadAllBytes(dst),copied=DailyGamePreferenceStore.PrepareDatabase(snapshot,original,codec,work);
            string inspect=Path.Combine(work,"inspect.db");File.WriteAllBytes(inspect,codec.Decode(copied));
            using(var db=new DailyGamePreferenceStore.Sqlite(inspect,false)) {
                Check(db.Query("SELECT productId FROM ShopFavorite WHERE ownerIndex=101").Single().Single()=="1","Directional copy replaces target favorites, including removal");
                Check(db.Query("SELECT productId FROM ShopFavorite WHERE ownerIndex=202").Single().Single()=="8","Other account favorites remain unchanged");
                Check(db.Query("SELECT ticks FROM DayReset WHERE ownerIndex=101").Single().Single()=="123","Non-preference state remains unchanged");
                Check(db.Query("SELECT optionsJson FROM EquipFilterPreset WHERE ownerIndex=101").Single().Single()=="测试'\"收藏","Unicode and quoted preference text round-trips safely");
            }
            File.Delete(inspect);
            var empty=snapshot with { Tables=snapshot.Tables.Select(t=>t.Name=="ShopFavorite"?t with{Rows=[]}:t).ToArray() };
            File.WriteAllBytes(inspect,codec.Decode(DailyGamePreferenceStore.PrepareDatabase(empty,original,codec,work)));
            using(var db=new DailyGamePreferenceStore.Sqlite(inspect,false))Check(db.Query("SELECT count(*) FROM ShopFavorite WHERE ownerIndex=101").Single().Single()=="0","An empty favorite list clears target favorites instead of resurrecting them");
            File.Delete(inspect);
            Reject(()=>DailyGamePreferenceStore.PrepareDatabase(snapshot with{DatabaseVersion=8},original,codec,work),"Different schema versions cannot overwrite each other");
            Reject(()=>DailyGamePreferenceStore.Validate(snapshot with{Owner=202}),"Account identity mismatch rejected");
            Reject(()=>DailyGamePreferenceStore.Validate(snapshot with{Values=[new("neon_access_token_h1862384816",RegistryValueKind.String,[]) ]}),"Incoming credentials cannot bypass the export filter");
            Reject(()=>DailyGamePreferenceStore.Export(account,new('b',64),parts,managed,source,work,registry),"An unrecognized owner cannot be guessed from the only row");
            File.WriteAllText(src+"-journal","fixture");Reject(()=>DailyGamePreferenceStore.RequireClosedDatabase(src),"Pending database journal prevents copying incomplete state");File.Delete(src+"-journal");
            registry.SetValue("LanguageGame_h123",7);registry.SetValue("SoundBGM_h555",new byte[4],RegistryValueKind.Binary);
            DailyGamePreferenceStore.WriteValues(registry,snapshot);
            Check((int)registry.GetValue("LanguageGame_h123")! == 3&&registry.GetValue("SoundBGM_h555")==null,"Registry copy replaces values and restores absent-source defaults");
            Check(RawRead(registry,"SoundMaster_h123").Bytes.AsSpan().SequenceEqual(BitConverter.GetBytes(0.75))&&
                registry.GetValueKind("SoundMaster_h123")==RegistryValueKind.DWord,"Combined personal/display transfer preserves the eight-byte DWORD exactly");
            Check((string)registry.GetValue("neon_access_token_h1862384816")! == "synthetic-token","Copying preferences never modifies login credentials");
            Check(File.ReadAllBytes(dst).AsSpan().SequenceEqual(original),"Preparing a transfer never writes the actual target database");
            int idleChecks=0;
            Reject(()=>DailySandboxPreferenceTransfer.Apply(snapshot,managed,destination,work,registry,()=>{
                if(++idleChecks==2)throw new IOException("became busy");
            },Path.Combine(directory,"backups")),"A game starting during preparation prevents the commit");
            Check(File.ReadAllBytes(dst).AsSpan().SequenceEqual(original),"A canceled pre-commit leaves the target unchanged");
            using(var readOnly=Registry.CurrentUser.OpenSubKey(registryPath)!) {
                bool denied=false;
                try{DailySandboxPreferenceTransfer.Apply(snapshot,managed,destination,work,readOnly,()=>{},Path.Combine(directory,"backups"));}
                catch(UnauthorizedAccessException){denied=true;}
                Check(denied&&File.ReadAllBytes(dst).AsSpan().SequenceEqual(original),"Registry write failure restores the already replaced database");
            }
            var result=DailySandboxPreferenceTransfer.Apply(snapshot,managed,destination,work,registry,()=>{},Path.Combine(directory,"backups"));
            Check(File.ReadAllBytes(Path.Combine(result.BackupDirectory,"database.bin")).AsSpan().SequenceEqual(original),"Commit retains the destination's original encrypted database");
            Check(File.ReadAllBytes(dst).AsSpan().SequenceEqual(copied),"Manual commit writes the fully validated replacement");
            Check((string)registry.GetValue("neon_access_token_h1862384816")! == "synthetic-token","Complete transfer preserves sign-in credentials");
            // Empty selected tables still belong to a known account; other local
            // records establish identity without being copied themselves.
            string clear=Path.Combine(work,"clear.db");File.WriteAllBytes(clear,codec.Decode(File.ReadAllBytes(src)));
            using(var db=new DailyGamePreferenceStore.Sqlite(clear,true))db.Execute("DELETE FROM ShopFavorite WHERE ownerIndex=101; DELETE FROM EquipFilterPreset WHERE ownerIndex=101;");
            File.WriteAllBytes(src,codec.Encode(File.ReadAllBytes(clear)));File.Delete(clear);
            var cleared=DailyGamePreferenceStore.Export(account,player,parts,managed,source,work,registry);
            Check(cleared.Owner==101&&cleared.Tables.All(t=>t.Rows.Length==0),"Clearing every preference still exports a verified empty account partition");
        } finally {Registry.CurrentUser.DeleteSubKeyTree(registryPath,false);}
        return Task.CompletedTask;
    }
    private static void RegistryRoundTrips(Action<bool,string> check,Action<Action,string> reject)
    {
        string path="Software\\Dustweave-PreferenceNumericTest-"+Guid.NewGuid().ToString("N");
        try {
            using var source=Registry.CurrentUser.CreateSubKey(path+"\\source");
            using var target=Registry.CurrentUser.CreateSubKey(path+"\\target");
            var fixtures=new GamePreferenceValue[] {
                new("LanguageGame_h1",RegistryValueKind.DWord,BitConverter.GetBytes(3)),
                new("FPS_SETTING_PC_h1",RegistryValueKind.DWord,BitConverter.GetBytes(int.MinValue)),
                new("SoundMaster_h1",RegistryValueKind.DWord,BitConverter.GetBytes(0.75)),
                new("SoundBGM_h1",RegistryValueKind.DWord,BitConverter.GetBytes(-1L)),
                new("SoundVoice_h1",RegistryValueKind.QWord,BitConverter.GetBytes(long.MinValue)),
                new("SoundSFX_h1",RegistryValueKind.Binary,[0,1,255,127]),
                new("Resolution_h1",RegistryValueKind.String,System.Text.Encoding.Unicode.GetBytes("测试 resolution\0"))
            };
            foreach(var f in fixtures)RawWrite(source,f.Name,f.Kind,f.Bytes);
            source.SetValue("neon_access_token_h1862384816","source-session",RegistryValueKind.String);
            target.SetValue("neon_access_token_h1862384816","target-session",RegistryValueKind.String);
            target.SetValue("auto_eat_food_flag_h1",1,RegistryValueKind.DWord);
            RawWrite(target,"SoundMaster_h1",RegistryValueKind.DWord,BitConverter.GetBytes(0.125));
            var prior=RawRead(target,"SoundMaster_h1");
            var settings=DailyGamePreferenceStore.ReadValues(source,GamePreferenceParts.Presentation,0);
            var incoming=new GamePreferenceSnapshot(1,new('a',64),"",GamePreferenceParts.Presentation,0,0,[],settings);
            var before=incoming with{Values=DailyGamePreferenceStore.ReadValues(target,incoming.Parts,0)};
            check(source.GetValue("SoundMaster_h1") is long,"Eight-byte DWORD fixture reproduces the CLR Int64 that the old encoder rejected");
            check(settings.Length==fixtures.Length,"Numeric capture remains limited to the selected setting whitelist");
            // Exercise the exchange/backup serialization, not only in-memory values.
            incoming=System.Text.Json.JsonSerializer.Deserialize<GamePreferenceSnapshot>(System.Text.Json.JsonSerializer.Serialize(incoming))!;
            for(int pass=0;pass<2;pass++) {
                DailyGamePreferenceStore.WriteValues(target,incoming);
                foreach(var f in fixtures) {
                    var actual=RawRead(target,f.Name);
                    check(actual.Kind==f.Kind&&actual.Bytes.AsSpan().SequenceEqual(f.Bytes),$"Forward/repeated copy {pass}: exact native type and bytes for {f.Name}");
                }
            }
            RawWrite(target,"SoundMaster_h1",RegistryValueKind.DWord,BitConverter.GetBytes(0.625));
            var reverse=incoming with{Values=DailyGamePreferenceStore.ReadValues(target,incoming.Parts,0)};
            DailyGamePreferenceStore.WriteValues(source,reverse);
            check(RawRead(source,"SoundMaster_h1").Bytes.AsSpan().SequenceEqual(BitConverter.GetBytes(0.625)),"Reverse direction copies the changed eight-byte DWORD without narrowing");
            DailyGamePreferenceStore.WriteValues(target,before);
            check(DailyGamePreferenceStore.ValuesEqual(DailyGamePreferenceStore.ReadValues(target,before.Parts,0),before.Values)&&
                RawRead(target,prior.Name).Bytes.AsSpan().SequenceEqual(prior.Bytes),"Restoring the destination backup retains its original eight-byte value and absent settings");
            check((string)source.GetValue("neon_access_token_h1862384816")! == "source-session"&&
                (string)target.GetValue("neon_access_token_h1862384816")! == "target-session"&&
                (int)target.GetValue("auto_eat_food_flag_h1")! == 1,"Both directions and restoration preserve credentials and unselected gameplay settings");
            foreach(var invalid in new[] {
                new GamePreferenceValue("SoundMaster_h1",RegistryValueKind.DWord,new byte[3]),
                new GamePreferenceValue("SoundMaster_h1",RegistryValueKind.DWord,new byte[12]),
                new GamePreferenceValue("SoundMaster_h1",RegistryValueKind.QWord,new byte[4])
            }) {
                RawWrite(source,invalid.Name,invalid.Kind,invalid.Bytes);
                reject(()=>DailyGamePreferenceStore.ReadValues(source,incoming.Parts,0),$"Unsupported numeric width {invalid.Kind}/{invalid.Bytes.Length} is not silently padded or truncated");
                reject(()=>DailyGamePreferenceStore.WriteValues(target,incoming with{Values=[invalid]}),$"Invalid incoming width {invalid.Kind}/{invalid.Bytes.Length} is rejected before writes");
                check(DailyGamePreferenceStore.ValuesEqual(DailyGamePreferenceStore.ReadValues(target,before.Parts,0),before.Values),"Invalid snapshot leaves all target preferences intact");
            }
            RawWrite(source,"SoundMaster_h1",RegistryValueKind.DWord,BitConverter.GetBytes(0.75));
            DailyGamePreferenceStore.WriteValues(target,incoming);
            check(DailyGamePreferenceStore.ValuesEqual(DailyGamePreferenceStore.ReadValues(target,incoming.Parts,0),incoming.Values),"A later valid synchronization succeeds after rejected input");
        } finally {Registry.CurrentUser.DeleteSubKeyTree(path,false);}
    }
    [DllImport("advapi32.dll",EntryPoint="RegSetValueExW",CharSet=CharSet.Unicode)]
    private static extern int RegSetValueEx(SafeRegistryHandle key,string name,int reserved,RegistryValueKind kind,byte[] bytes,int size);
    [DllImport("advapi32.dll",EntryPoint="RegQueryValueExW",CharSet=CharSet.Unicode)]
    private static extern int RegQueryValueEx(SafeRegistryHandle key,string name,IntPtr reserved,out RegistryValueKind kind,byte[] bytes,ref int size);
    private static void RawWrite(RegistryKey key,string name,RegistryValueKind kind,byte[] bytes)
    {
        int error=RegSetValueEx(key.Handle,name,0,kind,bytes,bytes.Length);
        if(error!=0)throw new IOException("Could not create numeric registry fixture: "+error);
    }
    private static GamePreferenceValue RawRead(RegistryKey key,string name)
    {
        byte[] bytes=new byte[1024];int size=bytes.Length;
        int error=RegQueryValueEx(key.Handle,name,IntPtr.Zero,out var kind,bytes,ref size);
        if(error!=0)throw new IOException("Could not independently inspect numeric registry fixture: "+error);
        Array.Resize(ref bytes,size);return new(name,kind,bytes);
    }
    private static void FixtureAssembly(string directory)
    {
        using var module=ModuleDefinition.CreateModule("PreferenceFixture",ModuleKind.Dll);
        var type=new TypeDefinition("Fixture","Codec",TypeAttributes.Public,module.TypeSystem.Object);module.Types.Add(type);
        type.Fields.Add(new("Marker",FieldAttributes.Public|FieldAttributes.Static|FieldAttributes.Literal|FieldAttributes.HasDefault,module.TypeSystem.String){Constant="common-dbdata.bin"});
        var version=new FieldDefinition("Version",FieldAttributes.Public|FieldAttributes.Static,module.TypeSystem.Int32);type.Fields.Add(version);
        var init=new MethodDefinition(".cctor",MethodAttributes.Static|MethodAttributes.SpecialName|MethodAttributes.RTSpecialName,module.TypeSystem.Void);type.Methods.Add(init);
        var il=init.Body.GetILProcessor();il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Stsfld,version);il.Emit(OpCodes.Ret);
        var hash=new MethodDefinition("Hash",MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.String);hash.Parameters.Add(new("text",ParameterAttributes.None,module.TypeSystem.String));type.Methods.Add(hash);
        il=hash.Body.GetILProcessor();il.Emit(OpCodes.Newobj,module.ImportReference(typeof(System.Security.Cryptography.SHA1CryptoServiceProvider).GetConstructor(Type.EmptyTypes)!));il.Emit(OpCodes.Pop);il.Emit(OpCodes.Ldstr,"");il.Emit(OpCodes.Ret);
        var getter=new MethodDefinition("get_Password",MethodAttributes.Public|MethodAttributes.Static|MethodAttributes.SpecialName,module.TypeSystem.String);type.Methods.Add(getter);
        il=getter.Body.GetILProcessor();il.Emit(OpCodes.Ldstr,"synthetic-format-secret");il.Emit(OpCodes.Call,hash);il.Emit(OpCodes.Ret);
        type.Properties.Add(new("Password",PropertyAttributes.None,module.TypeSystem.String){GetMethod=getter});module.Write(Path.Combine(directory,"Assembly-CSharp.dll"));
    }
}
