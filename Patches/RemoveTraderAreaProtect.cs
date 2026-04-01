using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public class World_GetTraderAreaAt_ReturnNull
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var tWorld = AccessTools.TypeByName("World");
            if (tWorld == null) return Enumerable.Empty<MethodBase>();

            // Find any overload named GetTraderAreaAt that returns TraderArea
            return tWorld.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name == "GetTraderAreaAt"
                                  && m.ReturnType != null
                                  && m.ReturnType.Name == "TraderArea");
        }

        static bool Prefix(ref object __result)
        {
            // TraderArea is a class, so null is valid.
            __result = null;
            return false;
        }
    }
}