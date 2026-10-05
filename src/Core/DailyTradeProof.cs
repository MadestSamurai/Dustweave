using System.Text.Json;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

public static class DailyTradeProof
{
    public static JsonObject Delta(JsonObject before, JsonObject after, JsonObject delta)
    {
        var b = DailyTradeResume.Wallet(before);
        var a = DailyTradeResume.Wallet(after);
        var expected = b["items"]!.DeepClone().AsObject();
        foreach (var p in delta["items"] as JsonObject ?? new())
            expected[p.Key] = checked(N(expected[p.Key]) + N(p.Value));
        Require(expected.All(p => N(p.Value) >= 0), "Planned food balance negative");
        Require(N(a["gold"]) - N(b["gold"]) == N(delta["gold"]) && N(a["potions"]) - N(b["potions"]) == N(delta["potions"]), "Currency delta not settled or differs");
        static JsonObject Positive(JsonObject values) => new(values.Where(p => N(p.Value) > 0).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Copy(p.Value))));
        Require(JsonNode.DeepEquals(Positive(a["items"]!.AsObject()), Positive(expected)), "Food delta not settled or differs");
        Require(JsonNode.DeepEquals(a["other"], b["other"]) && JsonNode.DeepEquals(a["protected"], b["protected"]), "Non-food or protected inventory changed");
        return O(("gold_delta", N(delta["gold"])), ("potions_delta", N(delta["potions"])), ("food_delta", delta["items"] ?? new JsonObject()), ("gold_after", a["gold"]), ("potions_after", a["potions"]));
    }
    public static JsonObject[] CookingResponses(JsonObject before, JsonArray events, JsonObject after, int packets, bool complete = true)
    {
        Identity(before, after);
        var selected = Rows(events).Where(e => S(e["Role"]) == "trade.cook").OrderBy(e => N(e["Sequence"])).ToArray();
        var req = selected.Where(e => S(e["Kind"]) == "request").ToArray();
        var res = selected.Where(e => S(e["Kind"]) == "response").ToArray();
        Require(packets > 0 && req.Length <= packets && res.Length <= req.Length && selected.Select(e => N(e["Sequence"])).Distinct().Count() == selected.Length, "Unexpected cooking chain");
        Require(selected.All(e => DailyEvidence.SameActor(e["Frame"]!.AsObject(), before["Frame"]!.AsObject())), "Cooking actor mismatch");
        for (int i = 0; i < res.Length; i++)
            Require(N(req[i]["Sequence"]) < N(res[i]["Sequence"]) && S(res[i]["Error"]) == "" && N(res[i]["ErrorCode"]) == 0 && B(res[i]["Accepted"]), "Cooking response rejected or out of order");
        if (complete)
            Require(req.Length == packets && res.Length == packets, "Cooking batch still running");
        return res.Select(DailyEvidence.Values).ToArray();
    }
    public static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after)
    {
        var before = op["before"]!.AsObject();
        var proof = op["scope"]!["_proof"]!.AsObject();
        string kind = S(proof["kind"]);
        switch (kind)
        {
            case "bargain":
                Require(B(Response("dispatch.start", events, before, after)!["IsSuccess"]) && B(R(after, "trade.bargain", "$self")), "Bargain did not activate");
                break;
            case "buy":
                Response("trade.buy", events, before, after);
                var old = Rows(State(before, "trade.native", "$self")["Offers"]).ToDictionary(r => (N(r["Shop"]), N(r["Product"])));
                var latest = Rows(State(after, "trade.native", "$self")["Offers"]).ToDictionary(r => (N(r["Shop"]), N(r["Product"])));
                foreach (var row in Rows(proof["stock"] ?? new JsonArray()))
                {
                    var key = (N(row["shop"]), N(row["product"]));
                    Require(old.ContainsKey(key) && latest.ContainsKey(key) && N(old[key]["Remaining"]) - N(latest[key]["Remaining"]) == N(row["count"]), "Shop supply delta differs");
                }
                break;
            case "sell":
                Response("trade.sell", events, before, after);
                break;
            case "cook":
                var parts = CookingResponses(before, events, after, I(proof["packets"])).SelectMany(r => Rows(r["ItemInfo"])).ToArray();
                Require(parts.All(r => N(r["type"]) == 5 && N(r["count"]) > 0), "Unexpected cooking output");
                var produced = parts.GroupBy(r => N(r["id"])).ToDictionary(g => g.Key, g => g.Sum(r => N(r["count"])));
                Require(produced.Count == 1 && produced.GetValueOrDefault(N(proof["item"])) == N(proof["output_count"] ?? proof["count"]), "Cooking output differs");
                break;
            default:
                throw new InvalidDataException("Unknown trade proof kind");
        }
        return Delta(before, after, proof["delta"]!.AsObject());
    }
    public static bool OwnsPreview(JsonObject op, DailyStageFrame observed)
    {
        if (S(op["state"]) is not ("preview_ready" or "unknown_preview"))
            return false;
        var frame = op["preview_frame"] as JsonObject;
        string ui = S(op["action"]?["ui"]);
        if (frame == null || op["action"] is not JsonObject action || !DailyTradePreview.Matches(action, frame, observed.Frame))
            return false;
        var before = DailyNavigationDecision.Rows(frame).SingleOrDefault(r => S(r["Type"]) == ui);
        var current = DailyNavigationDecision.Rows(observed.Frame).SingleOrDefault(r => S(r["Type"]) == ui);
        return before != null && current != null && JsonNode.DeepEquals(before["Id"], current["Id"])
            && (DailyTradePreview.IsConfirmation(action) || DailyNavigationDecision.ReadyInput(current))
            && DailyNavigationDecision.Blockers(observed.Frame, ui, DailyNavigationPolicy.Load()).Length == 0;
    }
    public static bool CanResumeCooking(JsonObject op, DailyStageFrame frame) => S(op["role"]) == "trade.cook" && S(op["scope"]?["_proof"]?["kind"]) == "cook" && DailyManagedBusiness.Pending(op) && DailyWorkflowRegistry.Owned(op, frame) && DailyNavigationDecision.Types(frame.Frame).Contains("CookingUI");
    public static IEnumerable<DailyBusinessProof> Proofs()
    {
        foreach (string role in new[] { "trade.buy", "trade.cook", "trade.sell" })
            yield return new(role, "trade", ["trade", "dispatch"], Verify, PreviewOwner: OwnsPreview, CanResume: role == "trade.cook" ? CanResumeCooking : null);
    }
    public static JsonObject Split(JsonObject plan, JsonObject state)
    {
        var supply = Rows(state["offers"]).ToDictionary(r => (N(r["shop"]), N(r["product"])));
        var full = new JsonArray();
        var partial = new JsonArray();
        foreach (var r in Rows(plan["purchases"]))
        {
            var offer = supply[(N(r["shop"]), N(r["product"]))];
            Require(N(r["count"]) > 0 && N(r["count"]) <= N(offer["remaining"]) && N(r["price"]) == N(offer["price"]), "Purchase plan became stale");
            (N(r["count"]) == N(offer["remaining"]) ? full : partial).Add(r.DeepClone());
        }
        return O(("full", full), ("partial", partial));
    }
    public static JsonObject[] CookingOrder(JsonObject plan, JsonObject catalog)
    {
        var buyable = Rows(catalog["offers"]).Select(r => N(r["item"])).ToHashSet();
        return Rows(plan["cooking"]).OrderByDescending(r => r["materials"]!.AsObject().Count(p => !buyable.Contains(long.Parse(p.Key)))).ThenBy(r => S(r["sale_date"]) != S(plan["game_date"])).ThenBy(r => S(r["sale_date"]), StringComparer.Ordinal).ThenBy(r => N(r["recipe"])).ToArray();
    }
    public static JsonObject Portion(JsonObject row, long count)
    {
        long total = N(row["count"]);
        Require(count > 0 && count <= total && N(row["potions"]) % total == 0 && row["materials"]!.AsObject().All(p => N(p.Value) % total == 0), "Invalid cooking portion");
        var part = row.DeepClone().AsObject();
        part["count"] = count;
        part["potions"] = N(row["potions"]) / total * count;
        part["materials"] = new JsonObject(row["materials"]!.AsObject().Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(N(p.Value) / total * count))));
        return part;
    }
    public static JsonArray SaleStacks(JsonObject plan, JsonObject wallet, JsonObject catalog, JsonObject[]? remainingCooking = null)
    {
        var allowed = Rows(catalog["items"]).Select(r => N(r["id"])).ToHashSet();
        var reserves = new Dictionary<string, long>();
        void Add(string k, long n) => reserves[k] = checked(reserves.GetValueOrDefault(k) + n);
        foreach (var r in remainingCooking ?? [])
            foreach (var p in r["materials"]!.AsObject())
                Add(p.Key, N(p.Value));
        foreach (var h in Rows(plan["holds"] ?? new JsonArray()))
            Add(S(h["item"]), N(h["count"]));
        foreach (var p in plan["settings"]!["reserve_items"]!.AsObject())
            Add(p.Key, N(p.Value));
        var result = new JsonArray();
        var stacks = Rows(wallet["stacks"]);
        foreach (var r in Rows(plan["sales"]).Where(r => B(r["today_quote_confirmed"])))
        {
            long id = N(r["item"]);
            Require(allowed.Contains(id), "Non-food sale rejected");
            string key = id.ToString();
            long unlocked = stacks.Where(s => N(s["id"]) == id && !DailyTradeSnapshot.Flag(s["keepFlag"])).Sum(s => N(s["count"]));
            long locked = N(wallet["items"]?[key]) - unlocked, explicitReserve = N(plan["settings"]!["reserve_items"]?[key]);
            long keep = reserves.GetValueOrDefault(key) - explicitReserve + Math.Max(0, explicitReserve - locked);
            long remaining = Math.Min(N(r["count"]), Math.Max(0, unlocked - keep));
            foreach (var s in stacks.OrderBy(s => N(s["count"])).ThenBy(s => N(s["invenIndex"])))
            {
                if (N(s["id"]) != id || DailyTradeSnapshot.Flag(s["keepFlag"]) || N(s["type"]) != 5)
                    continue;
                long count = Math.Min(remaining, Math.Min(N(s["count"]), 99999));
                if (count <= 0)
                    continue;
                var sale = r.DeepClone().AsObject();
                sale["count"] = count;
                sale["instance"] = N(s["invenIndex"]);
                sale["value"] = count * N(sale["price"]);
                result.Add(sale);
                remaining -= count;
            }
        }
        return result;
    }
}
