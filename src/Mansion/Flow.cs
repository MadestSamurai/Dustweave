using System;
using System.Linq;
using System.Collections;
using UnityEngine;
using Newtonsoft.Json.Linq;
using BD2.LocalIpc;

namespace Dustweave.Mansion.Runtime
{
    public sealed partial class Engine
    {
        readonly NativeNetworkWatch network = new NativeNetworkWatch();
        bool handingOff, faulted, finished, autoFlow, retry = true;
        string runId = "", selectedMode = "Normal";
        int attempts, targetChain;
        float nextFlow, uiAt, runStartedAt;
        object[] uiCache = new object[0];
        public void PrepareHandoff()
        {
            handingOff = true;
            Pause();
        }

        public string HandoffBusy()
        {
            if (manager == null)
                return "";
            string phase = Convert.ToString(Bound(manager, "managerState"));
            if (phase == "Entering" || phase == "ReloadingStage" || phase == "Exiting")
                return "等待小游戏场景切换";
            if ((State == "StageClear" || State == "GameOver") && result == null && !network.Idle)
                return "等待小游戏结算";
            return "";
        }

        void Control(JToken c)
        {
            string incoming = (string)c["RunId"] ?? "research";
            int requestedGoal = RunGoal.Normalize((int?)c["TargetChain"] ?? 0);
            bool refreshPlanner = incoming != runId || requestedGoal != targetChain;
            if (incoming != runId)
            {
                runId = incoming;
                runStartedAt = Time.unscaledTime;
                finished = false;
                faulted = false;
                result = null;
                attempts = 0;
                target = -1;
            }

            expires = (long? )c["Expires"] ?? 0;
            if (expires > DateTime.UtcNow.AddSeconds(10).Ticks)
                throw new InvalidOperationException("invalid-control-expiry");
            targetChain = requestedGoal;
            if (refreshPlanner)
            {
                var tuning = c["PlannerSettings"] as JObject;
                var normal = new PlannerOptions((float?)tuning?["CollisionHorizon"] ?? 3f, (double?)tuning?["GuideWeight"] ?? 110, (float?)tuning?["WaitSeconds"] ?? 0, (bool?)tuning?["CollectNearbyItems"] ?? true);
                plannerOptions = PlannerOptions.ForGoal(targetChain, normal);
                // Discard the previous mode's pickup target and route before the
                // next movement decision. Heartbeats do not rebuild the planner.
                graph = null;
                target = itemGoal = -1;
                waitUntil = nextItem = 0;
                plan = null;
            }
            selectedMode = targetChain > 0 || (string)c["Mode"] == "Challenge" ? "Challenge" : "Normal";
            retry = (bool? )c["Retry"] != false;
            autoFlow = (bool? )c["AutoFlow"] == true;
            running = (bool? )c["Enabled"] == true && !finished && !faulted && expires > DateTime.UtcNow.Ticks;
            if (!running)
                Pause();
        }

        object[] CachedUis()
        {
            if (Time.unscaledTime >= uiAt)
            {
                uiAt = Time.unscaledTime + 1;
                uiCache = Uis();
            }

            return uiCache;
        }

        UIBase Visible(string name)
        {
            return UnityEngine.Object.FindObjectsOfType<UIBase>().FirstOrDefault(u => u.GetType().Name == name && u.gameObject.activeInHierarchy);
        }

        bool Click(UIBase ui, string field)
        {
            if (ui == null)
                return false;
            var go = Field(ui, field) as GameObject;
            if (go == null || !go.activeInHierarchy)
                return false;
            ui.OnClickUI(go);
            uiAt = 0;
            nextFlow = Time.unscaledTime + 1.2f;
            return true;
        }

        void Flow()
        {
            if (Time.unscaledTime < nextFlow)
                return;
            nextFlow = Time.unscaledTime + 0.5f;
            var resultUi = Visible("MansionRunawayResultUI");
            if (resultUi != null)
            {
                if (result == null || resultAt < runStartedAt)
                {
                    if (Click(resultUi, "_objBtnExit"))
                        reason = "selecting-mode";
                    return;
                }

                bool clear = (bool)result.GetType().GetProperty("Clear").GetValue(result, null);
                int maxChain = Convert.ToInt32(result.GetType().GetProperty("MaxChain").GetValue(result, null));
                var retryButton = Field(resultUi, "_objBtnRetry") as GameObject;
                bool retryAvailable = retryButton != null && retryButton.activeInHierarchy;
                var action = RunGoal.Decide(true, clear, maxChain, retry, retryAvailable, targetChain);
                if (action == ResultAction.GoalCompleted || action == ResultAction.Completed)
                {
                    finished = true;
                    Pause();
                    reason = action == ResultAction.GoalCompleted ? "goal-completed" : "completed";
                    return;
                }

                if (action == ResultAction.Defeated)
                {
                    finished = true;
                    Pause();
                    reason = !retry ? "defeated" : "retry-unavailable";
                    return;
                }

                if (Time.unscaledTime - resultAt < 3)
                {
                    reason = "waiting-result";
                    return;
                }

                // Successful native results hide Retry. Exit normally, then
                // the existing main/mode-selection flow starts another round.
                string button = action == ResultAction.ExitAndRetry ? "_objBtnExit" : "_objBtnRetry";
                if (Click(resultUi, button))
                {
                    if (action == ResultAction.Retry) attempts++;
                    result = null;
                    target = -1;
                    reason = action == ResultAction.ExitAndRetry ? "goal-restarting" : "retrying";
                }
                else reason = "waiting-result";

                return;
            }

            if (State == "Playing")
                return;
            var select = Visible("MansionRunawayModeSelectUI");
            if (select != null)
            {
                var items = (IEnumerable)Field(select, "_modeItems");
                var item = items.Cast<object>().Single(x => Convert.ToString(x.GetType().GetProperty("ModeType").GetValue(x, null)) == selectedMode);
                var root = Field(item, "_objRoot") as GameObject;
                if (root == null || !root.activeInHierarchy)
                {
                    reason = "mode-unavailable";
                    return;
                }

                select.OnClickUI(root);
                if (Click(select, "_objBtnStart"))
                {
                    attempts++;
                    result = null;
                    target = -1;
                    reason = "entering";
                }

                return;
            }

            var main = Visible("MansionRunawayMainUI");
            if (main != null)
            {
                if (Click(main, "_objBtnStart"))
                    reason = "selecting-mode";
                return;
            }

            reason = State == "Caught" ? "recovering-life" : State == "StageClear" ? "next-stage" : "waiting-game";
        }
    }
}

