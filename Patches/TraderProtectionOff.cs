using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// 7DTD 2.6: Disable trader protection (placing/damage/loot restrictions) + disable forced teleport.
    /// This patches BOTH World and DynamicPrefabDecorator routes so we don't miss call paths.
    /// </summary>
    public static class TraderProtectionOff_26
    {
        // ----------------------------
        // A) Patch ALL World.IsWithinTraderPlacingProtection overloads
        // ----------------------------
        [HarmonyPatch]
        public class World_IsWithinTraderPlacingProtection_All
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                return typeof(World)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "IsWithinTraderPlacingProtection");
            }

            static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        // ----------------------------
        // C) Disable teleport kick-out logic (stay inside after close)
        // ----------------------------
        [HarmonyPatch(typeof(EntityAlive), "checkForTeleportOutOfTraderArea")]
        public class EntityAlive_checkForTeleportOutOfTraderArea
        {
            static bool Prefix() => false; // skip original
        }

        // ----------------------------
        // D) Belt-and-suspenders: trader bounds checks
        // ----------------------------
        [HarmonyPatch(typeof(TraderArea), "IsWithinProtectArea")]
        public class TraderArea_IsWithinProtectArea
        {
            static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch]
        public class TraderArea_IsWithinTeleportArea
        {
            static MethodBase TargetMethod()
                => AccessTools.Method(
                    typeof(TraderArea),
                    "IsWithinTeleportArea",
                    new[] { typeof(Vector3), typeof(Prefab.PrefabTeleportVolume).MakeByRefType() }
                );

            static bool Prefix(ref bool __result, ref Prefab.PrefabTeleportVolume tpVolume)
            {
                tpVolume = null;
                __result = false;
                return false;
            }
        }
    }
}