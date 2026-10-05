using BD2Daily;
using System.Text.Json;
using System.Text.Json.Nodes;

static class TradeShopExitCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        foreach (string scenario in new[] { "entry", "entry-after-potions", "late-exit", "active-purchase", "pending-purchase", "unexpected-cancel", "foreign-popup", "resource-change", "stop-after", "never-exits", "bargain-finish", "bargain-cache-persists", "bargain-no-confirm", "bargain-resource-change" })
        {
            string root = Path.Combine(output, "trade-shop-exit-" + scenario), account = new string('a', 64), session = new string('c', 32);
            var context = CommandDriverCases.Context();
            context["cycle"] = "123";
            bool bargain = scenario is "active-purchase" or "bargain-finish" or "bargain-cache-persists" or "bargain-no-confirm" or "bargain-resource-change", sent = false, confirmed = false, stopped = false;
            double time = 0;
            var box = new CommandDriverCases.Mailbox();
            var job = new JsonObject
            {
                ["id"] = session,
                ["account"] = account,
                ["state"] = "running",
                ["phase"] = scenario.StartsWith("bargain-", StringComparison.Ordinal) ? "cooking" : "purchase",
                ["context"] = new JsonObject { ["account"] = account, ["player"] = context["actor"]![4]!.DeepClone(), ["server"] = "test", ["cycle"] = "123" },
                ["operations"] = new JsonArray()
            };
            string jobPath = Path.Combine(root, "trade", "executions", account, "123", "execution.json");
            if (scenario is "entry-after-potions" or "pending-purchase")
            {
                string opId = new string('d', 32);
                job["operations"]!.AsArray().Add(opId);
                DailyJson.Write(Path.Combine(root, "live", "business", opId + ".json"), new JsonObject
                {
                    ["id"] = opId,
                    ["account"] = account,
                    ["player"] = context["actor"]![4]!.DeepClone(),
                    ["server"] = "test",
                    ["cycle"] = "123",
                    ["state"] = scenario == "pending-purchase" ? "unknown" : "completed",
                    ["role"] = "trade.buy",
                    ["scope"] = new JsonObject { ["trade_session"] = session }
                });
            }
            DailyJson.Write(jobPath, job);
            JsonObject Surface(string type, string field, int order = 0) => new()
            {
                ["Id"] = order + 1,
                ["Type"] = type,
                ["Popup"] = order > 0,
                ["Order"] = order,
                ["InputReady"] = true,
                ["Targets"] = new JsonArray(new JsonObject { ["Id"] = order + 2, ["Field"] = field, ["Enabled"] = true, ["Route"] = "ui" })
            };
            JsonObject Frame()
            {
                var frame = CommandDriverCases.Frame();
                frame["AtUtcTicks"] = 100000000 + (long)(time * TimeSpan.TicksPerSecond);
                var surfaces = new JsonArray();
                bool shop = !sent || scenario == "never-exits" || (scenario == "late-exit" && time < 1);
                surfaces.Add(shop ? Surface("ShopUI", "_objBackButton") : Surface("QuickMenuUI", "_buttonClose"));
                if (sent && !confirmed && (scenario is "unexpected-cancel" or "bargain-finish" or "bargain-cache-persists" or "bargain-resource-change"))
                    surfaces.Add(Surface("DiscountCancelPopupUI", "_objButtonOk", 100));
                if (sent && scenario == "foreign-popup")
                    surfaces.Add(Surface("MessagePopupUI", "_buttonConfirm", 100));
                surfaces.Add(new JsonObject { ["Id"] = 301, ["Type"] = "OverheadManageUI", ["Popup"] = true, ["Order"] = 301, ["InputReady"] = true });
                frame["Surfaces"] = surfaces;
                return frame;
            }
            JsonObject Reading(string id, string path, string value) => new()
            {
                ["Id"] = id,
                ["Error"] = "",
                ["Values"] = new JsonArray(new JsonObject { ["Path"] = path, ["Error"] = "", ["Json"] = value })
            };
            void Publish(JsonObject request)
            {
                var currency = Reading("trade.currency", "Gold", sent && (scenario == "resource-change" || confirmed && scenario == "bargain-resource-change") ? "999" : "1000");
                currency["Values"]!.AsArray().Add(new JsonObject { ["Path"] = "Catalyst", ["Error"] = "", ["Json"] = "100" });
                var evidence = new JsonObject
                {
                    ["ObservationRequest"] = request["Id"]!.DeepClone(),
                    ["Error"] = "",
                    ["AtUtcTicks"] = 100000000 + (long)(time * TimeSpan.TicksPerSecond),
                    ["Frame"] = Frame(),
                    ["Readings"] = new JsonArray(currency, Reading("trade.inventory", "$items", "[]"), Reading("trade.bargain", "$self", bargain && (!confirmed || scenario == "bargain-cache-persists") ? "true" : "false"))
                };
                box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(evidence);
            }
            box.AfterWrite = (_, name, bytes) => { if (name == "observation-request.json") Publish(JsonNode.Parse(bytes)!.AsObject()); };
            box.AfterCommand = command =>
            {
                if (command["SurfaceId"]?.GetValue<int>() == 101)
                    confirmed = true;
                else
                    sent = true;
                if (scenario == "stop-after")
                    stopped = true;
            };
            using var driver = new DailyCommandDriver(root, box, () =>
            {
                var raw = box.Read("live", "observation-request.json");
                if (raw != null)
                    Publish(JsonNode.Parse(raw)!.AsObject());
                return Task.FromResult(new DailyStageFrame(Frame(), context));
            }, () => stopped, () => 100000000 + (long)(time * TimeSpan.TicksPerSecond), () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; })
            {
                TradeStageActive = true
            };
            driver.Bind(context);
            JsonObject? result = null;
            string error = "";
            try
            {
                result = await driver.SubmitAsync(new()
                {
                    ["ui"] = "ShopUI",
                    ["field"] = "_objBackButton",
                    ["reason"] = "trade exit fixture"
                });
            }
            catch (Exception e) when (e is DailyStepException or StageHostException or InvalidDataException) { error = e.Message; }
            if (scenario is "entry" or "entry-after-potions" or "late-exit" or "pending-purchase")
            {
                Check(error.Length == 0 && box.Commands.Count == 1, "ordinary shop exit works during purchase: " + scenario);
                var proof = DailyJson.TryRead<JsonObject>(result!["managed_trade_close"]!.GetValue<string>())!;
                Check(proof["state"]!.GetValue<string>() == "completed" && proof["purpose"]!.GetValue<string>() == "ordinary_shop_exit" && !confirmed, "ordinary shop exit records unchanged resources without bargain cancellation: " + scenario);
                Check(JsonNode.DeepEquals(DailyJson.TryRead<JsonObject>(jobPath), job), "ordinary navigation preserves original transaction and phase: " + scenario);
                if (scenario == "late-exit")
                    Check(time >= 1, "ordinary shop exit waits for animation without resending");
            }
            else if (scenario is "bargain-finish" or "bargain-cache-persists")
            {
                Check(error.Length == 0 && confirmed && box.Commands.Count == 2, "post-purchase bargain exit confirms the owned popup exactly once: " + scenario);
                var proof = DailyJson.TryRead<JsonObject>(result!["managed_trade_close"]!.GetValue<string>())!;
                Check(proof["purpose"]!.GetValue<string>() == "end_bargain" && proof["owned_cancel_confirmed"]!.GetValue<bool>(), "bargain end and ordinary navigation have separate durable purposes: " + scenario);
                if (scenario == "bargain-cache-persists")
                    Check(proof["bargain_flag_after"]!.GetValue<bool>() && proof["bargain_flag_semantics"]!.GetValue<string>() == "last_shop_open_response", "native cached bargain flag cannot falsely reject the confirmed unchanged exit");
            }
            else
                Check(error.Length > 0 && box.Commands.Count == (scenario == "active-purchase" ? 0 : scenario == "bargain-resource-change" ? 2 : 1), "trade shop exit preserves uncertainty without extra input: " + scenario);
        }
    }
}
