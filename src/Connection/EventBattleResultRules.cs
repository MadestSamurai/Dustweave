using System;

namespace BD2Daily.Live
{
    // Shared by observation and execution: a result is ready only when its
    // actual return control is visible and native consecutive play has stopped.
    public static class EventBattleResultRules
    {
        public static string ExitField(bool eventBattle, bool continuing, Func<string, bool> active)
        {
            if (!eventBattle || continuing) return "";
            bool win = active("_winObject"), lost = active("_loseObject");
            if (win == lost) return "";
            if (lost)
            {
                if (active("_objectLoseExitButton")) return "_objectLoseExitButton";
                if (active("_goLoseSafeArea")) return "_goLoseSafeArea";
            }
            else
            {
                // SetEventBattleResult exposes GoToSafeArea for event wins.
                // WinExit is used by other result layouts and may stay hidden.
                if (active("_goWinSafeArea")) return "_goWinSafeArea";
                if (active("_objectWinExitButton")) return "_objectWinExitButton";
            }
            return "";
        }
    }
}
