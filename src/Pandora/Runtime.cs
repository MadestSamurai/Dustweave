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
    public sealed class Engine : MonoBehaviour
    {
        const string HarmonyId = "dustweave.pandora.observation";
        static Engine current;
        Control control = new Control();
        readonly TransitionGate gate = new TransitionGate();
        readonly RoundEntry entry = new RoundEntry();
        readonly RegionPlanner regions = new RegionPlanner();
        readonly Dictionary<int, Vector3> positions = new Dictionary<int, Vector3>();
        readonly Dictionary<int, Pattern> patterns = new Dictionary<int, Pattern>();
        readonly List<Threat> threats = new List<Threat>();
        readonly List<Drop> loot = new List<Drop>();
        readonly List<AttackArea> warnings = new List<AttackArea>();
        List<SkillOption> catalog;
        Stage[] stages = new Stage[0]; Stage target;
        EventMiniGameSurvivalManager manager; PlayerController avatar;
        UIBase[] uis = new UIBase[0]; SurvivalUnit[] units = new SurvivalUnit[0]; EventMiniGameEnemyProjectile[] projectiles = new EventMiniGameEnemyProjectile[0];
        Point direction; bool ownsMove, handingOff, faulted, retryExpected;
        SurvivalResultUI retryResult;
        int resultRevision, retryRevision;
        int rounds, lastResult, lastManager, planCount, skillRevision; string reason = "ready", detail = "", lastUi = "";
        int acknowledgedSkillRevision = -1, acknowledgedSkillUi, closingSkillRevision = -1;
        float nextControl, nextObserve, nextPlan, nextWrite, nextUi, lastThreatTime, waitSince;
        double hp, maxHp, seconds, planMs; int level;
        sealed class Pattern { public object Table; public Vector3 End; public bool Locked; }
        public static object Create() { var host = new GameObject("Dustweave Pandora"); UnityEngine.Object.DontDestroyOnLoad(host); return host.AddComponent<Engine>(); }
        public void PrepareHandoff() { handingOff = true; control.Enabled = false; Release(); }
        public string HandoffBusy() { return ""; } // Native UI owns any outstanding request and completes it without this observer.
        public void Stop() { PrepareHandoff(); UnityEngine.Object.DestroyImmediate(gameObject); }
        bool Live { get { return !handingOff && !faulted && control.Enabled && control.Valid(DateTime.UtcNow.Ticks); } }
        bool Playing { get { return manager != null && Native.B(manager, "IsPlaying"); } }
        bool Paused { get { return manager != null && Native.B(manager, "IsPause"); } }
        object Player { get { return manager == null ? null : Native.Get(manager, "Player"); } }
        static Point Flat(Vector3 v) { return new Point(v.x, v.z); }
        void Awake() { current = this; var harmony = new Harmony(HarmonyId); for (int i = 0; i < 3; i++) harmony.Patch(Native.Bound("bossPattern" + i), prefix: new HarmonyMethod(typeof(Engine), nameof(PatternObserved))); harmony.Patch(typeof(SurvivalSelectUI).GetMethod("SetUI"), postfix: new HarmonyMethod(typeof(Engine), nameof(SkillPageObserved))); harmony.Patch(Native.Bound("skillApplied"), postfix: new HarmonyMethod(typeof(Engine), nameof(SkillApplied))); harmony.Patch(typeof(SurvivalResultUI).GetMethod("Initalize"), postfix: new HarmonyMethod(typeof(Engine), nameof(ResultPageObserved))); }
        public static void ResultPageObserved() { if (current != null) current.resultRevision++; }
        public static void SkillPageObserved() { if (current != null) { current.skillRevision++; current.closingSkillRevision = -1; } }
        public static void SkillApplied(SurvivalSelectUI __instance) { if (current != null) { current.acknowledgedSkillRevision = current.skillRevision; current.acknowledgedSkillUi = __instance.GetInstanceID(); } }
        public static void PatternObserved(SurvivalBossMonster __instance, object __0) { if (current == null) return; current.patterns[__instance.GetInstanceID()] = new Pattern { Table = __0 }; current.nextPlan = 0; }
        void Update()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now >= nextControl) { nextControl = now + .1f; ReadControl(); }
                if (now >= nextObserve) { nextObserve = now + .3f; Observe(); if (Live) Act(); else Release(); }
                if (Live && target != null && Playing && !Paused && avatar != null && hp > 0 && !entry.Pending && !LoopRules.WaitForEquipment(control, uis.Any(x => x is SurvivalSelectUI)))
                {
                    if (now >= nextPlan) { nextPlan = now + (units.Any(x => x is SurvivalBossMonster) ? .075f : .2f); PlanMove(); }
                    if (ownsMove) { var move = Native.BoundField(avatar, "move"); Native.Call("direction", move, new Vector3((float)direction.X, 0, (float)direction.Z)); avatar.SetMoveStart(); }
                }
                else Release();
                if (!Live && !faulted) reason = control.Enabled ? "lease-expired" : "paused";
                if (now >= nextWrite) { nextWrite = now + .5f; Snapshot(); }
            }
            catch (Exception e) { Fail("runtime-error", e.ToString()); }
        }
        void ReadControl()
        {
            if (handingOff) return; var bytes = RuntimeFiles.Read(Path.Combine(Loader.Root, "control.json")); if (bytes == null) return;
            var next = JsonConvert.DeserializeObject<Control>(System.Text.Encoding.UTF8.GetString(bytes)); if (next == null || !next.Valid(DateTime.UtcNow.Ticks)) return;
            if (next.RunId != control.RunId) { faulted = false; detail = ""; target = null; gate.Reset(); retryExpected = false; retryResult = null; entry.Reset(); rounds = 0; lastResult = 0; regions.Reset(); reason = "entering"; waitSince = Time.realtimeSinceStartup; }
            control = next;
        }
        void Observe()
        {
            var next = Native.Find<EventMiniGameSurvivalManager>(); if (next != manager) { Release(); manager = next; regions.Reset(); positions.Clear(); patterns.Clear(); }
            uis = UnityEngine.Object.FindObjectsOfType<UIBase>().Where(x => x.gameObject.activeInHierarchy).ToArray();
            var main = uis.OfType<SurvivalMainUI>().FirstOrDefault(); if (main != null && !main.IsAnimation()) stages = Native.Stages(main);
            if (manager == null || Player == null) { Release(); avatar = null; units = new SurvivalUnit[0]; projectiles = new EventMiniGameEnemyProjectile[0]; return; }
            avatar = Native.Get(Native.FieldManager, "PlayerController") as PlayerController;
            var health = Native.Call("health", null); if (health == null) { Release(); avatar = null; return; }
            hp = Native.D(health, "MiniGameHP"); maxHp = Native.D(health, "MiniGameMaxHP");
            level = Convert.ToInt32(Native.Call("level", Player)); var clock = Native.Get(manager, "TimeManager"); seconds = clock == null ? 0 : Convert.ToDouble(Native.Call("time", clock)) / 1000;
            if (manager.GetInstanceID() != lastManager) { lastManager = manager.GetInstanceID(); catalog = null; lastThreatTime = Time.realtimeSinceStartup; }
            if (avatar == null) return;
            units = UnityEngine.Object.FindObjectsOfType<SurvivalUnit>().Where(x => x.gameObject.activeInHierarchy && !x.IsDead()).OrderByDescending(x => x is SurvivalBossMonster).ThenBy(x => (x.transform.position - avatar.transform.position).sqrMagnitude).Take(104).ToArray();
            projectiles = UnityEngine.Object.FindObjectsOfType<EventMiniGameEnemyProjectile>().Where(x => x.gameObject.activeInHierarchy).OrderBy(x => (x.transform.position - avatar.transform.position).sqrMagnitude).Take(80).ToArray();
            loot.Clear(); double magnet = Native.Stat(Player, "MagnetRange");
            foreach (var drop in UnityEngine.Object.FindObjectsOfType<SurvivalItem>().Where(x => x.gameObject.activeInHierarchy).OrderBy(x => (x.transform.position - avatar.transform.position).sqrMagnitude).Take(256))
            {
                var table = Native.Call("item", null, Native.I(drop, "FieldRewardObjectId")); if (table == null) continue;
                int kind = Native.I(table, "ItemType"); var collider = drop.GetComponent<Collider>(); Vector3 center; double radius = Native.Footprint(collider, out center);
                loot.Add(new Drop { Id = drop.GetInstanceID(), Position = Flat(drop.transform.position), Type = kind, Amount = Native.D(table, "ItemValue"), PickupRadius = Planner.PickupRadius(kind, magnet, radius + .45) });
            }
        }
        void Act()
        {
            var main = uis.OfType<SurvivalMainUI>().FirstOrDefault(); var result = uis.OfType<SurvivalResultUI>().FirstOrDefault();
            var popup = uis.OfType<SurvivalDefaultPopupUI>().FirstOrDefault(); var select = uis.OfType<SurvivalSelectUI>().FirstOrDefault(); var reward = uis.OfType<SurvivalRewardPopupUI>().FirstOrDefault();
            var hub = uis.OfType<MiniGameHubUI>().FirstOrDefault();
            Component panel = main == null ? null : Native.BoundField(main, "panel") as Component;
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
                    retryResult = null; lastResult = 0; gate.Reset(); regions.Reset(); positions.Clear(); patterns.Clear(); catalog = null;
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
                if (SameStage() && Native.Click(result, "_objRetryButton")) { retryExpected = true; retryResult = result; retryRevision = resultRevision; reason = "retrying"; Sent(identity); return; }
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
                if (target == null) { target = control.Mode == "Currency" && Native.ActiveStage == control.StageId && Native.ActiveGroup == control.StageGroup || control.Mode == "Challenge" && Native.B(manager, "IsInifityMode") ? new Stage { Id = Native.ActiveStage, Group = Native.ActiveGroup, Challenge = Native.B(manager, "IsInifityMode") } : null; }
                if (target == null || Native.ActiveStage != target.Id || Native.ActiveGroup != target.Group) { Fail("wrong-stage", Native.ActiveStage.ToString()); return; }
                if (Paused) { reason = "game-paused"; return; }
                reason = "playing";
                if (threats.Count > 3) { var ult = Native.Find<UltimateSkillButton>(); if (ult != null) { ult.OnClickUI(ult.gameObject); nextUi = Time.realtimeSinceStartup + .5f; } }
                return;
            }
            reason = "open-game"; if (Time.realtimeSinceStartup - waitSince > 45) Fail("open-game", identity);
        }
        void Sent(string id) { gate.Sent(id, Time.realtimeSinceStartup); nextUi = Time.realtimeSinceStartup + .12f; }
        bool SameStage() { return target != null && Native.ActiveParam != null && Native.ActiveStage == target.Id && Native.ActiveGroup == target.Group; }
        void ChooseSkill(SurvivalSelectUI select)
        {
            if (catalog == null) { int character = Convert.ToInt32(Native.BoundField(Player, "playerCharGroup")); catalog = Native.Items(Native.Call("skillGroups", null)).Where(g => Native.I(g, "UniqueCharGroupId") == 0 || Native.I(g, "UniqueCharGroupId") == character).SelectMany(g => Enumerable.Range(1, Native.I(g, "MaxLevel")).Select(n => Native.Skill(Native.I(g, "Id"), n))).ToList(); }
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
            var choices = ((SurvivalCardItem[])Native.Field(select, "_slots")).Where(x => x.gameObject.activeInHierarchy).Select(card => new { Card = card, Evaluation = SkillPlanner.Evaluate(Native.Skill(Convert.ToInt32(Native.BoundField(card, "skillId")), Convert.ToInt32(Native.BoundField(card, "skillLevel"))), c) }).OrderByDescending(x => x.Evaluation.Score).ToArray();
            if (choices.Length == 0) throw new InvalidOperationException("Pandora upgrade choices unavailable");
            select.OnClickUI(choices[0].Card.gameObject); detail = choices[0].Evaluation.Name;
        }
        void PlanMove()
        {
            // A pending result/entry/upgrade click never blocks movement after the game has resumed.
            var watch = System.Diagnostics.Stopwatch.StartNew(); float now = Time.realtimeSinceStartup; double dt = Math.Max(.02, now - lastThreatTime); lastThreatTime = now; threats.Clear(); warnings.Clear();
            foreach (var unit in units)
            {
                if (unit == null || unit.IsDead() || !unit.gameObject.activeInHierarchy) continue; Vector3 pos = unit.transform.position, old; var velocity = positions.TryGetValue(unit.GetInstanceID(), out old) ? Vector3.ClampMagnitude((pos - old) / (float)dt, unit is SurvivalBossMonster ? 100 : 15) : Vector3.zero; positions[unit.GetInstanceID()] = pos;
                Vector3 center; double radius = Native.Footprint(unit.GetComponent<Collider>(), out center); threats.Add(new Threat { Position = Flat(pos), Velocity = Flat(velocity), Radius = radius, Weight = unit is SurvivalBossMonster ? 3 : 1, Boss = unit is SurvivalBossMonster });
                var boss = unit as SurvivalBossMonster; if (boss != null) ObserveWarning(boss, velocity);
            }
            foreach (var projectile in projectiles) { if (projectile == null || !projectile.gameObject.activeInHierarchy) continue; Vector3 pos = projectile.transform.position, old; var velocity = positions.TryGetValue(projectile.GetInstanceID(), out old) ? (pos - old) / (float)dt : Vector3.zero; positions[projectile.GetInstanceID()] = pos; Vector3 center; double radius = Native.Footprint(projectile.GetComponent<Collider>(), out center); threats.Add(new Threat { Position = Flat(pos), Velocity = Flat(velocity), Radius = radius, Weight = 3 }); }
            if (positions.Count > 2048) { var ids = new HashSet<int>(units.Where(x => x != null).Select(x => x.GetInstanceID()).Concat(projectiles.Where(x => x != null).Select(x => x.GetInstanceID()))); foreach (int id in positions.Keys.Where(id => !ids.Contains(id)).ToArray()) positions.Remove(id); }
            var origin = Flat(avatar.transform.position); double speed = Native.Stat(Player, "MoveSpeed");
            Point? region = units.Any(x => x is SurvivalBossMonster) ? null : regions.Select(origin, speed, loot, threats, now);
            var plan = Planner.Choose(origin, speed, threats, loot, direction, p => Convert.ToBoolean(Native.Call("passable", null, new Vector3((float)p.X, avatar.transform.position.y, (float)p.Z))), region, warnings, control.Mode == "Currency");
            direction = plan.Direction;
            // Keep the old ownership until Release has sent StopMove for a blocked route.
            if (Math.Abs(direction.X) + Math.Abs(direction.Z) > .01) { var move = Native.BoundField(avatar, "move"); var change = move.GetType().GetMethods().Single(m => m.Name == "ChangeMoveType"); change.Invoke(move, new[] { Enum.Parse(change.GetParameters()[0].ParameterType, "CharController") }); avatar.SetRotateAndAnimation(new Vector3((float)direction.X, 0, (float)direction.Z)); ownsMove = true; }
            else Release(); planMs = watch.Elapsed.TotalMilliseconds; planCount++;
        }
        void ObserveWarning(SurvivalBossMonster boss, Vector3 velocity)
        {
            Pattern pattern; if (!patterns.TryGetValue(boss.GetInstanceID(), out pattern)) return;
            int type = Native.I(pattern.Table, "AttackType"); var animator = (Animator)Native.Field(boss, "_animator"); var animation = animator.GetCurrentAnimatorStateInfo(0); var pos = boss.transform.position;
            if (type == 1)
            {
                var dash = (GameObject)Native.Field(boss, "_dashBox"); bool active = dash.activeInHierarchy || animation.IsName("SurBoss_Skill1_Action"); if (animation.IsName("SurBoss_Skill1_End") && !dash.activeInHierarchy) return;
                if (!pattern.Locked) { pattern.End = pos - boss.transform.forward * (float)Native.D(pattern.Table, "AttackRange"); if (FlowFieldController.Instance != null && !FlowFieldController.Instance.IsInnerBounds(pattern.End)) pattern.End = FlowFieldController.Instance.GetClosestPoint(pattern.End); if (active) pattern.Locked = true; }
                double radius = 2; foreach (var collider in dash.GetComponentsInChildren<Collider>(true)) { Vector3 center; radius = Math.Max(radius, Native.Footprint(collider, out center) + Point.Distance(Flat(center), Flat(pos))); }
                warnings.Add(new AttackArea { A = Flat(pos), B = Flat(pattern.End), Radius = radius + .65 });
            }
            else if (type == 2) { var collider = (Collider)Native.Field(boss, "_jumpCollider"); if (animation.IsName("SurBoss_Skill2_End") && !collider.enabled) return; Vector3 center; double radius = Native.Footprint(collider, out center); warnings.Add(new AttackArea { A = Flat(center), B = Flat(center), Radius = radius + .65, Velocity = Flat(velocity) }); }
        }
        void Release() { if (ownsMove && avatar != null) { var move = Native.BoundField(avatar, "move"); Native.Invoke(move, "StopMove"); } ownsMove = false; }
        void Fail(string code, string info) { faulted = true; reason = code; detail = info; try { Release(); } catch { } try { Snapshot(); } catch { } }
        void Snapshot() { var select = uis.OfType<SurvivalSelectUI>().FirstOrDefault(); var value = new { At = DateTime.UtcNow.ToString("O"), RunId = control.RunId, Running = Live, Faulted = faulted, Reason = reason, Detail = detail, Completed = rounds, Hp = hp, MaxHp = maxHp, Seconds = seconds, Level = level, PlanMs = planMs, Plans = planCount, Stages = stages, Stage = target, Ui = lastUi, Mode = control.Mode, AutoSelectEquipment = control.AutoSelectEquipment,
            Entry = new { entry.Pending, entry.ResultId, entry.ResultRevision, entry.CloseAttempts, CurrentResultRevision = resultRevision },
            TransitionUi = uis.Where(x => x is SurvivalResultUI || x is SurvivalDefaultPopupUI).Select(x => new {
                Type = x.GetType().Name, Id = x.GetInstanceID(), Ready = Native.Ready(x), CanClose = x.CanCloseUI(),
                RetrySelected = Native.RetrySelected(x as SurvivalResultUI), OwnedConfirmation = Native.RetryPopup(x as SurvivalDefaultPopupUI, retryResult)
            }).ToArray(),
            Selection = select == null ? null : new { Ready = Native.Ready(select), Busy = Convert.ToBoolean(Native.BoundField(select, "skillBusy")), Clickable = Native.B(select,"IsAvailableClick"), Animation = Native.BoundField(select,"uiAnimation") == null ? false : Native.B(Native.BoundField(select,"uiAnimation"),"IsPlaying"), Revision = skillRevision, Choices = ((SurvivalCardItem[])Native.Field(select,"_slots")).Where(x=>x.gameObject.activeInHierarchy).Select(x=>new {Id=Native.BoundField(x,"skillId"),Level=Native.BoundField(x,"skillLevel")}).ToArray() },
            Surfaces = uis.Select(x=>x.GetType().Name).ToArray() }; RuntimeFiles.Write(Path.Combine(Loader.Root, "state.json"), System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value))); }
        void OnDisable() { Release(); }
        void OnDestroy() { try { Release(); } finally { if (current == this) current = null; new Harmony(HarmonyId).UnpatchAll(HarmonyId); } }
    }
}
