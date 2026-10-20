using Mono.Cecil;
using Mono.Cecil.Cil;
namespace BD2Territory.Compatibility;

public static class SaffronBindings
{
    public static Dictionary<string, MethodDefinition> Resolve(ResolvedBindings r)
    {
        var inventory = r.Types[r.Contract.Roles["Inventory"]];
        var tables = BindingResolver.Api(r, r.Contract.Apis.Single(a => a.Role == "Tables.Cook")).DeclaringType;
        MethodDefinition Sender(string request, int count) => inventory.Methods.Single(m => m.IsStatic && m.Parameters.Count == count && m.HasBody && m.Body.Instructions.Any(i => i.OpCode.Code == Code.Newobj && i.Operand is MethodReference mr && mr.DeclaringType.FullName == "Proto.Net." + request));
        var eat = Sender("LifeEatFoodRequest", 2);
        if (!eat.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Collections.Generic.List`1<Proto.Net.ItemDBInfo>", "System.Action" })) throw new InvalidOperationException("Territory food API changed");
        var buy = Sender("LifeShopBuyRequest", 7);
        if (!buy.Parameters.Take(3).All(p => p.ParameterType.FullName == "System.Int32") || !buy.Parameters[3].ParameterType.Resolve().IsEnum || buy.Parameters[6].ParameterType.FullName != "System.Action") throw new InvalidOperationException("Territory shop API changed");
        var timer = inventory.Module.Types.Single(t => t.FullName == "gamfs.Thread.TimerManager");
        return new() {
            ["Saffron.Eat"] = eat, ["Saffron.Buy"] = buy,
            ["Saffron.Foods"] = inventory.Methods.Single(m => m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Collections.Generic.IReadOnlyCollection`1<Proto.Net.LifeEatFoodDBInfo>"),
            ["Saffron.BuyRows"] = tables.Methods.Single(m => m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32" && m.ReturnType.FullName == "System.Collections.Generic.List`1<Proto.Design.common.LifeBuyItemTable>"),
            ["Saffron.Buff"] = tables.Methods.Single(m => m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32" && m.ReturnType.FullName == "Proto.Design.common.LifeBuffTable"),
            ["Saffron.Clock"] = inventory.Module.Types.SelectMany(t => t.Methods).Single(m => m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == timer.FullName),
            ["Saffron.Now"] = timer.Methods.Single(m => m.Name == "UnixTimeStamp" && m.Parameters.Count == 0)
        };
    }
}
