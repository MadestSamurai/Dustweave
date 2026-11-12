using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Google.Protobuf;
using Proto.Net;
using gamfs;
using gamfs.Thread;
using Proto.Design.common;
using Dustweave.Golden;

namespace BD2Daily.Live {
 [DataContract] internal sealed class GoldenPresetView {
  [DataMember] public int Slot;
  [DataMember] public string Name, Code, Backup, Error;
 }
 [DataContract] internal sealed class GoldenPresetState {
  [DataMember] public long Revision;
  [DataMember] public string Status = "idle", Error = "", Account = "";
  [DataMember] public string Detail = "";
  [DataMember] public bool Dispatched;
  [DataMember] public bool ViewSynchronized;
  [DataMember] public string ViewError = "";
  [DataMember] public string NameError = "";
  [DataMember] public FormationRules Rules;
  [DataMember] public int[] Owned = new int[0];
  [DataMember] public Dictionary<int, PracticeDisplayName> Names = new Dictionary<int, PracticeDisplayName>();
  [DataMember] public GoldenPresetView[] Slots = new GoldenPresetView[0];
 }
 internal static class GoldenPresetNative {
  static GoldenPresetState state = new GoldenPresetState();
  static GoldenBindings binding;
  static Command owner;
  static DateTime deadline;
  static List<PresetDBInfo> before;
  static bool SameActor(Command c) { var f = Bridge.CurrentFrame; return c != null && f != null && f.ProcessId == c.ProcessId && f.ProcessStartTicks == c.ProcessStartTicks && f.AccountKey == c.AccountKey && f.PlayerKey == c.PlayerKey; }
  static bool Pending { get { return state.Status == "reading" || state.Status == "saving"; } }
  internal static GoldenPresetState Capture() {
   if (Pending && (DateTime.UtcNow > deadline || !SameActor(owner))) Fail("goldencode.read_again");
   return state;
  }
  static void Fail(string error) { state.Error = error; state.Status = state.Dispatched ? "uncertain" : "failed"; }
  static bool Current(GoldenPresetState expected, Command command) { return state == expected && Pending && SameActor(command) && Bridge.DailyActionsActive && DateTime.UtcNow < deadline; }
  static void Callback(GoldenPresetState expected, Command c, Action action) {
   if (!Current(expected, c)) { if (state == expected && Pending) Fail("goldencode.read_again"); return; }
   try { action(); } catch (Exception e) { string detail = e.ToString(); state.Detail = detail.Substring(0, Math.Min(4096, detail.Length)); Fail(e.GetBaseException().Message.StartsWith("goldencode.", StringComparison.Ordinal) ? e.GetBaseException().Message : "goldencode.native_failed"); UnityEngine.Debug.LogException(e); }
  }
  internal static void Execute(Command c) {
   Capture();
   if (!SameActor(c) || !Bridge.DailyActionsActive || Pending) throw new InvalidOperationException("goldencode.read_again");
   if (binding == null) binding = new GoldenBindings();
   bool save = c.Kind == "golden_preset_save";
   Formation formation = null; PresetDBInfo original = null;
   if (!save && c.Items.Length > 1) formation = FormationCode.Decode(new string(c.Items.Skip(1).Select(n => checked((char)n)).ToArray()));
   if (save) {
    if (owner == null || !SameActor(owner) || state.Status != "ready" || c.Items[0] != state.Revision) throw new InvalidOperationException("goldencode.read_again");
    formation = FormationCode.Decode(new string(c.Items.Skip(1).Select(n => checked((char)n)).ToArray()));
    original = before.Single(p => p.Slot == c.Value).Clone();
   }
   GoldenBindings.PendingBlessings = formation == null ? new int[0] : formation.Attack.Concat(formation.Defense).ToArray();
   var expected = new GoldenPresetState { Revision = state.Revision + 1, Account = c.AccountKey, Status = save ? "saving" : "reading" };
   state = expected; owner = c; deadline = DateTime.UtcNow.AddSeconds(75);
   // Fresh native preset response precedes every mutation. Never overwrite an edited slot from an old preview.
   binding.ReadPresets(() => Callback(expected, c, () => {
    var slots = binding.Slots();
    if (save && RawKey(slots.Single(p => p.Slot == c.Value)) != RawKey(original)) throw new InvalidOperationException("goldencode.slot_changed");
    int costSeason = binding.SeasonId;
    binding.CostEnsure.Invoke(null, new object[] { (Action<bool>)(ok => Callback(expected, c, () => {
     if (binding.SeasonId != costSeason) throw new InvalidOperationException("goldencode.read_again");
     var rules = binding.Rules(ok); var costumes = binding.Costumes(); var characters = binding.Characters();
     expected.Rules = rules; expected.Owned = costumes.Select(x => x.Id).Distinct().ToArray();
     if (!save) { Complete(slots, rules, costumes); return; }
     FormationImport.Validate(formation, rules, expected.Owned, Now());
     var target = original.Clone(); target.DeckInfo.Clear(); target.BlessInfo.Clear();
     if (string.IsNullOrEmpty(target.PresetName)) target.PresetName = "Golden";
     target.PresetResourceId = Math.Max(1, target.PresetResourceId);
     for (int i = 0; i < formation.Units.Length; i++) {
      var u = formation.Units[i]; var costume = costumes.Single(x => x.Id == u.Costume && x.InvenIndex > 0);
      var character = binding.CharacterFor(u.Costume, characters);
      var deck = new PresetDeckDBInfo { Team = 0, CostumeInvenIndex = costume.InvenIndex, DeckBaseInfo = new DeckDBInfo { CharInvenIndex = character.InvenIndex, Position = u.Position, Sequence = i + 1 } };
      for (int j = 0; j < 5; j++) deck.EquipInfo.Add(new PresetDeckEquipDBInfo { EquipType = j, EquipInvenIndex = 0 });
      target.DeckInfo.Add(deck);
     }
     foreach (var pair in new[] { Tuple.Create(formation.Attack, Define_CommonBattleDeckType.PbdAttack), Tuple.Create(formation.Defense, Define_CommonBattleDeckType.PbdDefense) }) {
      var bless = new PresetDeckBlessDBInfo { DeckType = (int)pair.Item2 }; bless.Id.Add(pair.Item1); target.BlessInfo.Add(bless);
     }
     // Validate once more immediately before save, including schedule rollover during a callback.
     if (binding.SeasonId != costSeason) throw new InvalidOperationException("goldencode.read_again");
     FormationImport.Validate(formation, binding.Rules(ok), binding.Costumes().Select(x => x.Id), Now());
          var finalFrame = Bridge.CurrentFrame;
     if (!Current(expected,c) || !LivePolicy.GameplayReady(finalFrame) ||
      !finalFrame.Surfaces.Any(u => u.InputReady && (u.Type == "MenuUI" || u.Type == "PVPColosseumLobbyUI" || u.Type == "CharPresetPopupUI")) ||
      finalFrame.Surfaces.Any(u => u.Type.StartsWith("BattleUI",StringComparison.Ordinal) || u.Type == "BattleResultUI" || u.Popup && u.Type != "CharPresetPopupUI" && !LivePolicy.PassiveSurface(u.Type)))
      throw new InvalidOperationException("goldencode.open_lobby");
     expected.Dispatched = true;
     binding.Save.Invoke(null, new object[] { target, target.Slot, (Action)(() => Callback(expected, c, () => binding.ReadPresets(() => Callback(expected, c, () => {
      var actual = binding.Slots();
      if (Key(actual.Single(p => p.Slot == target.Slot)) != Key(target) || slots.Where(p => p.Slot != target.Slot).Any(p => RawKey(p) != RawKey(actual.Single(q => q.Slot == p.Slot))))
       throw new InvalidOperationException("goldencode.readback_failed");
      Complete(actual, rules, binding.Costumes()); state.Status = "saved";
     })))) });
    })) });
   }));
  }
  static long Now() { return (DateTime.UtcNow.Ticks - 621355968000000000L) / 10000; }
  static string RawKey(PresetDBInfo value) { return Convert.ToBase64String(value.ToByteArray()); }
  static string Key(PresetDBInfo value) {
   var p = value.Clone(); foreach (var d in p.DeckInfo) { var used = d.EquipInfo.Where(e => e.EquipInvenIndex != 0).Select(e => e.Clone()).ToArray(); d.EquipInfo.Clear(); d.EquipInfo.Add(used); }
   return Convert.ToBase64String(p.ToByteArray());
  }
  static void Complete(List<PresetDBInfo> slots, FormationRules rules, CostumeDBInfo[] costumes) {
   before = slots.Select(p => p.Clone()).ToList();
   // ColosseumPresetInfoResponse updates the protocol list only. CharPresetPopupUI
   // uses a separate integrated view, as in the workbench's preset transaction.
   // A presentation failure must not cause an already verified save to be replayed.
   try { binding.SynchronizeView(slots); state.ViewSynchronized = true; }
   catch (Exception error) { state.ViewError = error.GetBaseException().Message; }
   // A pasted code may use any table costume, including one not owned or not in a
   // saved slot. Publish the small name catalog once, without a request per edit.
   try { state.Names = binding.Names(); }
   catch (Exception error) { state.NameError = error.GetBaseException().Message; }
   state.Slots = slots.OrderBy(p => p.Slot).Select(p => {
    var v = new GoldenPresetView { Slot = p.Slot, Name = p.PresetName, Backup = Convert.ToBase64String(p.ToByteArray()), Code = "", Error = "" };
    var deck = p.DeckInfo.Where(d => d.DeckBaseInfo != null && d.DeckBaseInfo.CharInvenIndex != 0).OrderBy(d => d.DeckBaseInfo.Sequence).ToArray();
    if (deck.Length == 0) return v;
    try {
     if (deck.Any(d => d.Team != 0) || !deck.Select(d => d.DeckBaseInfo.Sequence).SequenceEqual(Enumerable.Range(1, deck.Length))) throw new InvalidOperationException();
     var f = new Formation { Season = rules.Season, Width = rules.Width, Height = rules.Height,
      Units = deck.Select(d => new FormationUnit { Costume = costumes.Single(x => x.InvenIndex == d.CostumeInvenIndex).Id, Position = d.DeckBaseInfo.Position }).ToArray(),
      Attack = p.BlessInfo.Where(b => b.DeckType == (int)Define_CommonBattleDeckType.PbdAttack).SelectMany(b => b.Id).ToArray(),
      Defense = p.BlessInfo.Where(b => b.DeckType == (int)Define_CommonBattleDeckType.PbdDefense).SelectMany(b => b.Id).ToArray() };
     v.Code = FormationCode.Encode(f);
    } catch { v.Error = "goldencode.export_unavailable"; }
    return v;
   }).ToArray(); state.Status = "ready";
  }
 }
 // Resolve current native roles, not obfuscated names or an application-wide version ID.
 internal sealed class GoldenBindings {
  static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
  readonly MethodInfo[] methods;
  readonly MethodInfo read, season, rules, bless, costSnapshot, costDefault, costumeTable;
  readonly PropertyInfo presets;
  object presetMode;
  MethodInfo rebuildView, readView;
  PropertyInfo viewData;
  Dictionary<int, PracticeDisplayName> names;
  readonly FieldInfo costumeField, characterField;
  internal readonly MethodInfo Save, CostEnsure;
  static bool Args(MethodInfo m, params Type[] types) { return m.GetParameters().Select(p => p.ParameterType).SequenceEqual(types); }
  static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);
  internal static bool References(MethodInfo m, MemberInfo member) {
   var body = m.GetMethodBody(); if (body == null || member == null) return false; var bytes = body.GetILAsByteArray();
   for (int p = 0; p < bytes.Length;) {
    short code = bytes[p++]; if (code == 254) code = (short)(0xfe00 | bytes[p++]); var op = Codes[code];
    int size = op.OperandType == OperandType.InlineNone ? 0 : op.OperandType == OperandType.ShortInlineBrTarget || op.OperandType == OperandType.ShortInlineI || op.OperandType == OperandType.ShortInlineVar ? 1 : op.OperandType == OperandType.InlineVar ? 2 : op.OperandType == OperandType.InlineI8 || op.OperandType == OperandType.InlineR ? 8 : op.OperandType == OperandType.InlineSwitch ? 4 + 4 * BitConverter.ToInt32(bytes, p) : 4;
    if ((op.OperandType == OperandType.InlineMethod || op.OperandType == OperandType.InlineField) && m.Module == member.Module && BitConverter.ToInt32(bytes, p) == member.MetadataToken) return true;
    p += size;
   } return false;
  }
  MethodInfo Find(Type request, params Type[] args) { return methods.Single(m => m.ReturnType == typeof(void) && Args(m, args) && References(m, request.GetConstructor(Type.EmptyTypes))); }
  MethodInfo Table(Type type) { return methods.Single(m => m.IsPublic && !m.DeclaringType.IsNested && m.ReturnType == type && Args(m, typeof(int))); }
  internal GoldenBindings() {
   methods = typeof(UIBase).Assembly.GetTypes().SelectMany(t => t.GetMethods(Flags | BindingFlags.DeclaredOnly)).ToArray();
   read = Find(typeof(ColosseumPresetInfoRequest), typeof(Action), typeof(bool)); Save = Find(typeof(ColosseumPresetSaveRequest), typeof(PresetDBInfo), typeof(int), typeof(Action));
   presets = read.DeclaringType.GetProperties(Flags).Single(p => p.PropertyType == typeof(List<PresetDBInfo>) && p.GetIndexParameters().Length == 0);
   costumeField = InventoryField(typeof(Dictionary<long, CostumeDBInfo>), typeof(CostumeInfoResponse).GetProperty("CostumeInfo").GetGetMethod());
   characterField = InventoryField(typeof(Dictionary<long, CharDBInfo>), typeof(CharInfoResponse).GetProperty("CharInfo").GetGetMethod());
   costumeTable = Table(typeof(CostumeTable));
   season = Table(typeof(ColosseumSeasonTable)); rules = Table(typeof(List<ColosseumRuleGroupTable>)); bless = Table(typeof(ColosseumBlessTable));
   var costOwner = methods.Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(Action<bool>) && References(m, typeof(PopularCostumeInfoRequest).GetConstructor(Type.EmptyTypes))).Select(m => m.DeclaringType).Distinct().Single();
   var cm = methods.Where(m => m.DeclaringType == costOwner).ToArray();
   CostEnsure = cm.Single(m => m.IsPublic && m.ReturnType == typeof(void) && Args(m, typeof(Action<bool>)));
   costSnapshot = cm.Single(m => m.IsPublic && m.ReturnType == typeof(Dictionary<int, int>) && Args(m));
   costDefault = cm.Single(m => m.ReturnType == typeof(int) && Args(m) && References(m, typeof(ColosseumDefaultTable).GetProperty("CostDefault").GetGetMethod()));
  }
    FieldInfo InventoryField(Type type, MethodInfo response) {
   return methods.Where(m => m.ReturnType == typeof(bool) && Args(m, typeof(IMessage), typeof(int), typeof(int)) && References(m, response))
    .SelectMany(m => m.DeclaringType.GetFields(Flags).Where(f => f.FieldType == type)).Distinct().Single();
  }
  internal CharDBInfo CharacterFor(int costume, CharDBInfo[] owned) {
   var row = (CostumeTable)costumeTable.Invoke(null, new object[] { costume });
   return owned.Single(c => c.InvenIndex > 0 && BargainTrainingNative.CharacterTable(c.Id).UniqueCharId == row.UseUniqueCharId);
  }
  internal void SynchronizeView(List<PresetDBInfo> expected) {
   if (viewData == null) ResolveView();
   rebuildView.Invoke(null, new[] { presetMode });
   var shown = ((IEnumerable)readView.Invoke(null, new[] { presetMode })).Cast<object>().Select(p => (PresetDBInfo)viewData.GetValue(p, null)).ToArray();
   if (shown.Any(p => p == null) || !shown.OrderBy(p => p.Slot).Select(p => Convert.ToBase64String(p.ToByteArray()))
       .SequenceEqual(expected.OrderBy(p => p.Slot).Select(p => Convert.ToBase64String(p.ToByteArray()))))
    throw new InvalidOperationException("goldencode.view_refresh");
  }
  void ResolveView() {
   // Presentation bindings are optional: losing them must not block a confirmed
   // preset save. Resolve here so Complete can report a recoverable view warning.
   var mode = typeof(UIBase).Assembly.GetTypes().Single(t => t.IsEnum && new[] { "Colosseum", "MonsterHunt", "Normal" }.All(n => Enum.GetNames(t).Contains(n)));
   var rebuild = methods.Single(m => m.ReturnType == typeof(void) && Args(m, mode) && References(m, presets.GetGetMethod(true)));
   var view = methods.Single(m => m.DeclaringType == rebuild.DeclaringType && Args(m, mode) && m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(List<>));
   var data = view.ReturnType.GetGenericArguments()[0].GetProperties(BindingFlags.Public | BindingFlags.Instance).Single(p => p.PropertyType == typeof(PresetDBInfo) && p.GetIndexParameters().Length == 0);
   presetMode = Enum.Parse(mode, "Colosseum"); rebuildView = rebuild; readView = view; viewData = data;
  }
  internal Dictionary<int, PracticeDisplayName> Names() {
   if (names != null) return names;
   var data = Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
   return names = BuildNames(data.GetTableList<CostumeTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON, "CostumeTable"),
    data.GetTableList<CharTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON, "CharTable"), BargainTrainingNative.Name);
  }
  internal static Dictionary<int, PracticeDisplayName> BuildNames(IEnumerable<CostumeTable> costumes, IEnumerable<CharTable> characterRows, Func<int, PracticeDisplayName> lookup) {
   var characters = characterRows
    .Where(c => c.UniqueCharId > 0 && c.CharNameTextId > 0).GroupBy(c => c.UniqueCharId).ToDictionary(g => g.Key, g => g.First().CharNameTextId);
   var result = new Dictionary<int, PracticeDisplayName>();
   foreach (var costume in costumes.Where(c => c.Id > 0)) {
    int nameId; var character = characters.TryGetValue(costume.UseUniqueCharId, out nameId) ? lookup(nameId) : new PracticeDisplayName();
    var title = lookup(costume.CostumeNameTextId);
    Func<string, string, string> join = (a, b) => string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : a + " · " + b;
    result[costume.Id] = new PracticeDisplayName { Cn = join(character.Cn, title.Cn), Tw = join(character.Tw, title.Tw), En = join(character.En, title.En) };
   }
   return result;
  }
  internal int SeasonId { get { return GameScheduleManager.m_Instance.CurrentSeason(8); } }
  internal void ReadPresets(Action done) { read.Invoke(null, new object[] { done, false }); }
  internal List<PresetDBInfo> Slots() {
   var values = ((List<PresetDBInfo>)presets.GetValue(null, null)).OrderBy(p => p.Slot).ToList();
   if (values.Count == 0 || !values.Select(p => p.Slot).SequenceEqual(Enumerable.Range(0, values.Count))) throw new InvalidOperationException("goldencode.read_again");
   return values.Select(p => p.Clone()).ToList();
  }
  internal CostumeDBInfo[] Costumes() { return ((Dictionary<long, CostumeDBInfo>)costumeField.GetValue(null)).Values.ToArray(); }
  internal CharDBInfo[] Characters() { return ((Dictionary<long, CharDBInfo>)characterField.GetValue(null)).Values.ToArray(); }
  internal FormationRules Rules(bool costsReady) {
   var schedule = GameScheduleManager.m_Instance;
   if (schedule == null || !schedule.IsContainScheduleContentId(8)) throw new InvalidOperationException("goldencode.season_unavailable");
   int id = schedule.CurrentSeason(8); var s = (ColosseumSeasonTable)season.Invoke(null, new object[] { id });
   var rows = (List<ColosseumRuleGroupTable>)rules.Invoke(null, new object[] { s.RuleGroupId });
   Func<Define_ColosseumRuleType, int[]> values = type => rows.Single(r => r.RuleType == (int)type).RuleValue.ToArray();
   var size = values(Define_ColosseumRuleType.CRuleDeckGridSizeType);
   var r = new FormationRules { Season = id, Width = size[0], Height = size[1], MaxUnits = values(Define_ColosseumRuleType.CRuleDeckCharMaxCount).Single(),
    Starts = (DateTime.SpecifyKind(schedule.CurrentSeasonStartDateTime(8).AddHours(-9), DateTimeKind.Utc).Ticks - 621355968000000000L) / 10000,
    Ends = (DateTime.SpecifyKind(schedule.CurrentSeasonFinishDateTime(8).AddHours(-9), DateTimeKind.Utc).Ticks - 621355968000000000L) / 10000,
    BannedPositions = values(Define_ColosseumRuleType.CRuleDeckGridUnableIndex), BannedCostumes = values(Define_ColosseumRuleType.CRuleCostumeBan), BannedBlessings = values(Define_ColosseumRuleType.CRuleBlessBan),
    AttackPoints = values(Define_ColosseumRuleType.CRulePreAttackBlessMaxPoint).Single(), DefensePoints = values(Define_ColosseumRuleType.CRulePostAttackBlessMaxPoint).Single() };
   var costRule = rows.SingleOrDefault(row => row.RuleType == 32); r.CostLimit = costRule == null ? -1 : costRule.RuleValue.Single();
   var prices = costsReady ? (Dictionary<int, int>)costSnapshot.Invoke(null, null) : null;
   r.CompleteCosts = prices != null; if (prices != null) { r.Costs = new Dictionary<int, int>(prices); r.DefaultCost = (int)costDefault.Invoke(null, null); }
   // The table lookup remains open-ended: new blessing IDs are resolved when validating a code.
   foreach (int blessing in PendingBlessings.Concat(Slots().SelectMany(p => p.BlessInfo).SelectMany(b => b.Id)).Distinct()) {
    var b = (ColosseumBlessTable)bless.Invoke(null, new object[] { blessing }); if (b != null && b.Id == blessing) r.BlessingCosts[blessing] = b.UsePoint;
   }
   return r;
  }
  internal static int[] PendingBlessings = new int[0];
 }
}
