namespace BD2FiendHunter.Shared {
public enum CombatChoice{Wait,Dodge,Heal,Retreat,Recover,Approach,Face,Attack}
public sealed class CombatFacts{public double Hp,MaxHp,Stamina,Distance,Facing;public int Heals;public bool Threat,DodgeReady,HealReady,Idle;}
public static class CombatPolicy{
 public static CombatChoice Choose(CombatFacts f,Control c){
  if(f.Hp<=0||f.MaxHp<=0)return CombatChoice.Wait;
  if(f.Threat&&f.DodgeReady&&f.Stamina>=25)return CombatChoice.Dodge;
  if(!f.Idle)return CombatChoice.Wait;
  if(f.Hp<f.MaxHp*c.HealFraction&&f.Heals>0&&f.HealReady&&!f.Threat)return CombatChoice.Heal;
  if(f.Threat)return CombatChoice.Retreat;
  if(f.Stamina<24)return CombatChoice.Recover;
  if(f.Distance>c.AttackRange)return CombatChoice.Approach;
  if(f.Facing<.92)return CombatChoice.Face;
  return CombatChoice.Attack;
 }
}
}
