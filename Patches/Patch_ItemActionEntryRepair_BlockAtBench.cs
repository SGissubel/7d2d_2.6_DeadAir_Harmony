using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Shared helper only (no Harmony patches here). Kept so older fork files that still call
    /// <see cref="TryGetItemStack"/> compile. Field repair clamp has its own copy of this logic.
    /// </summary>
    public static class Patch_ItemActionEntryRepair_BlockAtBench
    {
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