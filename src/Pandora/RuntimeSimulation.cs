using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HarmonyLib;

namespace Dustweave.Pandora.Runtime
{
    public sealed partial class Engine
    {
        readonly CombatWorker combatWorker = new CombatWorker();
        readonly CombatWorkspace validationWorkspace = new CombatWorkspace();
        readonly CombatSafety combatSafety = new CombatSafety();
        CombatSuggestion lastSuggestion;
        int forecastAccepted,forecastRejected;
        readonly Dictionary<int, double> stunnedUntil = new Dictionary<int, double>();
        CombatFrame combatFrame;
        string simulationError = "";
        bool simulationReady;
        int flowInstance, flowMinX, flowMinZ, flowWidth, flowHeight;
        bool openArena;
        static int contactSource;
        BossArrival bossArrival;
        ArenaRoom arenaRoom;
        double[] bossSchedule;
        int bossScheduleGroup, bossScheduleStage;
        void ObserveBossArrival(double speed)
        {
            bossArrival=null;arenaRoom=null;
            if(!simulationReady || control.Mode!="Challenge" || FlowFieldController.Instance==null)return;
            try
            {
                int group=Native.ActiveGroup,stage=Native.ActiveStage;
                if(bossSchedule==null || bossScheduleGroup!=group || bossScheduleStage!=stage)
                {
                    var map=Native.Items(Native.Call("maps",null,group)).Single(m=>Native.I(m,"Id")==stage);
                    bossSchedule=Native.Items(Native.Call("simMonsterSchedule",null,Native.I(map,"MonsterGroupId")))
                        .Where(m=>Native.I(m,"Type")==2).Select(m=>Native.D(m,"StartSpawnTime")).OrderBy(t=>t).ToArray();
                    bossScheduleGroup=group;bossScheduleStage=stage;
                }
                double next=bossSchedule.Where(t=>t>=seconds-.5).DefaultIfEmpty(double.PositiveInfinity).First();
                var bounds=(Bounds)Native.BoundField(FlowFieldController.Instance,"simFlowBounds");
                bossArrival=BossArrival.Prepare(Flat(bounds.min),Flat(bounds.max),next-seconds,speed,threats.Any(t=>t.Boss));
                if(!threats.Any(t=>t.Boss))arenaRoom=ArenaRoom.Create(Flat(bounds.min),Flat(bounds.max),speed,weaponRange);
            }
            catch(Exception e){simulationError="boss-arrival: "+e.GetType().Name+": "+e.Message;}
        }
        void Start()
        {
            simulationReady = Bindings.Names.ContainsKey("simulationContract") && Bindings.Names["simulationContract"] == "1";
            if (!simulationReady) { simulationError = Bindings.Names.ContainsKey("simulationContractError") ? Bindings.Names["simulationContractError"] : "unsupported-native-contract"; return; }
            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.Patch(typeof(SurvivalMonster).GetMethod("Initialize"), postfix: new HarmonyMethod(typeof(Engine), nameof(MonsterInitialized)));
                harmony.Patch(typeof(SurvivalMonster).GetMethod("Stun"), postfix: new HarmonyMethod(typeof(Engine), nameof(MonsterStunned)));
                harmony.Patch(AccessTools.Method(typeof(SurvivalMonster), "FixedUpdate"), prefix: new HarmonyMethod(typeof(Engine), nameof(ContactStarting)), finalizer: new HarmonyMethod(typeof(Engine), nameof(ContactFinished)));
                harmony.Patch(AccessTools.Method(typeof(EventMiniGameEnemyProjectile), "OnTriggerEnter"),prefix:new HarmonyMethod(typeof(Engine),nameof(ProjectileContactStarting)),finalizer:new HarmonyMethod(typeof(Engine),nameof(ContactFinished)));
            }
            catch (Exception e) { simulationReady = false; simulationError = e.GetType().Name + ": " + e.Message; }
        }
        public static void ContactStarting(SurvivalMonster __instance) { contactSource = __instance.GetInstanceID(); }
        public static void ProjectileContactStarting(EventMiniGameEnemyProjectile __instance) { contactSource=__instance.GetInstanceID(); }
        public static void ContactFinished() { contactSource = 0; }
        public static void MonsterInitialized(SurvivalMonster __instance)
        { if (current == null) return; current.positions.Remove(__instance.GetInstanceID()); current.stunnedUntil.Remove(__instance.GetInstanceID()); current.nextObserve=current.nextPlan=0; }
        public static void MonsterStunned(SurvivalMonster __instance, float __0)
        { if (current != null) current.stunnedUntil[__instance.GetInstanceID()] = current.combatTime + __0; }
        void ResetSimulation() { combatWorker.Reset(); flowInstance=0; lastSuggestion=null; forecastAccepted=forecastRejected=0; combatFrame = null; stunnedUntil.Clear(); bossArrival=null;arenaRoom=null;bossSchedule=null; }
        Route RefineMovement(Route baseline, CombatFrame frame, IList<Route> candidates, Func<Point,double> speed, bool reserved)
        {
            if(reserved || frame==null || !frame.Bounded || !control.ForecastMovement || control.Mode!="Challenge")
            { combatWorker.Reset(); lastSuggestion=null; return baseline; }
            // Bounds and speeds are copied values. Background work cannot touch Unity.
            Point min=frame.MapMin,max=frame.MapMax;
            Func<Point,bool> clear=p=>p.X>=min.X&&p.X<=max.X&&p.Z>=min.Z&&p.Z<=max.Z;
            var suggested=combatWorker.Take(frame.Time);
            if(suggested==null && lastSuggestion!=null && frame.Time>=lastSuggestion.Time && frame.Time-lastSuggestion.Time<=.4)
                suggested=lastSuggestion;
            if(suggested!=null)
            {
                lastSuggestion=suggested;
                if(suggested.Route!=null && string.IsNullOrEmpty(suggested.Error))
                {
                    var updated=CombatSimulation.Run(frame,suggested.Route.Direction,suggested.Route.Direction,speed,clear,.4,workspace:validationWorkspace);
                    var original=CombatSimulation.Run(frame,baseline.Direction,baseline.Direction,speed,clear,.4,workspace:validationWorkspace);
                    if(!updated.Blocked && (original.Blocked || updated.LethalAt>.35 && updated.Cost(frame.Hp)<=original.Cost(frame.Hp)+Math.Max(10,frame.Hp*.1)))
                    { baseline=suggested.Route;forecastAccepted++; }
                    else forecastRejected++;
                }
                else { forecastRejected++;simulationError=suggested.Error ?? "no-safe-forecast"; }
            }
            baseline=combatSafety.Choose(frame,baseline,candidates,direction,speed,clear);
            combatWorker.Submit(frame,candidates,baseline.Direction,speed,clear);
            return baseline;
        }
        static double Number(object o, string field) { return Convert.ToDouble(Native.BoundField(o, field)); }
        Pattern RecoverBossPattern(SurvivalBossMonster boss)
        {
            if(!simulationReady)return null;
            var animator=(Animator)Native.Field(boss,"_animator");var state=animator.GetCurrentAnimatorStateInfo(0);int type=-1;
            for(int i=1;i<=2;i++)if(state.IsName("SurBoss_Skill"+i+"_Ready")||state.IsName("SurBoss_Skill"+i+"_Action")||state.IsName("SurBoss_Skill"+i+"_End"))type=i;
            if(type<0)return null;
            var tables=Native.Items(Native.Call("simBossPatterns",null,(int)Number(boss,"simBossPatternGroup"))).Where(t=>Native.I(t,"AttackType")==type).ToArray();
            if(tables.Length==0)return null;
            // The coroutine itself cannot be recovered. Its visible phase, native
            // facing and maximum matching range/speed give a conservative envelope
            // until the next normally observed pattern supplies an exact endpoint.
            return new Pattern {Table=tables[0],Recovered=true,Range=tables.Max(t=>Native.D(t,"AttackRange")),Speed=tables.Max(t=>Native.D(t,"AttackSpeed"))};
        }
        CombatFrame CaptureCombatFrame(Point origin, long nativeTime)
        {
            bool predict=control.ForecastMovement && control.Mode=="Challenge" && !threats.Any(t=>t.Boss);
            if (!simulationReady || (!control.TraceCombat && !predict)) return null;
            try
            {
                var bodies = new List<CombatBody>();
                double refresh = Math.Max(.025, Math.Min(.25, Time.smoothDeltaTime * 5));
                foreach (var enemy in units.OfType<SurvivalMonster>())
                {
                    if (enemy == null || enemy.IsDead() || !enemy.gameObject.activeInHierarchy) continue;
                    var collider = (Collider)Native.Field(enemy, "_collider"); Vector3 center;
                    double radius = Native.Footprint(collider, out center), until;
                    bool stunned = Convert.ToBoolean(Native.BoundField(enemy, "simMonsterStunned"));
                    bool knockback = Native.BoundField(enemy, "simMonsterKnockback") != null;
                    var velocity = Flat((Vector3)Native.BoundField(enemy, "simMonsterVelocity"));
                    var observed = threats.FirstOrDefault(t => t.Id == enemy.GetInstanceID());
                    if (knockback && observed != null) velocity = observed.Velocity;
                    bodies.Add(new CombatBody {
                        Id = enemy.GetInstanceID(), Position = Flat(center), OriginOffset = Flat(center - enemy.transform.position), Radius = radius, Velocity = velocity,
                        Known = true, ColliderEnabled = collider != null && collider.enabled,
                        Hp = Number(enemy, "simMonsterHp"), Speed = Number(enemy, "simMonsterSpeed"), Damage = Number(enemy, "simMonsterDamage"),
                        AttackAfter = Math.Max(0, (Number(enemy, "simMonsterNextAttack") - nativeTime) / 1000),
                        AttackInterval = Number(enemy, "simMonsterAttackInterval") / 1000,
                        RefreshAfter = ((Math.Abs(enemy.GetInstanceID()) % 5 - Time.frameCount % 5 + 5) % 5) * refresh / 5,
                        // An already-active stun with no observed start gets no invented future duration.
                        StunFor = stunned && stunnedUntil.TryGetValue(enemy.GetInstanceID(), out until) ? Math.Max(0, until - combatTime) : 0,
                        MotionHoldFor = knockback ? .075 : 0
                    });
                }
                foreach(var projectile in projectiles)
                {
                    if(projectile==null || !projectile.gameObject.activeInHierarchy)continue;
                    Vector3 center;var collider=projectile.GetComponent<Collider>();double radius=Native.Footprint(collider,out center);
                    bodies.Add(new CombatBody {Id=projectile.GetInstanceID(),Projectile=true,Known=true,Position=Flat(center),Radius=radius,
                        ColliderEnabled=collider!=null&&collider.enabled,Damage=Number(projectile,"simEnemyShotDamage"),
                        Velocity=CombatSimulation.Mul(Flat((Vector3)Native.BoundField(projectile,"simEnemyShotDirection")),Number(projectile,"simEnemyShotSpeed")),
                        Lifetime=Math.Max(0,Number(projectile,"simEnemyShotLifetime")-Number(projectile,"simEnemyShotElapsed")),AttackInterval=10});
                }
                Vector3 playerCenter; double playerRadius = Native.Footprint(avatar.GetComponent<Collider>(), out playerCenter);
                var attacks = new List<CombatAttack>(); double attack = Native.Stat(Player, "Attack");
                foreach (var skill in UnityEngine.Object.FindObjectsOfType<SurvivalSkill>())
                {
                    if (!skill.gameObject.activeInHierarchy) continue;
                    string type = skill.GetType().Name;
                    bool field = type == "SurvivalSkillField", orbit=type=="SurvivalSkillRotationProjectile", linear = type == "SurvivalSkillProjectile" || type == "SurvivalSkillBlade";
                    // Animated whips, future casts and unchosen random targets remain uncredited.
                    if (!field && !linear && !orbit) continue;
                    var table = Native.Call("simSkillTable", skill);
                    var colliders=skill.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&c.gameObject.activeInHierarchy);
                    // One native field owns one target set, even if its prefab has
                    // several overlapping shapes. A single shape is a safe subset.
                    if(field)colliders=colliders.Take(1);
                    foreach(var collider in colliders)
                    {
                    if(collider==null || !collider.enabled || !collider.gameObject.activeInHierarchy)continue;
                    Vector3 center;
                    double radius = Native.Footprint(collider, out center);
                    var shot = new CombatAttack { Id = collider.GetInstanceID(), SkillId = Native.I(table, "GroupId"), Position = Flat(center), Radius = radius,
                        Damage = (float)Native.D(table, "SkillValue") * attack, Known = true,
                        Motion = orbit ? AttackMotion.Orbit : linear ? AttackMotion.Linear : AttackMotion.Attached,
                        Lifetime = field ? 3 : orbit ? Math.Max(0,Number(skill,"simOrbitLifetime")-Number(skill,"simOrbitElapsed")) : Math.Max(0, Number(skill, type + "Lifetime") - Number(skill, type + "Elapsed")) };
                    if (collider is BoxCollider)
                    {
                        var box = (BoxCollider)collider;
                        var x = box.transform.TransformVector(new Vector3(box.size.x * .5f, 0, 0));
                        var z = box.transform.TransformVector(new Vector3(0, 0, box.size.z * .5f));
                        shot.Box = true; shot.Axis = CombatSimulation.Unit(Flat(x)); shot.HalfLength = Flat(x).X * shot.Axis.X + Flat(x).Z * shot.Axis.Z; shot.HalfWidth = Math.Sqrt(z.x * z.x + z.z * z.z);
                        // Non-planar/animated shapes do not provide guaranteed damage credit.
                        if (Math.Abs(x.y) > .01 || Math.Abs(z.y) > .01 || orbit) shot.Known = false;
                    }
                    else if (!(collider is SphereCollider)) shot.Known = false;
                    if (linear)
                    {
                        shot.Velocity = CombatSimulation.Mul(Flat((Vector3)Native.BoundField(skill, type + "Direction")), Number(skill, type + "Speed"));
                        shot.Pierce = Math.Max(0, Math.Max(1, Native.I(table, "Pierce")) - (int)Number(skill, type + "Hits"));
                        if (type == "SurvivalSkillProjectile") shot.ExplosionRadius = Native.D(table, "AttackSize");
                    }
                    else if(field) { shot.Interval = Number(skill, "simFieldInterval"); shot.After = Math.Max(0, shot.Interval - Number(skill, "simFieldElapsed")); }
                    else
                    {
                        // Native orbit center has a fixed world-space Z offset. Unity's
                        // positive Y rotation is clockwise in our X/Z plane.
                        shot.OrbitCenter=CombatSimulation.Add(Flat(skill.transform.position),Flat((Vector3)Native.BoundField(skill,"simOrbitOffset")));
                        shot.OrbitOffset=CombatSimulation.Sub(shot.Position,shot.OrbitCenter);
                        shot.AngularSpeed=-Number(skill,"simOrbitSpeed")*Native.Stat(Player,"ProjectileSpeed")*Math.PI/180;
                    }
                    attacks.Add(shot);
                    }
                }
                var healing=new List<CombatPickup>();
                foreach(var item in healingItems)
                {
                    if(item==null || !item.gameObject.activeInHierarchy)continue;
                    var table=Native.ItemTable(Native.I(item,"FieldRewardObjectId"));
                    if(table==null || Native.I(table,"ItemType")!=0)continue;
                    Vector3 center;double radius=Native.Footprint(item.GetComponent<Collider>(),out center);
                    healing.Add(new CombatPickup {Id=item.GetInstanceID(),Position=Flat(center),Radius=radius+playerRadius,Amount=Native.D(table,"ItemValue"),
                        Speed=Number(item,"simItemSpeed"),Following=Native.BoundField(item,"itemTarget") as Transform != null});
                }
                combatFrame = new CombatFrame { Player = origin, Time = combatTime, Hp = hp, MaxHp=maxHp, PlayerRadius = Math.Max(.1, playerRadius), Healing=healing.ToArray(),
                    PreserveControlSpace=control.Mode=="Challenge"&&(seconds>=900 || bossArrival!=null&&bossArrival.Active),
                    ControlledExit=lastTactic!=null&&lastTactic.ControlledExit,ControlTarget=lastTactic==null?new Point():lastTactic.ControlTarget,Room=arenaRoom,
                    Defence = Native.Stat(Player, "Defence"), InvulnerableFor = Math.Max(0, (Number(Player, "simPlayerInvulnerable") - nativeTime) / 1000),
                    // Omitted attack effects can only remove damage credit. Bound the
                    // planning cost even when an evolved weapon spawns many objects.
                    RefreshSeconds = refresh, Enemies = bodies.ToArray(), Attacks = attacks.OrderByDescending(a=>a.Motion==AttackMotion.Attached || a.Motion==AttackMotion.Orbit).ThenBy(a=>Point.Distance(origin,a.Position)).Take(32).ToArray(), Flow = CaptureFlow(),
                    Weapons = Native.Items(Native.Call("weapons", Player)).Select(o => new WeaponClock { Id = (int)Number(o,"ownedId"), Level = (int)Number(o,"ownedLevel"), Next = Math.Max(0,(Number(o,"simWeaponNext") - nativeTime) / 1000), Period = Number(o,"simWeaponPeriod") / 1000 }).ToArray() };
                var flowNative=FlowFieldController.Instance;
                if(flowNative!=null) { var bounds=(Bounds)Native.BoundField(flowNative,"simFlowBounds");combatFrame.MapMin=Flat(bounds.min);combatFrame.MapMax=Flat(bounds.max);combatFrame.Bounded=true; }
                CombatSimulation.Prepare(combatFrame); simulationError = ""; return combatFrame;
            }
            catch (Exception e) { combatFrame = null; simulationError = e.GetType().Name + ": " + e.Message; return null; }
        }
        CombatFlow CaptureFlow()
        {
            var native = FlowFieldController.Instance; if (native == null) return null;
            var vectors = (Dictionary<ValueTuple<int,int>,Vector3>)Native.BoundField(native,"simFlowDirections");
            var obstacles = (Dictionary<ValueTuple<int,int>,int>)Native.BoundField(native,"simFlowObstacles");
            if (vectors == null || vectors.Count == 0) return null;
            if (flowInstance != native.GetInstanceID())
            {
                flowInstance = native.GetInstanceID(); int min = int.MaxValue, mz = int.MaxValue, max = int.MinValue, maxz = int.MinValue;
                foreach(var pair in vectors) { min=Math.Min(min,pair.Key.Item1); max=Math.Max(max,pair.Key.Item1); mz=Math.Min(mz,pair.Key.Item2); maxz=Math.Max(maxz,pair.Key.Item2); }
                flowMinX=min; flowMinZ=mz; flowWidth=max-min+1; flowHeight=maxz-mz+1;
                openArena=!native.dynamicObstacles && !obstacles.Values.Any(v=>v==int.MaxValue);
            }
            int minX=flowMinX,minZ=flowMinZ,w=flowWidth,h=flowHeight; if ((long)w*h > 65536) return null;
            var flow = new CombatFlow { CellSize = native.cellSize, MinX = minX, MinZ = minZ, Width = w, Height = h,
                Origin = Flat(((Bounds)Native.BoundField(native,"simFlowBounds")).center), RefreshSeconds = native.refreshRate, RefreshAfter = Number(native,"simFlowNext"),
                OpenArena=openArena };
            if(openArena)
            {
                foreach(var pair in vectors) if(pair.Value==Vector3.zero) { flow.InitialGoal=(pair.Key.Item2-minZ)*w+pair.Key.Item1-minX; break; }
                if(flow.InitialGoal>=0) return flow;
                flow.OpenArena=false;
            }
            flow.Obstacles=new int[w*h];flow.Initial=new Point[w*h];
            foreach (var pair in vectors) { int at = (pair.Key.Item2-minZ)*w+pair.Key.Item1-minX; flow.Initial[at] = Flat(pair.Value); int cost; if(obstacles.TryGetValue(pair.Key,out cost)) flow.Obstacles[at]=cost; }
            return flow;
        }
    }
}
