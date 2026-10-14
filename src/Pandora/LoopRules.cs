#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
namespace Dustweave.Pandora
{
    public sealed class Stage
    {
        public int Id, Group, Difficulty, Index; public bool Challenge, Unlocked; public string Name = "";
    }
    public sealed class Control
    {
        public bool Enabled, AutoSelectEquipment = true; public string RunId = "", Mode = "Currency"; public int StageId, StageGroup; public long Expires;
        public bool Valid(long now) { return !string.IsNullOrEmpty(RunId) && (Mode == "Currency" || Mode == "Challenge") && Expires > now && Expires - now <= TimeSpan.FromSeconds(15).Ticks; }
    }
    public static class LoopRules
    {
        public static bool WaitForEquipment(Control control, bool selectionOpen) => selectionOpen && control.Mode == "Challenge" && !control.AutoSelectEquipment;
        // Native SetUI can refill the same pooled window at the same level (queued rewards).
        public static string SkillPage(int instance, int level, int revision) { return "skill:" + instance + ":" + level + ":" + revision; }
        public static Stage Select(IEnumerable<Stage> stages, string mode, int group, int stageId)
        {
            return mode == "Challenge" ? stages.FirstOrDefault(s => s.Challenge && s.Unlocked) : stages.FirstOrDefault(s => s.Group == group && s.Id == stageId && !s.Challenge && s.Unlocked);
        }
    }
    // UI submissions are scoped to one concrete UI instance. Waiting never repeats a request.
    public sealed class TransitionGate
    {
        string pending = ""; double since;
        public bool Waiting(string identity, double now, out bool timedOut) { timedOut = false; if (pending != identity) { pending = ""; return false; } timedOut = now - since >= 25; return true; }
        public void Sent(string identity, double now) { pending = identity; since = now; }
        public void Reset() { pending = ""; since = 0; }
        public bool ManualChoice(Control control, bool selectionOpen)
        {
            if (!LoopRules.WaitForEquipment(control, selectionOpen)) return false;
            Reset(); return true;
        }
    }

    public enum EntryAction { Wait, Started, CloseConfirmedResult, Blocked }
    // Confirming retry only selects the native result callback. It does not start
    // a round until the result page has actually closed. Never click Retry again.
    public sealed class RoundEntry
    {
        public bool Pending { get; private set; }
        public int ResultId { get; private set; }
        public int ResultRevision { get; private set; }
        public int CloseAttempts { get; private set; }
        double requested, lastClose;
        public void Begin(double now, int resultId = 0, int resultRevision = 0)
        {
            Pending = true; requested = now; lastClose = double.NegativeInfinity;
            ResultId = resultId; ResultRevision = resultRevision; CloseAttempts = 0;
        }
        public void Reset() { Pending = false; ResultId = ResultRevision = CloseAttempts = 0; }
        public EntryAction Observe(double now, bool playing, int resultId, int resultRevision,
            bool popup, bool main, bool sameStage, bool retrySelected, bool canClose)
        {
            if (!Pending) return EntryAction.Wait;
            if (playing && resultId == 0 && !popup && !main && sameStage)
            { Pending = false; return EntryAction.Started; }
            if (now - requested >= 45) return EntryAction.Blocked;
            if (ResultId != 0 && resultId == ResultId && resultRevision == ResultRevision &&
                !popup && !main && sameStage && retrySelected && canClose &&
                CloseAttempts < 3 && now - lastClose >= 1)
            {
                lastClose = now; CloseAttempts++;
                return EntryAction.CloseConfirmedResult;
            }
            return EntryAction.Wait;
        }
    }
}
