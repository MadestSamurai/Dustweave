namespace Dustweave.Mansion
{
    public enum ResultAction { Wait, Completed, Defeated, Retry, ExitAndRetry, GoalCompleted }
    // Only this round's acknowledged native result confirms a goal.
    public static class RunGoal
    {
        public static int Normalize(int requested) { return requested == 80 ? 80 : 0; }
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
