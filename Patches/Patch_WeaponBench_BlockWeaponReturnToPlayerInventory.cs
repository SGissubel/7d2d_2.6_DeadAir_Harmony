using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class Patch_WeaponBench_BlockWeaponReturnToPlayerInventory
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();

            var m1 = AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemStack) });
            if (m1 != null && seen.Add(m1))
            {
                yield return m1;
            }

            var m2 = AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemStack), typeof(int).MakeByRefType() });
            if (m2 != null && seen.Add(m2))
            {
                yield return m2;
            }
        }

        static bool Prefix(Inventory __instance, ItemStack _itemStack)
        {
            return AllowAdd(__instance, _itemStack);
        }

        internal static bool AllowAdd(Inventory inventory, ItemStack incoming)
        {
            try
            {
                if (incoming == null || incoming.IsEmpty() || incoming.itemValue?.ItemClass == null)
                {
                    return true;
                }

                var player = ResolveInventoryOwner(inventory);
                if (player?.PlayerUI?.xui == null)
                {
                    return true;
                }

                var xui = player.PlayerUI.xui;
                if (!DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
                {
                    return true;
                }

                var wg = TryGetBenchWorkstationGroup(xui);
                if (wg == null)
                {
                    return true;
                }

                var te = TryResolveBenchFromWorkstationGroup(wg);
                if (te == null)
                {
                    return true;
                }

                if (!DeadAirWeaponBenchRepairQueue.IsRepairInProgress(te))
                {
                    return true;
                }

                string incomingName = incoming.itemValue.ItemClass.Name;
                string pendingWeaponName = null;

                if (!DeadAirWeaponBenchRepairQueue.TryGetPendingWeaponNameForBench(te, out pendingWeaponName))
                {
                    DeadAirWeaponBenchRepairQueue.TryGetPendingWeaponNameForPlayer(player.entityId, out pendingWeaponName);
                }

                if (string.IsNullOrEmpty(pendingWeaponName))
                {
                    return true;
                }

                if (!string.Equals(pendingWeaponName, incomingName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                GameManager.ShowTooltip(player, Localization.Get("deadairRepairSlotLocked"));
                CompatLog.Out($"[DeadAirRepair] Blocked return of queued refurbishing weapon to player inventory: {incomingName}");
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] BlockWeaponReturnToPlayerInventory: {ex.Message}");
                return true;
            }
        }

        internal static bool ShouldBlockSetItem(Inventory inventory, ItemValue itemValue, int count, out EntityPlayerLocal player, out string itemName)
        {
            player = null;
            itemName = null;

            try
            {
                if (count <= 0 || itemValue == null || itemValue.ItemClass == null)
                {
                    return false;
                }

                player = ResolveInventoryOwner(inventory);
                if (player?.PlayerUI?.xui == null)
                {
                    return false;
                }

                var xui = player.PlayerUI.xui;
                if (!DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
                {
                    return false;
                }

                var wg = TryGetBenchWorkstationGroup(xui);
                if (wg == null)
                {
                    return false;
                }

                var te = TryResolveBenchFromWorkstationGroup(wg);
                if (te == null || !DeadAirWeaponBenchRepairQueue.IsRepairInProgress(te))
                {
                    return false;
                }

                itemName = itemValue.ItemClass.Name;
                string pendingWeaponName = null;

                if (!DeadAirWeaponBenchRepairQueue.TryGetPendingWeaponNameForBench(te, out pendingWeaponName))
                {
                    DeadAirWeaponBenchRepairQueue.TryGetPendingWeaponNameForPlayer(player.entityId, out pendingWeaponName);
                }

                if (string.IsNullOrEmpty(pendingWeaponName))
                {
                    return false;
                }

                return string.Equals(pendingWeaponName, itemName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                player = null;
                itemName = null;
                return false;
            }
        }

        internal static EntityPlayerLocal ResolveInventoryOwner(Inventory inv)
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

                foreach (var p in world.Players.list)
                {
                    if (p is EntityPlayerLocal local && ReferenceEquals(local.inventory, inv))
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

        internal static XUiC_WorkstationWindowGroup TryGetBenchWorkstationGroup(XUi xui)
        {
            if (xui == null)
            {
                return null;
            }

            try
            {
                object wgObj = xui.FindWindowGroupByName(DeadAirWeaponBenchHelpers.BenchWindowGroupName);
                if (wgObj is XUiC_WorkstationWindowGroup direct)
                {
                    return direct;
                }

                var tr = Traverse.Create(wgObj);

                try
                {
                    var p = tr.Property("Controller");
                    if (p.PropertyExists())
                    {
                        return p.GetValue() as XUiC_WorkstationWindowGroup;
                    }
                }
                catch { }

                try
                {
                    var f = tr.Field("controller");
                    if (f.FieldExists())
                    {
                        return f.GetValue() as XUiC_WorkstationWindowGroup;
                    }
                }
                catch { }

                try
                {
                    var f = tr.Field("Controller");
                    if (f.FieldExists())
                    {
                        return f.GetValue() as XUiC_WorkstationWindowGroup;
                    }
                }
                catch { }
            }
            catch
            {
            }

            return null;
        }

        internal static TileEntityWorkstation TryResolveBenchFromWorkstationGroup(XUiC_WorkstationWindowGroup wg)
        {
            if (wg == null)
            {
                return null;
            }

            try
            {
                var workstationData = GetMemberValue(wg, "WorkstationData")
                                   ?? GetMemberValue(wg, "workstationData");

                if (workstationData != null)
                {
                    var directTileEntity = GetMemberValue(workstationData, "tileEntity")
                                        ?? GetMemberValue(workstationData, "TileEntity");

                    if (directTileEntity is TileEntityWorkstation te)
                    {
                        return te;
                    }

                    if (directTileEntity is TileEntity baseTe)
                    {
                        return baseTe as TileEntityWorkstation;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        internal static object GetMemberValue(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            var type = obj.GetType();

            while (type != null && type != typeof(object))
            {
                try
                {
                    var f = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (f != null)
                    {
                        return f.GetValue(obj);
                    }
                }
                catch { }

                try
                {
                    var p = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                    {
                        return p.GetValue(obj, null);
                    }
                }
                catch { }

                type = type.BaseType;
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(Inventory), "SetItem", new[] { typeof(int), typeof(ItemValue), typeof(int), typeof(bool) })]
    public static class Patch_WeaponBench_BlockWeaponReturnToPlayerInventory_SetItemItemValue
    {
        static bool Prefix(Inventory __instance, int _idx, ItemValue _itemValue, int _count, bool _notifyListeners)
        {
            try
            {
                if (!Patch_WeaponBench_BlockWeaponReturnToPlayerInventory.ShouldBlockSetItem(__instance, _itemValue, _count, out var player, out var itemName))
                {
                    return true;
                }

                GameManager.ShowTooltip(player, Localization.Get("deadairRepairSlotLocked"));
                CompatLog.Out($"[DeadAirRepair] Blocked direct Inventory.SetItem of queued refurbishing weapon: slot={_idx} item={itemName} count={_count}");
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] BlockWeaponReturnToPlayerInventory.SetItem(ItemValue): {ex.Message}");
                return true;
            }
        }
    }
}
