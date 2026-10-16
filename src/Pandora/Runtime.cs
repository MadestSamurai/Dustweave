using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using Newtonsoft.Json;
using BD2.LocalIpc;

namespace Dustweave.Pandora.Runtime
{
    [DefaultExecutionOrder(-19900)]
    public sealed partial class Engine : MonoBehaviour
    {
        const string HarmonyId = "dustweave.pandora.observation";
        static Engine current;
        Control control = new Control();
        readonly TransitionGate gate = new TransitionGate();
        readonly RoundEntry entry = new RoundEntry();
        readonly BuildRestart buildRestart = new BuildRestart();
        ChallengeChoice buildChoice;
        object selectedCharacter;
        SurvivalPauseUI buildPause;
        readonly RegionPlanner regions = new RegionPlanner();
        readonly BossDamageModel bossDamage = new BossDamageModel();
        readonly MovementProfile movementProfile = new MovementProfile();
        double predictedNormalSpeed, predictedAxialSpeed;
        readonly CombatTactics tactics = new CombatTactics();
        TacticalDecision lastTactic; SkillOption ultimateSkill; UltimateSkillButton ultimateButton;
        readonly Dictionary<int, Vector3> positions = new Dictionary<int, Vector3>();
        readonly Dictionary<int, Pattern> patterns = new Dictionary<int, Pattern>();
        readonly List<Threat> threats = new List<Threat>();
        readonly List<Drop> loot = new List<Drop>();
        SurvivalItem[] healingItems = new SurvivalItem[0];
        readonly List<AttackArea> warnings = new List<AttackArea>();
        List<SkillOption> catalog;
        Stage[] stages = new Stage[0]; Stage target;
        CharacterChoice[] characters = new CharacterChoice[0];
        EventMiniGameSurvivalManager manager; PlayerController avatar;
        UIBase[] uis = new UIBase[0]; SurvivalUnit[] units = new SurvivalUnit[0]; EventMiniGameEnemyProjectile[] projectiles = new EventMiniGameEnemyProjectile[0];
        Point direction; bool ownsMove, handingOff, faulted, retryExpected;
        SurvivalResultUI retryResult;
        int resultRevision, retryRevision;
        int rounds, lastResult, lastManager, planCount, skillRevision; string reason = "ready", detail = "", lastUi = "";
        int acknowledgedSkillRevision = -1, acknowledgedSkillUi, closingSkillRevision = -1;
        float nextControl, nextObserve, nextPlan, nextWrite, nextUi, lastThreatTime, waitSince;
        double hp, maxHp, seconds, planMs; int level;
        double combatTime, weaponRange = 6; Route lastRoute; int weaponRevision = -1;
        bool wasPlaying;
        readonly Queue<object> hitEvents = new Queue<object>(); int hitSequence;
        sealed class Pattern { public object Table, Routine; public Vector3 End; public bool Locked, JumpMoved, JumpStopped, JumpFinished, Recovered; public double Range, Speed; }
        readonly Dictionary<int, AnimationEvent[]> animationEvents = new Dictionary<int, AnimationEvent[]>();
        public static object Create() { var host = new GameObject("Dustweave Pandora"); UnityEngine.Object.DontDestroyOnLoad(host); return host.AddComponent<Engine>(); }
        public void PrepareHandoff() { handingOff = true; control.Enabled = false; Release(); }
        public string HandoffBusy() { return ""; } // Native UI owns any outstanding request and completes it without this observer.
        public void Stop() { PrepareHandoff(); UnityEngine.Object.DestroyImmediate(gameObject); }
        bool Live { get { return !handingOff && !faulted && control.Enabled && control.Valid(DateTime.UtcNow.Ticks); } }
        bool Playing { get { return manager != null && Native.B(manager, "IsPlaying"); } }
        bool Paused { get { return manager != null && Native.B(manager, "IsPause"); } }
        object Player { get { return manager == null ? null : Native.Get(manager, "Player"); } }
        static Point Flat(Vector3 v) { return new Point(v.x, v.z); }
        void Awake() { current = this; var harmony = new Harmony(HarmonyId); foreach (string name in new[] { "EnableMoving", "DisableMoving", "AttackJumpStart", "AttackJumpEnd" }) harmony.Patch(typeof(SurvivalBossMonster).GetMethod(name), postfix: new HarmonyMethod(typeof(Engine), nameof(JumpEventObserved))); for (int i = 0; i < 3; i++) harmony.Patch(Native.Bound("bossPattern" + i), postfix: new HarmonyMethod(typeof(Engine), nameof(PatternObserved))); harmony.Patch(Native.Bound("bossSpawn"), postfix: new HarmonyMethod(typeof(Engine), nameof(BossSpawned))); harmony.Patch(Native.Bound("itemApplied"), postfix: new HarmonyMethod(typeof(Engine), nameof(ItemApplied))); harmony.Patch(typeof(SurvivalSelectUI).GetMethod("SetSkillSlots"), postfix: new HarmonyMethod(typeof(Engine), nameof(SkillPageObserved))); harmony.Patch(Native.Bound("skillApplied"), postfix: new HarmonyMethod(typeof(Engine), nameof(SkillApplied))); harmony.Patch(typeof(SurvivalResultUI).GetMethod("Initalize"), postfix: new HarmonyMethod(typeof(Engine), nameof(ResultPageObserved))); harmony.Patch(Native.Bound("playerDamaged"), prefix: new HarmonyMethod(typeof(Engine), nameof(DamageStarting)), postfix: new HarmonyMethod(typeof(Engine), nameof(DamageFinished))); }
        public static void DamageStarting(object __instance, out double __state)
        {
            __state = -1;
            if (current == null || !current.Live || !ReferenceEquals(__instance, current.Player)) return;
            try { __state = Native.D(Native.Call("health", null), "MiniGameHP"); } catch { }
        }
        public static void DamageFinished(int __0, bool __1, double __state)
        {
            if (current == null || __state < 0) return;
            // Read immediately around the game's own damage application. Periodic HP
            // snapshots can hide a hit followed by healing. Never alter the damage.
            try {
                double after = Native.D(Native.Call("health", null), "MiniGameHP");
                if(after<__state) current.nextObserve=current.nextPlan=0;
                if(!current.control.TraceCombat) return;
                current.hitEvents.Enqueue(new { Sequence = ++current.hitSequence, Time = current.combatTime,
                    Requested = __0, SourceId = contactSource, Knockback = __1, Before = __state, After = after, Applied = Math.Max(0, __state - after),
                    Position = current.avatar == null ? new Point() : Flat(current.avatar.transform.position),
                    Bosses = current.units.OfType<SurvivalBossMonster>().Where(b => b != null && !b.IsDead()).Select(b => new { Id = b.GetInstanceID(), Position = Flat(b.transform.position) }).ToArray() });
                while (current.hitEvents.Count > 32) current.hitEvents.Dequeue();
                current.nextWrite = 0;
            } catch { } // Optional instrumentation cannot interfere with native combat.
        }
        public static void ItemApplied(object __instance, int __0)
        {
            if (current == null || !current.Live || !ReferenceEquals(__instance, current.Player)) return;
            var table = Native.ItemTable(__0); int kind = Native.I(table, "ItemType");
            if (kind != 2 && kind != 6) return;
            current.tactics.ItemApplied(kind, Native.D(table, "ItemValue"), current.combatTime);
            current.nextObserve = current.nextPlan = 0;
        }
        public static void BossSpawned(SurvivalBossMonster __instance) { if (current == null) return; int id = __instance.GetInstanceID(); current.bossDamage.Forget(id); current.patterns.Remove(id); current.positions.Remove(id); current.nextObserve = current.nextPlan = 0; }
        public static void ResultPageObserved() { if (current != null) current.resultRevision++; }
        public static void SkillPageObserved() { if (current != null) { current.skillRevision++; current.closingSkillRevision = -1; } }
        public static void SkillApplied(SurvivalSelectUI __instance) { if (current != null) { current.acknowledgedSkillRevision = current.skillRevision; current.acknowledgedSkillUi = __instance.GetInstanceID(); } }
        public static void PatternObserved(SurvivalBossMonster __instance, object __0, object __result) { if (current == null) return; current.patterns[__instance.GetInstanceID()] = new Pattern { Table = __0, Routine = __result }; current.nextObserve = current.nextPlan = 0; }
        public static void JumpEventObserved(SurvivalBossMonster __instance, System.Reflection.MethodBase __originalMethod)
        {
            Pattern p;
            if (current == null || !current.patterns.TryGetValue(__instance.GetInstanceID(), out p) || Native.I(p.Table, "AttackType") != 2) return;
            if (__originalMethod.Name == "EnableMoving") p.JumpMoved = true;
            if (__originalMethod.Name == "DisableMoving" && p.JumpMoved) p.JumpStopped = true;
            if (__originalMethod.Name == "AttackJumpEnd") p.JumpFinished = true;
            current.nextPlan = current.nextWrite = 0;
        }
        void Update()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now >= nextControl) { nextControl = now + .1f; ReadControl(); }
                if (now >= nextObserve) { nextObserve = now + .3f; Observe(); if (Live) Act(); else Release(); }
                bool playing = Playing;
                if (playing && !wasPlaying) ResetCombat();
                wasPlaying = playing;
                if (Live && playing && !Paused) { combatTime += Time.deltaTime; movementProfile.Observe(Time.deltaTime); }
                if (Live && target != null && Playing && !Paused && avatar != null && hp > 0 && !entry.Pending && !buildRestart.Pending && !LoopRules.WaitForEquipment(control, uis.Any(x => x is SurvivalSelectUI)))
                {
                    if (now >= nextPlan) { nextPlan = now + (units.Any(x => x is SurvivalBossMonster) ? .075f : .2f); PlanMove(); }
                    if (ownsMove) { var move = Native.BoundField(avatar, "move"); Native.Call("direction", move, new Vector3((float)direction.X, 0, (float)direction.Z)); avatar.SetMoveStart(); }
                }
                else Release();
                if (!Live && !faulted) reason = control.Enabled ? "lease-expired" : "paused";
                if (now >= nextWrite) { nextWrite = now + (control.TraceCombat && Playing ? .1f : .5f); Snapshot(); }
            }
            catch (Exception e) { Fail("runtime-error", e.ToString()); }
        }
        void ReadControl()
        {
            if (handingOff) return; var bytes = RuntimeFiles.Read(Path.Combine(Loader.Root, "control.json")); if (bytes == null) return;
            var next = JsonConvert.DeserializeObject<Control>(System.Text.Encoding.UTF8.GetString(bytes)); if (next == null || !next.Valid(DateTime.UtcNow.Ticks)) return;
            if (next.RunId != control.RunId) { faulted = false; detail = ""; target = null; selectedCharacter = null; catalog = null; gate.Reset(); retryExpected = false; retryResult = null; entry.Reset(); buildRestart.Reset(); buildPause = null; buildChoice = null; rounds = 0; lastResult = 0; regions.Reset(); ResetCombat(); reason = "entering"; waitSince = Time.realtimeSinceStartup; }
            control = next;
        }
        void Observe()
        {
            var next = Native.Find<EventMiniGameSurvivalManager>(); if (next != manager) { Release(); manager = next; regions.Reset(); ResetCombat(); }
            uis = UnityEngine.Object.FindObjectsOfType<UIBase>().Where(x => x.gameObject.activeInHierarchy).ToArray();
            var main = uis.OfType<SurvivalMainUI>().FirstOrDefault();
            if (main != null && !main.IsAnimation())
            {
                stages = Native.Stages(main);
                var characterPanel = main.GetComponentInChildren<SurvivalCharSelectUI>(true);
                if (characterPanel != null) characters = Native.Characters(characterPanel);
            }
            if (manager == null || Player == null) { Release(); avatar = null; units = new SurvivalUnit[0]; projectiles = new EventMiniGameEnemyProjectile[0]; return; }
            avatar = Native.Get(Native.FieldManager, "PlayerController") as PlayerController;
            var health = Native.Call("health", null); if (health == null) { Release(); avatar = null; return; }
            hp = Native.D(health, "MiniGameHP"); maxHp = Native.D(health, "MiniGameMaxHP");
            level = Convert.ToInt32(Native.Call("level", Player)); var clock = Native.Get(manager, "TimeManager"); seconds = clock == null ? 0 : Convert.ToDouble(Native.Call("time", clock)) / 1000;
            int charGroup = Convert.ToInt32(Native.BoundField(Player, "playerCharGroup")), charId = Convert.ToInt32(Native.BoundField(Player, "playerCharId"));
            selectedCharacter = new { Group = charGroup, Id = charId, Name = characters.FirstOrDefault(c => c.Group == charGroup && c.Id == charId)?.Name ?? "" };
            if (manager.GetInstanceID() != lastManager) { lastManager = manager.GetInstanceID(); catalog = null; lastThreatTime = Time.realtimeSinceStartup; }
            if (avatar == null) return;
            units = UnityEngine.Object.FindObjectsOfType<SurvivalUnit>().Where(x => x.gameObject.activeInHierarchy && !x.IsDead()).OrderByDescending(x => x is SurvivalBossMonster).ThenBy(x => (x.transform.position - avatar.transform.position).sqrMagnitude).Take(104).ToArray();
            if (units.Any(x => x is SurvivalBossMonster) && weaponRevision != skillRevision)
            {
                // Table ranges are only a cold-start prior. Measured damage bands take over.
                // Native enumeration order is not a weapon/passive contract. Resolve the
                // actual skill type; otherwise passive slots can silently produce a default range.
                var weapons = Native.Items(Native.Call("weapons", Player)).Concat(Native.Items(Native.Call("passives", Player))).Select(o => Native.Skill(Convert.ToInt32(Native.BoundField(o, "ownedId")), Convert.ToInt32(Native.BoundField(o, "ownedLevel")))).Where(w => w.Type == 1 && w.Range > 0).OrderBy(w => w.Range).ToArray();
                var context = new SkillContext { Boss = true }; double total = weapons.Sum(w => SkillPlanner.WeaponPower(w, context)), cumulative = 0;
                weaponRange = 6;
                foreach (var weapon in weapons) { cumulative += SkillPlanner.WeaponPower(weapon, context); if (cumulative >= total * .5) { weaponRange = Math.Max(3, Math.Min(9, weapon.Range)); break; } }
                weaponRevision = skillRevision;
            }
            projectiles = UnityEngine.Object.FindObjectsOfType<EventMiniGameEnemyProjectile>().Where(x => x.gameObject.activeInHierarchy).OrderBy(x => (x.transform.position - avatar.transform.position).sqrMagnitude).Take(80).ToArray();
            ultimateButton = Native.Find<UltimateSkillButton>();
            ultimateSkill = ultimateButton == null || !Playing || hp <= 0 ? null : Native.Ultimate(Player);
            loot.Clear(); double magnet = Native.Stat(Player, "MagnetRange");
            var available = new List<Drop>();
            var items=UnityEngine.Object.FindObjectsOfType<SurvivalItem>();
            var healing=new List<SurvivalItem>();
            foreach (var drop in items)
            {
                if (!drop.gameObject.activeInHierarchy) continue;
                var table = Native.ItemTable(Native.I(drop, "FieldRewardObjectId")); if (table == null) continue;
                if(Native.I(table,"ItemType")==0)healing.Add(drop);
                int kind = Native.I(table, "ItemType");bool following=Native.BoundField(drop,"itemTarget") as Transform != null;
                if(following&&kind!=6)continue;
                var collider = drop.GetComponent<Collider>(); Vector3 center; double radius = Native.Footprint(collider, out center);
                available.Add(new Drop { Id = drop.GetInstanceID(), Position = Flat(drop.transform.position), Type = kind, Amount = Native.D(table, "ItemValue"), PickupRadius = Planner.PickupRadius(kind, magnet, radius + .45),Following=following });
            }
            healingItems=healing.ToArray();
            // Hundreds of ordinary drops must not hide a nearby rescue item.
            var sorted = available.OrderBy(x => Point.Distance(Flat(avatar.transform.position), x.Position)).ToArray();
            loot.AddRange(sorted.Where(x => x.Type != 3 && x.Type != 4).Take(32));
            loot.AddRange(sorted.Where(x => x.Type == 3 || x.Type == 4).Take(256));
        }
        void Act()
        {
            var main = uis.OfType<SurvivalMainUI>().FirstOrDefault(); var result = uis.OfType<SurvivalResultUI>().FirstOrDefault();
            var popup = uis.OfType<SurvivalDefaultPopupUI>().FirstOrDefault(); var select = uis.OfType<SurvivalSelectUI>().FirstOrDefault(); var reward = uis.OfType<SurvivalRewardPopupUI>().FirstOrDefault();
            var hub = uis.OfType<MiniGameHubUI>().FirstOrDefault();
            Component panel = main == null ? null : Native.BoundField(main, "panel") as Component;
            if (target == null && Playing && Native.ActiveParam != null)
                target = control.Mode == "Currency" && Native.ActiveStage == control.StageId && Native.ActiveGroup == control.StageGroup ||
                    control.Mode == "Challenge" && Native.B(manager, "IsInifityMode")
                    ? new Stage { Id = Native.ActiveStage, Group = Native.ActiveGroup, Challenge = Native.B(manager, "IsInifityMode") } : null;
            string identity = popup != null ? "confirm:" + popup.GetInstanceID() : result != null ? "result:" + result.GetInstanceID() : select != null ? LoopRules.SkillPage(select.GetInstanceID(), level, skillRevision) : reward != null ? "reward:" + reward.GetInstanceID() : panel != null ? panel.GetType().Name + ":" + panel.GetInstanceID() : hub != null ? "hub:" + hub.GetInstanceID() : Playing ? "playing" : "waiting";
            if (identity != lastUi) { lastUi = identity; waitSince = Time.realtimeSinceStartup; }
            if (entry.Pending)
            {
                Release();
                var action = entry.Observe(Time.realtimeSinceStartup, Playing && hp > 0,
                    result == null ? 0 : result.GetInstanceID(), resultRevision, popup != null, main != null,
                    SameStage(), Native.RetrySelected(result), result != null && result.CanCloseUI());
                if (action == EntryAction.Started)
                {
                    retryResult = null; lastResult = 0; gate.Reset(); regions.Reset(); ResetCombat(); catalog = null;
                }
                else
                {
                    reason = "entering";
                    // Finish the already-selected native callback, without submitting
                    // another retry or bypassing animation / CanCloseUI guards.
                    if (action == EntryAction.CloseConfirmedResult) result.CloseUI();
                    else if (action == EntryAction.Blocked) Fail("entry-blocked", result != null ? "retry-result-not-closed" : "awaiting-round");
                    return;
                }
            }
            if (select != null && Convert.ToBoolean(Native.BoundField(select,"skillBusy")) && closingSkillRevision != skillRevision &&
                (acknowledgedSkillRevision == skillRevision && acknowledgedSkillUi == select.GetInstanceID() || Native.SkillAlreadyApplied(select, Player)) && Native.Ready(select) && select.CanCloseUI())
            {
                // The response already updated native inventory, but CloseUI can be
                // rejected while an underlying page is animating. Never resubmit a skill.
                closingSkillRevision = skillRevision; select.CloseUI(); reason = "upgrading"; Sent(identity); return;
            }
            // A human choice is not a stalled automation request. Keep the
            // control lease alive, release movement and resume when the UI closes.
            if (gate.ManualChoice(control, select != null && popup == null && result == null))
            {
                Release(); waitSince = Time.realtimeSinceStartup;
                reason = Convert.ToBoolean(Native.BoundField(select, "skillBusy")) ? "upgrading" : "manual-equipment";
                return;
            }
            if (HandleBuildRestart(main, result, popup, select, reward)) return;
            bool timedOut; if (gate.Waiting(identity, Time.realtimeSinceStartup, out timedOut)) { if (timedOut) Fail("transition-timeout", identity); return; }
            if (Time.realtimeSinceStartup < nextUi) return;
            if (hub != null && main == null && !Playing) { reason = "entering"; if (Native.OpenFromHub(hub)) Sent(identity); else if (Time.realtimeSinceStartup - waitSince > 15) Fail("open-game", "Pandora entry unavailable"); return; }
            if (popup != null)
            {
                Release();
                if (!retryExpected || retryResult == null || result != null && retryResult != result || retryRevision != resultRevision || !SameStage() || !Native.RetryPopup(popup, retryResult))
                { Fail("confirm-manually", popup.GetType().Name); return; }
                if (Native.Ready(popup) && popup.CanCloseUI())
                {
                    int resultId = retryResult.GetInstanceID();
                    if (Native.Click(popup, "_buttonOk")) { retryExpected = false; entry.Begin(Time.realtimeSinceStartup, resultId, resultRevision); Sent(identity); }
                }
                else if (Time.realtimeSinceStartup - waitSince > 45) Fail("transition-timeout", identity);
                return;
            }
            if (result != null)
            {
                Release(); if (lastResult != result.GetInstanceID()) { lastResult = result.GetInstanceID(); rounds++; }
                if (!Native.Ready(result) || !result.CanCloseUI()) { reason = "settling"; if (Time.realtimeSinceStartup - waitSince > 45) Fail("transition-timeout", identity); return; }
                // Retry is valid only for a stage verified during this run. A different round exits to selection.
                if (SameStage() && SameCharacter() && Native.Click(result, "_objRetryButton")) { retryExpected = true; retryResult = result; retryRevision = resultRevision; reason = "retrying"; Sent(identity); return; }
                if (Native.Click(result, "_objExitButton")) { reason = "selecting"; Sent(identity); } else if (Time.realtimeSinceStartup - waitSince > 45) Fail("transition-timeout", identity); return;
            }
            if (main != null)
            {
                Release(); if (main.IsAnimation()) { reason = "selecting"; return; }
                target = LoopRules.Select(stages, control.Mode, control.StageGroup, control.StageId); if (target == null) { Fail("stage-unavailable", control.StageGroup + "/" + control.StageId); return; }
                if (panel is SurvivalMenuUI) { if (Native.Click(panel, "_objEnterButton")) Sent(identity); }
                else if (panel is SurvivalMapSelectUI)
                {
                    var map = (SurvivalMapSelectUI)panel; int index = Convert.ToInt32(Native.BoundField(map, "mapIndex"));
                    if (index != target.Index) { if (Native.Click(map, index < target.Index ? "_objNextButton" : "_objPrevButton")) nextUi = Time.realtimeSinceStartup + .15f; return; }
                    var selected = Native.BoundField(map, "difficulty");
                    if (selected == null || Convert.ToInt32(Native.BoundField(selected, "difficultyId")) != target.Id)
                    {
                        var buttons = map.GetComponentsInChildren<SurvivalDifficultyButton>().Where(x => x.gameObject.activeInHierarchy && !x.IsLock());
                        var choice = buttons.FirstOrDefault(x => Convert.ToInt32(Native.BoundField(x, "difficultyId")) == target.Id);
                        if (choice == null) { Fail("stage-unavailable", target.Id.ToString()); return; }
                        if (Native.Click(map, choice.gameObject)) nextUi = Time.realtimeSinceStartup + .15f; return;
                    }
                    if (Native.Click(map, "_buttonEnter")) Sent(identity);
                }
                else if (panel is SurvivalCharSelectUI)
                {
                    // Always revisit selection once per run; otherwise a manually-open character page could refer to another stage.
                    var map = Native.Field(main, "_selectMapUI"); var selected = Native.BoundField(map, "difficulty");
                    if (Convert.ToInt32(Native.BoundField(map, "mapGroup")) != target.Group || selected == null || Convert.ToInt32(Native.BoundField(selected, "difficultyId")) != target.Id) { main.OpenSelectMap(); Sent(identity); return; }
                    var characters = (SurvivalCharSelectUI)panel;
                    if (!LoopRules.UsesGameCharacter(control))
                    {
                        var wanted = LoopRules.SelectCharacter(Native.Characters(characters), control);
                        if (wanted == null) { Fail("character-unavailable", control.CharacterGroup + "/" + control.CharacterId); return; }
                        var slots = (SurvivalCharSlot[])Native.Field(characters, "_characterSlots");
                        var choice = slots.FirstOrDefault(s => { if (s == null || !s.gameObject.activeInHierarchy || s.IsDisable()) return false; int g, i; s.GetCharInfo(out g, out i); return g == wanted.Group && i == wanted.Id; });
                        if (choice == null) { Fail("character-unavailable", wanted.Group + "/" + wanted.Id); return; }
                        int currentGroup, currentId; characters.GetSelectedCharInfo(out currentGroup, out currentId);
                        if (!LoopRules.CharacterMatches(control, currentGroup, currentId))
                        {
                            if (Native.Click(characters, choice.gameObject)) nextUi = Time.realtimeSinceStartup + .15f;
                            return;
                        }
                        selectedCharacter = wanted;
                    }
                    if (Native.Click(panel, "_objEnterButton")) { entry.Begin(Time.realtimeSinceStartup); Sent(identity); }
                }
                reason = "selecting"; if (Time.realtimeSinceStartup - waitSince > 30) Fail("entry-blocked", identity); return;
            }
            if (select != null)
            {
                reason = "upgrading"; if (Native.Ready(select) && select.CanCloseUI() && !Convert.ToBoolean(Native.BoundField(select, "skillBusy"))) { ChooseSkill(select); Sent(identity); }
                else if (Time.realtimeSinceStartup - waitSince > 25) Fail("upgrade-timeout", identity);
                return;
            }
            if (reward != null) { if (Native.Ready(reward) && reward.CanCloseUI()) { reward.CloseUI(); Sent(identity); } else if (Time.realtimeSinceStartup - waitSince > 25) { Fail("upgrade-timeout", identity); return; } reason = "upgrading"; return; }
            if (Playing)
            {
                if (target == null || Native.ActiveStage != target.Id || Native.ActiveGroup != target.Group) { Fail("wrong-stage", Native.ActiveStage.ToString()); return; }
                if (Paused) { reason = "game-paused"; return; }
                reason = lastTactic != null && lastTactic.CornerHold ? "corner-hold" : "playing";
                return;
            }
            reason = "open-game"; if (Time.realtimeSinceStartup - waitSince > 45) Fail("open-game", identity);
        }
        void Sent(string id) { gate.Sent(id, Time.realtimeSinceStartup); nextUi = Time.realtimeSinceStartup + .12f; }
        bool SameStage() { return target != null && Native.ActiveParam != null && Native.ActiveStage == target.Id && Native.ActiveGroup == target.Group; }
        bool SameCharacter() { return LoopRules.UsesGameCharacter(control) || Player != null && LoopRules.CharacterMatches(control,
            Convert.ToInt32(Native.BoundField(Player, "playerCharGroup")), Convert.ToInt32(Native.BoundField(Player, "playerCharId"))); }
        bool HandleBuildRestart(SurvivalMainUI main, SurvivalResultUI result, SurvivalDefaultPopupUI popup, SurvivalSelectUI select, SurvivalRewardPopupUI reward)
        {
            if (!buildRestart.Pending) return false;
            Release();
            // Death and a completed exit use the existing normal result/entry loop.
            if (main != null || result != null || uis.Any(x => x is MiniGameHubUI))
            { buildRestart.Reset(); buildPause = null; gate.Reset(); catalog = null; return false; }
            if (buildRestart.Expired(Time.realtimeSinceStartup)) { Fail("build-restart-blocked", buildRestart.Phase.ToString()); return true; }
            reason = "build-restarting";
            // The accepted native exit may destroy its manager and pause before the
            // confirmation animation finishes. Wait for selection; never confirm again.
            if (buildRestart.Phase == BuildRestartPhase.Confirmed) return true;
            if (manager == null || !buildRestart.Owns(manager.GetInstanceID(), control.Mode == "Challenge", control.AutoSelectEquipment, SameStage()))
            { Fail("build-restart-blocked", "round-or-control-changed"); return true; }
            // Complete an outstanding native upgrade before opening pause. Never discard a selection or its response.
            if (select != null || reward != null) return false;
            var pause = uis.OfType<SurvivalPauseUI>().FirstOrDefault();
            if (popup != null)
            {
                if (!buildRestart.CanConfirm(manager.GetInstanceID(), pause == null ? 0 : pause.GetInstanceID(), Native.ExitPopup(popup, buildPause)))
                { Fail("confirm-manually", "unowned-build-exit"); return true; }
                if (Native.Ready(popup) && popup.CanCloseUI() && Native.Click(popup, "_buttonOk")) buildRestart.Phase = BuildRestartPhase.Confirmed;
                return true;
            }
            if (pause != null)
            {
                if (buildRestart.Phase == BuildRestartPhase.PauseSent && ReferenceEquals(Native.BoundField(pause, "pauseManager"), manager) && Native.Ready(pause) && pause.CanCloseUI())
                {
                    buildPause = pause; buildRestart.PauseId = pause.GetInstanceID();
                    if (Native.Click(pause, "_objExitButton")) buildRestart.Phase = BuildRestartPhase.ExitSent;
                }
                return true;
            }
            if (buildRestart.Phase == BuildRestartPhase.Requested && Playing && !Paused && hp > 0)
            {
                var hud = Native.Find<UI.EventMainUI.EventMiniGame.Survival.EventMiniGameSurvivalHUD>();
                if (hud != null && ReferenceEquals(Native.BoundField(hud, "hudManager"), manager))
                { hud.Pause(); buildRestart.Phase = BuildRestartPhase.PauseSent; }
            }
            return true;
        }
        void ChooseSkill(SurvivalSelectUI select)
        {
            if (catalog == null)
            {
                var character = Native.Call("character", null, Convert.ToInt32(Native.BoundField(Player, "playerCharGroup")), Convert.ToInt32(Native.BoundField(Player, "playerCharId")));
                int starter = Native.I(character, "CharSkillGroupId"), ultimate = Native.I(character, "CharUltimateSkillGroupId");
                // A mini-game group is not a character identity. Use the selected
                // character's actual native starter/ultimate associations.
                catalog = Native.Items(Native.Call("skillGroups", null)).Where(g => Native.I(g, "UniqueCharGroupId") == 0 || Native.I(g, "Id") == starter || Native.I(g, "Id") == ultimate)
                    .SelectMany(g => Enumerable.Range(1, Native.I(g, "MaxLevel")).Select(n => Native.Skill(Native.I(g, "Id"), n))).ToList();
            }
            var table = manager.GetFieldMiniGameSurvivalTable(); var c = new SkillContext
            {
                FarmCurrency = control.Mode == "Currency",
                Hp = hp,
                MaxHp = maxHp,
                Seconds = seconds,
                Boss = units.Any(x => x is SurvivalBossMonster),
                Capacity = table.SkillCapacity,
                PlayerAttackInterval = table.PlayerAttackInterval,
                Catalog = catalog,
                Cooldown = Native.Stat(Player, "Cooltime"),
                Duration = Native.Stat(Player, "SkillDuration"),
                ProjectileSpeed = Native.Stat(Player, "ProjectileSpeed"),
                Defence = Native.Stat(Player, "Defence"),
                Recovery = Native.Stat(Player, "HpRecovery"),
                ExtraProjectiles = (int)Native.Stat(Player, "ProjectileCount", true)
            };
            foreach (var own in Native.Items(Native.Call("weapons", Player)).Concat(Native.Items(Native.Call("passives", Player)))) c.Owned.Add(Native.Skill(Convert.ToInt32(Native.BoundField(own, "ownedId")), Convert.ToInt32(Native.BoundField(own, "ownedLevel"))));
            var choices = ((SurvivalCardItem[])Native.Field(select, "_slots")).Where(x => x.gameObject.activeInHierarchy).Select(card => new { Card = card, Skill = Native.Skill(Convert.ToInt32(Native.BoundField(card, "skillId")), Convert.ToInt32(Native.BoundField(card, "skillLevel"))) }).ToArray();
            if (choices.Length == 0) throw new InvalidOperationException("Pandora upgrade choices unavailable");
            if (control.Mode == "Challenge" && Native.B(manager, "IsInifityMode") && control.AutoSelectEquipment && !buildRestart.Pending)
            {
                int rerolls = Convert.ToInt32(Native.BoundField(select, "skillRerolls"));
                var rerollButton = Native.Field(select, "_objRerollButton") as GameObject;
                buildChoice = ChallengeBuildPlanner.Choose(choices.Select(x => x.Skill).ToArray(), c,
                    rerollButton != null && rerollButton.activeInHierarchy ? rerolls : 0, true);
                if (buildChoice.Action == "reroll")
                {
                    if (!Native.Click(select, rerollButton)) throw new InvalidOperationException("Pandora reroll control unavailable");
                    reason = "build-rerolling"; detail = buildChoice.Reason; return;
                }
                var chosen = choices.Single(x => x.Skill.Id == buildChoice.Id && x.Skill.Level == buildChoice.Level);
                select.OnClickUI(chosen.Card.gameObject); detail = chosen.Skill.Name;
                if (buildChoice.Action == "restart") buildRestart.Request(manager.GetInstanceID(), Time.realtimeSinceStartup);
                return;
            }
            // Preserve the existing currency policy and its original tie order exactly.
            var ordinary = choices.OrderByDescending(x => SkillPlanner.Evaluate(x.Skill, c).Score).First();
            select.OnClickUI(ordinary.Card.gameObject); detail = ordinary.Skill.Name;
        }
        void PlanMove()
        {
            // A pending result/entry/upgrade click never blocks movement after the game has resumed.
            var watch = System.Diagnostics.Stopwatch.StartNew(); float now = Time.realtimeSinceStartup; double dt = Math.Max(.02, now - lastThreatTime); lastThreatTime = now; threats.Clear(); warnings.Clear();
            foreach (var unit in units)
            {
                if (unit == null || unit.IsDead() || !unit.gameObject.activeInHierarchy) continue; Vector3 pos = unit.transform.position, old; var velocity = positions.TryGetValue(unit.GetInstanceID(), out old) ? Vector3.ClampMagnitude((pos - old) / (float)dt, unit is SurvivalBossMonster ? 100 : 15) : Vector3.zero; positions[unit.GetInstanceID()] = pos;
                var body = (Collider)Native.Field(unit, "_collider"); Vector3 center; double radius = Native.Footprint(body, out center); if (body == null) center = pos;
                var threat = new Threat { Id = unit.GetInstanceID(), Position = Flat(center), Velocity = Flat(velocity), Radius = radius, Weight = unit is SurvivalBossMonster ? 3 : 1, Boss = unit is SurvivalBossMonster }; threats.Add(threat);
                var boss = unit as SurvivalBossMonster;
                double stunEnd;
                if(boss==null && unit is SurvivalMonster && simulationReady &&
                    Convert.ToBoolean(Native.BoundField(unit,"simMonsterStunned")) && stunnedUntil.TryGetValue(unit.GetInstanceID(),out stunEnd))
                    threat.StunFor=Math.Max(0,stunEnd-combatTime);
                if(boss==null && unit is SurvivalMonster && simulationReady)threat.MoveSpeed=Number(unit,"simMonsterSpeed");
                if (boss != null)
                {
                    threat.MoveSpeed = Convert.ToDouble(Native.BoundField(boss, "bossMoveSpeed"));
                    ObserveWarning(boss, velocity, threat);
                    bossDamage.Observe(threat, combatTime, Point.Distance(Flat(avatar.transform.position), threat.Position) - radius,
                        Convert.ToDouble(Native.BoundField(boss, "bossHp")), !threat.JumpEvading, weaponRange - radius, hp / Math.Max(1, maxHp));
                }
            }
            foreach (var projectile in projectiles) { if (projectile == null || !projectile.gameObject.activeInHierarchy) continue; Vector3 pos = projectile.transform.position, old; var velocity = positions.TryGetValue(projectile.GetInstanceID(), out old) ? (pos - old) / (float)dt : Vector3.zero; positions[projectile.GetInstanceID()] = pos; Vector3 center; double radius = Native.Footprint(projectile.GetComponent<Collider>(), out center); threats.Add(new Threat { Position = Flat(pos), Velocity = Flat(velocity), Radius = radius, Weight = 3, Projectile = true }); }
            if (positions.Count > 2048) { var ids = new HashSet<int>(units.Where(x => x != null).Select(x => x.GetInstanceID()).Concat(projectiles.Where(x => x != null).Select(x => x.GetInstanceID()))); foreach (int id in positions.Keys.Where(id => !ids.Contains(id)).ToArray()) positions.Remove(id); }
            var origin = Flat(avatar.transform.position); double speed = Native.Stat(Player, "MoveSpeed");
            var movement = Native.BoundField(avatar, "move");
            double controllerMaximum = Convert.ToDouble(Native.BoundField(movement, "moveMaximum"));
            double slope = Math.Max(0, 1 - Native.D(movement, "CurrentSlope") * Native.D(movement, "SlopePerSpeedRate"));
            var movementSpeed = movementProfile.Resolve(controllerMaximum * slope, Native.AxialMultiplier, Native.QuantizeMovement);
            predictedNormalSpeed = movementSpeed(new Point(1, 0)); predictedAxialSpeed = movementSpeed(new Point(0, 1));
            Point? region = units.Any(x => x is SurvivalBossMonster) ? null : regions.Select(origin, speed, loot, threats, now);
            float height = avatar.transform.position.y;
            Func<Point, bool> passable = p => Native.Passable(new Vector3((float)p.X, height, (float)p.Z));
            long cooldown = ultimateButton == null ? long.MaxValue : Convert.ToInt64(Native.BoundField(ultimateButton, "ultimateEnd"));
            long nativeTime = Convert.ToInt64(Native.Call("time", Native.Get(manager, "TimeManager")));
            ObserveBossArrival(Math.Max(predictedNormalSpeed,predictedAxialSpeed));
            double tacticalSpeed=control.Mode=="Challenge" && (seconds>=900 || bossArrival!=null&&bossArrival.Active)?Math.Min(predictedNormalSpeed,predictedAxialSpeed):speed;
            lastTactic = tactics.Decide(origin, tacticalSpeed, hp, maxHp, combatTime, threats, loot, passable, warnings, ultimateSkill,
                ultimateButton != null && ultimateButton.gameObject.activeInHierarchy && nativeTime > cooldown, control.Mode == "Currency",
                control.Mode=="Challenge"?Math.Max(0,1200-seconds):double.PositiveInfinity,bossArrival,arenaRoom);
            if(bossArrival!=null && bossArrival.Active)
            {
                lastTactic.LootScale=0;
                if(lastTactic.Target!=null && (lastTactic.Target.Type==1 || lastTactic.Target.Type==5))lastTactic.Target=null;
            }
            var frame = CaptureCombatFrame(origin, nativeTime);
            var candidates = frame != null && control.ForecastMovement ? new List<Route>() : null;
            var plan = Planner.Choose(origin, speed, threats, loot, direction, passable, region, warnings, control.Mode == "Currency", lastTactic, movementSpeed,
                    candidates: candidates,arrival:bossArrival,room:arenaRoom);
            plan = RefineMovement(plan,frame,candidates,movementSpeed,lastTactic.CornerLocked || threats.Any(t=>t.Boss));
            reason = lastTactic.CornerHold ? "corner-hold" : "playing";
            direction = plan.Direction;
            lastRoute = plan;
            // Keep the old ownership until Release has sent StopMove for a blocked route.
            if (Math.Abs(direction.X) + Math.Abs(direction.Z) > .01) { var move = Native.BoundField(avatar, "move"); var change = move.GetType().GetMethods().Single(m => m.Name == "ChangeMoveType"); change.Invoke(move, new[] { Enum.Parse(change.GetParameters()[0].ParameterType, "CharController") }); avatar.SetRotateAndAnimation(new Vector3((float)direction.X, 0, (float)direction.Z)); ownsMove = true; }
            else Release();
            if (lastTactic.Cast && Live && Playing && !Paused)
            {
                // Set the native facing used by directional skills, then immediately
                // resume the independently checked movement. A cast never freezes input.
                avatar.SetRotation(new Vector3((float)lastTactic.Aim.X, 0, (float)lastTactic.Aim.Z));
                ultimateButton.OnClickUI(ultimateButton.gameObject);
                bool confirmed = Convert.ToInt64(Native.BoundField(ultimateButton, "ultimateEnd")) > cooldown;
                tactics.CastAttempt(combatTime, confirmed, lastTactic.Aim);
                if (confirmed) nextObserve = nextPlan = 0;
            }
            planMs = watch.Elapsed.TotalMilliseconds; planCount++;
        }
        void ResetCombat() { ResetSimulation(); bossDamage.Reset(); movementProfile.Reset(); tactics.Reset(); hitEvents.Clear(); hitSequence = 0; lastTactic = null; combatTime = 0; positions.Clear(); patterns.Clear(); weaponRevision = -1; lastRoute = null; direction = new Point(); nextPlan = 0; }
        AnimationEvent[] Events(AnimationClip clip)
        {
            AnimationEvent[] events;
            if (!animationEvents.TryGetValue(clip.GetInstanceID(), out events)) animationEvents[clip.GetInstanceID()] = events = clip.events;
            return events;
        }
        Dictionary<string, double> JumpEvents(Animator animator, AnimatorStateInfo state, bool ready, double remaining)
        {
            var result = new Dictionary<string, double>();
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips.Length == 0) return result;
            var clip = clips.OrderByDescending(c => c.weight).First().clip;
            double rate = Math.Max(.01, animator.speed * Math.Abs(state.speed * state.speedMultiplier));
            foreach (var e in Events(clip))
            {
                double delay = (e.time - Math.Min(1, state.normalizedTime) * clip.length) / rate;
                if (delay >= 0) result[e.functionName] = delay;
            }
            // Read the next landing clip's real animation events, not a guessed
            // constant-velocity circle. If the controller is unfamiliar, retain a
            // conservative tracking envelope until native DisableMoving is observed.
            if (ready && animator.runtimeAnimatorController != null)
            {
                var landing = animator.runtimeAnimatorController.animationClips.Where(c => c != clip && Events(c).Any(e => e.functionName == "AttackJumpStart")).Distinct().ToArray();
                if (landing.Length == 1) foreach (var e in Events(landing[0]))
                    if (!result.ContainsKey(e.functionName)) result[e.functionName] = remaining + e.time / rate;
            }
            return result;
        }
        void ObserveWarning(SurvivalBossMonster boss, Vector3 velocity, Threat threat)
        {
            Pattern pattern;
            if (!patterns.TryGetValue(boss.GetInstanceID(), out pattern))
            {
                pattern=RecoverBossPattern(boss);
                if(pattern==null)return;
                patterns[boss.GetInstanceID()]=pattern;
            }
            int type = Native.I(pattern.Table, "AttackType"); var animator = (Animator)Native.Field(boss, "_animator"); var animation = animator.GetCurrentAnimatorStateInfo(0); var pos = boss.transform.position;
            double remaining = Math.Max(.05, Math.Min(3, (1 - Math.Min(1, animation.normalizedTime)) * animation.length / Math.Max(.01, animator.speed * Math.Abs(animation.speed * animation.speedMultiplier))));
            // Pattern records outlive their coroutine. Idle/move must not resurrect a completed warning.
            bool ready = animation.IsName("SurBoss_Skill" + type + "_Ready"), action = animation.IsName("SurBoss_Skill" + type + "_Action"), ending = animation.IsName("SurBoss_Skill" + type + "_End");
            if (!ready && !action && !ending) return;
            if (type == 1)
            {
                var dash = (GameObject)Native.Field(boss, "_dashBox"); bool active = dash.activeInHierarchy || animation.IsName("SurBoss_Skill1_Action");
                if (ending && !dash.activeInHierarchy) { threat.Motion = BossMotion.Hold; threat.PhaseSeconds = remaining; return; }
                if (active && !pattern.Locked && pattern.Routine!=null) { pattern.End = (Vector3)Native.BoundField(pattern.Routine, "bossDashDestination"); pattern.Locked = true; }
                double range=pattern.Recovered?pattern.Range:Native.D(pattern.Table,"AttackRange");
                if (!pattern.Locked) { var dashDirection = -boss.transform.forward; pattern.End = pos + dashDirection * (float)range; if (FlowFieldController.Instance != null && !FlowFieldController.Instance.IsInnerBounds(pattern.End)) pattern.End = FlowFieldController.Instance.GetClosestPoint(pattern.End) - dashDirection; if(active)pattern.Locked=true; }
                threat.Motion = BossMotion.Dash; threat.MotionEnd = Flat(pattern.End); threat.MotionSpeed = pattern.Recovered?pattern.Speed:Native.D(pattern.Table, "AttackSpeed");
                if (Convert.ToBoolean(Native.BoundField(boss,"bossEnraged"))) threat.MotionSpeed *= Convert.ToDouble(Native.BoundField(boss,"bossRageMultiplier"));
                threat.MotionDelay = active ? 0 : remaining;
                threat.DashAiming = !active; threat.DashRange = range;
                threat.WindupSpeed = Convert.ToBoolean(Native.BoundField(boss,"bossCanMove")) ? Convert.ToDouble(Native.BoundField(boss,"bossCurrentSpeed")) : 0;
                threat.PhaseSeconds = threat.MotionDelay + Point.Distance(Flat(pos), threat.MotionEnd) / Math.Max(.1, threat.MotionSpeed);
                double radius = .5; foreach (var collider in dash.GetComponentsInChildren<Collider>(true)) radius = Math.Max(radius, Native.CorridorRadius(collider, pos, pattern.End - pos));
                threat.DashRadius = radius + .2;
                // During wind-up the boss still tracks the player. A fixed warning lane
                // falsely rewards circling at melee range. First create separation;
                // only after native direction lock does lateral lane escape make sense.
                if (active) warnings.Add(new AttackArea { BossId = threat.Id, Kind = "dash", A = Flat(pos), B = Flat(pattern.End), Radius = radius + .65, ActiveFor = Math.Max(.2, threat.PhaseSeconds) + .2 });
            }
            else if (type == 2)
            {
                var collider = (Collider)Native.Field(boss, "_jumpCollider");
                bool canMove = Convert.ToBoolean(Native.BoundField(boss,"bossCanMove"));
                pattern.JumpMoved |= canMove;
                pattern.JumpStopped |= pattern.JumpMoved && !canMove || ending;
                if (pattern.JumpFinished) { threat.Motion = BossMotion.Hold; threat.PhaseSeconds = remaining; return; }
                Vector3 center; double radius = Native.Footprint(collider, out center);
                var events = JumpEvents(animator, animation, ready, remaining); double delay;
                if(pattern.Recovered && !canMove && action)
                {
                    var clips=animator.GetCurrentAnimatorClipInfo(0);
                    if(clips.Length>0)pattern.JumpStopped |= Events(clips[0].clip).Any(e=>e.functionName=="DisableMoving"&&e.time<=animation.normalizedTime*clips[0].clip.length);
                }
                threat.Motion = BossMotion.Jump; threat.JumpEvading = true; threat.JumpTracking = !pattern.JumpStopped;
                threat.MotionSpeed = pattern.Recovered?pattern.Speed:Native.D(pattern.Table, "AttackSpeed"); threat.PhaseSeconds = remaining;
                threat.JumpTrackStart = canMove ? 0 : events.TryGetValue("EnableMoving", out delay) ? delay : 0;
                threat.JumpTrackEnd = pattern.JumpStopped ? 0 : events.TryGetValue("DisableMoving", out delay) ? delay : Math.Max(2.4, remaining);
                threat.JumpImpactAfter = collider.enabled ? 0 : events.TryGetValue("AttackJumpStart", out delay) ? delay : pattern.JumpStopped ? 0 : threat.JumpTrackEnd;
                threat.JumpAttackUntil = events.TryGetValue("AttackJumpEnd", out delay) ? delay + .1 : Math.Max(threat.JumpImpactAfter + .3, remaining + .2);
                threat.JumpRadius = radius + .2;
                // Once tracking stops, use the actual fixed landing center. Never
                // extrapolate its last chasing velocity beyond direction/position lock.
                if (!threat.JumpTracking) warnings.Add(new AttackArea { BossId = threat.Id, Kind = "jump", A = Flat(center), B = Flat(center), Radius = radius + .65, ActiveFor = threat.JumpAttackUntil });
            }
        }
        void Release() { if (ownsMove && avatar != null) { var move = Native.BoundField(avatar, "move"); Native.Invoke(move, "StopMove"); } ownsMove = false; }
        void Fail(string code, string info) { faulted = true; reason = code; detail = info; try { Release(); } catch { } try { Snapshot(); } catch { } }
        void Snapshot() { var select = uis.OfType<SurvivalSelectUI>().FirstOrDefault(); var value = new { At = DateTime.UtcNow.ToString("O"), RunId = control.RunId, Running = Live, Faulted = faulted, Reason = reason, Detail = detail, Completed = rounds, Hp = hp, MaxHp = maxHp, Seconds = seconds, Level = level, PlanMs = planMs, Plans = planCount, Stages = stages, Stage = target, Ui = lastUi, Mode = control.Mode, AutoSelectEquipment = control.AutoSelectEquipment,
            Combat = !control.TraceCombat || avatar == null || Player == null ? null : new {
                Time = combatTime, Playing, Paused, Position = Flat(avatar.transform.position), Speed = Native.Stat(Player, "MoveSpeed"),
                NormalSpeed = predictedNormalSpeed, AxialSpeed = predictedAxialSpeed,
                Threats = threats.ToArray(), Warnings = warnings.ToArray(),
                HitEvents = hitEvents.ToArray(),
                Simulation = combatFrame, SimulationError = simulationError,
                Forecast = lastSuggestion == null ? null : lastSuggestion.Forecast,
                ForecastEvaluations = lastSuggestion == null ? 0 : lastSuggestion.Evaluations,
                ForecastMs = lastSuggestion == null ? 0 : lastSuggestion.Milliseconds,
                ForecastAccepted = forecastAccepted, ForecastRejected = forecastRejected,
                SafetyForecast = combatSafety.Selected, SafetyEvaluations = combatSafety.Evaluations,
                RecoveredBossPatterns = patterns.Values.Count(p=>p.Recovered),
                RescueItems=loot.Where(d=>d.Type==0||d.Type==2||d.Type==6).ToArray(),
                Equipment = Native.Items(Native.Call("weapons", Player)).Concat(Native.Items(Native.Call("passives", Player))).Select(o => new { Id = Native.BoundField(o,"ownedId"), Level = Native.BoundField(o,"ownedLevel") }).ToArray(),
                Bosses = units.OfType<SurvivalBossMonster>().Where(b=>b!=null&&!b.IsDead()).Select(b=>new { Id=b.GetInstanceID(), Position=Flat(b.transform.position), Hp=Native.BoundField(b,"bossHp"), Animation=((Animator)Native.Field(b,"_animator")).GetCurrentAnimatorStateInfo(0).shortNameHash }).ToArray()
            },
            Movement = lastRoute == null ? null : new { lastRoute.Reason, lastRoute.Direction, lastRoute.ObservedDps, KillSeconds = double.IsInfinity(lastRoute.KillSeconds) ? (double?)null : lastRoute.KillSeconds, lastRoute.PreferredGap, lastRoute.EscapeSpace, lastRoute.PredictionSeconds, lastRoute.WarningCost, lastRoute.Collision },
            Tactics = lastTactic == null ? null : new { lastTactic.Reason, lastTactic.SkillReason, lastTactic.Surrounded, lastTactic.Emergency, lastTactic.Stunned, lastTactic.StunRemaining, lastTactic.StunSafety, lastTactic.Stalling, lastTactic.BreakingOut,lastTactic.ControlledExit,lastTactic.ControlTarget,lastTactic.CornerLocked,lastTactic.CornerHold, lastTactic.SurvivalUrgency, lastTactic.StunUrgency, lastTactic.Hits, lastTactic.Aim, TargetType = lastTactic.Target == null ? (int?)null : lastTactic.Target.Type, tactics.Bombs, tactics.Stuns, tactics.Casts },
            BossPreparation = bossArrival,
            Build = new { Decision = buildChoice, Restart = buildRestart.Phase.ToString() },
            Character = selectedCharacter,
            Characters = characters, RequestedCharacter = new { Group = control.CharacterGroup, Id = control.CharacterId },
            Entry = new { entry.Pending, entry.ResultId, entry.ResultRevision, entry.CloseAttempts, CurrentResultRevision = resultRevision },
            TransitionUi = uis.Where(x => x is SurvivalResultUI || x is SurvivalDefaultPopupUI || x is SurvivalPauseUI).Select(x => new {
                Type = x.GetType().Name, Id = x.GetInstanceID(), Ready = Native.Ready(x), CanClose = x.CanCloseUI(),
                RetrySelected = Native.RetrySelected(x as SurvivalResultUI), OwnedConfirmation = Native.RetryPopup(x as SurvivalDefaultPopupUI, retryResult),
                BuildExitConfirmation = Native.ExitPopup(x as SurvivalDefaultPopupUI, buildPause),
                BuildExitSelected = x is SurvivalPauseUI && Convert.ToBoolean(Native.BoundField(x, "pauseExited"))
            }).ToArray(),
            Selection = select == null ? null : new { Ready = Native.Ready(select), Busy = Convert.ToBoolean(Native.BoundField(select, "skillBusy")), Rerolls = Native.BoundField(select,"skillRerolls"), Clickable = Native.B(select,"IsAvailableClick"), Animation = Native.BoundField(select,"uiAnimation") == null ? false : Native.B(Native.BoundField(select,"uiAnimation"),"IsPlaying"), Revision = skillRevision, Choices = ((SurvivalCardItem[])Native.Field(select,"_slots")).Where(x=>x.gameObject.activeInHierarchy).Select(x=>new {Id=Native.BoundField(x,"skillId"),Level=Native.BoundField(x,"skillLevel")}).ToArray() },
            Surfaces = uis.Select(x=>x.GetType().Name).ToArray() }; RuntimeFiles.Write(Path.Combine(Loader.Root, "state.json"), System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value))); }
        void OnDisable() { Release(); }
        void OnDestroy() { try { Release(); } finally { if (current == this) current = null; new Harmony(HarmonyId).UnpatchAll(HarmonyId); } }
    }
}
