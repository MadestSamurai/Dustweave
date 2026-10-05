using System;
using System.Linq;
using System.Runtime.Serialization;
namespace BD2Daily.Live {
 [DataContract] internal sealed class BargainCharacterState {
  [DataMember] public long Character,Experience,Previous,Target;
  [DataMember] public int Id,Level,MaxLevel,Group,Skill,PerUse,Cost;
  [DataMember] public string Name="";
 }
 [DataContract] internal sealed class BargainTrainingState {
  [DataMember] public long Catalyst,Gold;
  [DataMember] public BargainCharacterState[] Characters;
 }
 // Read-only account data. The cap follows CharTalentUI's cumulative experience formula.
 internal static class BargainTrainingNative {
  internal static object Capture(){
   var inventory=(System.Collections.Generic.Dictionary<long,Proto.Net.CharDBInfo>)typeof(ὣὡὧὡὦὣὣὬὨὪὫ).GetField("ὩὬὮὭὦὮὢὥὣὦὪ",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
   var rows=inventory.Values.Where(ch=>!ch.Temporary).Select(ch=>{
    var character=ὢὭὤὣὠὦὥὠὢὣὩ.ὣὢὨὠὦὥὯὢὥὠὪ(ch.Id);
    if(character==null||character.TalentId<=0)return null;
    var talent=ὯὤὮὦὮὬὨὢὩὪὦ.ὮὠὯὦὥὤὣὯὣὥὭ(character.TalentId);
    if(talent==null||talent.ClassType!=16)return null;
    var skill=ὯὤὮὦὮὬὨὢὩὪὦ.ὩὫὩὤὤὤὢὣὥὤὨ(talent.TalentSkillGroupId,ch.TalentLevel);
    if(skill==null||skill.ClassType!=16)throw new InvalidOperationException("Bargain skill definition unavailable");
    var growth=ὯὤὮὦὮὬὨὢὩὪὦ.ὤὡὠὬὩὣὯὨὪὥὧ(character.TalentId,ch.TalentLevel);
    var all=ὯὤὮὦὮὬὨὢὩὪὦ.ὭὢὩὯὨὢὭὪὭὪὥ(character.TalentId);
    long previous=all.Where(g=>g.Id<ch.TalentLevel).Sum(g=>(long)g.NeedExp);
    if(ch.TalentLevel<talent.MaxLevel&&(growth==null||growth.NeedExp<=0))throw new InvalidOperationException("Bargain growth definition unavailable");
    return new BargainCharacterState{Character=ch.InvenIndex,Id=ch.Id,Name=ὪὥὦὥὭὠὦὡὠὧὤ.ὥὠὬὢὭὬὣὠὦὣὥ(character.CharNameTextId),Level=ch.TalentLevel,MaxLevel=talent.MaxLevel,Group=skill.GroupId,Skill=skill.Id,Experience=ch.TalentExp,Previous=previous,Target=checked(previous+(growth==null?0:growth.NeedExp)),PerUse=skill.GetExp,Cost=skill.CatalystValue};
   }).Where(x=>x!=null).ToArray();
   return new BargainTrainingState{Characters=rows,Catalyst=ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Catalyst),Gold=ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Gold)};
  }
 }
}
