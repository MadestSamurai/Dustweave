using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Dustweave.Pandora.Runtime
{
    static class Native
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();
        static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();
        public static MethodInfo Bound(string key) { MethodInfo m; if (!methods.TryGetValue(key, out m)) { var p = Bindings.Names[key].Split('|'); m = typeof(SurvivalMainUI).Assembly.GetType(p[0], true).GetMethods(All).Single(x => x.Name == p[1] && x.GetParameters().Length == int.Parse(p[2])); methods[key] = m; } return m; }
        public static object Call(string key, object instance, params object[] args) { return Bound(key).Invoke(instance, args); }
        public static object Invoke(object instance, string name, params object[] args)
        {
            var type = instance as Type ?? instance.GetType(); string key = type.FullName + "::" + name + "/" + args.Length; MethodInfo m;
            if (!methods.TryGetValue(key, out m)) { m = type.GetMethods(All).Single(x => x.Name == name && x.GetParameters().Length == args.Length); methods[key] = m; }
            return m.Invoke(instance is Type ? null : instance, args);
        }
        public static object Get(object instance, string property) { return Invoke(instance, "get_" + property); }
        public static object Field(object instance, string name)
        {
            var type = instance.GetType(); string key = type.FullName + ":" + name; FieldInfo f;
            if (!fields.TryGetValue(key, out f)) { for (var t = type; t != null; t = t.BaseType) { f = t.GetField(name, All); if (f != null) break; } if (f == null) throw new MissingFieldException(type.Name, name); fields[key] = f; }
            return f.GetValue(instance);
        }
        public static object BoundField(object instance, string key) { return Field(instance, Bindings.Names[key]); }
        public static int I(object x, string p) { return Convert.ToInt32(Get(x, p)); }
        public static double D(object x, string p) { return Convert.ToDouble(Get(x, p)); }
        public static bool B(object x, string p) { return Convert.ToBoolean(Get(x, p)); }
        public static IEnumerable<object> Items(object value) { return ((IEnumerable)value).Cast<object>(); }
        public static double Stat(object player, string name, bool upgrade = false) { var m = Bound(upgrade ? "upgradeStat" : "stat"); return Convert.ToDouble(m.Invoke(player, new[] { Enum.Parse(m.GetParameters()[0].ParameterType, name) })); }
        public static object FieldManager { get { return Invoke(typeof(GameFieldManager), "get_Instance"); } }
        public static object ActiveParam { get { return Get(FieldManager, "EnterHiddenPackDataParam"); } }
        public static int ActiveStage { get { return Convert.ToInt32(BoundField(ActiveParam, "activeStage")); } }
        public static int ActiveGroup { get { return Convert.ToInt32(BoundField(ActiveParam, "activeGroup")); } }
        public static T Find<T>() where T : Component { return UnityEngine.Object.FindObjectsOfType<T>().FirstOrDefault(x => x.gameObject.activeInHierarchy); }
        public static string Local(int id) { return Convert.ToString(Call("localText", null, id)); }
        public static bool Ready(UIBase ui)
        {
            if (ui == null || !B(ui, "IsAvailableClick")) return false;
            // Some native pages intentionally have no animation controller. Their generic
            // IsPlayingAnimation getter dereferences it unconditionally.
            var animation = BoundField(ui, "uiAnimation");
            return animation == null || !B(animation, "IsPlaying");
        }
        public static bool Click(Component panel, string field) { var v = Field(panel, field); var go = v as GameObject ?? (v as Component)?.gameObject; return Click(panel, go); }
        public static bool Click(Component panel, GameObject go) { if (go == null || !go.activeInHierarchy) return false; var root = panel as UIBase ?? panel.GetComponentInParent<UIBase>(); if (!Ready(root)) return false; root.OnClickUI(go); return true; }
        public static bool OpenFromHub(MiniGameHubUI hub)
        {
            if (!Ready(hub)) return false;
            // Use the game's current available list; no historic schedule or cartridge IDs.
            foreach (var entry in Items(BoundField(hub, "hubEntries")))
            {
                var table = Call("hubTable", null, I(entry, "EventId"));
                if (table != null && I(table, "EventClearType") == int.Parse(Bindings.Names["survivalKind"]))
                {
                    hub.ClickPlayMiniGame(I(entry, "Id"), I(entry, "EventId")); return true;
                }
            }
            return false;
        }
        public static Stage[] Stages(SurvivalMainUI main)
        {
            var groups = Items(Call("groups", null, I(main, "SurvivalTableid"))).ToArray(); var result = new List<Stage>();
            for (int index = 0; index < groups.Length; index++)
            {
                var group = groups[index]; int groupId = I(group, "Id"); bool unlocked = Convert.ToBoolean(Call("unlocked", null, groupId));
                string name = Local(I(group, "MapNameTextId")); try { name = string.Format(name, groupId); } catch (FormatException) { }
                foreach (var map in Items(Call("maps", null, groupId)))
                {
                    int difficulty = I(map, "StageDifficulty");
                    result.Add(new Stage
                    {
                        Id = I(map, "Id"),
                        Group = groupId,
                        Difficulty = difficulty,
                        Index = index,
                        Challenge = I(map, "StageType") == 1,
                        Name = name,
                        Unlocked = unlocked && (difficulty <= 1 || Convert.ToBoolean(Call("cleared", null, groupId, difficulty - 1)))
                    });
                }
            }
            return result.ToArray();
        }
        public static bool SkillAlreadyApplied(SurvivalSelectUI ui, object player)
        {
            if (player == null) return false;
            var owned = Items(Call("weapons", player)).Concat(Items(Call("passives", player))).ToArray();
            // Native cards show the next level, not the owned level. This also recovers
            // an acknowledged but still-open page after a host/component restart.
            return ((SurvivalCardItem[])Field(ui,"_slots")).Where(x=>x.gameObject.activeInHierarchy).Any(card =>
                owned.Any(o => Convert.ToInt32(BoundField(o,"ownedId")) == Convert.ToInt32(BoundField(card,"skillId")) &&
                    Convert.ToInt32(BoundField(o,"ownedLevel")) >= Convert.ToInt32(BoundField(card,"skillLevel"))));
        }
        public static SkillOption Skill(int id, int level)
        {
            var g = Call("skillGroup", null, id); var r = Call("skill", null, id, level);
            if (g == null || r == null) throw new InvalidOperationException("Pandora skill table unavailable: " + id + "/" + level);
            return new SkillOption
            {
                Id = id,
                Level = level,
                Type = I(g, "SkillType"),
                Subtype = I(g, "SkillSubType"),
                Stat = I(r, "StatType"),
                MaxLevel = I(g, "MaxLevel"),
                SynergyId = I(g, "UpgradeSynergyId"),
                TargetType = I(g, "TargetType"),
                Name = Local(I(r, "SkillNameLocalTextId")),
                Value = D(r, "SkillValue"),
                Cooldown = D(r, "Cooldown"),
                Duration = D(r, "Duration"),
                Range = D(r, "AttackRange"),
                Size = D(r, "AttackSize"),
                AttackCount = I(r, "AttackCount"),
                Pierce = I(r, "Pierce"),
                ProjectileTerm = D(r, "ProjectileTerm"),
                ProjectileSpeed = D(r, "ProjectileSpeed"),
                Knockback = I(r, "KnockbackType")
            };
        }
        public static double Footprint(Collider co, out Vector3 center)
        {
            center = co == null ? Vector3.zero : co.transform.position; if (co == null) return .5;
            if (co is SphereCollider) { var c = (SphereCollider)co; center = c.transform.TransformPoint(c.center); var s = c.transform.lossyScale; return c.radius * Math.Max(Math.Abs(s.x), Math.Abs(s.z)); }
            if (co is CapsuleCollider) { var c = (CapsuleCollider)co; center = c.transform.TransformPoint(c.center); var s = c.transform.lossyScale; return c.radius * Math.Max(Math.Abs(s.x), Math.Abs(s.z)); }
            if (co is BoxCollider) { var c = (BoxCollider)co; center = c.transform.TransformPoint(c.center); var s = c.transform.lossyScale; return .5 * Math.Sqrt(c.size.x * c.size.x * s.x * s.x + c.size.z * c.size.z * s.z * s.z); }
            return co.bounds.size.sqrMagnitude > .001 ? Math.Sqrt(co.bounds.extents.x * co.bounds.extents.x + co.bounds.extents.z * co.bounds.extents.z) : .5;
        }
    }
}
