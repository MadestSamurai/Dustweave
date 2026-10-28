using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Dustweave.Accounts;
using Dustweave.Compatibility;

namespace Dustweave;

[Flags] public enum GamePreferenceParts { None=0, Personal=1, Presentation=2, Gameplay=4 }
public sealed record GamePreferenceValue(string Name,RegistryValueKind Kind,byte[] Bytes);
public sealed record GamePreferenceTable(string Name,string[] Columns,string?[][] Rows);
public sealed record GamePreferenceSnapshot(int Schema,string Account,string Player,GamePreferenceParts Parts,long Owner,
    int DatabaseVersion,GamePreferenceTable[] Tables,GamePreferenceValue[] Values);

// A directional copy of explicit preferences, never a merge of sessions or inventory.
internal static class DailyGamePreferenceStore
{
    internal static readonly string[] Tables=["ShopFavorite","LifeFavorite","EquipFilterPreset","ItemBreakBatch",
        "ScrollPinContent","FavoriteFriendshipCostume","AquariumHiddenFish","SkillCutSceneSetting","MenuBGM",
        "LobbySettingSlotData","LastSpineInteraction","Preset","PresetTalent","DeckCostumeSettingData",
        "PrioritySkillUseStateEx","MonsterHuntDeckData","MonsterHuntPracticeDeckData","MonsterHuntTeam",
        "GuildRaidDeckData","GuildRaidPracticeDeckData","ApSelectOption"];
    private static readonly HashSet<string> Presentation=new(StringComparer.Ordinal) {
        "LanguageGame","LanguageSound","Resolution","SoundQuality","FPS_SETTING_PC","PCFullScreenMode","PCResolutionAspectValue",
        "PCPlayBackgroundSound","SoundMasterMute","SoundMaster","IsSoundDisable","SoundBGMMute","SoundBGM","SoundSFXMute","SoundSFX",
        "SoundAmbientMute","SoundAmbient","SoundVoice","SoundVolumeVoice","IsReverb","IsFieldBlur","IsUIBlur","SkillCutScene","LobbyCutScene",
        "IsEnableYellowDot","IsSilhouetteModeKey","IsUseSilhouetteModeHotKey","BGM_Fold","HuntOrAirwayFold_Fold" };
    private static readonly HashSet<string> Gameplay=new(StringComparer.Ordinal) {
        "ScreenFix","TouchPadType","CharMoveType","ControlType","AutoRunType","AutoMoveConfirmDoNotSee","AutoWarpConfirmDoNotSee",
        "EnablePackRewardSummary","ShowMapFieldRewardObjects","AutoDispatch","is_auto_balloon_script","auto_balloon_script_speed_type",
        "is_auto_battle","is_auto_burst_battle","is_auto_skill_battle","gamespeed","auto_eat_food_flag","auto_eat_food_grade_or_less",
        "auto_eat_only_entry_char_flag","auto_eat_target_field_deck","auto_recovery_limit","is_retry_charaction_save",
        "is_display_newCostume_auto_mount","display_newCostume_auto_mount_grade_option" };
    private static readonly string[] PersonalPrefixes=["is_join_field_characters_newCostume_auto_mount_","is_equip_newCostume_auto_mount_",
        "is_auto_equipment_auto_mount_","is_auto_level_up_auto_mount_","is_change_user_portrait_newCostume_auto_mount_",
        "is_batch_lobbysetting_cutscene_newCostume_auto_mount_","is_batch_lobbysetting_illust_newCostume_auto_mount_"];
    internal static bool AllowedValue(string name,GamePreferenceParts parts,long owner)
    {
        // Unity's suffix is not an arbitrary wildcard: accept only a decimal hash.
        string key=name;int marker=key.LastIndexOf("_h",StringComparison.Ordinal);
        if(marker>=0) {
            string hash=key[(marker+2)..];if(hash.Length==0||hash.Length>10||!hash.All(char.IsAsciiDigit))return false;
            key=key[..marker];
        }
        if(parts.HasFlag(GamePreferenceParts.Presentation)&&(Presentation.Contains(key)||
            new[]{"PCResolutionWidth_","PCResolutionHeight_"}.Any(p=>key.StartsWith(p,StringComparison.Ordinal)&&
                int.TryParse(key[p.Length..],NumberStyles.None,CultureInfo.InvariantCulture,out _))))return true;
        if(parts.HasFlag(GamePreferenceParts.Gameplay)&&Gameplay.Contains(key))return true;
        return parts.HasFlag(GamePreferenceParts.Personal)&&owner>0&&PersonalPrefixes.Any(p=>key==p+owner.ToString(CultureInfo.InvariantCulture));
    }
    internal static void Validate(GamePreferenceSnapshot snapshot)
    {
        if(snapshot.Schema!=1||!DailyProfiles.ValidKey(snapshot.Account)||snapshot.Parts==0||((int)snapshot.Parts&~7)!=0)
            throw new InvalidDataException("sandbox.preferences.invalid");
        if(snapshot.Parts.HasFlag(GamePreferenceParts.Personal)&&
            (snapshot.Owner<=0||DailyIdentity.PlayerKey(snapshot.Account,snapshot.Owner)!=snapshot.Player))
            throw new InvalidDataException("sandbox.preferences.identity");
        if(!snapshot.Parts.HasFlag(GamePreferenceParts.Personal)&&(snapshot.Owner!=0||snapshot.Tables.Length!=0))
            throw new InvalidDataException("sandbox.preferences.invalid");
        if(snapshot.Tables.Select(t=>t.Name).Distinct().Count()!=snapshot.Tables.Length||snapshot.Values.Select(v=>v.Name).Distinct().Count()!=snapshot.Values.Length)
            throw new InvalidDataException("sandbox.preferences.invalid");
        foreach(var table in snapshot.Tables) {
            if(!Tables.Contains(table.Name)||table.Columns.Length==0||!table.Columns.Contains("ownerIndex")||
                table.Columns.Distinct().Count()!=table.Columns.Length||table.Columns.Any(c=>!Sqlite.Name(c))||table.Rows.Length>50000)
                throw new InvalidDataException("sandbox.preferences.invalid");
            int owner=Array.IndexOf(table.Columns,"ownerIndex");
            foreach(var row in table.Rows)if(row.Length!=table.Columns.Length||!decimal.TryParse(row[owner],NumberStyles.Float,CultureInfo.InvariantCulture,out var id)||id!=snapshot.Owner)
                throw new InvalidDataException("sandbox.preferences.identity");
        }
        foreach(var value in snapshot.Values) {
            if(!AllowedValue(value.Name,snapshot.Parts,snapshot.Owner)||value.Bytes.Length>65536)throw new InvalidDataException("sandbox.preferences.invalid");
            _=Value(value);
        }
    }
    internal static string DataDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Gamfs","BrownDust II","Data");
    internal static RegistryKey OpenRegistry(bool write)=>write?Registry.CurrentUser.CreateSubKey(SessionConstants.RegistrySubKey):
        Registry.CurrentUser.OpenSubKey(SessionConstants.RegistrySubKey)??throw new InvalidDataException("sandbox.preferences.empty");
    internal static GamePreferenceValue[] ReadValues(RegistryKey registry,GamePreferenceParts parts,long owner)=>registry.GetValueNames()
        .Where(n=>AllowedValue(n,parts,owner)).Order(StringComparer.Ordinal).Select(n=>new GamePreferenceValue(n,registry.GetValueKind(n),
            SessionRegistry.EncodeRegistryValue(registry.GetValueKind(n),registry.GetValue(n,null,RegistryValueOptions.DoNotExpandEnvironmentNames)!))).ToArray();
    internal static void WriteValues(RegistryKey registry,GamePreferenceSnapshot snapshot)
    {
        Validate(snapshot);
        foreach(string name in registry.GetValueNames().Where(n=>AllowedValue(n,snapshot.Parts,snapshot.Owner)))registry.DeleteValue(name,false);
        foreach(var value in snapshot.Values)registry.SetValue(value.Name,Value(value),value.Kind);
        registry.Flush();
        var actual=ReadValues(registry,snapshot.Parts,snapshot.Owner);
        if(!ValuesEqual(actual,snapshot.Values))throw new IOException("sandbox.preferences.verify");
    }
    internal static bool ValuesEqual(GamePreferenceValue[] a,GamePreferenceValue[] b)=>a.Length==b.Length&&a.All(v=>
        b.Any(w=>v.Name==w.Name&&v.Kind==w.Kind&&v.Bytes.AsSpan().SequenceEqual(w.Bytes)));
    private static object Value(GamePreferenceValue value)=>value.Kind switch {
        RegistryValueKind.DWord when value.Bytes.Length==4=>BitConverter.ToInt32(value.Bytes),
        RegistryValueKind.QWord when value.Bytes.Length==8=>BitConverter.ToInt64(value.Bytes),
        RegistryValueKind.Binary=>value.Bytes,
        RegistryValueKind.String=>Encoding.UTF8.GetString(value.Bytes),
        _=>throw new InvalidDataException("sandbox.preferences.invalid") };
    internal static GamePreferenceSnapshot Export(string account,string player,GamePreferenceParts parts,string managed,string data,string work,RegistryKey registry)
    {
        long owner=0;int version=0;GamePreferenceTable[] tables=[];
        if(parts.HasFlag(GamePreferenceParts.Personal)) {
            if(!DailyProfiles.ValidKey(player))throw new InvalidDataException("sandbox.preferences.identity");
            var codec=GamePreferenceCodec.Read(managed);string database=Path.Combine(data,codec.FileName);RequireClosedDatabase(database);
            byte[] raw=Read(database);string plain=Path.Combine(work,"source.db");File.WriteAllBytes(plain,codec.Decode(raw));
            try {
                using var sql=new Sqlite(plain,false);sql.Check();var names=ExistingTables(sql);
                // An account may have cleared every favorite. Identify its partition
                // from any owner-scoped table, while exporting only the whitelist.
                var ownerTables=sql.Query("SELECT name FROM sqlite_master WHERE type='table'").Select(r=>r[0]!)
                    .Where(Sqlite.Name).Where(n=>Columns(sql,n).Contains("ownerIndex"));
                var owners=ownerTables.SelectMany(n=>sql.Query("SELECT DISTINCT ownerIndex FROM "+Sqlite.QuoteName(n))).Select(r=>r[0]).Distinct()
                    .Select(s=>decimal.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var x)&&x>0&&x<=long.MaxValue&&decimal.Truncate(x)==x?(long)x:0)
                    .Where(x=>DailyIdentity.PlayerKey(account,x)==player).Distinct().ToArray();
                if(owners.Length!=1)throw new InvalidDataException("sandbox.preferences.identity");owner=owners[0];
                version=Version(sql);tables=names.Select(n=>new GamePreferenceTable(n,Columns(sql,n),
                    sql.Query("SELECT * FROM "+Sqlite.QuoteName(n)+" WHERE ownerIndex="+owner.ToString(CultureInfo.InvariantCulture)))).ToArray();
            } finally {File.Delete(plain);}
        }
        var snapshot=new GamePreferenceSnapshot(1,account,player,parts,owner,version,tables,ReadValues(registry,parts,owner));Validate(snapshot);return snapshot;
    }
    internal static byte[] PrepareDatabase(GamePreferenceSnapshot source,byte[] target,GamePreferenceCodec codec,string work)
    {
        Validate(source);string plain=Path.Combine(work,"target.db");File.WriteAllBytes(plain,codec.Decode(target));
        try {
            using(var sql=new Sqlite(plain,true)) {
                sql.Check();if(Version(sql)!=source.DatabaseVersion||!ExistingTables(sql).Order().SequenceEqual(source.Tables.Select(t=>t.Name).Order()))
                    throw new InvalidDataException("sandbox.preferences.version");
                sql.Execute("BEGIN IMMEDIATE");
                foreach(var table in source.Tables) {
                    if(!Columns(sql,table.Name).SequenceEqual(table.Columns))throw new InvalidDataException("sandbox.preferences.version");
                    string name=Sqlite.QuoteName(table.Name);
                    sql.Execute("DELETE FROM "+name+" WHERE ownerIndex="+source.Owner.ToString(CultureInfo.InvariantCulture));
                    foreach(var row in table.Rows)sql.Execute("INSERT INTO "+name+" ("+string.Join(",",table.Columns.Select(Sqlite.QuoteName))+") VALUES ("+string.Join(",",row.Select(Sqlite.Literal))+")");
                    var actual=sql.Query("SELECT * FROM "+name+" WHERE ownerIndex="+source.Owner.ToString(CultureInfo.InvariantCulture));
                    static string Row(string?[] row)=>System.Text.Json.JsonSerializer.Serialize(row);
                    if(!actual.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(table.Rows.Select(Row).Order(StringComparer.Ordinal)))
                        throw new IOException("sandbox.preferences.verify");
                }
                sql.Check();sql.Execute("COMMIT");
            }
            return codec.Encode(File.ReadAllBytes(plain));
        } finally {if(File.Exists(plain))File.Delete(plain);if(File.Exists(plain+"-journal"))File.Delete(plain+"-journal");}
    }
    private static string[] ExistingTables(Sqlite sql)=>sql.Query("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").Select(r=>r[0]!).Where(Tables.Contains).ToArray();
    private static string[] Columns(Sqlite sql,string table)=>sql.Query("PRAGMA table_info("+Sqlite.QuoteName(table)+")").Select(r=>r[1]!).ToArray();
    private static int Version(Sqlite sql)=>int.Parse(sql.Query("SELECT version FROM ClientDbVersionInfoTable").Single().Single()!,CultureInfo.InvariantCulture);
    internal static void RequireClosedDatabase(string database)
    {
        DailyCacheStorage.NoLinks(database);
        foreach(string suffix in new[]{"-wal","-journal"})if(File.Exists(database+suffix))throw new IOException("sandbox.preferences.busy");
    }
    internal static byte[] Read(string file)
    {
        DailyCacheStorage.NoLinks(file);using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>128*1024*1024)throw new InvalidDataException("sandbox.preferences.format");
        byte[] bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);return bytes;
    }
    internal sealed class Sqlite : IDisposable
    {
        private IntPtr db;
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr db,int flags,IntPtr vfs);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_close(IntPtr db);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Callback(IntPtr arg,int count,IntPtr values,IntPtr names);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,Callback? callback,IntPtr arg,out IntPtr error);
        [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern void sqlite3_free(IntPtr ptr);
        internal Sqlite(string path,bool write){if(sqlite3_open_v2(path,out db,write?2:1,IntPtr.Zero)!=0){Dispose();throw new IOException("sandbox.preferences.database");}}
        internal static bool Name(string value)=>value.Length is >0 and <100&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_');
        internal static string QuoteName(string value)=>Name(value)?"\""+value+"\"":throw new InvalidDataException("sandbox.preferences.invalid");
        internal static string Literal(string? value)=>value==null?"NULL":"'"+value.Replace("'","''")+"'";
        internal void Execute(string sql)=>Run(sql,null);
        internal string?[][] Query(string sql)
        {
            var rows=new List<string?[]>();Exception? failure=null;
            Run(sql,(_,count,values,_)=>{try {
                if(rows.Count>=100000)throw new InvalidDataException("sandbox.preferences.format");
                rows.Add(Enumerable.Range(0,count).Select(i=>Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values,i*IntPtr.Size))).ToArray());return 0;
            }catch(Exception e){failure=e;return 1;}});
            if(failure!=null)throw failure;return rows.ToArray();
        }
        private void Run(string sql,Callback? callback){int rc=sqlite3_exec(db,sql,callback,IntPtr.Zero,out var error);if(error!=IntPtr.Zero)sqlite3_free(error);if(rc!=0)throw new IOException("sandbox.preferences.database");}
        internal void Check(){if(Query("PRAGMA quick_check").Single().Single()!="ok")throw new IOException("sandbox.preferences.database");}
        public void Dispose(){if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}
    }
}
