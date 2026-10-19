using BD2FiendHunter.Shared;
internal static class FiendHunterCases {
 public static void Run(List<string> cases){
  void Check(bool condition,string name){if(!condition)throw new Exception(name);cases.Add("fiend-hunter: "+name);}
  var control=new Control();Check(control.BossGroup==1,"existing users retain Berserker selection");
  Check(control.Character==1&&control.Difficulty==3,"legacy settings retain Blade Very Hard");
  for(int character=1;character<=3;character++)for(int monster=1;monster<=2;monster++)for(int difficulty=1;difficulty<=3;difficulty++){
   var setup=new Control{Character=character,BossGroup=monster,Difficulty=difficulty};setup.Validate();
   Check(setup.Matches(character,monster*100+difficulty)&&!setup.Matches(character%3+1,monster*100+difficulty)&&!setup.Matches(character,monster*100+4),"solo setup identity "+character+monster+difficulty);
  }
  foreach(var setup in new[]{new Control{Character=0},new Control{Character=4},new Control{Difficulty=0},new Control{Difficulty=4}}){bool invalid=false;try{setup.Validate();}catch(ArgumentException){invalid=true;}Check(invalid,"invalid role/difficulty rejected");}
  Check(CombatPolicy.Special(1,0,true,false,true)&&!CombatPolicy.Special(1,100,false,false,true),"Blade still requires gauge");
  Check(CombatPolicy.Special(3,55,false,false,true)&&!CombatPolicy.Special(3,54,false,false,true),"Granhildr reserves guard stamina");
  Check(!CombatPolicy.Special(3,100,false,true,true)&&!CombatPolicy.Special(3,100,false,false,false),"do not refresh active buff or special into danger");
  Check(!CombatPolicy.Charge(2,100,2,false)&&CombatPolicy.Charge(2,100,3.2,false)&&CombatPolicy.Charge(2,45,1.7,true),"Teresse charge respects actual stage time reduction");
  Check(!CombatPolicy.Charge(2,44,10,true)&&!CombatPolicy.Charge(3,100,10,true),"charge needs reserve and matching character");
  foreach(int character in new[]{1,2,3}){
   var setup=new Control{Character=character};
   Check(CombatPolicy.Choose(new(){Hp=2000,MaxHp=2000,Stamina=100,Idle=true,Distance=1.1,Facing=1},setup)==CombatChoice.Attack,"all characters attack within ground reach "+character);
   Check(CombatPolicy.Choose(new(){Hp=2000,MaxHp=2000,Stamina=100,Idle=true,Distance=3,Facing=1},setup)==CombatChoice.Approach,"distant characters still approach "+character);
  }
  control.BossGroup=2;control.Validate();control.BossGroup=3;
  bool rejected=false;try{control.Validate();}catch(ArgumentException){rejected=true;}
  Check(rejected,"unknown monster cannot start automation");control.BossGroup=1;
  Check(AttackTiming.NextImpact(1,"Attack1",.1)==AttackTiming.Impact("Attack1"),"Berserker timing is preserved");
  Check(AttackTiming.NextImpact(2,"Attack1",.1)<AttackTiming.NextImpact(1,"Attack1",.1),"same-named Tiger attack is not given Berserker timing");
  Check(AttackTiming.NextImpact(2,"Attack14",.1)<.2&&AttackTiming.NextImpact(2,"Attack14",.3)>.4,"Tiger contact and explosion use separate windows");
  Check(AttackTiming.NextImpact(2,"Attack12",.1)>AttackTiming.NextImpact(2,"Attack11",.1),"Tiger leap is later than the similar claw animation");
  Check(AttackTiming.NextImpact(2,"Attack22",.1)>.46&&AttackTiming.NextImpact(2,"Attack17",.1)>.49,"Tiger large attacks use observed activation phases");
  Check(AttackTiming.NextImpact(2,"Idle",0)<0&&AttackTiming.NextImpact(2,"FutureAttack",0)<0,"unknown animation does not fabricate an impact");
  Check(CombatPolicy.Choose(new(){Hp=400,MaxHp=1800,Stamina=0,Heals=3,Threat=true,HealReady=true,Idle=true},control)==CombatChoice.Retreat,"healing cannot preempt an incoming attack");
  Check(CombatPolicy.Choose(new(){Hp=1141,MaxHp=2000,IncomingDamage=1170,Stamina=80,Heals=4,HealReady=true,Idle=true},control)==CombatChoice.Heal,"heal above the percentage threshold before a lethal heavy hit");
  Check(!CombatPolicy.NeedsHeal(1800,2000,1170,4,control),"healthy character keeps attacking instead of topping up");
  Check(!CombatPolicy.NeedsHeal(1141,2000,1170,0,control),"empty healing stock cannot stall offense");
  Check(Math.Abs(CombatPolicy.HealThreshold(2000,100,control)-2000.0*control.HealFraction)<.001,"low difficulty does not inherit Very Hard healing reserve");
  Check(CombatPolicy.HealThreshold(2000,4000,control)==1700,"unavoidable lethal hit does not consume heals at full health");
  var key="BD2.LocalIpc.SuiteDataRoot.v1";var previous=AppDomain.CurrentDomain.GetData(key);
  try{var isolated=Path.Combine(Path.GetTempPath(),"dw-fiend-fixture");AppDomain.CurrentDomain.SetData(key,isolated);Check(ToolStorage.Diagnostics==Path.Combine(isolated,"tools","fiend-hunter","diagnostics"),"native diagnostics follow the exact host data root");}
  finally{AppDomain.CurrentDomain.SetData(key,previous);}
 }
}
