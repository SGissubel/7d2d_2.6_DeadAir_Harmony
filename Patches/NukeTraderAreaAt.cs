using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public class World_GetTraderAreaAt_Disable
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var worldType = typeof(World);

            return worldType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m =>
                    m.Name == "GetTraderAreaAt" &&
                    m.ReturnType == typeof(TraderArea));
        }

        static bool Prefix(ref TraderArea __result)
        {
            __result = null;
            return false;
        }
    }


    // World.IsWithinTraderPlacingProtection(Vector3i _worldBlockPos) : bool
    [HarmonyPatch(typeof(World), nameof(World.IsWithinTraderPlacingProtection), new[] { typeof(Vector3i) })]
    public static class World_IsWithinTraderPlacingProtection_Vec3i_Off
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    // World.IsWithinTraderPlacingProtection(Bounds _bounds) : bool
    [HarmonyPatch(typeof(World), nameof(World.IsWithinTraderPlacingProtection), new[] { typeof(Bounds) })]
    public static class World_IsWithinTraderPlacingProtection_Bounds_Off
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

}