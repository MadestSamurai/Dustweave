using BD2FiendHunter.Shared;
internal static class FiendHunterCases {
 public static void Run(List<string> cases){
  void Check(bool condition,string name){if(!condition)throw new Exception(name);cases.Add("fiend-hunter: "+name);}
  var control=new Control();Check(control.BossGroup==1,"existing users retain Berserker selection");
  control.BossGroup=2;control.Validate();control.BossGroup=3;
  bool rejected=false;try{control.Validate();}catch(ArgumentException){rejected=true;}
  Check(rejected,"unknown monster cannot start automation");control.BossGroup=1;
  Check(AttackTiming.NextImpact(1,"Attack1",.1)==AttackTiming.Impact("Attack1"),"Berserker timing is preserved");
  Check(AttackTiming.NextImpact(2,"Attack1",.1)<AttackTiming.NextImpact(1,"Attack1",.1),"same-named Tiger attack is not given Berserker timing");
  Check(AttackTiming.NextImpact(2,"Attack14",.1)<.2&&AttackTiming.NextImpact(2,"Attack14",.3)>.4,"Tiger contact and explosion use separate windows");
  Check(AttackTiming.NextImpact(2,"Idle",0)<0&&AttackTiming.NextImpact(2,"FutureAttack",0)<0,"unknown animation does not fabricate an impact");
  Check(CombatPolicy.Choose(new(){Hp=400,MaxHp=1800,Stamina=0,Heals=3,Threat=true,HealReady=true,Idle=true},control)==CombatChoice.Retreat,"healing cannot preempt an incoming attack");
  var key="BD2.LocalIpc.SuiteDataRoot.v1";var previous=AppDomain.CurrentDomain.GetData(key);
  try{var isolated=Path.Combine(Path.GetTempPath(),"dw-fiend-fixture");AppDomain.CurrentDomain.SetData(key,isolated);Check(ToolStorage.Diagnostics==Path.Combine(isolated,"tools","fiend-hunter","diagnostics"),"native diagnostics follow the exact host data root");}
  finally{AppDomain.CurrentDomain.SetData(key,previous);}
 }
}
