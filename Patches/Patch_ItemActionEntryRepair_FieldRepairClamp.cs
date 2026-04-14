using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Inventory / in-pocket repair for supported weapons: after vanilla applies repair in the same
    /// activation call, clamp durability so at most ~10% of max is restored per action (weapon repair kit).
    /// Bench window is skipped so the weapon bench Refurbish flow is untouched.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_ItemActionEntryRepair_FieldRepairClamp
    {
        const float FieldRepairFraction = 0.10f;

        struct PendingState
        {
            internal bool Armed;
            internal float UseTimesBefore;
            internal string WeaponName;
        }

        static readonly Dictionary<object, PendingState> PendingByController =
            new Dictionary<object, PendingState>(ReferenceEqualityComparer.Instance);

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

        static void Prefix(ItemActionEntryRepair __instance)
        {
            var xui = __instance?.ItemController?.xui;
            if (DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
            {
                return;
            }

            var stack = TryGetItemStack(__instance);
            if (stack == null || stack.IsEmpty())
            {
                return;
            }

            var iv = stack.itemValue;
            var ic = iv?.ItemClass;

            if (ic == null || !DeadAirWeaponBenchHelpers.IsSupportedWeapon(ic.Name))
            {
                return;
            }

            float beforeUseTimes = GetUseTimesSafe(iv);
            if (beforeUseTimes <= 0f)
            {
                return;
            }

            var key = GetRepairContextKey(__instance);
            if (key == null)
            {
                return;
            }

            PendingByController[key] = new PendingState
            {
                Armed = true,
                UseTimesBefore = beforeUseTimes,
                WeaponName = ic.Name
            };
        }

        /// <summary>
        /// Runs immediately after vanilla <c>OnActivated</c>/<c>Execute</c>. Repair is applied in this call on current builds.
        /// </summary>
        static void Postfix(ItemActionEntryRepair __instance)
        {
            TryApplyFieldClampAfterVanilla(__instance, "ImmediatePostfix");
        }

        /// <summary>
        /// Optional: timer/callback path if the game defers applying durability (Prepare fails if method missing).
        /// </summary>
        [HarmonyPatch]
        static class Patch_ItemActionEntryRepair_FieldRepairCompletionFallback
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var seen = new HashSet<MethodBase>();

                foreach (var mi in typeof(ItemActionEntryRepair).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (mi == null || mi.IsAbstract)
                    {
                        continue;
                    }

                    var n = mi.Name ?? string.Empty;

                    // Cast a wider net for timer/completion-style handlers.
                    if (n.IndexOf("TimeInterval", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Elapsed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Completed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Finish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Finished", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Timer", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (seen.Add(mi))
                        {
                            yield return mi;
                        }
                    }
                }
            }

            static void Postfix(ItemActionEntryRepair __instance, MethodBase __originalMethod)
            {
                try
                {
                    var key = GetRepairContextKey(__instance);
                    if (key == null)
                    {
                        return;
                    }

                    if (!PendingByController.TryGetValue(key, out var state) || !state.Armed)
                    {
                        return;
                    }

                    CompatLog.Out($"[DeadAirFieldRepair] Completion fallback hit via {__originalMethod.Name}");

                    TryApplyFieldClampAfterVanilla(__instance, __originalMethod.Name);
                }
                catch (Exception ex)
                {
                    CompatLog.Out($"[DeadAirFieldRepair] Completion fallback exception in {__originalMethod?.Name ?? "<null>"}: {ex.Message}");
                }
            }
        }
        static void TryApplyFieldClampAfterVanilla(ItemActionEntryRepair __instance, string source = "Postfix")
        {
            var key = GetRepairContextKey(__instance);
            if (key == null)
            {
                CompatLog.Out($"[DeadAirFieldRepair] {source}: no repair context key");
                return;
            }

            if (!PendingByController.TryGetValue(key, out var state) || !state.Armed)
            {
                CompatLog.Out($"[DeadAirFieldRepair] {source}: no armed pending state");
                return;
            }

            var xui = __instance?.ItemController?.xui;
            if (DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
            {
                PendingByController.Remove(key);
                return;
            }

            var stack = TryGetItemStack(__instance);
            if (stack == null || stack.IsEmpty())
            {
                PendingByController.Remove(key);
                return;
            }

            var iv = stack.itemValue;
            if (iv == null || iv.ItemClass == null ||
                !string.Equals(iv.ItemClass.Name, state.WeaponName, StringComparison.OrdinalIgnoreCase))
            {
                PendingByController.Remove(key);
                return;
            }

            float afterVanilla = GetUseTimesSafe(iv);

            // Repair not applied in this call yet — keep pending for a later Postfix (e.g. timer callback).
            if (afterVanilla >= state.UseTimesBefore - 0.01f)
            {
                    CompatLog.Out($"[DeadAirFieldRepair] {source}: no durability improvement yet (before={state.UseTimesBefore}, after={afterVanilla})");

                return;
            }

            float maxUt = TryGetMaxUseTimes(iv);
            if (maxUt <= 0f)
            {
                PendingByController.Remove(key);
                return;
            }

            float fieldRepairAmount = Mathf.Max(1f, Mathf.Ceil(maxUt * FieldRepairFraction));
            float clampedTargetUseTimes = Mathf.Max(0f, state.UseTimesBefore - fieldRepairAmount);

            // Vanilla over-repaired — restore UseTimes so only ~10% of max durability is recovered per action.
            if (afterVanilla < clampedTargetUseTimes - 0.01f)
            {
                ApplyUseTimesAndPercentage(ref iv, clampedTargetUseTimes, maxUt);
                TryWriteBackPatchedItem(__instance, iv, state.UseTimesBefore);
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
            }
            CompatLog.Out($"[DeadAirFieldRepair] {source}: pending state cleared");
            PendingByController.Remove(key);
        }

        static object GetRepairContextKey(ItemActionEntryRepair __instance)
        {
            try
            {
                return __instance?.ItemController ?? (object)__instance;
            }
            catch
            {
                return __instance;
            }
        }

        static bool TryWriteBackPatchedItem(ItemActionEntryRepair __instance, ItemValue patchedValue, float originalUseTimes)
        {
            try
            {
                if (__instance?.ItemController is XUiC_EquipmentStack equipmentStack)
                {
                    var liveStack = equipmentStack.ItemStack;
                    if (liveStack != null && !liveStack.IsEmpty())
                    {
                        liveStack.itemValue = patchedValue;
                        equipmentStack.ItemStack = liveStack;
                        return true;
                    }
                }

                if (__instance?.ItemController is XUiC_ItemStack itemControllerStack)
                {
                    var liveStack = itemControllerStack.ItemStack;
                    if (liveStack != null && !liveStack.IsEmpty())
                    {
                        liveStack.itemValue = patchedValue;
                        itemControllerStack.ItemStack = liveStack;
                        return true;
                    }
                }

                var player = __instance?.ItemController?.xui?.playerUI?.entityPlayer as EntityPlayerLocal;
                if (player?.inventory == null)
                {
                    return false;
                }

                var targetClassName = patchedValue?.ItemClass?.Name;
                if (string.IsNullOrEmpty(targetClassName))
                {
                    return false;
                }

                var inv = player.inventory;
                var invType = inv.GetType();

                var getItem = invType.GetMethod("GetItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null)
                           ?? invType.GetMethod("GetItemInSlot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);

                var setItem = invType.GetMethod("SetItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(ItemStack) }, null);

                int slotCount = 0;
                var slotCountProp = invType.GetProperty("SlotCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (slotCountProp != null)
                {
                    try
                    {
                        slotCount = Convert.ToInt32(slotCountProp.GetValue(inv));
                    }
                    catch
                    {
                    }
                }

                if (slotCount <= 0)
                {
                    var getSlotCount = invType.GetMethod("GetSlotCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (getSlotCount != null)
                    {
                        try
                        {
                            slotCount = Convert.ToInt32(getSlotCount.Invoke(inv, null));
                        }
                        catch
                        {
                        }
                    }
                }

                if (getItem == null || setItem == null || slotCount <= 0)
                {
                    return false;
                }

                for (int i = 0; i < slotCount; i++)
                {
                    try
                    {
                        var slotStack = getItem.Invoke(inv, new object[] { i }) as ItemStack;
                        if (slotStack == null || slotStack.IsEmpty() || slotStack.itemValue?.ItemClass == null)
                        {
                            continue;
                        }

                        if (!string.Equals(slotStack.itemValue.ItemClass.Name, targetClassName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        float slotUseTimes = GetUseTimesSafe(slotStack.itemValue);

                        if (Math.Abs(slotUseTimes - originalUseTimes) > 0.01f)
                        {
                            continue;
                        }

                        slotStack.itemValue = patchedValue;
                        setItem.Invoke(inv, new object[] { i, slotStack });

                        try
                        {
                            inv.CallOnToolbeltChangedInternal();
                        }
                        catch
                        {
                        }

                        return true;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        static ItemStack TryGetItemStack(ItemActionEntryRepair __instance)
        {
            if (__instance == null)
            {
                return null;
            }

            var direct = TryGetItemStackFromObject(__instance);
            if (direct != null && !direct.IsEmpty())
            {
                return direct;
            }

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

        sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
