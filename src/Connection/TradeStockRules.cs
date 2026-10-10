using System;

namespace BD2Daily.Live
{
    // Mirrors ShopPacket.OpenShop's common timer and ShopUI's missing-product
    // semantics. Missing data is usable only with a fresh common shop snapshot.
    public static class TradeStockRules
    {
        public static void RequireFresh(int shop, long? ownResetTicks, long? commonResetTicks, long nowTicks)
        {
            long? reset = ownResetTicks ?? commonResetTicks;
            if (!reset.HasValue || reset.Value <= nowTicks)
                throw new InvalidOperationException("Shop supply is missing or expired: " + shop);
        }

        public static int Remaining(int limit, bool unlimited, int? purchased)
        {
            if (limit < 0 || purchased.GetValueOrDefault() < 0)
                throw new InvalidOperationException("Invalid shop supply count");
            int remaining = limit - (unlimited ? 0 : purchased.GetValueOrDefault());
            if (remaining < 0) throw new InvalidOperationException("Negative remaining stock");
            return remaining;
        }
    }
}
