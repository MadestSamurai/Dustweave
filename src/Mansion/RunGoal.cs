namespace Dustweave.Mansion
{
    public sealed class EntryIdentity
    {
        public readonly string Origin;
        public readonly int ScheduleId, MiniGameId;
        public EntryIdentity(string origin, int scheduleId, int miniGameId)
        { Origin = origin; ScheduleId = scheduleId; MiniGameId = miniGameId; }
        public bool Empty { get { return Origin == "None" && ScheduleId == 0 && MiniGameId == 0; } }
        public bool Complete { get { return !string.IsNullOrEmpty(Origin) && Origin != "None" && ScheduleId > 0 && MiniGameId > 0; } }
        public bool Same(EntryIdentity other)
        { return other != null && Origin == other.Origin && ScheduleId == other.ScheduleId && MiniGameId == other.MiniGameId; }
    }

    public enum EntryAction { Ready, Wait, Reopen, ExitToRestore, Missing, Changed, Unavailable }

    // Recover only the exact native entry observed during this control run.
    // A different nonempty context belongs to another activity and is never overwritten.
    public sealed class EntryRecovery
    {
        public EntryIdentity Saved { get; private set; }
        public int Recoveries { get; private set; }
        bool attempted;
        public void Observe(EntryIdentity entry, bool nativeAvailable)
        {
            if (Saved == null && entry != null && entry.Complete && nativeAvailable) Saved = entry;
        }
        public void NewRound() { attempted = false; }
        public void Reopening() { attempted = true; Recoveries++; }
        public static bool Transitioning(string phase)
        { return phase == "Entering" || phase == "ReloadingStage" || phase == "Exiting"; }
        public EntryAction Decide(EntryIdentity current, bool savedAvailable, string phase, bool lobby, bool result)
        {
            if (Transitioning(phase)) return EntryAction.Wait;
            if (Saved == null) return EntryAction.Missing;
            if (current == null || (!current.Empty && !Saved.Same(current))) return EntryAction.Changed;
            if (!savedAvailable) return EntryAction.Unavailable;
            if (Saved.Same(current)) return EntryAction.Ready;
            if (result && phase == "InGame") return EntryAction.ExitToRestore;
            if (lobby && phase == "None" && !attempted) return EntryAction.Reopen;
            return EntryAction.Missing;
        }
    }

    public enum ResultAction { Wait, Completed, Defeated, Retry, ExitAndRetry, GoalCompleted }
    public enum ChainRetryReason { None, InsufficientCandy, UnpromisingRoute }

    // This is a time-efficiency policy, not a proof that no route exists. Route
    // evidence must come from several fresh searches; one failed tour is insufficient.
    public sealed class ChainRetryPolicy
    {
        float shortSince = -1, routeSince = -1;
        int samples, lastSequence;
        public void Reset() { shortSince = routeSince = -1; samples = lastSequence = 0; }
        public ChainRetryReason Observe(float now, float mapAge, int target, int maximum,
            int chain, int remaining, int futureRewards, int sequence, int projected, float evidenceAge)
        {
            if (RunGoal.Normalize(target) == 0 || maximum >= target || remaining < 0 || futureRewards < 0)
            { Reset(); return ChainRetryReason.None; }
            if (chain + remaining + futureRewards < target)
            {
                if (shortSince < 0) shortSince = now;
                if (mapAge >= 2 && now - shortSince >= .75f) return ChainRetryReason.InsufficientCandy;
            }
            else shortSince = -1;
            if (projected < 0 || evidenceAge < 0 || evidenceAge > 3 || projected + futureRewards >= target)
            { routeSince = -1; samples = 0; lastSequence = sequence; return ChainRetryReason.None; }
            if (sequence != lastSequence && sequence > 0)
            {
                lastSequence = sequence;
                if (routeSince < 0) routeSince = now;
                samples++;
            }
            // Near misses receive more time for native bonus spawns and alternate branches.
            float grace = projected + futureRewards >= target - 5 ? 12 : 6;
            return mapAge >= 8 && samples >= 3 && routeSince >= 0 && now - routeSince >= grace
                ? ChainRetryReason.UnpromisingRoute : ChainRetryReason.None;
        }
    }
    // Only this round's acknowledged native result confirms a goal.
    public static class RunGoal
    {
        public static int Normalize(int requested) { return requested == 80 ? 80 : 0; }
        public static bool AwaitSettlement(int target, int roundMaximum)
        { return Normalize(target) > 0 && roundMaximum >= target; }
        public static ResultAction Decide(bool currentResult, bool clear, int maxChain, bool retry, bool retryAvailable, int target)
        {
            if (!currentResult) return ResultAction.Wait;
            target = Normalize(target);
            if (target > 0)
            {
                if (maxChain >= target) return ResultAction.GoalCompleted;
                return retryAvailable ? ResultAction.Retry : ResultAction.ExitAndRetry;
            }
            if (clear) return ResultAction.Completed;
            return retry ? (retryAvailable ? ResultAction.Retry : ResultAction.Defeated) : ResultAction.Defeated;
        }
    }
}
