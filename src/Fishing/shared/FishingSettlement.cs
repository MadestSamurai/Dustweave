namespace BD2Fishing
{
    public static class FishingSettlement
    {
        public static bool Known(string type) => type == "AvatarFishingGetPopupUI" || type == "AvatarFishingLevelUpPopupUI";

        // The result close callback opens the level-up popup. Both can remain
        // visible during their animations. Only close the top known surface;
        // an unrelated confirmation must still block automation.
        public static int SelectPopup(string[] orderedBlockingTypes)
        {
            if (orderedBlockingTypes.Length == 0) return -1;
            foreach (var type in orderedBlockingTypes) if (!Known(type)) return -1;
            return 0;
        }
    }
}
