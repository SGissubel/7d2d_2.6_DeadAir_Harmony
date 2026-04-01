using System;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    public static class DeadAirDebugQuestTier
    {
        // 0 = disabled, otherwise forces quest tier logic
        public static int ForcedQuestTier = 0;

        public static bool Enabled => ForcedQuestTier > 0;

        public static int GetEffectiveTier(int actualTier)
        {
            if (ForcedQuestTier > 0)
                return ForcedQuestTier;

            return actualTier;
        }

        public static void SetForcedTier(int tier)
        {
            ForcedQuestTier = Math.Max(0, tier);
            CompatLog.Out($"[DeadAir] Debug forced quest tier set to {ForcedQuestTier}");
        }

        public static void Clear()
        {
            ForcedQuestTier = 0;
            CompatLog.Out("[DeadAir] Debug forced quest tier cleared");
        }
    }
}