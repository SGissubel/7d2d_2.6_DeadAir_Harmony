using System;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// While a DeadAir weapon bench refurbish timer is running, block writes that would clear tool slot 0
    /// (covers drag / sync paths that do not go through <see cref="XUiC_ItemStack.CanSwap"/>).
    /// Completion still applies a non-empty repaired stack to slot 0, so that path is allowed.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_WeaponBench_BlockClearWeaponSlotDuringRepair
    {
        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static MethodBase TargetMethod()
        {
            return WeaponBenchRepairReflection.FindInstanceMethodInHierarchy(typeof(TileEntityWorkstation), "SetToolStacks", typeof(ItemStack[]));
        }

        static bool Prefix(TileEntityWorkstation __instance, ItemStack[] stacks)
        {
            try
            {
                if (__instance == null || stacks == null)
                {
                    return true;
                }

                ItemStack newSlot0 = stacks.Length >= 1 ? stacks[0] : null;
                if (!WeaponBenchRepairGuards.ShouldBlockClearingToolSlotZero(__instance, newSlot0))
                {
                    return true;
                }

                var player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
                WeaponBenchRepairGuards.ShowSlotLockedTooltip(player);
                CompatLog.Out("[DeadAirRepair] Blocked SetToolStacks: would clear weapon slot during active refurbish.");
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] BlockClearWeaponSlotDuringRepair: {ex.Message}");
                return true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_WeaponBench_BlockSetToolInSlot_IntDuringRepair
    {
        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static MethodBase TargetMethod()
        {
            return WeaponBenchRepairReflection.FindInstanceMethodInHierarchy(typeof(TileEntityWorkstation), "SetToolInSlot", typeof(int), typeof(ItemStack));
        }

        static bool Prefix(TileEntityWorkstation __instance, int toolSlot, ItemStack stack)
        {
            try
            {
                if (!WeaponBenchRepairGuards.ShouldBlockClearingToolSlotAtIndex(__instance, toolSlot, stack))
                {
                    return true;
                }

                var player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
                WeaponBenchRepairGuards.ShowSlotLockedTooltip(player);
                CompatLog.Out("[DeadAirRepair] Blocked SetToolInSlot(int): would clear weapon slot during active refurbish.");
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] BlockSetToolInSlot int: {ex.Message}");
                return true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_WeaponBench_BlockSetToolInSlot_ByteDuringRepair
    {
        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static MethodBase TargetMethod()
        {
            return WeaponBenchRepairReflection.FindInstanceMethodInHierarchy(typeof(TileEntityWorkstation), "SetToolInSlot", typeof(byte), typeof(ItemStack));
        }

        static bool Prefix(TileEntityWorkstation __instance, byte toolSlot, ItemStack stack)
        {
            try
            {
                if (!WeaponBenchRepairGuards.ShouldBlockClearingToolSlotAtIndex(__instance, toolSlot, stack))
                {
                    return true;
                }

                var player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
                WeaponBenchRepairGuards.ShowSlotLockedTooltip(player);
                CompatLog.Out("[DeadAirRepair] Blocked SetToolInSlot(byte): would clear weapon slot during active refurbish.");
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] BlockSetToolInSlot byte: {ex.Message}");
                return true;
            }
        }
    }
}
