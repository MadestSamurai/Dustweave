using Mono.Cecil;
using Mono.Cecil.Cil;
namespace Dustweave.Compatibility;
public static partial class PandoraCompiler
{
    static void PrepareSimulation(ModuleDefinition module, Dictionary<string, string> names)
    {
        var found = new Dictionary<string, string>();
        TypeDefinition T(string n) => module.Types.Single(t => t.Name == n);
        MethodDefinition M(TypeDefinition t, string n) => t.Methods.Single(m => m.Name == n);
        IEnumerable<MethodReference> Calls(MethodDefinition m) => m.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>();
        void Field(string key, FieldReference f) => found.Add(key, f.Name);
        void Method(string key, MethodReference m) => found.Add(key, m.DeclaringType.FullName.Replace('/', '+') + "|" + m.Name + "|" + m.Parameters.Count);
        FieldReference FromArgument(MethodDefinition method, int index) => (FieldReference)method.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld &&
            (i.Previous.Operand == method.Parameters[index] || i.Previous.OpCode.Code == (index == 0 ? Code.Ldarg_1 : index == 1 ? Code.Ldarg_2 : index == 2 ? Code.Ldarg_3 : Code.Nop))).Operand;
        try
        {
            var spawnManager=M(T("EventMiniGameSurvivalManager"),"get_SpawnerManager").ReturnType.Resolve();
            Method("simMonsterSchedule",spawnManager.Methods.Where(m=>m.HasBody).SelectMany(Calls)
                .Where(m=>m.ReturnType is GenericInstanceType g && g.GenericArguments.Count==1 && g.GenericArguments[0].Name=="FieldMiniGameMonsterTable")
                .DistinctBy(m=>m.FullName).Single());
            var monster = T("SurvivalMonster"); var initialize = M(monster, "Initialize"); var damaged = M(monster, "Damaged"); var contact = M(monster, "FixedUpdate");
            Field("simMonsterHp", (FieldReference)damaged.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Previous.OpCode == OpCodes.Sub).Operand);
            Field("simMonsterSpeed", FromArgument(initialize, 5));
            var contactFields = contact.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().ToArray();
            Field("simMonsterDamage", contactFields.Single(f => f.FieldType.Name == "Int32"));
            var timers = contactFields.Where(f => f.FieldType.Name == "Int64").DistinctBy(f => f.FullName).ToArray();
            if (timers.Length != 2) throw new InvalidOperationException("Monster contact clock changed");
            Field("simMonsterNextAttack", timers[0]); Field("simMonsterAttackInterval", timers[1]);
            Field("simMonsterStunned", contactFields.Single(f => f.FieldType.Name == "Boolean"));
            var moving = monster.Methods.Single(m => m.HasBody && Calls(m).Any(c => c.Name == "get_frameCount"));
            Field("simMonsterVelocity", moving.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Where(f => f.FieldType.FullName == "UnityEngine.Vector3").DistinctBy(f => f.FullName).Single());
            Field("simMonsterKnockback", moving.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single(f => f.FieldType.Name == "Coroutine"));
            var flow = T("FlowFieldController");
            Field("simFlowDirections", flow.Fields.Single(f => f.FieldType is GenericInstanceType g && g.ElementType.Name == "Dictionary`2" && g.GenericArguments[1].Name == "Vector3"));
            Field("simFlowObstacles", flow.Fields.Single(f => f.FieldType is GenericInstanceType g && g.ElementType.Name == "Dictionary`2" && g.GenericArguments[1].Name == "Int32"));
            Field("simFlowBounds", flow.Fields.Single(f => f.FieldType.Name == "Bounds"));
            Field("simFlowNext", (FieldReference)M(flow,"LateUpdate").Body.Instructions.Single(i=>i.OpCode==OpCodes.Stfld && i.Previous.OpCode==OpCodes.Sub).Operand);
            var player = M(T("EventMiniGameSurvivalManager"), "get_Player").ReturnType.Resolve();
            var hit = Calls(contact).Single(c => c.DeclaringType.FullName == player.FullName).Resolve();
            Field("simPlayerInvulnerable", hit.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().First(f => f.FieldType.Name == "Int64"));
            Field("simItemSpeed",T("SurvivalItem").Fields.Single(f=>f.FieldType.Name=="Single"));
            var projectile=T("EventMiniGameEnemyProjectile");var projectileInit=M(projectile,"Intiailize");
            Field("simEnemyShotDamage",FromArgument(projectileInit,0));
            var enemyShotSpeed=FromArgument(projectileInit,1);Field("simEnemyShotSpeed",enemyShotSpeed);
            Field("simEnemyShotDirection",FromArgument(projectileInit,2));
            var shotElapsed=(FieldReference)M(projectile,"Update").Body.Instructions.Single(i=>i.OpCode==OpCodes.Stfld && i.Previous.OpCode==OpCodes.Add).Operand;
            Field("simEnemyShotElapsed",shotElapsed);
            Field("simEnemyShotLifetime",projectile.Fields.Single(f=>f.FieldType.Name=="Single"&&f.FullName!=shotElapsed.FullName&&f.FullName!=enemyShotSpeed.FullName));
            var own = player.Methods.Select(m => m.ReturnType).OfType<GenericInstanceType>().First(g => g.ElementType.Name == "IEnumerable`1").GenericArguments[0].Resolve();
            var cooldown = player.Methods.Single(m => m.HasBody && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == own.FullName && Calls(m).Any(c => c.Name == "get_Cooldown") && Calls(m).Any(c => c.DeclaringType.FullName == own.FullName && c.ReturnType.Name == "Void" && c.Parameters.Count == 1 && c.Parameters[0].ParameterType.Name == "Int64"));
            var next = Calls(cooldown).Single(c => c.DeclaringType.FullName == own.FullName && c.ReturnType.Name == "Void" && c.Parameters[0].ParameterType.Name == "Int64").Resolve();
            Field("simWeaponNext", FromArgument(next, 0));
            var period = Calls(cooldown).Single(c => c.DeclaringType.FullName == own.FullName && c.ReturnType.Name == "Void" && c.Parameters[0].ParameterType.Name == "Int32").Resolve();
            Field("simWeaponPeriod", FromArgument(period, 0));
            Method("simSkillTable", T("SurvivalSkill").Methods.Single(m => m.ReturnType.Name == "FieldMiniGameSkillTable"));
            foreach (string type in new[] { "SurvivalSkillProjectile", "SurvivalSkillBlade", "SurvivalSkillWhip", "SurvivalSkillLaserDrone" })
            {
                var t = T(type); var update = M(t, "Update");
                var elapsed = (FieldReference)update.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Previous.OpCode == OpCodes.Add).Operand;
                Field(type + "Elapsed", elapsed);
                Field(type + "Lifetime", update.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Where(f => f.FieldType.Name == "Single" && f.FullName != elapsed.FullName).DistinctBy(f => f.FullName).Last());
                if (type == "SurvivalSkillProjectile" || type == "SurvivalSkillBlade")
                {
                    Field(type + "Direction", FromArgument(M(t, "SetDirection"), 0));
                    var init = M(t, "Initialize");
                    var speedStore = init.Body.Instructions.First(i => i.OpCode == OpCodes.Stfld && i.Previous.OpCode == OpCodes.Mul);
                    Field(type + "Speed", (FieldReference)speedStore.Operand);
                    Field(type + "Hits", M(t, "OnTriggerEnter").Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Where(f => f.DeclaringType.FullName == t.FullName && (f.FieldType.Name == "Int32" || f.FieldType.Name == "Single"))
                        .GroupBy(f => f.FullName).Where(g => M(t,"OnTriggerEnter").Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.FullName == g.Key)).Select(g=>g.First()).Single());
                }
            }
            var field = T("SurvivalSkillField"); var fieldUpdate = M(field, "Update");
            Field("simFieldElapsed", (FieldReference)fieldUpdate.Body.Instructions.Last(i => i.OpCode == OpCodes.Stfld && i.Previous.OpCode == OpCodes.Add).Operand);
            Field("simFieldInterval", (FieldReference)M(field,"Initialize").Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Previous.OpCode == OpCodes.Conv_R4 && i.Previous.Previous.Operand is MethodReference c && c.Name == "get_PlayerAttackInterval").Operand);
            var orbit=T("SurvivalSkillRotationProjectile");var orbitUpdate=M(orbit,"Update");
            var orbitElapsed=(FieldReference)orbitUpdate.Body.Instructions.Last(i=>i.OpCode==OpCodes.Stfld && i.Previous.OpCode==OpCodes.Add).Operand;
            Field("simOrbitElapsed",orbitElapsed);
            Field("simOrbitLifetime",orbitUpdate.Body.Instructions.Select(i=>i.Operand).OfType<FieldReference>().Where(f=>f.FieldType.Name=="Single" && f.FullName!=orbitElapsed.FullName).DistinctBy(f=>f.FullName).Last());
            var orbitInit=M(orbit,"Initialize");
            Field("simOrbitSpeed",(FieldReference)orbitInit.Body.Instructions.SkipWhile(i=>!(i.Operand is MethodReference c && c.Name=="get_ProjectileSpeed")).Skip(1).First(i=>i.OpCode==OpCodes.Stfld).Operand);
            Field("simOrbitOffset",orbit.Fields.Single(f=>f.FieldType.FullName=="UnityEngine.Vector3"));
            var boss=T("SurvivalBossMonster");var bossInit=M(boss,"Initialize");
            Field("simBossPatternGroup",(FieldReference)bossInit.Body.Instructions.Single(i=>i.OpCode==OpCodes.Stfld && i.Previous.Operand is MethodReference c && c.Name=="get_AttackPatternGroupId").Operand);
            Method("simBossPatterns",module.Types.SelectMany(t=>t.Methods).Single(m=>m.IsStatic&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.Name=="Int32"&&m.ReturnType is GenericInstanceType g&&g.ElementType.Name=="List`1"&&g.GenericArguments[0].Name=="FieldMiniGameBossPatternTable"));
            foreach (var pair in found) names.Add(pair.Key, pair.Value);
            names.Add("simulationContract", "1");
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            // A new unsupported native attack must not disable all existing controls.
            names["simulationContract"] = "0";
            names["simulationContractError"] = e.ToString();
        }
    }
}
