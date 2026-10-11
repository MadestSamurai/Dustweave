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
        EntryRecovery entryRecovery = new EntryRecovery();
        bool openingMode;
        float modeRequestedAt;
        readonly ChainRetryPolicy chainRetry = new ChainRetryPolicy();
        System.Threading.Tasks.Task<RouteGuide.Evidence> opportunitySearch;
        RouteGuide.Evidence opportunity;
        float goalMapAt, nextOpportunity, opportunityAt, nextOpportunityCheck, earlyRetryAt;
        int opportunitySequence, earlyRetryStep, earlyRetries;
        bool retrySawReload;
        object earlyRetryEvidence;

        void ResetOpportunity()
        {
            chainRetry.Reset();
            opportunitySearch = null;
            opportunity = null;
            opportunitySequence = 0;
            goalMapAt = Time.unscaledTime;
            nextOpportunity = nextOpportunityCheck = 0;
        }

        int FutureChainRewards(int chain)
        {
            var rules = Bound(ruler, "chainRules");
            var lookup = rules.GetType().GetMethod(Bindings.Names["rareRewardLookup"], All);
            int count = 0;
            // Optimistically include every bonus before the target, even when its
            // spawn position or travel time will make it unusable in practice.
            for (int n = chain + 1; n < targetChain; n++)
                if ((bool)lookup.Invoke(rules, new object[] { n, 0 })) count++;
            return count;
        }

        bool CheckOpportunity()
        {
            if (!autoFlow || targetChain == 0 || Time.unscaledTime < nextOpportunityCheck) return false;
            float now = Time.unscaledTime;
            nextOpportunityCheck = now + .25f;
            int chain = Convert.ToInt32(Bound(ruler, "chain"));
            int maximum = RoundMaximum();
            if (maximum >= targetChain) return false;
            // Include magnet-attracted coins until native collection removes them.
            int remaining = Coins().Count(c => c != null && c.isActiveAndEnabled);
            int future = FutureChainRewards(chain);
            if (opportunitySearch != null && opportunitySearch.IsCompleted)
            {
                if (opportunitySearch.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                { opportunity = opportunitySearch.Result; opportunityAt = now; opportunitySequence++; }
                else { var ignored = opportunitySearch.Exception; opportunity = null; }
                opportunitySearch = null;
            }
            if (opportunitySearch == null && now >= nextOpportunity)
            {
                nextOpportunity = now + 2;
                var coins = new bool[graph.Count];
                foreach (var c in Coins())
                    if (c != null && c.isActiveAndEnabled && !Convert.ToBoolean(Bound(c, "coinCollected")))
                        coins[graph.Nearest(c.transform.position.x, c.transform.position.z)] = true;
                var pos = player.transform.position;
                int start = graph.Nearest(pos.x, pos.z), seed = opportunitySequence * 65537;
                float speed = Convert.ToSingle(Field(player, "_moveSpeed")) * Convert.ToSingle(Bound(player, "speedMultiplier"));
                float window = ChainWindow(), time = Convert.ToSingle(Bound(ruler, "chainRemaining"));
                var guide = new RouteGuide(graph);
                opportunitySearch = System.Threading.Tasks.Task.Run(() => {
                    guide.Search(start, coins, speed, true, 80, chain, time, window, seed);
                    return guide.LastSearch;
                });
            }
            var decision = chainRetry.Observe(now, now - goalMapAt, targetChain, maximum, chain,
                remaining, future, opportunitySequence, opportunity == null ? -1 : opportunity.ProjectedChain,
                opportunity == null ? -1 : now - opportunityAt);
            if (decision == ChainRetryReason.None) return false;
            if (!EntryReady("InGame", false, false)) return true;
            if (Visible("MansionRunawayPausePopupUI") != null || Visible("MansionRunawayNoticePopupUI") != null) return false;
            if (!Click(Visible("MansionRunawayHUD"), "_objBtnPause")) return false;
            earlyRetryStep = 1;
            earlyRetryAt = now;
            retrySawReload = false;
            earlyRetryEvidence = new { At = DateTime.UtcNow, Reason = decision.ToString(), Chain = chain,
                Maximum = maximum, Remaining = remaining, FutureRewards = future,
                Projected = opportunity == null ? -1 : opportunity.ProjectedChain, Samples = opportunitySequence,
                MapSeconds = now - goalMapAt };
            desired = Vector3.zero;
            reason = decision == ChainRetryReason.InsufficientCandy ? "chain-insufficient" : "chain-route-retry";
            return true;
        }

        bool EarlyRetryFlow(string phase)
        {
            if (earlyRetryStep == 0) return false;
            // Never confirm a user-created notice. Only our pause -> retry sequence
            // can reach step two, and an achieved native record always takes priority.
            if (RunGoal.AwaitSettlement(targetChain, RoundMaximum()))
            {
                if (phase == "Paused")
                {
                    // A late native pickup can arrive after the pause request.
                    // Cancel only our own pending retry and resume for settlement.
                    if (earlyRetryStep == 2) Click(Visible("MansionRunawayNoticePopupUI"), "_objBtnCancel");
                    else if (earlyRetryStep == 1) Click(Visible("MansionRunawayPausePopupUI"), "_objBtnResume");
                    return true;
                }
                earlyRetryStep = 0; reason = "goal-finishing"; return true;
            }
            if (phase == "ReloadingStage") retrySawReload = true;
            if (earlyRetryStep == 3 && retrySawReload && phase == "InGame" && State == "Playing")
            {
                earlyRetryStep = 0;
                attempts++; earlyRetries++;
                graph = null; target = itemGoal = -1; result = null;
                ResetOpportunity();
                reason = "retrying";
                return true;
            }
            if (Time.unscaledTime - earlyRetryAt > 20)
            { earlyRetryStep = 0; EntryFault("goal-retry-timeout"); return true; }
            if (earlyRetryStep == 1 && phase == "Paused" && Click(Visible("MansionRunawayPausePopupUI"), "_objBtnRetry"))
            { earlyRetryStep = 2; earlyRetryAt = Time.unscaledTime; }
            else if (earlyRetryStep == 2 && phase == "Paused" && Click(Visible("MansionRunawayNoticePopupUI"), "_objBtnConfirm"))
            { earlyRetryStep = 3; earlyRetryAt = Time.unscaledTime; }
            reason = "goal-restarting";
            return true;
        }
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
                entryRecovery = new EntryRecovery();
                openingMode = false;
                earlyRetryStep = earlyRetries = 0;
                earlyRetryEvidence = null;
                ResetOpportunity();
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

        void EntryFault(string code)
        {
            faulted = true;
            Pause();
            reason = code;
            Write("error.json", new { At = DateTime.UtcNow, Error = code, CurrentEntry = EntryNative.Read(),
                SavedEntry = entryRecovery.Saved, Recoveries = entryRecovery.Recoveries,
                ManagerPhase = manager == null ? "None" : Convert.ToString(Bound(manager, "managerState")) });
        }

        bool EntryReady(string phase, bool lobby, bool resultVisible)
        {
            var entry = EntryNative.Read();
            var action = entryRecovery.Decide(entry, EntryNative.Available(entryRecovery.Saved), phase, lobby, resultVisible);
            if (action == EntryAction.Ready) return true;
            if (action == EntryAction.Reopen)
            {
                // Reopen through the native entry; never replace another activity
                // or bypass the game's schedule check.
                entryRecovery.Reopening();
                EntryNative.Open(entryRecovery.Saved);
                openingMode = false;
                nextFlow = Time.unscaledTime + 1.2f;
                reason = "restoring-entry";
                return false;
            }
            if (action == EntryAction.ExitToRestore)
            {
                if (Click(Visible("MansionRunawayResultUI"), "_objBtnExit"))
                {
                    result = null;
                    target = -1;
                }
                reason = "restoring-entry";
                return false;
            }
            if (action == EntryAction.Wait) { reason = "waiting-transition"; return false; }
            EntryFault(action == EntryAction.Unavailable ? "event-unavailable" :
                action == EntryAction.Changed ? "entry-changed" : "entry-missing");
            return false;
        }

        void Flow()
        {
            if (Time.unscaledTime < nextFlow)
                return;
            nextFlow = Time.unscaledTime + 0.5f;
            string phase = manager == null ? "None" : Convert.ToString(Bound(manager, "managerState"));
            if (EarlyRetryFlow(phase)) return;
            if (EntryRecovery.Transitioning(phase) || Visible("LoadingUI") != null)
            {
                reason = "waiting-transition";
                return;
            }
            if (Visible("MessagePopupUI") != null)
            {
                reason = "waiting-popup";
                return;
            }
            var resultUi = Visible("MansionRunawayResultUI");
            var main = Visible("MansionRunawayMainUI");
            var select = Visible("MansionRunawayModeSelectUI");
            if (entryRecovery.Saved == null && (resultUi != null || main != null || select != null || phase == "InGame"))
            {
                var entry = EntryNative.Read();
                entryRecovery.Observe(entry, EntryNative.Available(entry));
            }
            if (resultUi != null)
            {
                if (phase != "InGame") { reason = "waiting-transition"; return; }
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
                if (action == ResultAction.Retry && !EntryReady(phase, false, true)) return;
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
            {
                entryRecovery.NewRound();
                return;
            }
            if (phase != "None")
            {
                reason = State == "Caught" ? "recovering-life" : State == "StageClear" ? "next-stage" : "waiting-transition";
                return;
            }
            if (select != null)
            {
                openingMode = false;
                if (!EntryReady(phase, true, false)) return;
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

            if (main != null)
            {
                if (!EntryReady(phase, true, false)) return;
                // Wait for the native callback instead of issuing duplicate requests.
                if (openingMode)
                {
                    if (Time.unscaledTime - modeRequestedAt >= 20) EntryFault("entry-timeout");
                    else reason = "selecting-mode";
                    return;
                }
                if (Click(main, "_objBtnStart"))
                {
                    openingMode = true;
                    modeRequestedAt = Time.unscaledTime;
                    reason = "selecting-mode";
                }
                return;
            }

            reason = State == "Caught" ? "recovering-life" : State == "StageClear" ? "next-stage" : "waiting-game";
        }
    }
}
