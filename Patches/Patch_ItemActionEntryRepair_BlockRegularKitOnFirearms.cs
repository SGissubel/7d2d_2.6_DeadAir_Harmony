using System.Reflection;
using HarmonyLib;
namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Firearms must use <see cref="DeadAirWeaponRepairState.RepairKitName"/> only; regular
    /// <c>resourceRepairKit</c> must not repair them (XML should list weapon kit only; this enforces old saves/mods).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_ItemActionEntryRepair_BlockRegularKitOnFirearms
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
            if (DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
            {
                return true;
            }
            var stack = Patch_ItemActionEntryRepair_BlockAtBench.TryGetItemStack(__instance);
            if (stack == null || stack.IsEmpty())
            {
                return true;
            }
            var ic = stack.itemValue.ItemClass;
            if (ic == null || !DeadAirGameApiCompat.ItemClassHasAnyRepairTool(ic))
            {
                return true;
            }
            if (!DeadAirWeaponBenchHelpers.IsFirearmSupportedWeapon(ic.Name))
            {
                return true;
            }
            if (!DeadAirGameApiCompat.ItemClassRepairToolsMentions(ic, DeadAirWeaponRepairState.VanillaRepairKitName))
            {
                return true;
            }
            if (xui?.playerUI?.entityPlayer is EntityPlayerLocal loc)
            {
                DeadAirNotify.Msg(loc, "deadairRepairRegularKitBlocked");
            }
            return false;
        }
    }
}
