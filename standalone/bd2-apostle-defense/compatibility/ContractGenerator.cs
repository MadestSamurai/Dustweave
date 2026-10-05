using Mono.Cecil;
namespace BD2ApostleDefense.Compatibility;
public static class ContractGenerator
{
 public static BindingContract Generate(MetadataIndex index,string unused)
 {
  var selected=new HashSet<IMemberDefinition>();var types=new HashSet<TypeDefinition>();var apis=new List<ApiContract>();var roles=new Dictionary<string,string>();
  void Role(string role,string name){roles.Add(role,name);types.Add(index.Find(name));}
  void Api(string role,string type,string name,int arity=-1)
  {
   var t=index.Find(type);IMemberDefinition[] matches;
   do{matches=MetadataIndex.Members(t).Where(m=>m.Name==name&&(arity<0||m is MethodDefinition f&&f.Parameters.Count==arity)).ToArray();if(matches.Length>0)break;t=t.BaseType?.Resolve();}while(t!=null);
   if(matches.Length!=1)throw new InvalidOperationException(role+": ambiguous member "+type+"."+name);
   var member=matches[0];selected.Add(member);types.Add(member.DeclaringType);apis.Add(new(role,member.DeclaringType.FullName,name,arity,MetadataIndex.Signature(member)));
  }
  Role("NetworkKind","ὦὢὫὯὬὫὦὤὯὡὢ");
  Api("Net.Defense","ὧὥὢὯὯὣὩὧὡὠὨ","ὡὤὣὩὩὨὢὬὡὯὧ");
  Api("Net.Mode","BDNetwork.NetworkTCPManager","ὫὢὬὠὪὢὫὡὥὤὭ");
  Api("Net.Client","BDNetwork.NetworkTCPManager","ὯὮὦὪὫὤὦὦὦὮὠ");
  Api("Net.Connected","BDNetwork.NetworkTCPManager","IsConnect",1);
  Api("Net.Lost","BDNetwork.NetworkTCPManager","OnNetworkLost",0);
  Api("Net.Restored","BDNetwork.NetworkTCPManager","OnNetworkRestored",0);
  Api("Net.Owner","ὤὦὮὢὣὫὭὦὤὡὦ","ὬὭὭὯὦὣὫὫὨὮὥ");
  Api("Net.Receive","ὤὦὮὢὣὫὭὦὤὡὦ","ὢὯὩὯὣὫὠὡὦὢὣ",1);
  Api("Net.Message","ὡὡὯὫὤὢὫὫὭὣὠ","ὯὯὮὢὭὣὫὭὮὧὡ");
  Api("Net.Monitor","NetworkConnectivityMonitor","ὨὯὣὮὬὠὭὧὥὥὬ");
  Api("Net.Timeout","NetworkConnectivityMonitor","connectionTimeout");
  Api("Net.LostCallback","NetworkConnectivityMonitor","OnNetworkLost");
  Role("PlayerKind","ὠὪὭὥὦὪὩὥὣὧὨ");
  Role("ElementKind","ὥὣὮὨὦὨὬὭὤὨὮ");
  Role("Raw","RawDataManager");
  Role("EventPopup","EventPopupUI");
  Api("Manager.Instance","MiniGameDefenseManager","ὪὨὦὬὡὬὠὧὥὪὭ");
  Api("Manager.Currency","MiniGameDefenseManager","ὬὠὥὬὨὮὭὮὫὫὡ");
  Api("Manager.View","MiniGameDefenseManager","ὩὫὪὠὧὭὨὠὬὫὥ");
  Api("Manager.Playing","MiniGameDefenseManager","ὯὬὡὢὬὫὫὥὣὡὥ");
  Api("Manager.Win","MiniGameDefenseManager","ὤὠὢὪὥὩὦὬὯὯὭ");
  Api("Manager.Dead","MiniGameDefenseManager","ὮὧὭὯὤὩὪὪὠὧὧ");
  Api("Manager.Exit","MiniGameDefenseManager","ὢὯὧὬὢὠὥὧὪὯὧ");
  Api("Manager.RoomState","MiniGameDefenseManager","ὬὭὤὫὨὦὫὣὠὦὨ");
  Api("Manager.Levels","MiniGameDefenseManager","ὩὩὥὮὪὠὪὠὤὥὯ");
  Api("Manager.Units","MiniGameDefenseManager","ὦὢὠὤὨὤὡὢὯὭὫ");
  Api("Manager.Enemies","MiniGameDefenseManager","ὠὭὢὠὭὬὥὫὫὤὣ");
  Api("Manager.Camera","MiniGameDefenseManager","ὤὡὯὩὭὭὧὧὮὬὨ");
  Api("Manager.Wave","MiniGameDefenseManager","ὫὦὥὫὯὬὣὯὠὥὪ");
  Api("Manager.Count","MiniGameDefenseManager","ὫὮὯὯὦὢὦὠὢὦὪ");
  Api("Manager.Deadline","MiniGameDefenseManager","ὤὭὢὬὪὣὧὬὦὨὫ");
  Api("Manager.Selected","MiniGameDefenseManager","ὯὧὡὬὧὤὥὭὡὥὨ");
  Api("Manager.Pointer","MiniGameDefenseManager","ὤὤὢὮὡὬὪὧὫὯὮ");
  Api("Manager.ClearSelection","MiniGameDefenseManager","ὪὩὢὮὫὯὢὬὫὠὠ");
  Api("Field.Instance","GameFieldManager","ὪὨὦὬὡὬὠὧὥὪὭ");
  Api("Field.Boards","GameFieldManager","ὨὤὪὥὠὡὭὩὬὬὮ");
  Api("Field.Path","GameFieldManager","ὯὦὣὨὥὢὥὨὤὬὢ");
  Api("Field.Id","MiniGameDefenseFieldBase","ὪὢὡὬὮὠὯὢὠὣὩ");
  Api("Board.Element","MiniGameDefenseBoard","ὪὢὦὬὪὬὯὮὩὢὭ");
  Api("Board.Unit","MiniGameDefenseBoard","ὪὠὪὦὯὦὦὢὡὥὮ");
  Api("Board.Occupied","MiniGameDefenseBoard","ὯὧὡὬὣὬὭὮὪὫὠ");
  Api("Unit.Table","MiniGameDefenseUnit","ὯὠὢὩὣὦὥὩὬὥὪ");
  Api("Unit.Grid","MiniGameDefenseUnit","ὣὩὧὧὪὪὫὭὫὤὬ");
  Api("Unit.Target","MiniGameDefenseUnit","ὮὧὨὫὠὤὦὠὫὣὦ");
  Api("Unit.AttackElapsed","MiniGameDefenseUnit","ὪὫὬὩὯὩὬὡὮὦὭ");
  Api("Unit.AttackState","MiniGameDefenseUnit","_currentState");
  Api("Enemy.NextPoint","MiniGameDefenseEnemy","ὨὥὯὠὫὣὣὥὬὮὣ");
  Api("Unit.Ready","MiniGameDefenseUnit","ὥὭὭὩὡὮὦὯὥὤὮ");
  Api("Enemy.Table","MiniGameDefenseEnemy","ὭὥὧὣὦὦὡὣὫὣὨ");
  Api("Enemy.Hp","MiniGameDefenseEnemy","ὦὬὢὢὮὩὨὨὠὠὯ");
  Api("Enemy.Dead","MiniGameDefenseEnemy","ὤὧὩὠὤὠὨὪὯὭὭ");
  Api("Services.Network","ὧὥὢὯὯὣὩὧὡὠὨ","ὪὬὡὢὮὣὣὩὣὩὩ");
  Api("Services.Clock","ὧὥὢὯὯὣὩὧὡὠὨ","ὤὬὯὣὡὭὪὤὥὧὧ");
  Api("Network.Room","DefenseNetworkManager","ὡὪὥὮὭὨὣὨὮὬὦ");
  Api("Network.Rare","DefenseNetworkManager","ὣὪὨὥὭὦὥὣὤὡὯ");
  Api("Account.User","ὨὬὣὫὩὯὩὩὣὠὧ","ὫὩὢὤὬὭὢὣὭὮὣ");
  Api("Text.Local","ὨὣὡὪὨὬὡὭὭὬὬ","ὡὪὦὮὣὫὯὡὬὦὬ");
  Api("Achievement.Refresh","ὪὥὮὮὯὯὫὪὩὫὣ","ὡὨὫὥὩὡὬὧὤὨὪ");
  Api("Achievement.Read","ὪὥὮὮὯὯὫὪὩὫὣ","ὧὦὥὮὨὮὭὡὫὧὫ");
  Api("Ui.Popup","UIBase","ὡὡὡὯὬὨὢὧὦὦὫ");
  Api("Ui.CanClose","UIBase","CanCloseUI");
  Api("Ui.Back","UIBase","OnClickBackButton");
  Api("Ui.IsHud","ὨὧὠὯὪὦὩὣὤὢὡ","ὩὠὮὥὫὧὢὣὯὯὫ");
  Api("Hud.Click","DefenseHUD","OnClickUI");
  Api("DefenseHUD._goRecallButton","DefenseHUD","_goRecallButton");
  Api("DefenseHUD._goEnableRecall","DefenseHUD","_goEnableRecall");
  Api("DefenseHUD._goSellButton","DefenseHUD","_goSellButton");
  Api("DefenseHUD._goUnitCloseButton","DefenseHUD","_goUnitCloseButton");
  Api("DefenseHUD._elementButtons","DefenseHUD","_elementButtons");
  Api("DefenseHUD._goExitButton","DefenseHUD","_goExitButton");
  Api("DefenseHUD/ElementButton._elementType","DefenseHUD/ElementButton","_elementType");
  Api("DefenseHUD/ElementButton._goUpgradeButton","DefenseHUD/ElementButton","_goUpgradeButton");
  Api("DefenseHUD/ElementButton._goUpgradeEnable","DefenseHUD/ElementButton","_goUpgradeEnable");
  Api("DefenseHUD/ElementButton._goUpgradeMax","DefenseHUD/ElementButton","_goUpgradeMax");
  Api("DefenseMainUI._goQuickStartButton","DefenseMainUI","_goQuickStartButton");
  Api("DefenseResultPopupUI._goExitButton","DefenseResultPopupUI","_goExitButton");
  Api("DefenseMatchingFailPopupUI._goOKButton","DefenseMatchingFailPopupUI","_goOKButton");
  Api("DefenseGiveUpConfirmPopupUI._goOkButton","DefenseGiveUpConfirmPopupUI","_goOkButton");
  Api("Matching.StartButton","DefenseQuickMatchingPopupUI","_goGameStartButton");
  Api("Matching.Ready","DefenseQuickMatchingPopupUI","ὡὣὡὯὨὨὢὬὫὣὥ");
  Api("Matching.Leaving","DefenseQuickMatchingPopupUI","ὬὫὪὤὪὥὪὨὡὥὣ");
  Api("Hud.Notice","DefenseHUD","_noticeUI");
  Api("Ui.RegisteredName","UIBase","ὧὨὦὯὣὣὡὣὪὮὨ");
  return new(1,types.OrderBy(t=>t.FullName,StringComparer.Ordinal).Select(t=>new TypeContract(t.FullName,index.Shape(t),t.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Count>=10).OrderByDescending(m=>m.Body.Instructions.Count).Take(8).Select(index.Body).ToArray(),selected.Where(m=>m.DeclaringType==t).OrderBy(m=>m.FullName,StringComparer.Ordinal).Select(m=>new MemberContract(m.Name,MetadataIndex.Signature(m),index.MemberBody(m),index.Uses(m))).ToArray())).ToArray(),apis.ToArray(),roles);
 }
}
