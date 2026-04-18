using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    public static class DeadAirFieldRepairPending
    {
        public sealed class PendingRepair
        {
            public int PlayerEntityId;
            public string WeaponName;
            public float UseTimesBefore;
            public float StartedAtRealtime;
        }

        static readonly Dictionary<int, PendingRepair> PendingByPlayerId =
            new Dictionary<int, PendingRepair>();

        public static void Arm(EntityPlayerLocal player, string weaponName, float useTimesBefore)
        {
            if (player == null || string.IsNullOrEmpty(weaponName) || useTimesBefore <= 0f)
            {
                return;
            }

            PendingByPlayerId[player.entityId] = new PendingRepair
            {
                PlayerEntityId = player.entityId,
                WeaponName = weaponName,
                UseTimesBefore = useTimesBefore,
                StartedAtRealtime = Time.realtimeSinceStartup
            };

            CompatLog.Out($"[DeadAirFieldRepair] Armed player={player.entityId} weapon={weaponName} before={useTimesBefore}");
        }

        public static bool TryGet(EntityPlayerLocal player, out PendingRepair pending, float maxAgeSeconds = 15f)
        {
            pending = null;

            if (player == null)
            {
                return false;
            }

            if (!PendingByPlayerId.TryGetValue(player.entityId, out pending) || pending == null)
            {
                return false;
            }

            if (Time.realtimeSinceStartup - pending.StartedAtRealtime > maxAgeSeconds)
            {
                PendingByPlayerId.Remove(player.entityId);
                pending = null;
                return false;
            }

            return true;
        }

        public static void Clear(EntityPlayerLocal player)
        {
            if (player == null)
            {
                return;
            }

            PendingByPlayerId.Remove(player.entityId);
        }
    }

    [HarmonyPatch]
    public static class Patch_ItemActionEntryRepair_ArmFieldRepair
    {
        static Patch_ItemActionEntryRepair_ArmFieldRepair()
        {
            CompatLog.Out("[DeadAirFieldRepair] Arm patch class loaded");
        }

        static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();

            foreach (var name in new[] { "OnActivated", "Execute", "ExecuteAction" })
            {
                var mi = AccessTools.Method(typeof(ItemActionEntryRepair), name);
                CompatLog.Out($"[DeadAirFieldRepair] TargetMethods lookup: {name} => {(mi == null ? "<null>" : mi.ToString())}");

                if (mi != null && seen.Add(mi))
                {
                    yield return mi;
                }
            }
        }

    static void Prefix(ItemActionEntryRepair __instance)
    {
        CompatLog.Out("[DeadAirFieldRepair] Arm Prefix runtime hit");

        try
        {
            var xui = __instance?.ItemController?.xui;
            CompatLog.Out($"[DeadAirFieldRepair] Arm: xui={(xui == null ? "null" : "ok")}");

            if (DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
            {
                CompatLog.Out("[DeadAirFieldRepair] Arm: skipped because bench is open");
                return;
            }

            var player = xui?.playerUI?.entityPlayer as EntityPlayerLocal;
            CompatLog.Out($"[DeadAirFieldRepair] Arm: player={(player == null ? "null" : player.entityId.ToString())}");

            if (player == null)
            {
                return;
            }

            if (!TryResolveRepairContext(__instance, out var weaponName, out var useTimesBefore))
            {
                CompatLog.Out("[DeadAirFieldRepair] Arm: could not resolve repair context");
                return;
            }

            if (!DeadAirWeaponBenchHelpers.IsSupportedWeapon(weaponName))
            {
                CompatLog.Out($"[DeadAirFieldRepair] Arm: unsupported weapon {weaponName}");
                return;
            }

            DeadAirFieldRepairPending.Arm(player, weaponName, useTimesBefore);
        }
        catch (Exception ex)
        {
            CompatLog.Out($"[DeadAirFieldRepair] Arm prefix exception: {ex.Message}");
        }
    }

    static bool TryResolveRepairContext(ItemActionEntryRepair action, out string weaponName, out float useTimesBefore)
    {
        weaponName = null;
        useTimesBefore = -1f;

        if (action == null)
        {
            return false;
        }

        // 1) Try ItemStack-like objects first
        foreach (var obj in new object[] { action, action.ItemController })
        {
            var stack = TryGetItemStackFromObject(obj);
            if (stack != null && !stack.IsEmpty() && stack.itemValue?.ItemClass != null)
            {
                weaponName = stack.itemValue.ItemClass.Name;
                useTimesBefore = stack.itemValue.UseTimes;
                CompatLog.Out($"[DeadAirFieldRepair] Arm: resolved from ItemStack weapon={weaponName} before={useTimesBefore}");
                return useTimesBefore > 0f;
            }
        }

        // 2) Fallback to ItemValue-like objects
        foreach (var obj in new object[] { action, action.ItemController })
        {
            var iv = TryGetItemValueFromObject(obj);
            if (iv != null && iv.ItemClass != null)
            {
                weaponName = iv.ItemClass.Name;
                useTimesBefore = iv.UseTimes;
                CompatLog.Out($"[DeadAirFieldRepair] Arm: resolved from ItemValue weapon={weaponName} before={useTimesBefore}");
                return useTimesBefore > 0f;
            }
        }

        return false;
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

        foreach (var name in new[] { "ItemStack", "itemStack", "FocusedItemStack", "m_ItemStack", "stack", "Stack" })
        {
            try
            {
                var p = tr.Property(name);
                if (p.PropertyExists())
                {
                    var v = p.GetValue();
                    if (v is ItemStack s)
                    {
                        return s;
                    }
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
                    var v = f.GetValue();
                    if (v is ItemStack s)
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

    static ItemValue TryGetItemValueFromObject(object obj)
    {
        if (obj == null)
        {
            return null;
        }

        if (obj is ItemValue directValue)
        {
            return directValue;
        }

        var tr = Traverse.Create(obj);

        foreach (var name in new[] { "ItemValue", "itemValue", "FocusedItemValue", "m_ItemValue" })
        {
            try
            {
                var p = tr.Property(name);
                if (p.PropertyExists())
                {
                    var v = p.GetValue();
                    if (v is ItemValue iv)
                    {
                        return iv;
                    }
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
                    var v = f.GetValue();
                    if (v is ItemValue iv)
                    {
                        return iv;
                    }
                }
            }
            catch
            {
            }
        }

        return null;
    }
    }

    [HarmonyPatch(typeof(Inventory), "SetItem", new[] { typeof(int), typeof(ItemValue), typeof(int), typeof(bool) })]
    public static class Patch_Inventory_SetItem_ItemValue_ClampFieldRepair
    {
        const float FieldRepairFraction = 0.10f;

        static Patch_Inventory_SetItem_ItemValue_ClampFieldRepair()
        {
            CompatLog.Out("[DeadAirFieldRepair] Inventory.SetItem(ItemValue) clamp patch class loaded");
        }

        static void Prefix(Inventory __instance, int _idx, ref ItemValue _itemValue, int _count, bool _notifyListeners)
        {
            try
            {
                if (_itemValue == null || _itemValue.ItemClass == null)
                {
                    return;
                }

                EntityPlayerLocal player = TryResolveInventoryOwner(__instance);
                if (player == null)
                {
                    return;
                }

                if (!DeadAirFieldRepairPending.TryGet(player, out var pending) || pending == null)
                {
                    return;
                }

                string itemName = _itemValue.ItemClass.Name;
                if (!string.Equals(itemName, pending.WeaponName, StringComparison.OrdinalIgnoreCase))
                {
                    CompatLog.Out($"[DeadAirFieldRepair] SetItem(ItemValue): weapon mismatch pending={pending.WeaponName} actual={itemName}");
                    return;
                }

                float before = pending.UseTimesBefore;
                float afterVanilla = _itemValue.UseTimes;

                if (afterVanilla >= before - 0.01f)
                {
                    CompatLog.Out("[DeadAirFieldRepair] SetItem(ItemValue): no durability improvement");
                    return;
                }

                float maxUseTimes = TryGetMaxUseTimes(_itemValue);
                if (maxUseTimes <= 0f)
                {
                    DeadAirFieldRepairPending.Clear(player);
                    return;
                }

                float allowedRepair = Mathf.Max(1f, Mathf.Ceil(maxUseTimes * 0.10f));
                float clampedTargetUseTimes = Mathf.Max(0f, before - allowedRepair);

                CompatLog.Out($"[DeadAirFieldRepair] SetItem(ItemValue) slot={_idx} item={itemName} before={before} afterVanilla={afterVanilla} clampTarget={clampedTargetUseTimes}");

                if (afterVanilla < clampedTargetUseTimes - 0.01f)
                {
                    _itemValue.UseTimes = clampedTargetUseTimes;

                    try
                    {
                        var pct = typeof(ItemValue).GetProperty("Percentage", BindingFlags.Public | BindingFlags.Instance);
                        if (pct != null && pct.CanWrite && maxUseTimes > 0f)
                        {
                            pct.SetValue(_itemValue, Mathf.Clamp01(1f - clampedTargetUseTimes / maxUseTimes), null);
                        }
                    }
                    catch
                    {
                    }

                    CompatLog.Out($"[DeadAirFieldRepair] Clamp applied through Inventory.SetItem(ItemValue). finalUseTimes={_itemValue.UseTimes}");
                }

                DeadAirFieldRepairPending.Clear(player);
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirFieldRepair] SetItem(ItemValue) clamp exception: {ex.Message}");
            }
        }
        static EntityPlayerLocal TryResolveInventoryOwner(Inventory inv)
        {
            if (inv == null)
            {
                return null;
            }

            try
            {
                var world = GameManager.Instance?.World;
                if (world == null)
                {
                    return null;
                }

                foreach (var player in world.Players.list)
                {
                    if (player is EntityPlayerLocal local && ReferenceEquals(local.inventory, inv))
                    {
                        return local;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        static ItemStack TryGetInventorySlotStack(Inventory inv, int idx)
        {
            if (inv == null || idx < 0)
            {
                return null;
            }

            try
            {
                var t = inv.GetType();

                var mi = t.GetMethod("GetItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null)
                      ?? t.GetMethod("GetItemInSlot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);

                if (mi == null)
                {
                    return null;
                }

                var v = mi.Invoke(inv, new object[] { idx });
                if (v is ItemStack stack)
                {
                    return stack;
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
    }

}