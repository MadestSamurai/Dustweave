using Mono.Cecil;
namespace BD2Sichuan.Compatibility;

// Maintainer bootstrap only. Released EXEs resolve the embedded contract against local metadata.
public static class ContractGenerator
{
    public static BindingContract Generate(MetadataIndex index,string hookSource)
    {
        var selected=new HashSet<IMemberDefinition>();var types=new HashSet<TypeDefinition>();
        var roles=new Dictionary<string,string>();var apis=new List<ApiContract>();
        IMemberDefinition Select(string type,string name,int arity=-1)
        {
            for(var t=index.Find(type);t!=null;t=t.BaseType?.Resolve())
            {
                var matches=MetadataIndex.Members(t).Where(m=>m.Name==name &&
                    (arity<0 || m is MethodDefinition method && method.Parameters.Count==arity)).ToArray();
                if(matches.Length==0)continue;
                if(matches.Length!=1)throw new InvalidOperationException(type+"."+name+": "+matches.Length+" matches");
                selected.Add(matches[0]);types.Add(matches[0].DeclaringType);return matches[0];
            }
            throw new MissingMemberException(type,name);
        }
        IMemberDefinition Api(string role,string type,string name,int arity=-1)
        {
            var m=Select(type,name,arity);
            apis.Add(new(role,m.DeclaringType.FullName,name,arity,MetadataIndex.Signature(m)));return m;
        }
        TypeReference Return(IMemberDefinition m)=>m switch{
            PropertyDefinition p=>p.PropertyType,FieldDefinition f=>f.FieldType,MethodDefinition method=>method.ReturnType,
            _=>throw new ArgumentException("Unsupported member")};
        void Role(string role,string type){roles[role]=type;types.Add(index.Find(type));}
        const string ui="SichuanBoardUI",view="SichuanBlockItem",model="ὣὣὯὪὮὯὩὫὨὯὮ";
        Role("Model",model);Role("NetworkHelper","ὢὭὪὩὫὢὩὩὯὨὪ");
        Api("UI.Active",ui,"TryGetActiveBoardUI",1);Api("UI.Frame",ui,"ὥὯὮὢὡὯὠὢὮὢὯ",0);
        foreach(var pair in new[]{
            ("Model","ὫὭὨὯὯὠὥὨὦὥὣ"),("Views","ὣὭὬὬὢὮὬὮὫὦὫ"),
            ("NormalAnimations","ὦὧὭὢὪὩὨὡὮὯὣ"),("ComboAnimations","ὣὬὨὬὤὡὭὯὠὧὦ"),
            ("Paused","ὡὪὨὧὬὩὤὤὨὥὪ"),("Locked","ὭὢὤὦὪὫὦὣὤὢὪ"),
            ("LevelGroup","ὤὤὣὡὢὮὠὢὭὯὣ"),("Level","ὧὭὣὡὦὧὠὭὬὮὭ")})
            Api("UI."+pair.Item1,ui,pair.Item2);
        Api("Model.Create",model,"ὨὨὫὥὥὦὭὬὠὤὡ");
        foreach(var pair in new[]{
            ("Width","ὮὡὨὣὪὢὩὢὡὬὫ"),("Height","ὮὠὣὬὦὡὪὩὪὪὮ"),
            ("Cells","ὠὬὣὯὪὫὡὬὡὯὧ"),("Playing","ὦὦὦὮὪὡὠὩὧὮὬ"),
            ("Shuffling","ὯὡὠὯὫὥὧὨὯὡὥ"),("ComboSeconds","ὣὮὠὤὨὯὦὦὣὫὠ"),
            ("TimerBonus","ὢὤὤὨὯὡὧὠὭὤὧ")})Api("Model."+pair.Item1,model,pair.Item2);
        var cells=Return(Select(model,"ὠὬὣὯὪὫὡὬὡὯὧ"));
        var block=cells is GenericInstanceType list?list.GenericArguments.Single():((ArrayType)cells).ElementType;
        Api("Block.Kind",block.Resolve().FullName,"ὭὯὧὩὪὯὤὯὦὤὧ");
        foreach(var pair in new[]{("RemainingTimer","ὤὬὯὣὡὭὪὤὥὧὧ","Remaining"),("ElapsedTimer","ὡὡὯὦὭὪὢὯὫὡὥ","Elapsed")})
        {
            var timer=Return(Api("Model."+pair.Item1,model,pair.Item2));
            Api("Timer."+pair.Item3,timer.Resolve().FullName,"ὠὧὡὢὭὮὫὪὧὭὠ",0);
        }
        Api("Model.ComboCount",model,"ὩὥὭὬὥὣὤὠὫὯὪ",0);
        var pairEvent=index.Find(model).Events.Single(e=>e.Name=="ὯὨὨὧὨὫὩὠὩὢὯ");
        Api("Model.PairAdd",model,pairEvent.AddMethod.Name,1);
        Api("Model.PairRemove",model,pairEvent.RemoveMethod.Name,1);
        var kind=Return(Api("View.Kind",view,"ὭὯὧὩὪὯὤὯὦὤὧ")).Resolve();Role("TileKind",kind.FullName);Api("View.Selected",view,"ὠὠὮὪὤὨὡὮὨὡὩ");
        Api("View.Tile",view,"_imgTile");Api("View.Click",view,"OnClickItemAuto",0);
        Api("Camera.Pump","GameCameraManager","LateUpdate",0);
        // The explicit runtime Send lookup validates its IMessage signature before patching.
        return new(1,types.OrderBy(t=>t.FullName,StringComparer.Ordinal).Select(t=>new TypeContract(
            t.FullName,index.Shape(t),t.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Count>=10)
            .OrderByDescending(m=>m.Body.Instructions.Count).Take(8).Select(index.Body).ToArray(),
            selected.Where(m=>m.DeclaringType==t).OrderBy(m=>m.FullName,StringComparer.Ordinal)
            .Select(m=>new MemberContract(m.Name,MetadataIndex.Signature(m),index.MemberBody(m),index.Uses(m))).ToArray()
        )).ToArray(),apis.ToArray(),roles,kind.Fields.Where(f=>f.HasConstant).ToDictionary(f=>f.Name,f=>Convert.ToInt64(f.Constant)));
    }
}
