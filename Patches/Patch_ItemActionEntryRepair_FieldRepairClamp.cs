using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Firearms in player inventory: only <c>resourceWeaponRepairKit</c> applies (see XML + block patch);
    /// each use restores at most <see cref="FieldRepairFraction"/> of max durability.
    /// Not applied at the DeadAir weapon bench (full refurb uses the bench button).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_ItemActionEntryRepair_FieldRepairClamp
    {
        const float FieldRepairFraction = 0.10f;
        struct ClampState
        {
            internal bool Active;
            internal float UseTimesBefore;
            internal int WeaponKitsBefore;
        }
        static bool Prepare()
        {
            return TargetMethod() != null;
        }
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ItemActionEntryRepair), "OnActivated")
                ?? AccessTools.Method(typeof(ItemActionEntryRepair), "Execute");
        }
        static void Prefix(ItemActionEntryRepair __instance, ref ClampState __state)
        {
            __state = default;
            var xui = __instance?.ItemController?.xui;
            if (DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
            {
                return;
            }
            var stack = Patch_ItemActionEntryRepair_BlockAtBench.TryGetItemStack(__instance);
            if (stack == null || stack.IsEmpty())
            {
                return;
            }
            var ic = stack.itemValue.ItemClass;
            if (ic == null || !DeadAirGameApiCompat.ItemClassHasAnyRepairTool(ic))
            {
                return;
            }
            if (!DeadAirWeaponBenchHelpers.IsFirearmSupportedWeapon(ic.Name))
            {
                return;
            }
            if (!(xui?.playerUI?.entityPlayer is EntityPlayerLocal player))
            {
                return;
            }
            __state.Active = true;
            __state.UseTimesBefore = stack.itemValue.UseTimes;
            __state.WeaponKitsBefore = DeadAirWeaponRepairActions.CountPlayerItem(player, DeadAirWeaponRepairState.RepairKitName);
        }
        static void Postfix(ItemActionEntryRepair __instance, ref ClampState __state)
        {
            if (!__state.Active)
            {
                return;
            }
            var xui = __instance?.ItemController?.xui;
            if (DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
            {
                return;
            }
            if (!(xui?.playerUI?.entityPlayer is EntityPlayerLocal player))
            {
                return;
            }
            var stack = Patch_ItemActionEntryRepair_BlockAtBench.TryGetItemStack(__instance);
            if (stack == null || stack.IsEmpty())
            {
                return;
            }
            int kitsAfter = DeadAirWeaponRepairActions.CountPlayerItem(player, DeadAirWeaponRepairState.RepairKitName);
            if (kitsAfter >= __state.WeaponKitsBefore)
            {
                return;
            }
            var iv = stack.itemValue;
            float maxUt = TryGetMaxUseTimes(iv);
            if (maxUt <= 0f)
            {
                return;
            }
            float repairBudget = Mathf.Max(1f, Mathf.Ceil(maxUt * FieldRepairFraction));
            float target = Mathf.Max(0f, __state.UseTimesBefore - repairBudget);
            ApplyUseTimesAndPercentage(ref iv, target, maxUt);
            stack.itemValue = iv;
            try
            {
                player.inventory?.CallOnToolbeltChangedInternal();
            }
            catch
            {
            }
        }
        static float TryGetMaxUseTimes(ItemValue iv)
        {
            if (iv == null)
            {
                return 0f;
            }
            try
            {
                return iv.MaxUseTimes;
            }
            catch
            {
                try
                {
                    var p = typeof(ItemValue).GetProperty("MaxUseTimes", BindingFlags.Public | BindingFlags.Instance);
                    if (p != null)
                    {
                        var v = p.GetValue(iv);
                        if (v is float f)
                        {
                            return f;
                        }
                        if (v is int i)
                        {
                            return i;
                        }
                        return Convert.ToSingle(v);
                    }
                }
                catch
                {
                }
                return 0f;
            }
        }
        static void ApplyUseTimesAndPercentage(ref ItemValue iv, float newUseTimes, float maxUseTimes)
        {
            iv.UseTimes = newUseTimes;
            try
            {
                var pct = typeof(ItemValue).GetProperty("Percentage", BindingFlags.Public | BindingFlags.Instance);
                if (pct != null && pct.CanWrite && maxUseTimes > 0f)
                {
                    pct.SetValue(iv, Mathf.Clamp01(1f - newUseTimes / maxUseTimes), null);
                }
            }
            catch
            {
            }
        }
    }
}
