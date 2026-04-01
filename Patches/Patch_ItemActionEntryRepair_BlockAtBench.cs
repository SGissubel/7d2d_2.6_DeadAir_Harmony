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

            if (!DeadAirGameApiCompat.ItemClassRepairToolsMentions(ic, DeadAirWeaponRepairState.RepairKitName))
            {
                return true;
            }

            if (xui?.playerUI?.entityPlayer is EntityPlayerLocal loc)
            {
                DeadAirNotify.Msg(loc, "deadairRepairUseBenchButton");
            }

            return false;
        }

        static ItemStack TryGetItemStack(ItemActionEntryRepair __instance)
        {
            var tr = Traverse.Create(__instance);
            foreach (var field in new[] { "itemStack", "stack", "m_itemStack", "focusedItemStack" })
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