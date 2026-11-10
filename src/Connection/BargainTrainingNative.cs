using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Collections.Generic;
using Proto.Design.common;
using gamfs;
namespace BD2Daily.Live {
 [DataContract] internal sealed class PracticeDisplayName {
  [DataMember] public string Cn="",Tw="",En="";
 }
 [DataContract] internal sealed class PracticeTalentName {
  [DataMember] public int Kind;
  [DataMember] public PracticeDisplayName Names;
 }
 [DataContract] internal sealed class BargainCharacterState {
  [DataMember] public long Character,Experience,Previous,Target;
  [DataMember] public int Id,Level,MaxLevel,Group,Skill,PerUse,Cost,Kind;
  [DataMember] public bool Active;
  [DataMember] public string Name="";
  [DataMember] public PracticeDisplayName Names;
 }
 [DataContract] internal sealed class BargainTrainingState {
  [DataMember] public long Catalyst,Gold;
  [DataMember] public BargainCharacterState[] Characters;
  [DataMember] public PracticeTalentName[] Talents;
 }
 // Read-only account data. The cap follows CharTalentUI's cumulative experience formula.
 internal static class BargainTrainingNative {
  static readonly int[] fieldKinds={2,6,12,13,17};
  // Only immutable game definitions are cached. Never cache XP, ownership or effects.
  static readonly Dictionary<int,PracticeDisplayName> names=new Dictionary<int,PracticeDisplayName>();
  static PracticeTalentName[] talents;
  static PracticeDisplayName Name(int id){
   PracticeDisplayName value;
   if(names.TryGetValue(id,out value))return value;
   var row=Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ.GetValueObject<NameTextTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON,"NameTextTable","id",id);
   value=row==null?new PracticeDisplayName():new PracticeDisplayName{Cn=row.TextCn,Tw=row.TextTw,En=row.TextEn};
   if(row!=null)names[id]=value;
   return value;
  }
  static PracticeTalentName[] TalentNames(){
   if(talents!=null)return talents;
   var rows=Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ.GetTableList<TalentTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON,"TalentTable");
   return talents=rows.Where(t=>t.ClassType==16||fieldKinds.Contains(t.ClassType)).OrderBy(t=>t.Id).GroupBy(t=>t.ClassType)
    .Select(g=>{var titles=g.Select(t=>Name(t.TalentNameTextId)).ToArray();return new PracticeTalentName{Kind=g.Key,Names=new PracticeDisplayName{
     Cn=string.Join(" / ",titles.Select(n=>n.Cn).Where(s=>!string.IsNullOrEmpty(s)).Distinct().ToArray()),
     Tw=string.Join(" / ",titles.Select(n=>n.Tw).Where(s=>!string.IsNullOrEmpty(s)).Distinct().ToArray()),
     En=string.Join(" / ",titles.Select(n=>n.En).Where(s=>!string.IsNullOrEmpty(s)).Distinct().ToArray())}};}).ToArray();
  }
  internal static object Capture(bool fieldPractice=false){
   var inventory=(System.Collections.Generic.Dictionary<long,Proto.Net.CharDBInfo>)typeof(ὣὡὧὡὦὣὣὬὨὪὫ).GetField("ὩὬὮὭὦὮὢὥὣὦὪ",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
   var rows=inventory.Values.Where(ch=>!ch.Temporary).Select(ch=>{
    var character=ὢὭὤὣὠὦὥὠὢὣὩ.ὣὢὨὠὦὥὯὢὥὠὪ(ch.Id);
    if(character==null||character.TalentId<=0)return null;
    var talent=ὯὤὮὦὮὬὨὢὩὪὦ.ὮὠὯὦὥὤὣὯὣὥὭ(character.TalentId);
    if(talent==null||!(talent.ClassType==16||fieldPractice&&fieldKinds.Contains(talent.ClassType)))return null;
    var skill=ὯὤὮὦὮὬὨὢὩὪὦ.ὩὫὩὤὤὤὢὣὥὤὨ(talent.TalentSkillGroupId,ch.TalentLevel);
    if(skill==null||skill.ClassType!=talent.ClassType)throw new InvalidOperationException("Training skill definition unavailable");
    var growth=ὯὤὮὦὮὬὨὢὩὪὦ.ὤὡὠὬὩὣὯὨὪὥὧ(character.TalentId,ch.TalentLevel);
    var all=ὯὤὮὦὮὬὨὢὩὪὦ.ὭὢὩὯὨὢὭὪὭὪὥ(character.TalentId);
    long previous=all.Where(g=>g.Id<ch.TalentLevel).Sum(g=>(long)g.NeedExp);
    if(ch.TalentLevel<talent.MaxLevel&&(growth==null||growth.NeedExp<=0))throw new InvalidOperationException("Bargain growth definition unavailable");
    var manager=TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ;
    bool active=manager!=null&&(skill.ClassType==6||skill.ClassType==12?manager.IsTalentSkillReseaching((ὪὯὯὢὨὮὨὠὥὤὬ)skill.ClassType):skill.ClassType==2||skill.ClassType==13||skill.ClassType==17?manager.IsTalentSkillDurationing((ὪὯὯὢὨὮὨὠὥὤὬ)skill.ClassType):false);
    return new BargainCharacterState{Character=ch.InvenIndex,Id=ch.Id,Kind=skill.ClassType,Active=active,Name=ὪὥὦὥὭὠὦὡὠὧὤ.ὥὠὬὢὭὬὣὠὦὣὥ(character.CharNameTextId),Names=Name(character.CharNameTextId),Level=ch.TalentLevel,MaxLevel=talent.MaxLevel,Group=skill.GroupId,Skill=skill.Id,Experience=ch.TalentExp,Previous=previous,Target=checked(previous+(growth==null?0:growth.NeedExp)),PerUse=skill.GetExp,Cost=skill.CatalystValue};
   }).Where(x=>x!=null).ToArray();
   return new BargainTrainingState{Characters=rows,Talents=fieldPractice?TalentNames():null,Catalyst=ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Catalyst),Gold=ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Gold)};
  }
 }
}
