using System;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Shared checks for blocking weapon pickup from the DeadAir bench during an active refurbish timer.
    /// </summary>
    internal static class WeaponBenchRepairGuards
    {
        internal const string WeaponBenchInputsWindowId = "windowDeadAirWeaponBenchInputs";

        /// <summary>True when this item stack is weapon slot 0 on the DeadAir inputs window and a refurbish is in progress.</summary>
        internal static bool IsWeaponSlotLockedDuringRepair(XUiC_ItemStack itemStackCtrl)
        {
            if (itemStackCtrl?.xui == null)
            {
                return false;
            }

            if (!DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(itemStackCtrl.xui))
            {
                return false;
            }

            if (!IsUnderWeaponBenchInputsWindow(itemStackCtrl))
            {
                return false;
            }

            var wg = TryGetBenchWorkstationGroup(itemStackCtrl.xui);
            if (wg == null || !DeadAirWeaponBenchHelpers.IsWeaponRepairBench(wg))
            {
                return false;
            }

            var te = TryResolveBenchFromWorkstationGroup(wg);
            if (te == null)
            {
                return false;
            }

            if (!DeadAirWeaponBenchRepairQueue.IsRepairInProgress(te))
            {
                return false;
            }

            int slotIndex = TryGetSlotIndex(itemStackCtrl);
            if (slotIndex != 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>True when applying this stack to the given tool slot would clear slot 0's weapon during refurbish.</summary>
        internal static bool ShouldBlockClearingToolSlotAtIndex(TileEntityWorkstation te, int slotIndex, ItemStack newStack)
        {
            if (slotIndex != 0)
            {
                return false;
            }

            return ShouldBlockClearingToolSlotZero(te, newStack);
        }

        /// <summary>True when applying this stack to tool slot 0 would clear a weapon while refurbish is running.</summary>
        internal static bool ShouldBlockClearingToolSlotZero(TileEntityWorkstation te, ItemStack newSlot0)
        {
            if (te == null)
            {
                return false;
            }

            if (!DeadAirWeaponRepairActions.IsOurBench(te))
            {
                return false;
            }

            if (!DeadAirWeaponBenchRepairQueue.IsRepairInProgress(te))
            {
                return false;
            }

            var cur = DeadAirWeaponRepairActions.TryGetToolStacks(te);
            if (cur == null || cur.Length < 1 || cur[0].IsEmpty())
            {
                return false;
            }

            return newSlot0 == null || newSlot0.IsEmpty();
        }

        internal static void ShowSlotLockedTooltip(EntityPlayerLocal playerIfKnown = null)
        {
            var player = playerIfKnown ?? GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
            if (player != null)
            {
                GameManager.ShowTooltip(player, Localization.Get("deadairRepairSlotLocked"));
            }
        }

        /// <summary>Heuristic for SetItem(ItemValue, int) calls that would empty the slot.</summary>
        internal static bool LooksLikeClearingItem(ItemValue iv, int count)
        {
            if (count <= 0)
            {
                return true;
            }

            if (iv == null)
            {
                return true;
            }

            try
            {
                if (iv.ItemClass == null)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (iv.type == 0)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        static bool IsUnderWeaponBenchInputsWindow(XUiController ctrl)
        {
            for (var p = ctrl; p != null; p = TryGetParentController(p))
            {
                try
                {
                    var id = DeadAirGameApiCompat.TryGetXUiControlName(p);
                    if (string.Equals(id, WeaponBenchInputsWindowId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        static XUiController TryGetParentController(XUiController ctrl)
        {
            if (ctrl == null)
            {
                return null;
            }

            try
            {
                var tr = Traverse.Create(ctrl);
                var prop = tr.Property("Parent");
                if (prop.PropertyExists())
                {
                    return prop.GetValue() as XUiController;
                }
            }
            catch
            {
            }

            try
            {
                var tr = Traverse.Create(ctrl);
                var f = tr.Field("parent");
                if (f.FieldExists())
                {
                    return f.GetValue() as XUiController;
                }
            }
            catch
            {
            }

            return null;
        }

        static int TryGetSlotIndex(XUiC_ItemStack ctrl)
        {
            if (ctrl == null)
            {
                return -1;
            }

            try
            {
                return ctrl.SlotNumber;
            }
            catch
            {
            }

            var controlName = DeadAirGameApiCompat.TryGetXUiControlName(ctrl);
            if (!string.IsNullOrEmpty(controlName) && int.TryParse(controlName, out int fromId))
            {
                return fromId;
            }

            var tr = Traverse.Create(ctrl);

            foreach (var member in new[] { "SlotNumber", "slotNumber", "Slot", "slot", "Index", "index" })
            {
                try
                {
                    var p = tr.Property(member);
                    if (p.PropertyExists())
                    {
                        object v = p.GetValue();
                        if (v is int i)
                        {
                            return i;
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    var f = tr.Field(member);
                    if (f.FieldExists())
                    {
                        object v = f.GetValue();
                        if (v is int i)
                        {
                            return i;
                        }
                    }
                }
                catch
                {
                }
            }

            return -1;
        }

        static XUiC_WorkstationWindowGroup TryGetBenchWorkstationGroup(XUi xui)
        {
            if (xui == null)
            {
                return null;
            }

            try
            {
                object wgObj = xui.FindWindowGroupByName(DeadAirWeaponBenchHelpers.BenchWindowGroupName);
                if (wgObj == null)
                {
                    return null;
                }

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
                catch
                {
                }

                try
                {
                    var f = tr.Field("controller");
                    if (f.FieldExists())
                    {
                        return f.GetValue() as XUiC_WorkstationWindowGroup;
                    }
                }
                catch
                {
                }

                try
                {
                    var f = tr.Field("Controller");
                    if (f.FieldExists())
                    {
                        return f.GetValue() as XUiC_WorkstationWindowGroup;
                    }
                }
                catch
                {
                }
            }
            catch
            {
            }

            return null;
        }

        static TileEntityWorkstation TryResolveBenchFromWorkstationGroup(XUiC_WorkstationWindowGroup wg)
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

        static object GetMemberValue(object obj, string memberName)
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
                catch
                {
                }

                try
                {
                    var p = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                    {
                        return p.GetValue(obj, null);
                    }
                }
                catch
                {
                }

                type = type.BaseType;
            }

            return null;
        }
    }

    /// <summary>Resolve private methods declared on <paramref name="startType"/> or its base types (DeclaredOnly walk).</summary>
    internal static class WeaponBenchRepairReflection
    {
        internal static MethodInfo FindInstanceMethodInHierarchy(Type startType, string name, params Type[] parameterTypes)
        {
            if (startType == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var t = startType; t != null && t != typeof(object); t = t.BaseType)
            {
                MethodInfo mi;
                if (parameterTypes == null || parameterTypes.Length == 0)
                {
                    mi = t.GetMethod(name, flags, null, Type.EmptyTypes, null);
                }
                else
                {
                    mi = t.GetMethod(name, flags, null, parameterTypes, null);
                }

                if (mi != null)
                {
                    return mi;
                }
            }

            return null;
        }
    }
}
