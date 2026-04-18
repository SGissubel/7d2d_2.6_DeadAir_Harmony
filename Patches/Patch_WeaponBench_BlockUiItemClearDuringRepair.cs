using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Blocks UI code paths that clear the weapon slot without consulting <see cref="XUiC_ItemStack.CanSwap"/> —
    /// e.g. <c>ItemStack</c> property setters and <c>SetItem</c> overloads.
    /// </summary>
    [HarmonyPatch(typeof(XUiC_ItemStack))]
    [HarmonyPatch(nameof(XUiC_ItemStack.ItemStack), MethodType.Setter)]
    public static class Patch_WeaponBench_BlockUiItemStackSetterDuringRepair
    {
        static bool Prepare()
        {
            try
            {
                return AccessTools.Property(typeof(XUiC_ItemStack), nameof(XUiC_ItemStack.ItemStack))?.GetSetMethod(true) != null;
            }
            catch
            {
                return false;
            }
        }

        static bool Prefix(XUiC_ItemStack __instance, ItemStack value)
        {
            try
            {
                if (value != null && !value.IsEmpty())
                {
                    return true;
                }

                if (!WeaponBenchRepairGuards.IsWeaponSlotLockedDuringRepair(__instance))
                {
                    return true;
                }

                var player = __instance?.xui?.playerUI?.entityPlayer as EntityPlayerLocal;
                WeaponBenchRepairGuards.ShowSlotLockedTooltip(player);
                CompatLog.Out("[DeadAirRepair] Blocked ItemStack setter: would clear weapon slot during active refurbish.");
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] BlockUiItemStackSetter: {ex.Message}");
                return true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_WeaponBench_BlockUiSetItemItemStackDuringRepair
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();

            foreach (var t in new[] { typeof(XUiC_ItemStack), typeof(XUiC_RequiredItemStack) })
            {
                var mi = WeaponBenchRepairReflection.FindInstanceMethodInHierarchy(t, "SetItem", typeof(ItemStack));
                if (mi != null && seen.Add(mi))
                {
                    yield return mi;
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

        static bool Prefix(XUiC_ItemStack __instance, ItemStack stack)
        {
            try
            {
                if (stack != null && !stack.IsEmpty())
                {
                    return true;
                }

                if (!WeaponBenchRepairGuards.IsWeaponSlotLockedDuringRepair(__instance))
                {
                    return true;
                }

                var player = __instance?.xui?.playerUI?.entityPlayer as EntityPlayerLocal;
                WeaponBenchRepairGuards.ShowSlotLockedTooltip(player);
                CompatLog.Out("[DeadAirRepair] Blocked SetItem(ItemStack): would clear weapon slot during active refurbish.");
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] BlockUiSetItemItemStack: {ex.Message}");
                return true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_WeaponBench_BlockUiSetItemItemValueIntDuringRepair
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();

            foreach (var t in new[] { typeof(XUiC_ItemStack), typeof(XUiC_RequiredItemStack) })
            {
                var mi = WeaponBenchRepairReflection.FindInstanceMethodInHierarchy(t, "SetItem", typeof(ItemValue), typeof(int));
                if (mi != null && seen.Add(mi))
                {
                    yield return mi;
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

        static bool Prefix(XUiC_ItemStack __instance, ItemValue _itemValue, int _count)
        {
            try
            {
                if (!WeaponBenchRepairGuards.LooksLikeClearingItem(_itemValue, _count))
                {
                    return true;
                }

                if (!WeaponBenchRepairGuards.IsWeaponSlotLockedDuringRepair(__instance))
                {
                    return true;
                }

                var player = __instance?.xui?.playerUI?.entityPlayer as EntityPlayerLocal;
                WeaponBenchRepairGuards.ShowSlotLockedTooltip(player);
                CompatLog.Out("[DeadAirRepair] Blocked SetItem(ItemValue,int): would clear weapon slot during active refurbish.");
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] BlockUiSetItemItemValueInt: {ex.Message}");
                return true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_WeaponBench_BlockUiSetItemItemValueIntBoolDuringRepair
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();

            foreach (var t in new[] { typeof(XUiC_ItemStack), typeof(XUiC_RequiredItemStack) })
            {
                for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                {
                    foreach (var m in cur.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (m.Name != "SetItem")
                        {
                            continue;
                        }

                        var ps = m.GetParameters();
                        if (ps.Length != 3 || ps[0].ParameterType != typeof(ItemValue) || ps[1].ParameterType != typeof(int) || ps[2].ParameterType != typeof(bool))
                        {
                            continue;
                        }

                        if (seen.Add(m))
                        {
                            yield return m;
                        }
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

        static bool Prefix(XUiC_ItemStack __instance, ItemValue _itemValue, int _count, bool _arg2)
        {
            try
            {
                if (!WeaponBenchRepairGuards.LooksLikeClearingItem(_itemValue, _count))
                {
                    return true;
                }

                if (!WeaponBenchRepairGuards.IsWeaponSlotLockedDuringRepair(__instance))
                {
                    return true;
                }

                var player = __instance?.xui?.playerUI?.entityPlayer as EntityPlayerLocal;
                WeaponBenchRepairGuards.ShowSlotLockedTooltip(player);
                CompatLog.Out("[DeadAirRepair] Blocked SetItem(ItemValue,int,bool): would clear weapon slot during active refurbish.");
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] BlockUiSetItemItemValueIntBool: {ex.Message}");
                return true;
            }
        }
    }
}
