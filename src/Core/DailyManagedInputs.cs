using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
namespace Dustweave;

public sealed partial class DailyCommandDriver
{
    private static readonly HashSet<string> NavigationKeys = ["ui", "field", "back", "expect", "absent", "operation", "value", "reason"];
    private readonly Queue<(double At, string Key)> talentRecoveries = new();
    private readonly Queue<double> quitRecoveries = new();
    private static bool SameAction(JsonObject a, JsonObject b) => new[] { "ui", "field", "back", "expect", "absent", "operation", "value" }.All(k => JsonNode.DeepEquals(a[k] ?? (k == "back" ? JsonValue.Create(false) : k == "value" ? JsonValue.Create(0) : null), b[k] ?? (k == "back" ? JsonValue.Create(false) : k == "value" ? JsonValue.Create(0) : null)));
    // Keep the public two-argument API used by private plugins. Visibility and
    // readiness are distinct contracts: result animations may need early skipping,
    // whereas a consumptive preview must settle before its ownership is recorded.
    public async Task<JsonObject> SendObservedAsync(JsonObject action, JsonObject? ownedFrame = null)
    {
        try { return await SendObservedCoreAsync(action, ownedFrame, false); }
        catch (Exception e)
        {
            Diagnostics.Event("input_failed", DailyData.O(("action", action), ("error", e.Message), ("submitted", e is DailyStepException step && step.Submitted), ("last_observed", lastDiagnosticFrame)));
            throw;
        }
    }
    internal Task<JsonObject> SendPreviewObservedAsync(JsonObject action) => SendObservedCoreAsync(action, null, true);
    private async Task<JsonObject> SendObservedCoreAsync(JsonObject action, JsonObject? ownedFrame, bool requireReady)
    {
        action = DailyMenuNavigation.Normalize(action);
        ValidateAction(action);
        double end = clock() + Seconds(action);
        using var diagnostic = Diagnostics.Scope("ui_input", DailyData.O(("action", action), ("require_ready", requireReady)));
        var bound = (await ReadBound()).Frame;
        JsonObject sent;
        for (int attempt = 0; ; attempt++)
        {
            while (true)
            {
                if (stopped() || mailbox.Read("live", "pause") != null)
                    throw new DailyStepException("rejected", "Paused before managed UI input; no command sent");
                var current = (await ReadBound()).Frame;
                if (Text(current, "Scene") != Text(bound, "Scene"))
                    throw new DailyStepException("rejected", "Scene changed during preflight; no command sent");
                if (ownedFrame != null && !DailyTradePreview.Matches(action, ownedFrame, current))
                    throw new DailyStepException("rejected", "Owned confirmation changed while waiting for input; no command sent");
                if (clock() >= end)
                    throw new DailyStepException("rejected", "UI did not become ready; no command sent");
                if (await RecoverPresentationAsync(current, Text(action, "ui")))
                    continue;
                var surfaces = DailyNavigationDecision.Rows(current).Where(r => Text(r, "Type") == Text(action, "ui")).ToArray();
                bool animationSkip = Text(action, "ui") == "GachaResultUI" && Text(action, "field") == "_objSkipButton" && current["BridgeVersion"]?.GetValue<int>() >= 32;
                if (surfaces.Length != 1 || DailyNavigationDecision.ReadyInput(surfaces[0]) || animationSkip)
                    break;
                await delay(TimeSpan.FromMilliseconds(200));
            }
            try
            {
                sent = ownedFrame == null ? await SubmitBoundAsync(action, bound) : await SubmitOwnedAsync(action, ownedFrame);
                break;
            }
            catch (DailyStepException e) when (e.Kind == "rejected" && attempt < 5 && e.Message is "rejected: screen_changed" or "rejected: ui_not_ready" or "rejected: notice_checkbox_not_confirmed")
            {
                await delay(TimeSpan.FromMilliseconds(600));
            }
        }
        // Submission can include its own native readiness wait (for example a field
        // talent menu). It must not consume the subsequent presentation deadline.
        end = clock() + Seconds(action);
        string expect = Text(action, "expect"), absent = Text(action, "absent");
        JsonObject? lastObserved = null, openingFrame = null;
        try
        {
            while (true)
            {
                if (stopped() || mailbox.Read("live", "pause") != null)
                    throw new StageHostException("stopped", "Paused after managed UI dispatch");
                var after = (await ReadBound()).Frame;
                lastObserved = after.DeepClone().AsObject();
                Diagnostics.Observe(after, "await_result", DailyData.O(("command_id", sent["id"]), ("expect", expect), ("absent", absent)));
                if ((expect.Length > 0 || absent.Length > 0) && await RecoverPresentationAsync(after, Text(action, "ui")))
                {
                    if (clock() >= end) break;
                    continue;
                }
                var types = DailyNavigationDecision.Types(after);
                bool expected = (expect.Length == 0 || types.Contains(expect)) && (absent.Length == 0 || !types.Contains(absent));
                if (requireReady)
                {
                    if (Text(after, "Scene") != Text(bound, "Scene"))
                        throw new InvalidDataException("Preview scene changed while opening");
                    var matches = DailyNavigationDecision.Rows(after).Where(r => Text(r, "Type") == expect).ToArray();
                    if (matches.Length > 1)
                        throw new InvalidDataException("Ambiguous preview surface");
                    if (openingFrame != null)
                    {
                        var original = DailyNavigationDecision.Rows(openingFrame).Single(r => Text(r, "Type") == expect);
                        if (matches.Length != 1 || !JsonNode.DeepEquals(original["Id"], matches[0]["Id"]))
                            throw new InvalidDataException("Preview closed or replaced while opening");
                    }
                    else if (matches.Length == 1) openingFrame = after.DeepClone().AsObject();
                    expected = expected && matches.Length == 1 && DailyNavigationDecision.ReadyInput(matches[0], fallback: false)
                        && DailyNavigationDecision.Blockers(after, expect, policy).Length == 0;
                }
                if (expected)
                {
                    var result = new JsonObject { ["state"] = expect.Length > 0 || absent.Length > 0 ? "observed_expected_ui" : "dispatched_only", ["engine"] = "dotnet-driver-v1", ["id"] = sent["id"]!.DeepClone(), ["expect"] = action["expect"]?.DeepClone(), ["absent"] = action["absent"]?.DeepClone(), ["receipt"] = sent["receipt"]!.DeepClone(), ["after"] = after.DeepClone() };
                    result["completion"] = requireReady ? "input_ready" : expect.Length > 0 || absent.Length > 0 ? "visibility" : "dispatch";
                    if (sent["native_readiness_seconds"] != null) result["native_readiness_seconds"] = sent["native_readiness_seconds"]!.DeepClone();
                    if (openingFrame != null) result["opening_frame"] = openingFrame.DeepClone();
                    DailyJson.Write(Path.Combine(root, "live", "steps", sent["id"]!.GetValue<string>(), "result.json"), result);
                    return result;
                }
                // Always inspect a fresh frame first, including after a suspended
                // desktop resumes. Never infer failure solely from elapsed time.
                if (clock() >= end) break;
                await delay(TimeSpan.FromMilliseconds(200));
            }
        }
        catch (Exception error)
        {
            DailyJson.Write(Path.Combine(root, "live", "steps", sent["id"]!.GetValue<string>(), "result.json"), new
            {
                state = "unknown_after_dispatch",
                receipt = sent["receipt"],
                error = error.Message,
                last_observed = lastObserved,
                expected_action = action,
                engine = "dotnet-driver-v1"
            });
            throw new DailyStepException("pending", "UI observation interrupted after dispatch; no replay: " + error.Message, true) { Command = sent["command"]!.DeepClone().AsObject() };
        }
        DailyJson.Write(Path.Combine(root, "live", "steps", sent["id"]!.GetValue<string>(), "result.json"), new
        {
            state = "unknown_timeout",
            receipt = sent["receipt"],
            last_observed = lastObserved,
            expected_action = action,
            engine = "dotnet-driver-v1"
        });
        throw new DailyStepException("pending", "No expected UI; original command is preserved without replay", true) { Command = sent["command"]!.DeepClone().AsObject() };
    }
    private async Task<bool> RecoverPresentationAsync(JsonObject frame, string destination)
    {
        var talent = DailyNavigationDecision.TalentAction(frame, policy);
        if (talent != null && Text(talent, "ui") != destination)
        {
            await NavigationAsync(talent);
            return true;
        }
        var action = DailyNavigationDecision.Notice(frame, policy, out var notice);
        if (notice == null || Text(notice, "Type") == destination)
            return false;
        if (action == null)
        {
            await delay(TimeSpan.FromMilliseconds(200));
            return true;
        }
        Diagnostics.Event("popup_recovery", DailyData.O(("action", action), ("destination", destination)));
        await NavigationAsync(action);
        if (Text(action, "operation") == "notice_suppress")
        {
            double end = clock() + 5;
            while (clock() < end)
            {
                if (stopped() || mailbox.Read("live", "pause") != null)
                    throw new StageHostException("stopped", "Paused while confirming notice checkbox");
                var after = (await ReadBound()).Frame;
                var matches = DailyNavigationDecision.Rows(after).Where(r => JsonNode.DeepEquals(r["Id"], notice["Id"]) && Text(r, "Type") == Text(notice, "Type")).ToArray();
                if (matches.Length == 0 || matches.Length == 1 && Text(matches[0], "NoticeSuppression") == "checked")
                    return true;
                await delay(TimeSpan.FromMilliseconds(100));
            }
            throw new StageHostException("adapter", "7天选项未确认，保留弹窗供检查");
        }
        await delay(TimeSpan.FromMilliseconds(200));
        return true;
    }
    private static void RestrictedAction(JsonObject action, bool guild = false)
    {
        if (action == null)
            throw new StageHostException("protocol", "Missing managed navigation action");
        if (action.Any(p => !NavigationKeys.Contains(p.Key) && !(guild && p.Key == "allow_guild")))
            throw new StageHostException("protocol", "Unsupported managed navigation action field");
        action = DailyMenuNavigation.Normalize(action);
        ValidateAction(action);
    }
    public async Task<JsonObject> NavigationAsync(JsonObject action)
    {
        RestrictedAction(action);
        var observed = await ReadBound();
        var frame = observed.Frame;
        var options = new List<JsonObject?> { DailyNavigationDecision.CloseAction(frame, policy), DailyNavigationDecision.TalentAction(frame, policy), DailyNavigationDecision.StoryAction(frame, policy), DailyNavigationDecision.WeeklyResult(frame, policy), DailyNavigationDecision.OverlayAction(frame, policy) };
        options.Add(DailyNavigationDecision.Notice(frame, policy, out _));
        options.Add(DailyHomeDecision.Inspect(frame, policy).Action);
        try
        {
            options.Add(DailyNavigationDecision.Home(frame, policy));
        }
        catch (StageHostException e) when (e.Kind == "adapter") { }
        var selected = options.FirstOrDefault(a => a != null && SameAction(a, action));
        if (selected == null)
            throw new StageHostException("navigation_changed", "Navigation changed before dispatch; no input submitted");
        if (Text(selected, "expect") == "MenuUI" && Text(selected, "reason") == "Prepare menu for daily queue")
            DailyGameVisibility.EnsureVisible(frame["ProcessId"]!.GetValue<int>());
        try
        {
            if (Text(selected, "ui") == "GameQuitPopupUI")
            {
                while (quitRecoveries.TryPeek(out double prior) && clock() - prior > 60) quitRecoveries.Dequeue();
                if (quitRecoveries.Count >= 3) throw new StageHostException("adapter", "退出游戏确认反复出现，已取消3次；保留现场检查其他输入来源。");
                quitRecoveries.Enqueue(clock());
                Diagnostics.Event("cancel_quit", DailyData.O(("attempt", quitRecoveries.Count), ("action", selected)));
            }
            if (Text(selected, "operation") == "talent_error_ack")
                return await RecoverTalentAsync(selected, observed);
            return await SendObservedAsync(selected);
        }
        catch (DailyStepException e)
        {
            if (e.Kind == "rejected")
                throw new StageHostException("navigation_changed", e.Message);
            throw new StageHostException(e.Kind == "identity" ? "identity" : "pending", e.Message);
        }
    }
    private async Task<JsonObject> RecoverTalentAsync(JsonObject action, DailyStageFrame observed)
    {
        string key = observed.Context["actor"]!.ToJsonString() + "|" + Text(observed.Frame, "Scene");
        double at = clock();
        while (talentRecoveries.TryPeek(out var old) && at - old.At > 60)
            talentRecoveries.Dequeue();
        if (talentRecoveries.Count(r => r.Key == key) >= 4)
            throw new StageHostException("adapter", "探查失效提示持续重复，保留现场；没有重放消耗操作");
        talentRecoveries.Enqueue((at, key));
        string id = Guid.NewGuid().ToString("N"), path = Path.Combine(root, "live", "popup-recovery", id + ".json");
        var record = new JsonObject { ["id"] = id, ["state"] = "prepared", ["engine"] = "dotnet-driver-v1", ["kind"] = "talent_inactive", ["error_code"] = 100005, ["before"] = observed.Frame.DeepClone(), ["business_replayed"] = false };
        DailyJson.Write(path, record);
        try
        {
            var done = await SendObservedAsync(action);
            var after = done["after"]!.AsObject();
            if (Text(after, "Scene") != Text(observed.Frame, "Scene"))
                throw new StageHostException("identity", "弹窗恢复期间地图改变，原操作未重放");
            record["state"] = "closed";
            record["after"] = after.DeepClone();
            record["command_id"] = done["id"]!.DeepClone();
            DailyJson.Write(path, record);
            return done;
        }
        catch (Exception error) { record["state"] = "unknown"; record["error"] = error.Message; DailyJson.Write(path, record); throw; }
    }
    public async Task<JsonObject> GuildAsync(JsonObject action, string guildKey)
    {
        RestrictedAction(action, true);
        if (!DailyProfiles.ValidKey(guildKey))
            throw new StageHostException("protocol", "Invalid expected guild identity");
        var observed = await ReadBound();
        var daily = observed.Daily ?? throw new StageHostException("identity", "Guild observation unavailable");
        if (daily.Guild.GuildKey != guildKey)
            throw new StageHostException("identity", "Guild changed before submission; no input sent");
        var notice = DailyNavigationDecision.Notice(observed.Frame, policy, out _);
        var prior = new GuildStore(root).Prior(daily);
        DailyGuildStage.ValidateConfirmed(prior);
        var decision = DailyGuildStage.Decide(observed.Frame, daily, prior, DailyGuildStage.LoadRecipe(), policy);
        JsonObject? selected = notice != null && !Flag(action, "allow_guild") && SameAction(notice, action) ? notice : decision["action"] is JsonObject candidate && Flag(candidate, "allow_guild") == Flag(action, "allow_guild") && SameAction(candidate, action) ? candidate : null;
        if (selected == null)
            throw new StageHostException("guild_changed", "Guild page or receipt changed before dispatch; no input submitted");
        try
        {
            return await SendObservedAsync(selected);
        }
        catch (DailyStepException error) { throw new StageHostException(error.Kind == "rejected" ? "guild_rejected" : error.Kind == "identity" ? "identity" : "pending", error.Message); }
    }
}
/// <summary>Restore Unity rendering without foreground focus, pointer or keyboard input.</summary>
public static class DailyGameVisibility
{
    private delegate bool EnumerateWindow(nint hwnd, nint value);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow inspect, nint value);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int command);
    public static bool EnsureVisible(int pid)
    {
        bool restored = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != (uint)pid)
                return true;
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "UnityWndClass" && (!IsWindowVisible(hwnd) || IsIconic(hwnd)))
            {
                ShowWindowAsync(hwnd, 4);
                restored = true;
            }
            return true;
        }, 0);
        return restored;
    }
}
