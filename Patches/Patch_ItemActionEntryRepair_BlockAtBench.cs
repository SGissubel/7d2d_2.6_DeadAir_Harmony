using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class Patch_ItemActionEntryRepair_BlockAtBench
    {
        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ItemActionEntryRepair), "OnActivated")
                ?? AccessTools.Method(typeof(ItemActionEntryRepair), "Execute");
        }

        static bool Prefix(ItemActionEntryRepair __instance)
        {
            var xui = __instance?.ItemController?.xui;
            if (!DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
            {
                return true;
            }

            var stack = TryGetItemStack(__instance);
            if (stack == null || stack.IsEmpty())
            {
                return true;
            }

            var ic = stack.itemValue.ItemClass;
            if (ic == null || !DeadAirGameApiCompat.ItemClassHasAnyRepairTool(ic))
            {
                return true;
            }

            // Any vanilla/context-menu repair on supported weapons at this bench must use the Repair button
            // (kits + parts). Weapons often still list resourceRepairKit, not resourceWeaponRepairKit.
            if (!DeadAirWeaponBenchHelpers.IsFirearmSupportedWeapon(ic.Name))
            {
                return true;
            }

            if (xui?.playerUI?.entityPlayer is EntityPlayerLocal loc)
            {
                DeadAirNotify.Msg(loc, "deadairRepairUseBenchButton");
            }

            return false;
        }

        internal static ItemStack TryGetItemStack(ItemActionEntryRepair __instance)
        {
            if (__instance == null)
            {
                return null;
            }

            var tr = Traverse.Create(__instance);

            foreach (var prop in new[] { "ItemStack", "itemStack", "FocusedItemStack", "m_ItemStack" })
            {
                try
                {
                    var p = tr.Property(prop);
                    if (p.PropertyExists())
                    {
                        var v = p.GetValue() as ItemStack;
                        if (v != null)
                        {
                            return v;
                        }
                    }
                }
                catch
                {
                }
            }

            foreach (var field in new[] { "itemStack", "stack", "m_itemStack", "focusedItemStack", "m_ItemStack" })
            {
                var f = tr.Field(field);
                if (f.FieldExists())
                {
                    var v = f.GetValue() as ItemStack;
                    if (v != null)
                    {
                        return v;
                    }
                }
            }

            return null;
        }
    }
}