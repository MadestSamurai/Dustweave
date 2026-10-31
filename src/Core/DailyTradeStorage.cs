using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Normal inventory slots, shared with non-food items. StackCount is not an item limit.</summary>
public sealed class DailyTradeStorage
{
    public long Free { get; }
    public long FoodSlots { get; }
    private readonly JsonObject[] stacks;
    public DailyTradeStorage(JsonObject storage)
    {
        long limit=N(storage["slots"]), used=N(storage["used"]);
        stacks=Rows(storage["stacks"]);
        Require(limit>0 && used>=stacks.Length, "背包容量数据不完整，请重新读取库存。");
        Free=Math.Max(0,limit-used);
        FoodSlots=Free+stacks.Length;
    }
    public static JsonObject Capture(JsonObject evidence, JsonArray stacks) =>
        O(("slots",R(evidence,"trade.currency","InvenSlot")),
            ("used",R(evidence,"trade.storage","Count")),("stacks",stacks));
    public long Existing(long item) => stacks.LongCount(s=>N(s["id"])==item);
    public long Headroom(long item,long size) => stacks.Where(s=>N(s["id"])==item)
        .Sum(s=>Math.Max(0,size-N(s["count"])));
    public long Additional(long item,long size) => checked(Headroom(item,size)+Free*size);
    public long NewSlots(long item,long size,long additions) =>
        (Math.Max(0,additions-Headroom(item,size))+size-1)/size;
    // Future stocks are fractional forecasts. Reserve unknown food stacks and
    // existing fragmentation; only today's exact integer allocation is executable.
    public double ForecastSlots(JsonObject[] items, IReadOnlyDictionary<long,long> stock,
        IReadOnlyDictionary<long,long> reserved)
    {
        var ids=items.Select(i=>N(i["id"])).ToHashSet();
        double budget=FoodSlots-stacks.Count(s=>!ids.Contains(N(s["id"])));
        foreach(var item in items)
        {
            long id=N(item["id"]), size=N(item["stack"]), total=stock.GetValueOrDefault(id);
            budget-=Math.Max(0,Existing(id)-Math.Ceiling((double)total/size));
            budget-=(double)Math.Min(total,reserved.GetValueOrDefault(id))/size;
        }
        return Math.Max(0,budget);
    }
}
