using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;
using Native = BD2Daily.Live;

static class PassiveUiCases
{
    private static readonly JsonSerializerOptions Fields = new() { IncludeFields = true };
    internal static string Token(JsonObject frame) => Native.LivePolicy.BuildUiToken(JsonSerializer.Deserialize<Native.Frame>(frame.ToJsonString(), Fields)!);
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        var policy = DailyNavigationPolicy.Load();
        Check(policy.Background.SetEquals(Native.LivePolicy.PassiveSurfaces), "native and managed passive surfaces agree");
        foreach (string page in new[] { "MessagePopupUI", "ShopPopupUI", "BuyFavoritePopupUI", "GachaMainUI", "MenuUI", "GameFieldDefaultUI", "MonsterHuntUI", "GuildUI", "PassUI", "MissionUI", "EquipmentMakingUI", "BattleResultUI" })
        {
            var frame = HomeNavigationCases.Frame(HomeNavigationCases.Surface(page, field: "_buttonOK"));
            string token = Token(frame), signature = DailyHomeDecision.Signature(frame);
            foreach (string type in Native.LivePolicy.PassiveSurfaces)
            {
                var passive = HomeNavigationCases.Surface(type, true, "_button", 999);
                passive["Text"] = new JsonArray("每日任務 嘗試抽抽樂 1/1");
                frame["Surfaces"]!.AsArray().Add(passive);
                Check(Token(frame) == token && DailyHomeDecision.Signature(frame) == signature
                    && DailyNavigationDecision.Blockers(frame, page, policy).Length == 0, page + " ignores added " + type);
                passive["Text"] = new JsonArray(); passive["Targets"] = new JsonArray(); passive["Id"] = 998; passive["InputReady"] = false;
                Check(Token(frame) == token && DailyHomeDecision.Signature(frame) == signature, page + " ignores changed " + type);
                frame["Surfaces"]!.AsArray().Remove(passive);
                Check(Token(frame) == token, page + " ignores removed " + type);
            }
            frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("UnknownPurchaseUI", true, "_buttonOK", 1000));
            Check(Token(frame) != token && DailyNavigationDecision.Blockers(frame, page, policy).Length == 1, page + " still rejects actual foreground modal");
            frame["Surfaces"]!.AsArray().RemoveAt(1);
            frame["Surfaces"]![0]!["Targets"]![0]!["Enabled"] = false;
            Check(Token(frame) != token, page + " still invalidates disabled target");
        }
        var order = HomeNavigationCases.Frame(HomeNavigationCases.Surface("MenuUI", field: "_buttonOK"));
        order["Surfaces"]![0]!["Targets"]!.AsArray().Add(new JsonObject { ["Id"] = 22, ["Enabled"] = true, ["Field"] = "_other" });
        string ordered = Token(order);
        var reversed = order["Surfaces"]![0]!["Targets"]!.AsArray().Reverse().Select(t => t!.DeepClone()).ToArray();
        order["Surfaces"]![0]!["Targets"] = new JsonArray(reversed);
        Check(Token(order) == ordered, "native target enumeration order is not a screen change");

        using (var f = new FreeDrawStageCases.Fixture(Path.Combine(output, "notice-free-draw")))
        {
            f.UseNativeTokens = true;
            int reads = 0;
            f.BeforeRead = () =>
            {
                var rows = f.Current["Surfaces"]!.AsArray();
                foreach (var row in rows.OfType<JsonObject>().Where(r => DailyNavigationDecision.PassiveSurface(r["Type"]?.GetValue<string>() ?? "")).ToArray()) rows.Remove(row);
                if (++reads % 2 == 0) rows.Add(HomeNavigationCases.Surface("NoticeUI", true, "_changingTarget", 1000 + reads));
            };
            var result = await f.Stage.ExecuteAsync(f.Context, (_, _, _) => throw new Exception("unexpected relay"));
            Check(result["state"]?.GetValue<string>() == "completed" && f.Confirmations == 2 && f.Previews == 2
                && f.Cancellations == 0 && f.Records().All(op => op["state"]?.GetValue<string>() == "completed"), "mission notices changing on every read do not interrupt, retry or clean up free draws");
        }

        int number = 0;
        foreach (string scenario in new[] { "owned", "changed-dialog", "changed-account", "changed-cycle", "foreign-modal", "submitted", "unknown", "no-proof", "stopped", "uncertain-cancel" })
        {
            using var f = new FreeDrawStageCases.Fixture(Path.Combine(output, "preview-cleanup-" + (++number)));
            var op = await f.PreviewRecord();
            f.Current["Surfaces"]![1]!["Targets"]!.AsArray().Add(new JsonObject { ["Id"] = 52, ["Field"] = "_buttonCancel", ["Enabled"] = true, ["Route"] = "ui" });
            op["preview_frame"] = f.Current.DeepClone();
            op["state"] = "rejected"; op["confirmation_not_submitted"] = true;
            switch (scenario)
            {
                case "changed-dialog": f.Current["Surfaces"]![1]!["Text"] = new JsonArray("another purchase"); break;
                case "changed-account": f.Current["AccountKey"] = "other"; break;
                case "changed-cycle": op["cycle"] = "yesterday"; break;
                case "foreign-modal": f.Current["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("UnknownPurchaseUI", true, "_buttonOK", 999)); break;
                case "submitted": op["command_id"] = "submitted"; break;
                case "unknown": op["state"] = "unknown"; break;
                case "no-proof": op.Remove("confirmation_not_submitted"); break;
                case "stopped": f.Stopped = true; break;
                case "uncertain-cancel": f.Box.State = "unknown"; break;
            }
            f.Business.Save(op);
            try { await f.Business.CleanupRejectedPreviewsAsync(f.Context); }
            catch (StageHostException) when (scenario == "changed-account") { }
            if (scenario is "owned" or "uncertain-cancel")
            {
                Check(f.Box.Commands.Count == 1 && f.Box.Commands[0]["TargetId"]?.GetValue<int>() == 52 && f.Confirmations == 0, scenario + " only cancels an unchanged, owned unsubmitted dialog");
                Check(f.Records().Single()["preview_cleanup"]?["state"]?.GetValue<string>() == (scenario == "owned" ? "closed" : "unknown"), scenario + " cleanup result is recorded separately from business");
                await f.Business.CleanupRejectedPreviewsAsync(f.Context);
                Check(f.Box.Commands.Count == 1, scenario + " cancellation is never blindly repeated");
            }
            else Check(f.Box.Commands.Count == 0, scenario + " cleanup sends no input");
        }
        using (var f = new FreeDrawStageCases.Fixture(Path.Combine(output, "preview-rejection-cleanup")))
        {
            var op = await f.PreviewRecord();
            f.Current["Surfaces"]![1]!["Targets"]!.AsArray().Add(new JsonObject { ["Id"] = 52, ["Field"] = "_buttonCancel", ["Enabled"] = true, ["Route"] = "ui" });
            op["preview_frame"] = f.Current.DeepClone(); f.Business.Save(op);
            bool rejectOnce = true;
            f.Driver.SubmissionGuard = () => { if (rejectOnce) { rejectOnce = false; throw new DailyStepException("rejected", "synthetic preflight rejection"); } };
            try { await f.Business.CommitAsync(op, f.Context); throw new Exception("Expected local rejection"); }
            catch (DailyStepException e) when (e.Kind == "rejected") { }
            var saved = f.Records().Single();
            Check(saved["state"]?.GetValue<string>() == "rejected" && saved["preview_cleanup"]?["state"]?.GetValue<string>() == "closed"
                && f.Cancellations == 1 && f.Confirmations == 0, "local rejection cleans its preview immediately while retaining failed business status");
            Check(DailyHomeDecision.Inspect(f.Current, policy).Kind == "action", "next stage can leave gacha after rejected confirmation cleanup");
        }
    }
}