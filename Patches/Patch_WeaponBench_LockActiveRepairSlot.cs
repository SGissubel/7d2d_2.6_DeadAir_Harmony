using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Blocks interaction with slot 0 on the DeadAir weapon repair bench
    /// while that bench has an active queued refurbish.
    /// Very narrow: only slot 0 on <c>windowDeadAirWeaponBenchInputs</c>, only this bench, only while pending.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_WeaponBench_LockActiveRepairSlot_CanSwap
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var seen = new HashSet<MethodBase>();

            foreach (var type in new[] { typeof(XUiC_ItemStack), typeof(XUiC_RequiredItemStack) })
            {
                foreach (var mi in type.GetMethods(flags))
                {
                    if (!string.Equals(mi.Name, "CanSwap", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (mi.ReturnType != typeof(bool))
                    {
                        continue;
                    }

                    if (mi.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    if (seen.Add(mi))
                    {
                        yield return mi;
                    }
                }
            }
        }

        static bool Prepare()
        {
            foreach (var _ in TargetMethods())
            {
                return true;
            }

            return false;
        }

        static bool Prefix(XUiC_ItemStack __instance, ref bool __result)
        {
            try
            {
                if (!WeaponBenchRepairGuards.IsWeaponSlotLockedDuringRepair(__instance))
                {
                    return true;
                }

                __result = false;

                var player = __instance?.xui?.playerUI?.entityPlayer as EntityPlayerLocal
                          ?? GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;

                WeaponBenchRepairGuards.ShowSlotLockedTooltip(player);
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] LockActiveRepairSlot.CanSwap: {ex.Message}");
                return true;
            }
        }
    }
}
