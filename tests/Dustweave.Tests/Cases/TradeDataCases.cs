using BD2Daily;
using System.Text.Json.Nodes;
static class TradeDataCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        void Reject(Action action, string name)
        {
            bool refused = false;
            try
            {
                action();
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException or StageHostException or DailyStepException) { refused = true; }
            Check(refused, name);
        }
        string folder = Path.Combine(output, "trade-tables");
        Directory.CreateDirectory(folder);
        var fixtures = new Dictionary<string, string>
        {
            ["CookingTable"] = "[{\"id\":1,\"resultItemId\":2,\"resultItemCount\":1,\"talentLevel\":1,\"materialItemId\":[1],\"materialItemCount\":[2]}]",
            ["FoodTable"] = "[{\"id\":1,\"itemNameTextId\":1,\"stackCount\":999},{\"id\":2,\"itemNameTextId\":2,\"stackCount\":999}]",
            ["ProductTable"] = "[{\"id\":1,\"groupId\":1,\"elementId\":1,\"elementType\":5,\"elementCount\":1,\"priceType\":4,\"priceCount\":100,\"buyMaxCount\":100,\"noBargain\":0,\"reputationType\":0,\"discountRate\":0,\"premiumRate\":0}]",
            ["SellItemTable"] = "[{\"id\":1,\"elementId\":1,\"elementType\":5,\"elementCount\":1,\"priceType\":4,\"priceCount\":100,\"highPremiumDay\":1,\"highPremiumRate\":20,\"highPremiumshopId\":1},{\"id\":2,\"elementId\":2,\"elementType\":5,\"elementCount\":1,\"priceType\":4,\"priceCount\":300,\"highPremiumDay\":2,\"highPremiumRate\":20,\"highPremiumshopId\":1}]",
            ["ShopTable"] = "[{\"id\":1}]",
            ["TalentSkillTable"] = "[{\"id\":1,\"groupId\":11,\"classType\":7,\"catalystValue\":3,\"valueList\":[100,60]}]",
            ["TalentTable"] = "[{\"id\":11,\"classType\":7,\"talentSkillGroupId\":11}]",
            ["NameTextTable"] = "[{\"id\":1,\"textCn\":\"原料😀\"},{\"id\":2,\"textCn\":\"料理\"}]",
            ["CharTable"] = "[{\"id\":100,\"talentId\":11}]"
        };
        var counts = new JsonObject();
        foreach (var pair in fixtures)
        {
            File.WriteAllText(Path.Combine(folder, pair.Key + ".json"), pair.Value);
            counts[pair.Key] = JsonNode.Parse(pair.Value)!.AsArray().Count;
        }
        var manifest = new JsonObject { ["databaseSha256"] = new string('a', 64), ["assemblySha256"] = new string('b', 64), ["tables"] = counts };
        DailyJson.Write(Path.Combine(folder, "manifest.json"), manifest);
        string client = "cfa42b4a-41f6-47e3-bd50-7f4bab110e25";
        var original = DailyTradeCatalog.Build(folder, client);
        Check(original["items"]!.AsArray().Count == 2 && original["offers"]!.AsArray().Count == 1 && original["recipes"]![0]!["potions"]!.GetValue<int>() == 3 && original["characters"]!["100"]!["kind"]!.GetValue<int>() == 7, ".NET trade catalog includes complete formulas and character talents");
        Check(original["items"]![0]!["sale"]!.GetValue<int>() == 120 && original["items"]![0]!["name"]!.GetValue<string>() == "原料😀", ".NET trade catalog preserves integer valuation and Unicode");
        string canonical = "{\"a\":1,\"z\":\"中文😀\"}";
        string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        Check(DailyTradeCatalog.Fingerprint(new JsonObject { ["z"] = "中文😀", ["a"] = 1 }) == expected, "trade catalog fingerprint retains the original canonical Unicode protocol");
        var compatible = original.DeepClone().AsObject();
        compatible["database_sha256"] = new string('c', 64);
        compatible["source_hashes"]!["NameTextTable"] = new string('d', 64);
        compatible["character_sha256"] = new string('e', 64);
        Check(DailyTradeCatalog.Changes(original, compatible).Length == 0, "database and unrelated source changes require equivalent regenerated trade rules");
        foreach (string field in new[] { "schema", "client", "assembly_sha256", "characters", "items", "offers", "recipes" })
        {
            var changed = original.DeepClone().AsObject();
            changed[field] = field == "schema" ? JsonValue.Create(2) : JsonValue.Create("changed");
            Check(DailyTradeCatalog.Changes(original, changed).SequenceEqual(new[] { field }), "trade compatibility rejects changed " + field);
        }
        foreach (var (table, key, value) in new[] { ("SellItemTable", "priceType", 5), ("SellItemTable", "highPremiumRate", 30), ("ProductTable", "noBargain", 1), ("ProductTable", "elementCount", 2), ("ProductTable", "reputationType", 1) })
        {
            var rows = JsonNode.Parse(fixtures[table])!.AsArray();
            rows[0]![key] = value;
            DailyJson.Write(Path.Combine(folder, table + ".json"), rows);
            Reject(() => DailyTradeCatalog.Build(folder, client), "trade catalog rejects unsupported " + table + " " + key);
            File.WriteAllText(Path.Combine(folder, table + ".json"), fixtures[table]);
        }
        var cooking = JsonNode.Parse(fixtures["CookingTable"])!.AsArray();
        cooking[0]!["materialItemCount"] = new JsonArray();
        DailyJson.Write(Path.Combine(folder, "CookingTable.json"), cooking);
        Reject(() => DailyTradeCatalog.Build(folder, client), "trade catalog refuses truncated recipe amounts");
        File.WriteAllText(Path.Combine(folder, "CookingTable.json"), fixtures["CookingTable"]);
        manifest["tables"]!["CharTable"] = 2;
        DailyJson.Write(Path.Combine(folder, "manifest.json"), manifest);
        Reject(() => DailyTradeCatalog.Build(folder, client), "trade catalog refuses truncated character table");
        manifest["tables"]!["CharTable"] = 1;
        DailyJson.Write(Path.Combine(folder, "manifest.json"), manifest);
        string root = Path.Combine(output, "trade-transactions"), account = new string('a', 64);
        var context = new JsonObject { ["actor"] = new JsonArray(1, 2, "instance", account, new string('b', 64)), ["server"] = "server", ["cycle"] = "123" };
        string job = Path.Combine(root, "trade", "executions", account, "123", "execution.json"), business = Path.Combine(root, "live", "business", "receipt.json");
        foreach (string state in new[] { "prepared", "running", "stopped" })
        {
            DailyJson.Write(job, new JsonObject { ["account"] = account, ["context"] = new JsonObject { ["cycle"] = "123" }, ["state"] = state });
            Reject(() => DailyTradeData.CheckTransactions(root, context), "database refresh preserves original " + state + " transaction");
        }
        DailyJson.Write(job, new JsonObject { ["account"] = account, ["context"] = new JsonObject { ["cycle"] = "123" }, ["state"] = "completed" });
        DailyTradeData.CheckTransactions(root, context);
        Check(true, "completed trade record is preserved without replay");
        foreach (string state in new[] { "prepared", "dispatching", "unknown" })
            foreach (string role in new[] { "trade.buy", "dispatch.start" })
            {
                DailyJson.Write(business, new JsonObject { ["account"] = account, ["state"] = state, ["role"] = role, ["scope"] = new JsonObject { ["trade_session"] = "id" } });
                Reject(() => DailyTradeData.CheckTransactions(root, context), "database refresh preserves pending " + role + " " + state);
            }
        DailyJson.Write(business, new JsonObject { ["account"] = account, ["state"] = "completed", ["role"] = "trade.buy" });
        DailyTradeData.CheckTransactions(root, context);
        Check(true, "confirmed trade receipt does not block readonly data comparison");
        var data = await DailyTradeData.PrepareAsync(folder, root, new(1, 2, Path.Combine(folder, "missing-game.exe")), new(new(), context), () => false, _ => { });
        Reject(data.AssertReady, "missing game data blocks before trade navigation");
        Check(data.Proof["actions"]!.GetValue<int>() == 0 && data.Proof["resources_spent"]!.GetValue<bool>() == false, "failed managed preflight does not fabricate gameplay or a trade result");
        var resumeJob = new JsonObject { ["id"] = new string('c', 32), ["account"] = account, ["context"] = new JsonObject { ["account"] = account, ["player"] = context["actor"]![4]!.DeepClone(), ["server"] = "server", ["cycle"] = "123", ["database_sha256"] = original["database_sha256"]!.DeepClone() }, ["state"] = "stopped", ["phase"] = "cooking", ["operations"] = new JsonArray(), ["catalog_hash"] = DailyTradeCatalog.Fingerprint(original) };
        DailyJson.Write(job, resumeJob);
        DailyTradeData.CheckTransactions(root, context, original);
        Check(true, "unchanged verified current catalog resumes the original stopped session without rebinding its plan");
        var different = original.DeepClone().AsObject();
        different["database_sha256"] = new string('f', 64);
        Reject(() => DailyTradeData.CheckTransactions(root, context, different), "stopped trade cannot resume against a different catalog fingerprint");
        resumeJob["state"] = "running";
        DailyJson.Write(job, resumeJob);
        DailyTradeClose.Owner(root, context);
        Check(true, "managed shop close requires the current original post-purchase session");
        foreach (string phase in new[] { "purchase", "unknown" })
        {
            resumeJob["phase"] = phase;
            DailyJson.Write(job, resumeJob);
            Reject(() => DailyTradeClose.Owner(root, context), "managed close cannot end unconfirmed " + phase + " purchase");
        }
        resumeJob["phase"] = "purchase";
        DailyJson.Write(job, resumeJob);
        DailyTradeClose.Owner(root, context, endingBargain: false);
        Check(true, "ordinary shop exit is allowed before starting bargain in the running purchase phase");
        foreach (string invalidState in new[] { "prepared", "stopped", "completed" })
        {
            resumeJob["state"] = invalidState;
            DailyJson.Write(job, resumeJob);
            Reject(() => DailyTradeClose.Owner(root, context, endingBargain: false), "ordinary purchase exit rejects original " + invalidState + " transaction");
        }
        resumeJob["state"] = "running";
        resumeJob["phase"] = "unknown";
        DailyJson.Write(job, resumeJob);
        Reject(() => DailyTradeClose.Owner(root, context, endingBargain: false), "ordinary shop exit does not bypass an unknown trade phase");
        JsonObject Popup(bool ready) => new()
        {
            ["Surfaces"] = new JsonArray(new JsonObject { ["Type"] = "DiscountCancelPopupUI", ["InputReady"] = ready, ["Targets"] = new JsonArray(new JsonObject { ["Field"] = "_objButtonOk", ["Enabled"] = ready }) })
        };
        Check(DailyTradeClose.Next(Popup(false), true, false) == "wait", "managed close waits for the native popup to become ready");
        Check(DailyTradeClose.Next(Popup(true), true, false) == "confirm", "managed close confirms only a known owned bargain once");
        Check(DailyTradeClose.Next(Popup(true), true, true) == "wait", "managed close never resends its accepted confirmation during popup animation");
        Reject(() => DailyTradeClose.Next(Popup(true), false, false), "unowned bargain popup is preserved");
        var ambiguous = Popup(true);
        ambiguous["Surfaces"]!.AsArray().Add(ambiguous["Surfaces"]![0]!.DeepClone());
        Reject(() => DailyTradeClose.Next(ambiguous, true, false), "duplicate bargain popup is preserved");
        Check(DailyTradeClose.Next(new()
        {
            ["Surfaces"] = new JsonArray(new JsonObject { ["Type"] = "ShopUI" })
        }, true, true) == "wait" && DailyTradeClose.Next(new()
        {
            ["Surfaces"] = new JsonArray()
        }, true, true) == "settling", "managed close waits for both shop and confirmation surfaces to leave");
        Reject(() => DailyTradeClose.Next(new() { ["Surfaces"] = new JsonArray(new JsonObject { ["Type"] = "MessagePopupUI", ["Popup"] = true }) }, true, false), "managed trade close preserves foreign popup without generic confirmation");
        JsonObject Wallet()
        {
            JsonObject Reading(string id, string path, string json) => new()
            {
                ["Id"] = id,
                ["Error"] = "",
                ["Values"] = new JsonArray(new JsonObject { ["Path"] = path, ["Error"] = "", ["Json"] = json })
            };
            var currency = Reading("trade.currency", "Gold", "1000");
            currency["Values"]!.AsArray().Add(new JsonObject { ["Path"] = "Catalyst", ["Error"] = "", ["Json"] = "100" });
            return new()
            {
                ["Frame"] = CommandDriverCases.Frame(),
                ["Readings"] = new JsonArray(currency, Reading("trade.inventory", "$items", "[]"), Reading("trade.bargain", "$self", "false"))
            };
        }
        var wallet = Wallet();
        DailyTradeClose.Unchanged(wallet, Wallet());
        Check(true, "managed trade close proves currency and inventory unchanged after bargain exit");
        var activeWallet = Wallet();
        activeWallet["Readings"]![2]!["Values"]![0]!["Json"] = "true";
        var cachedExit = activeWallet.DeepClone().AsObject();
        cachedExit["Frame"]!["Surfaces"] = new JsonArray(new JsonObject { ["Type"] = "QuickMenuUI" });
        DailyTradeClose.VerifyExit(activeWallet, cachedExit, true);
        Check(true, "owned native cancel and unchanged absent shop prove exit with last-open cached flag still true");
        Reject(() => DailyTradeClose.VerifyExit(activeWallet, cachedExit, false), "cached bargain exit without owned confirmation cannot continue");
        foreach (string foreground in new[] { "ShopUI", "DiscountCancelPopupUI", "MessagePopupUI" })
        {
            var blocked = cachedExit.DeepClone().AsObject();
            blocked["Frame"]!["Surfaces"] = new JsonArray(new JsonObject { ["Type"] = foreground, ["Popup"] = true });
            Reject(() => DailyTradeClose.VerifyExit(activeWallet, blocked, true), "cached exit preserves remaining foreground " + foreground);
        }
        Reject(() => DailyTradeClose.VerifyExit(wallet, cachedExit, false), "ordinary exit still rejects unexpected bargain flag change");
        foreach (string resource in new[] { "Gold", "Catalyst" })
        {
            var changed = cachedExit.DeepClone().AsObject();
            changed["Readings"]![0]!["Values"]!.AsArray().Single(v => v!["Path"]!.GetValue<string>() == resource)!["Json"] = "1";
            Reject(() => DailyTradeClose.VerifyExit(activeWallet, changed, true), "owned cancel never permits changed " + resource);
        }
        foreach (string fault in new[] { "gold", "potions", "inventory", "bargain", "identity" })
        {
            var changed = Wallet();
            switch (fault)
            {
                case "gold":
                    changed["Readings"]![0]!["Values"]![0]!["Json"] = "999";
                    break;
                case "potions":
                    changed["Readings"]![0]!["Values"]![1]!["Json"] = "99";
                    break;
                case "inventory":
                    changed["Readings"]![1]!["Values"]![0]!["Json"] = "[1]";
                    break;
                case "bargain":
                    changed["Readings"]![2]!["Values"]![0]!["Json"] = "true";
                    break;
                case "identity":
                    changed["Frame"]!["AccountKey"] = "other";
                    break;
            }
            Reject(() => DailyTradeClose.Unchanged(wallet, changed), "managed trade close preserves uncertain " + fault + " rather than continuing");
        }
        await TradeShopExitCases.Run(output, cases);
    }
}
