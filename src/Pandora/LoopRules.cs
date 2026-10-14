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
        public bool Enabled; public string RunId = "", Mode = "Currency"; public int StageId, StageGroup; public long Expires;
        public bool Valid(long now) { return !string.IsNullOrEmpty(RunId) && (Mode == "Currency" || Mode == "Challenge") && Expires > now && Expires - now <= TimeSpan.FromSeconds(15).Ticks; }
    }
    public static class LoopRules
    {
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
    }
}
