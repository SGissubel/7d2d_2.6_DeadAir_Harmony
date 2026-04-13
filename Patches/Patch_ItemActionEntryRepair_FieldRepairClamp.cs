using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Normal in-pocket / inventory repair for supported weapons.
    ///
    /// Rules:
    /// - applies only outside the custom weapon bench
    /// - consume only the repair kit through the normal vanilla repair path
    /// - do NOT consume weapon parts here
    /// - clamp repair effectiveness to 10% of max durability
    ///
    /// This intentionally mirrors the older working split:
    /// supported weapon + normal repair path + not bench = partial repair only
    /// </summary>
    [HarmonyPatch]
    public static class Patch_ItemActionEntryRepair_FieldRepairClamp
    {
        const float FieldRepairFraction = 0.10f;

        struct ClampState
        {
            internal bool Active;
            internal float UseTimesBefore;
            internal ItemStack StackRef;
            internal string WeaponName;
        }

        static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();

            foreach (var name in new[] { "OnActivated", "Execute", "ExecuteAction" })
            {
                var mi = AccessTools.Method(typeof(ItemActionEntryRepair), name);
                if (mi != null && seen.Add(mi))
                {
                    yield return mi;
                }
            }
        }

        static void Prefix(ItemActionEntryRepair __instance, ref ClampState __state)
        {
            __state = default;

            CompatLog.Out("[DeadAirFieldRepair] Prefix hit");

            var xui = __instance?.ItemController?.xui;
            if (DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
            {
                CompatLog.Out("[DeadAirFieldRepair] Skipping because weapon bench is open");
                return;
            }

            var stack = TryGetItemStack(__instance);
            if (stack == null || stack.IsEmpty())
            {
                CompatLog.Out("[DeadAirFieldRepair] No item stack found");
                return;
            }

            var ic = stack.itemValue?.ItemClass;
            CompatLog.Out($"[DeadAirFieldRepair] Item={ic?.Name ?? "<null>"} useTimesBefore={GetUseTimesSafe(stack.itemValue)}");

            if (ic == null)
            {
                return;
            }

            // Important:
            // do NOT gate on DeadAirGameApiCompat.ItemClassHasAnyRepairTool(ic)
            // that compat gate was what prevented activation in the recent logs.
            if (!DeadAirWeaponBenchHelpers.IsSupportedWeapon(ic.Name))
            {
                CompatLog.Out("[DeadAirFieldRepair] Item is not a supported DeadAir weapon");
                return;
            }

            __state.Active = true;
            __state.UseTimesBefore = GetUseTimesSafe(stack.itemValue);
            __state.StackRef = stack;
            __state.WeaponName = ic.Name;

            CompatLog.Out($"[DeadAirFieldRepair] Armed for supported weapon {ic.Name}");
        }

        static void Postfix(ItemActionEntryRepair __instance, ref ClampState __state)
        {
            CompatLog.Out($"[DeadAirFieldRepair] Postfix hit active={__state.Active}");

            if (!__state.Active)
            {
                return;
            }

            var xui = __instance?.ItemController?.xui;
            if (DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
            {
                CompatLog.Out("[DeadAirFieldRepair] Postfix skip because weapon bench is open");
                return;
            }

            var stack = __state.StackRef ?? TryGetItemStack(__instance);
            if (stack == null || stack.IsEmpty())
            {
                CompatLog.Out("[DeadAirFieldRepair] No item stack found in Postfix");
                return;
            }

            var iv = stack.itemValue;
            if (iv == null)
            {
                CompatLog.Out("[DeadAirFieldRepair] ItemValue null in Postfix");
                return;
            }

            float afterVanilla = GetUseTimesSafe(iv);
            CompatLog.Out($"[DeadAirFieldRepair] useTimes before={__state.UseTimesBefore} afterVanilla={afterVanilla}");

            // If vanilla did not improve the weapon, do nothing.
            if (afterVanilla >= __state.UseTimesBefore)
            {
                CompatLog.Out("[DeadAirFieldRepair] Vanilla did not improve durability; no clamp needed");
                return;
            }

            float maxUt = TryGetMaxUseTimes(iv);
            if (maxUt <= 0f)
            {
                CompatLog.Out("[DeadAirFieldRepair] Invalid MaxUseTimes");
                return;
            }

            // Older working logic was "cap field repair to X% of total durability".
            // In useTimes terms, lower is better, so clamp the final useTimes to:
            // original damage minus at most 10% of max durability.
            float fieldRepairAmount = Mathf.Max(1f, Mathf.Ceil(maxUt * FieldRepairFraction));
            float clampedTargetUseTimes = Mathf.Max(0f, __state.UseTimesBefore - fieldRepairAmount);

            CompatLog.Out($"[DeadAirFieldRepair] maxUseTimes={maxUt} fieldRepairAmount={fieldRepairAmount} clampedTargetUseTimes={clampedTargetUseTimes}");

            // Only clamp if vanilla repaired MORE than our allowed field-repair cap.
            // Example:
            // - before = 267 damage
            // - vanilla after = 0 (full repair)
            // - clamp target = 240
            // Then we force it back to 240 damage remaining.
            if (afterVanilla < clampedTargetUseTimes)
            {
                ApplyUseTimesAndPercentage(ref iv, clampedTargetUseTimes, maxUt);

                bool wroteBack = TryWriteBackClampedItem(__instance, iv);

                // Fallback in case the controller write-back path is not available
                stack.itemValue = iv;

                try
                {
                    if (xui?.playerUI?.entityPlayer is EntityPlayerLocal player)
                    {
                        player.inventory?.CallOnToolbeltChangedInternal();
                    }
                }
                catch
                {
                }

                CompatLog.Out($"[DeadAirFieldRepair] Clamp applied. wroteBack={wroteBack} finalUseTimes={GetUseTimesSafe(iv)}");
            }
            else
            {
                CompatLog.Out("[DeadAirFieldRepair] Vanilla repair was already within field cap");
            }
        }

        static bool TryWriteBackClampedItem(ItemActionEntryRepair __instance, ItemValue clampedValue)
        {
            try
            {
                if (__instance?.ItemController is XUiC_EquipmentStack equipmentStack)
                {
                    var liveStack = equipmentStack.ItemStack;
                    if (liveStack != null && !liveStack.IsEmpty())
                    {
                        liveStack.itemValue = clampedValue;
                        equipmentStack.ItemStack = liveStack;
                        return true;
                    }
                }

                if (__instance?.ItemController is XUiC_ItemStack itemControllerStack)
                {
                    var liveStack = itemControllerStack.ItemStack;
                    if (liveStack != null && !liveStack.IsEmpty())
                    {
                        liveStack.itemValue = clampedValue;
                        itemControllerStack.ItemStack = liveStack;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirFieldRepair] TryWriteBackClampedItem failed: {ex.Message}");
            }

            return false;
        }

        static ItemStack TryGetItemStack(ItemActionEntryRepair __instance)
        {
            if (__instance == null)
            {
                return null;
            }

            // 1) Probe the repair action object itself
            var direct = TryGetItemStackFromObject(__instance);
            if (direct != null && !direct.IsEmpty())
            {
                return direct;
            }

            // 2) Probe the ItemController
            object itemController = null;
            try
            {
                itemController = __instance.ItemController;
            }
            catch
            {
            }

            var fromController = TryGetItemStackFromObject(itemController);
            if (fromController != null && !fromController.IsEmpty())
            {
                return fromController;
            }

            // 3) Probe common nested members on both action + controller
            foreach (var memberName in new[]
            {
                "itemController",
                "ItemController",
                "itemActionData",
                "ItemActionData",
                "itemStackController",
                "ItemStackController",
                "itemData",
                "ItemData",
                "focusedItemStack",
                "FocusedItemStack",
                "itemStack",
                "ItemStack"
            })
            {
                object child = TryGetMemberValue(__instance, memberName);
                var fromChild = TryGetItemStackFromObject(child);
                if (fromChild != null && !fromChild.IsEmpty())
                {
                    return fromChild;
                }

                if (itemController != null)
                {
                    child = TryGetMemberValue(itemController, memberName);
                    fromChild = TryGetItemStackFromObject(child);
                    if (fromChild != null && !fromChild.IsEmpty())
                    {
                        return fromChild;
                    }
                }
            }

            return null;
        }

        static ItemStack TryGetItemStackFromObject(object obj)
        {
            if (obj == null)
            {
                return null;
            }

            if (obj is ItemStack directStack)
            {
                return directStack;
            }

            var tr = Traverse.Create(obj);

            foreach (var prop in new[] { "ItemStack", "itemStack", "FocusedItemStack", "m_ItemStack", "Stack", "stack" })
            {
                try
                {
                    var p = tr.Property(prop);
                    if (p.PropertyExists())
                    {
                        var v = p.GetValue();
                        if (v is ItemStack s && !s.IsEmpty())
                        {
                            return s;
                        }
                    }
                }
                catch
                {
                }
            }

            foreach (var field in new[] { "itemStack", "stack", "m_itemStack", "focusedItemStack", "m_ItemStack", "ItemStack" })
            {
                try
                {
                    var f = tr.Field(field);
                    if (f.FieldExists())
                    {
                        var v = f.GetValue();
                        if (v is ItemStack s && !s.IsEmpty())
                        {
                            return s;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        static object TryGetMemberValue(object obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var tr = Traverse.Create(obj);

            try
            {
                var p = tr.Property(name);
                if (p.PropertyExists())
                {
                    return p.GetValue();
                }
            }
            catch
            {
            }

            try
            {
                var f = tr.Field(name);
                if (f.FieldExists())
                {
                    return f.GetValue();
                }
            }
            catch
            {
            }

            return null;
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

        static float GetUseTimesSafe(ItemValue iv)
        {
            try
            {
                return iv?.UseTimes ?? -1f;
            }
            catch
            {
                return -1f;
            }
        }
    }
}