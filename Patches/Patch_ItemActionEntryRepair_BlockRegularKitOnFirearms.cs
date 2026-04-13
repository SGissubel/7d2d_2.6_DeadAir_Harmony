using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Disabled stub. Regular-kit blocking was removed; firearms use <c>RepairTools</c> in items.xml.
    /// Class kept so existing projects that still compile this file do not break.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_ItemActionEntryRepair_BlockRegularKitOnFirearms
    {
        static bool Prepare()
        {
            return false;
        }
    }
}