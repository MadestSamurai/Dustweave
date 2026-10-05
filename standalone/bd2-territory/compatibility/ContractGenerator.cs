using Mono.Cecil;
namespace BD2Territory.Compatibility;
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
  const string tables="ὪὠὩὭὫὥὪὬὡὥὭ", data="ὫὬὩὫὯὨὬὩὧὠὭ";
  Role("Inventory",data);Role("ToolKind","ὤὤὭὬὯὦὢὣὨὯὮ");Role("FunctionKind","ὫὫὨὥὩὯὦὣὥὭὢ");
  Api("Tables.CropIds",tables,"ὫὥὦὯὪὭὭὦὨὠὡ",0);Api("Tables.Crop",tables,"ὫὫὮὩὭὫὥὦὯὪὩ",1);Api("Tables.Item",tables,"ὫὬὡὩὭὡὩὣὪὩὣ",1);
  Api("Tables.Tool",tables,"ὯὫὠὨὩὫὬὢὯὭὩ",2);Api("Inventory.Tool",data,"ὧὭὨὣὮὩὠὠὥὩὪ",1);Api("Inventory.User",data,"ὫὩὢὤὬὭὢὣὭὮὣ");
  Api("Inventory.Items",data,"ὤὭὧὮὯὦὨὨὩὮὮ");Api("Inventory.Materials",data,"ὯὨὢὡὯὢὭὤὩὬὥ");
  Api("Text.Name","ὨὣὡὪὨὬὡὭὭὬὬ","ὥὢὮὣὮὠὦὧὫὡὥ",1);
  Api("Field.Instance","GameFieldManager","ὪὨὦὬὡὬὠὧὥὪὭ");Api("Field.Player","GameFieldManager","ὬὪὧὦὭὬὪὧὣὣὫ");Api("Field.MoveState","GameFieldManager","ὨὣὠὭὭὤὣὬὢὦὬ");
  Role("MoveKind","MoveController/ὯὥὮὢὯὨὯὧὬὩὯ");
  Api("Player.ChangeMoveType","PlayerMoveController","ChangeMoveType",1);Api("Player.MoveType","MoveController","ὠὤὣὫὮὢὬὨὫὢὡ");
  Api("Player.Direction","MoveController","ὫὥὯὪὡὤὭὮὬὦὢ");Api("Player.AvatarKind","PlayerController","ὯὨὤὬὣὯὩὯὠὧὡ");
  Api("Player.Face","PlayerController","SetRotationForce",1);Api("Player.AvatarRoot","PlayerMoveController","_avatarRootTr");
  Api("Player.Move","PlayerController","ὥὠὬὪὨὯὭὬὡὡὣ");
  Api("Gather.Function","LifeGatheringObject","ὮὯὤὢὣὪὤὩὥὠὪ");Api("Gather.Hp","LifeGatheringObject","ὥὭὫὥὧὤὧὣὥὬὠ");Api("Gather.Parent","LifeGatheringObject","ὮὣὤὯὬὬὣὫὣὥὣ");
  Api("Gather.Id","LifeObjectBase","ὢὯὧὤὮὭὮὣὭὪὣ");Api("Gather.Index","LifeObjectBase","ὪὢὡὬὮὠὯὢὠὣὩ");
  Api("Parent.Chunk","ὯὥὨὡὠὫὬὤὥὢὠ","ὪὮὭὮὢὣὨὠὫὤὥ");Api("Parent.Id","ὯὥὨὡὠὫὬὤὥὢὠ","ὢὯὧὤὮὭὮὣὭὪὣ");Api("Parent.X","ὯὥὨὡὠὫὬὤὥὢὠ","ὨὮὦὬὩὢὫὨὥὣὬ");Api("Parent.Y","ὯὥὨὡὠὫὬὤὥὢὠ","ὪὢὣὪὠὨὧὤὭὫὢ");
  Api("Tool.Busy","FieldEvent.Life.LifePlayerToolEquipmentController","ὪὦὠὣὡὭὨὮὪὧὤ");
  Api("Tool.LoadingBase","FieldEvent.Life.LifePlayerToolEquipmentController","ὭὬὠὮὦὡὥὫὦὧὭ");Api("Tool.LoadingParts","FieldEvent.Life.LifePlayerToolEquipmentController","ὩὧὨὫὥὤὨὧὥὩὫ");
  Api("Farm.Open","LifeFarmFieldObject","ὡὮὭὧὨὯὢὢὥὭὤ",0);Api("Farm.Occupied","LifeFarmFieldObject","ὫὧὡὨὢὫὥὡὫὡὦ");Api("Farm.Preview","LifeFarmFieldObject","ὠὯὠὫὪὤὯὩὭὡὨ");Api("Farm.Seed","LifeFarmFieldObject","ὡὮὣὧὧὩὫὩὬὧὠ");
  Api("Farm.Db","FieldEvent.Life.Chunk.LifePlaceableObject","ὮὤὠὪὨὪὠὢὬὭὡ");Api("Farm.Chunk","FieldEvent.Life.Chunk.LifePlaceableObject","_placedChunkId");
  Role("FarmGroup","ὧὮὪὯὭὬὧὠὭὣὡ");Api("FarmGroup.Preview","ὧὮὪὯὭὬὧὠὭὣὡ","ὨὩὤὭὥὦὣὠὧὯὠ",0);
  Api("Panel.Multi","LifeFarmingUIPanel","ὥὯὧὮὣὣὤὧὡὮὪ");Api("Panel.CanAfford","LifeFarmingUIPanel","ὪὤὮὪὫὭὢὣὦὧὢ");
  Api("Ui.IsHud","ὨὧὠὯὪὦὩὣὤὢὡ","ὩὠὮὥὫὧὢὣὯὯὫ",1);Api("Ui.Visible","UIBase","ὡὡὡὯὬὨὢὧὦὦὫ");
  foreach(var field in new[]{"_cropButtonObj","_completeButtonObj","_multiCropSeedButtonObj"})Api("Panel."+field,"LifeFarmingUIPanel",field);
  Api("Popup.Ok","AvatarLifeFarmingCroplistPopupUI","_objOkButton");
  Api("Cooking.SelectItems",data,"ὦὯὬὦὯὢὯὡὥὪὭ",2);Api("Cooking.Make",data,"ὨὧὠὢὯὣὤὤὧὮὪ",4);
  Api("Tables.Default",tables,"ὠὫὤὥὪὣὯὨὠὠὭ");
  Api("Tables.ItemById",tables,"ὥὮὧὦὠὩὤὦὢὯὬ",1);
  Api("Tables.CookIds",tables,"ὠὯὣὦὫὤὬὤὥὢὫ",0);
  Api("Tables.Cook",tables,"ὢὣὮὨὥὠὬὥὪὩὠ",1);Api("Tables.ItemIds",tables,"ὩὧὩὭὧὥὮὦὭὣὩ",1);Api("Tables.Harvest",tables,"ὠὤὬὯὨὮὢὡὧὤὮ",1);
  Api("Inventory.Count",data,"ὨὦὤὭὬὯὣὩὧὡὩ",1);Api("Inventory.World",data,"ὬὨὨὥὮὫὬὬὦὬὣ");Api("Network.QueuedHarvest",data,"ὢὢὩὤὨὢὢὧὭὯὬ");
  Api("Account.User","ὨὬὣὫὩὯὩὩὣὠὧ","ὫὩὢὤὬὭὢὣὭὮὣ");
  Api("Popup.Seeds","AvatarLifeFarmingCroplistPopupUI","ὩὦὮὧὠὥὥὯὠὮὬ");
  Api("Button.On","ButtonOnOffComponent","_objOn");Api("Button.Off","ButtonOnOffComponent","_objOff");
  Api("Tool.Event","FieldEvent.Life.LifePlayerToolEquipmentController","ὧὭὤὡὥὠὫὬὠὬὯ",1);
  Api("Detector.Distance","CircleSectorCollider","_distance");
  Api("Ui.Back","UIBase","OnClickBackButton",0);
  Api("Gather.Near","gamfs.Life.LifeManager","GetNearestDetectedGatheringObject",2);
  Api("Gather.Detected","gamfs.Life.LifeManager","ὠὪὫὮὧὩὨὪὡὢὪ");
  Api("Field.Context","GameFieldManager","ὢὢὢὣὡὣὠὫὫὧὦ");
  Api("Tables.Building",tables,"ὯὯὡὭὮὯὢὡὭὪὣ",1);
  Api("Gather.FinalStage","LifeGatheringObject","ὥὣὥὦὯὫὨὣὬὣὭ");Api("Gather.CanGather","LifeGatheringObject","CanGatheringState",0);
  Api("Farm.Crop","LifeFarmFieldObject","ὠὣὪὪὫὨὭὯὪὭὣ");
  Api("Player.NavAgent","MoveController","ὦὣὠὨὣὨὡὯὪὢὦ");
  Api("Player.StartMove","PlayerController","SetMoveStart",0);Api("Player.SetMoveNav","PlayerMoveController","SetMoveNav",3);
  Api("Quest.Pause","QuestNavigationManager","PauseNav",0);
  foreach(var pair in new[]{("Dash.Can","CanDashAction"),("Dash.Play","PlayDashAction"),("Dash.Stop","StopDashAction"),("Dash.Active","IsInDashAction"),("Dash.FieldAction","IsInActionWithFieldItem")})Api(pair.Item1,"FieldActionManager",pair.Item2,0);
  Api("Dash.Data","FieldActionManager","ὠὢὮὨὩὭὭὧὫὭὯ");Api("Dash.Ready","ὡὩὢὭὯὪὤὯὡὥὥ","ὩὡὣὭὪὪὫὧὥὡὤ",0);
  Api("Vehicle.Can","PlayerController","IsCanGetOnVehicle",0);Api("Vehicle.On","PlayerController","GetOnVehicle",1);Api("Vehicle.Off","PlayerController","GetOffVehicle",1);Api("Vehicle.Active","PlayerController","IsGetOnVehicle",0);
  Api("Tables.Objects",tables,"ὪὯὡὣὧὥὯὣὡὮὡ",0);Api("Tables.Object",tables,"ὠὣὤὬὨὣὦὭὤὥὡ",1);Api("Tables.Deco",tables,"ὭὥὣὡὬὣὣὮὩὪὦ",1);
  Api("Layout.Unlocked",data,"ὮὤὬὮὢὣὫὦὣὠὨ",2);
  Api("Layout.CanBuild","ὪὠὠὧὨὭὮὣὤὠὥ","ὩὤὬὬὬὩὫὬὡὦὩ",1);
  Api("Layout.LayerLimit","ὪὠὠὧὨὭὮὣὤὠὥ","ὫὪὡὩὠὢὯὧὦὥὯ",1);
  Role("CurrencyKind","ὪὡὢὦὢὡὯὩὫὤὥ");Api("Inventory.Currency","ὨὬὣὫὩὯὩὩὣὠὧ","ὩὧὮὦὧὨὯὥὭὪὨ",1);
  Api("Layout.HousingButton","AvatarLifeGameFieldDefaultUI","_buttonHousing");
  Api("Layout.Edit","AvatarLifeHousingEditUI","_editMenuUI");
  Api("Layout.Select","AvatarLifeHousingEditUI","ὬὪὧὫὡὪὨὮὫὩὧ",1);
  Api("Layout.Selected","AvatarLifeHousingEditUI_EditUI","ὩὪὬὯὡὮὥὣὠὡὩ");
  Api("Layout.Controller","AvatarLifeHousingEditUI_EditUI","ὢὫὡὯὣὦὢὡὯὢὪ");
  Api("Layout.Temporary","FieldEvent.Life.Chunk.LifePlaceableObject","ὮὬὭὠὪὡὠὧὡὭὩ");
  Api("Layout.Confirm","FieldEvent.Life.LifeChunkController","ὡὢὨὬὨὦὢὧὬὤὥ",6);
  return new(1,types.OrderBy(t=>t.FullName,StringComparer.Ordinal).Select(t=>new TypeContract(t.FullName,index.Shape(t),t.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Count>=10).OrderByDescending(m=>m.Body.Instructions.Count).Take(8).Select(index.Body).ToArray(),selected.Where(m=>m.DeclaringType==t).OrderBy(m=>m.FullName,StringComparer.Ordinal).Select(m=>new MemberContract(m.Name,MetadataIndex.Signature(m),index.MemberBody(m),index.Uses(m))).ToArray())).ToArray(),apis.ToArray(),roles);
 }
}
