using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch(typeof(ItemActionEat), "consume", new[] { typeof(ItemActionData) })]
    public static class DeadAirWaterCoolerDrinkPatch
    {
        private const string CoolerCleanDrinkBuff = "buffDeadAirCoolerCleanDrink";

        static void Prefix(ItemActionData __0)
        {
            if (!WaterCoolerActionHelpers.TryGetPlayerAndWorld(__0, out EntityPlayerLocal player, out _))
            {
                return;
            }

            bool isCoolerDrink = WaterCoolerActionHelpers.IsTargetingFullCooler(__0);
            if (!isCoolerDrink)
            {
                return;
            }

            WaterCoolerActionHelpers.RememberDrinkCoolerTarget(__0);

            player.Buffs.AddBuff(CoolerCleanDrinkBuff);
        }

        static void Postfix(ItemActionData __0)
        {
            if (!WaterCoolerActionHelpers.TryGetPlayerAndWorld(__0, out EntityPlayerLocal player, out World world))
            {
                return;
            }

            bool hadRecentCoolerTarget = WaterCoolerActionHelpers.HasRecentDrinkCoolerTarget(player);

            player.Buffs.RemoveBuff(CoolerCleanDrinkBuff);

            if (hadRecentCoolerTarget)
            {
                bool swapped = WaterCoolerActionHelpers.TrySwapRememberedDrinkCooler(player, world);
            }
        }
    }
}