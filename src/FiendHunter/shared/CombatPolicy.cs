namespace BD2FiendHunter.Shared {
public enum CombatChoice{Wait,Dodge,Heal,Retreat,Recover,Approach,Face,Attack}
public sealed class CombatFacts{public double Hp,MaxHp,Stamina,Distance,Facing,IncomingDamage;public int Heals;public bool Threat,DodgeReady,HealReady,Idle;}
public static class CombatPolicy{
 // Reserve enough health for a missed heavy hit, without repeatedly topping up.
 // IncomingDamage comes from this boss's native skills and current attack power.
 public static double HealThreshold(double maxHp,double incomingDamage,Control c){
  return System.Math.Max(maxHp*c.HealFraction,System.Math.Min(maxHp*.85,incomingDamage*1.08+maxHp*.03));
 }
 public static bool NeedsHeal(double hp,double maxHp,double incomingDamage,int heals,Control c){
  return hp>0&&maxHp>0&&heals>0&&hp<HealThreshold(maxHp,incomingDamage,c);
 }
 // A full gauge belongs to Blade only. The other specials spend stamina.
 public static bool Special(int character,double stamina,bool gaugeFull,bool attackBuff,bool safe){
  if(!safe)return false;
  if(character==1)return gaugeFull;
  return character==3&&!attackBuff&&stamina>=55;
 }
 public static bool Charge(int character,double stamina,double safeSeconds,bool chargedBuff){
  return character==2&&stamina>=45&&safeSeconds>=(chargedBuff?1.65:3.15);
 }
 public static CombatChoice Choose(CombatFacts f,Control c){
  if(f.Hp<=0||f.MaxHp<=0)return CombatChoice.Wait;
  if(f.Threat&&f.DodgeReady&&f.Stamina>=25)return CombatChoice.Dodge;
  if(!f.Idle)return CombatChoice.Wait;
  if(NeedsHeal(f.Hp,f.MaxHp,f.IncomingDamage,f.Heals,c)&&f.HealReady&&!f.Threat)return CombatChoice.Heal;
  if(f.Threat)return CombatChoice.Retreat;
  if(f.Stamina<24)return CombatChoice.Recover;
  if(f.Distance>c.AttackRange)return CombatChoice.Approach;
  if(f.Facing<.92)return CombatChoice.Face;
  return CombatChoice.Attack;
 }
}
}
