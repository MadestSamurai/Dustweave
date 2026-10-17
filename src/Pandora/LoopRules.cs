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
    public sealed class CharacterChoice
    {
        public int Id, Group; public string Name = ""; public bool Unlocked;
    }
    public sealed class Control
    {
        public bool Enabled, AutoSelectEquipment = true, TraceCombat; public string RunId = "", Mode = "Currency"; public int StageId, StageGroup; public long Expires;
        // Both zero retain the game's selection. Slot order is not a stable identity.
        public int CharacterGroup, CharacterId;
        // Applies to challenge movement only. Unsupported native contracts retain
        // the established observed-motion controller.
        public bool ForecastMovement = true;
        public bool Valid(long now) { return !string.IsNullOrEmpty(RunId) && (Mode == "Currency" || Mode == "Challenge") && Expires > now && Expires - now <= TimeSpan.FromSeconds(15).Ticks; }
    }
    public static class LoopRules
    {
        public static bool WaitForEquipment(Control control, bool selectionOpen) => selectionOpen && control.Mode == "Challenge" && !control.AutoSelectEquipment;
        public static bool UsesGameCharacter(Control control) => control.CharacterGroup == 0 && control.CharacterId == 0;
        public static bool CharacterMatches(Control control, int group, int id) => UsesGameCharacter(control) ||
            control.CharacterGroup > 0 && control.CharacterId > 0 && control.CharacterGroup == group && control.CharacterId == id;
        public static CharacterChoice SelectCharacter(IEnumerable<CharacterChoice> choices, Control control) =>
            UsesGameCharacter(control) ? null : choices.FirstOrDefault(c => c.Unlocked && c.Group == control.CharacterGroup && c.Id == control.CharacterId);
        // Native SetUI can refill the same pooled window at the same level (queued rewards).
        public static string SkillPage(int instance, int level, int revision) { return "skill:" + instance + ":" + level + ":" + revision; }
        public static Stage Select(IEnumerable<Stage> stages, string mode, int group, int stageId)
        {
            return mode == "Challenge" ? stages.FirstOrDefault(s => s.Challenge && s.Unlocked) : stages.FirstOrDefault(s => s.Group == group && s.Id == stageId && !s.Challenge && s.Unlocked);
        }
    }
    // Read from the native result callback, not the last periodically sampled clock.
    // Challenge results use a generic result panel even after surviving to the limit.
    public sealed class RoundResult
    {
        public string RunId = "";
        public int Id, Revision, Group, StageId;
        public bool Challenge, Survived;
        public long ElapsedMilliseconds;
        public bool Completes(Control control, Stage target, int resultId, int revision)
        {
            return control.Mode == "Challenge" && target != null && target.Challenge && Challenge &&
                RunId.Length > 0 && RunId == control.RunId && Id == resultId && Revision == revision && ElapsedMilliseconds >= 0 &&
                Group == target.Group && StageId == target.Id &&
                (Survived || ElapsedMilliseconds >= 20 * 60 * 1000L);
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
    public enum BuildRestartPhase { None, Requested, PauseSent, ExitSent, Confirmed }
    // Exit authority belongs to one round and one native pause window, never an arbitrary popup.
    public sealed class BuildRestart
    {
        public BuildRestartPhase Phase;
        public int ManagerId, PauseId;
        public double Started;
        public bool Pending { get { return Phase != BuildRestartPhase.None; } }
        public void Request(int manager, double now) { if (Pending) return; ManagerId = manager; Started = now; Phase = BuildRestartPhase.Requested; }
        public bool Owns(int manager, bool challenge, bool autoEquipment, bool sameStage)
        { return Pending && manager == ManagerId && challenge && autoEquipment && sameStage; }
        public bool CanConfirm(int manager, int pause, bool matchingCallback)
        { return Phase == BuildRestartPhase.ExitSent && manager == ManagerId && pause == PauseId && matchingCallback; }
        public bool Expired(double now) { return Pending && now - Started >= 45; }
        public void Reset() { Phase = BuildRestartPhase.None; ManagerId = PauseId = 0; Started = 0; }
    }
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
