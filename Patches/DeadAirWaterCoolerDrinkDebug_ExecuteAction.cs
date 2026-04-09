using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch(typeof(ItemActionEat), "StopHolding", new[] { typeof(ItemActionData) })]
    public static class DeadAirWaterCoolerDrinkDebug_StopHolding
    {
        static void Prefix(ItemActionData __0)
        {
            if (WaterCoolerActionHelpers.IsTargetingFullCooler(__0))
            {
                CompatLog.Out("[DeadAir][CoolerDebug] ItemActionEat.StopHolding Prefix fired");
            }
        }
    }

    [HarmonyPatch(typeof(ItemActionEat), "Completed", new[] { typeof(ItemActionData) })]
    public static class DeadAirWaterCoolerDrinkDebug_Completed
    {
        static void Prefix(ItemActionData __0)
        {
            if (WaterCoolerActionHelpers.IsTargetingFullCooler(__0))
            {
                CompatLog.Out("[DeadAir][CoolerDebug] ItemActionEat.Completed Prefix fired");
            }
        }
    }

}