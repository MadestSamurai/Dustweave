using System;
using System.Linq;
using UnityEngine;
using TMPro;
using gamfs.PackMan;

namespace Dustweave.Mansion.Runtime
{
    public sealed partial class Engine
    {
        object Objective()
        {
            var hud = manager == null ? null : Typed<MansionRunawayHUD>(manager);
            string stage = "", candy = "", score = "";
            float? seconds = null;
            if (hud != null)
            {
                stage = ((TMP_Text)Field(hud, "_textStageCount")).text;
                candy = ((TMP_Text)Field(hud, "_textCoinCount")).text;
                score = ((TMP_Text)Field(hud, "_textTotalScore")).text;
                var timer = Field(hud, "_timerItem") as Component;
                if (timer != null && timer.gameObject.activeInHierarchy)
                {
                    int minutes, sec, millis;
                    if (int.TryParse(((TMP_Text)Field(timer, "_textMinute")).text, out minutes) && int.TryParse(((TMP_Text)Field(timer, "_textSecond")).text, out sec) && int.TryParse(((TMP_Text)Field(timer, "_textMilliSecond")).text, out millis))
                        seconds = minutes * 60 + sec + millis * .01f;
                }
            }

            if (manager != null && Bound(manager, "mode") != null)
                stage = Convert.ToString(Bound(Bound(manager, "mode"), "stage"));
            if (ruler != null && manager != null && Bound(manager, "mode") != null && Convert.ToString(Bound(Bound(manager, "mode"), "modeType")) == "Challenge")
                seconds = Convert.ToSingle(ruler.GetType().GetMethod(Bindings.Names["timeRemaining"], All).Invoke(ruler, null));
            int ordinary = OrdinaryCoins().Count(c => c != null && !Convert.ToBoolean(Bound(c, "coinCollected")));
            var door = ruler == null ? null : Typed<PackManObjectExit>(ruler);
            bool open = door != null && Convert.ToBoolean(Bound(door, "exitActive"));
            int chain = ruler == null ? 0 : Convert.ToInt32(Bound(ruler, "chain"));
            float chainSeconds = ruler == null ? 0 : Convert.ToSingle(Bound(ruler, "chainRemaining"));
            var record = manager == null ? null : Bound(manager, "scoreRecord");
            int maxChain = record == null ? 0 : Convert.ToInt32(record.GetType().GetMethod(Bindings.Names["maxChain"], All).Invoke(record, null));
            if (record != null)
                score = Convert.ToString(record.GetType().GetMethod(Bindings.Names["score"], All).Invoke(record, null));
            return new
            {
                Chain = chain,
                MaxChain = maxChain,
                ChainSeconds = chainSeconds,
                Stage = stage,
                MaxStage = manager == null || Bound(manager, "mode") == null ? 0 : Convert.ToInt32(Bound(Bound(manager, "mode"), "maxStage")),
                Candy = candy,
                Score = score,
                Seconds = seconds,
                OrdinaryRemaining = ordinary,
                ExitOpen = open,
                Goal = open ? "exit" : ordinary > 0 ? "candy" : "transition"
            };
        }
    }
}
