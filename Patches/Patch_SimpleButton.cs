using System;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class Patch_SimpleButton_DeadAirRepair
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(XUiC_SimpleButton), "Btn_OnPress");
        }

        static void Postfix(XUiC_SimpleButton __instance)
        {
            try
            {
                var id = DeadAirGameApiCompat.TryGetXUiControlName(__instance);
                if (!string.Equals(id, "btnDeadAirWeaponRepair", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                CompatLog.Out("[DeadAirRepair] Repair button hit via XUiC_SimpleButton.Btn_OnPress");
                DeadAirWeaponRepairActions.TryRepairFromUiButton(__instance);
            }
            catch (Exception ex)
            {
                CompatLog.Error($"[DeadAirRepair] Patch_SimpleButton_DeadAirRepair exception: {ex}");
            }
        }
    }
}