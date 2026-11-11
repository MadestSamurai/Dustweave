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
  [DataMember] public bool? NetworkBusy;
  [DataMember] public BargainCharacterState[] Characters;
  [DataMember] public PracticeTalentName[] Talents;
  [DataMember] public PracticeAchievementState[] Achievements;
 }
 [DataContract] internal sealed class PracticeAchievementState {
  [DataMember] public int Kind,Group,ContentsGroup;
  [DataMember] public long Target,Current;
  [DataMember] public bool Known;
 }
 // Read-only account data. The cap follows CharTalentUI's cumulative experience formula.
 internal static class BargainTrainingNative {
  static readonly BD2.LocalIpc.NativeNetworkProbe networkProbe=new BD2.LocalIpc.NativeNetworkProbe();
  // Read the game's real queue, not our possibly orphaned response-listener
  // counter. Optional evidence: unreadable metadata is unknown, never idle.
  static bool? NetworkBusy(){
   try{
    object network=ὫὨὪὠὢὨὮὤὩὤὮ.ὩὮὠὠὩὭὭὠὥὬὪ;
    bool idle;
    return networkProbe.TryIdle(network,out idle)?(bool?)!idle:null;
   }catch{return null;}
  }
  static readonly int[] fieldKinds={2,6,12,13,17};
  // Only immutable game definitions are cached. Never cache XP, ownership or effects.
  static readonly Dictionary<int,PracticeDisplayName> names=new Dictionary<int,PracticeDisplayName>();
  static PracticeTalentName[] talents;
  static AchievementTable[] achievementDefinitions;
  static System.Reflection.FieldInfo achievementIndex;
  static bool achievementIndexResolved;
  static PracticeAchievementState[] Achievements(){
   // Login loads this protocol dictionary. Resolve its structural value type,
   // not an obfuscated owner name; account progress is never cached here.
   try{
    if(!achievementIndexResolved){
     var matches=typeof(Proto.Net.AchievementDBInfo).Assembly.GetTypes()
      .Where(t=>t.IsAbstract&&t.IsSealed)
      .SelectMany(t=>t.GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
      .Where(f=>f.FieldType.IsGenericType&&f.FieldType.GetGenericTypeDefinition()==typeof(Dictionary<,>)
       &&f.FieldType.GetGenericArguments()[1]==typeof(Proto.Net.AchievementDBInfo)).ToArray();
     achievementIndexResolved=true;if(matches.Length==1)achievementIndex=matches[0];
    }
    var index=achievementIndex==null?null:achievementIndex.GetValue(null) as System.Collections.IDictionary;
    bool loaded=index!=null&&index.Count>0;
    var current=loaded?index.Values.Cast<Proto.Net.AchievementDBInfo>().ToArray():new Proto.Net.AchievementDBInfo[0];
    if(achievementDefinitions==null)achievementDefinitions=Singleton<RawDataManager>.ὪὫὢὨὯὭὦὪὦὨὣ.GetTableList<AchievementTable>(ὯὭὣὩὩὦὬὢὯὩὪ.DB_COMMON,"AchievementTable")
     .Where(r=>r.ConditionType==(int)Proto.Net.Define_AchievementConditionType.AchieveTalentSkillUse&&r.UseType==0&&r.EventId==0
      &&(r.ConditionSubType==16||fieldKinds.Contains(r.ConditionSubType))&&r.ConditionValue>0).ToArray();
    return achievementDefinitions.GroupBy(r=>new{r.ConditionSubType,r.GroupId,r.ContentsGroup})
     .Select(g=>{var goal=g.OrderByDescending(r=>r.ConditionValue).First();
      var live=current.SingleOrDefault(r=>r.GroupId==goal.GroupId&&r.ContentsGroup==goal.ContentsGroup);
      long cleared=live==null?0:(long)g.Where(r=>r.Id<=live.MaxClearId).Select(r=>r.ConditionValue).DefaultIfEmpty(0).Max();
      return new PracticeAchievementState{Kind=goal.ConditionSubType,Group=goal.GroupId,ContentsGroup=goal.ContentsGroup,
       Target=checked((long)Math.Ceiling(goal.ConditionValue)),Current=Math.Max(cleared,live==null?0:live.Value),Known=loaded};}).ToArray();
   }catch{return null;} // Optional achievement evidence never disables XP training.
  }
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
   return new BargainTrainingState{Characters=rows,Achievements=Achievements(),Talents=fieldPractice?TalentNames():null,NetworkBusy=fieldPractice?NetworkBusy():null,Catalyst=ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Catalyst),Gold=ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Gold)};
  }
 }
}
