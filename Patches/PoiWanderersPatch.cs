using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class PoiWanderersPatch
    {
        private static readonly HashSet<string> TriggeredVolumes = new HashSet<string>();

        private static readonly string[] WandererPool =
        {
            "zombieJoe",
            "zombieSteve",
            "zombieArlene",
            "zombieBusinessMan",
            "zombieUtilityWorker",
            "zombieNurse",
            "zombieMoe"
        };

        private const int ExtraWanderersPerVolume = 2;
        private const float MinSpawnRadius = 3.0f;
        private const float MaxSpawnRadius = 7.5f;

        static IEnumerable<MethodBase> TargetMethods()
        {
            return DeadAirPoiSpawnHelpers.TargetSleeperVolumeMethods();
        }

        static void Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var world = GameManager.Instance?.World;
                if (world == null)
                    return;

                Vector3? centerOpt = DeadAirPoiSpawnHelpers.TryGetVolumeCenter(__instance);
                if (!centerOpt.HasValue)
                    return;

                Vector3 center = centerOpt.Value;
                string volumeKey = DeadAirPoiSpawnHelpers.MakeLocalSpawnKey("wander", __instance, center);
                if (!TriggeredVolumes.Add(volumeKey))
                    return;

                for (int i = 0; i < ExtraWanderersPerVolume; i++)
                {
                    string entityName = WandererPool[UnityEngine.Random.Range(0, WandererPool.Length)];
                    Vector3 spawnPos = DeadAirPoiSpawnHelpers.RandomPointAroundCenter(center, MinSpawnRadius, MaxSpawnRadius);
                    DeadAirPoiSpawnHelpers.TrySpawnEntity(world, entityName, spawnPos);
                }
            }
            catch (Exception e)
            {
                CompatLog.Out($"[DeadAir] PoiWanderersPatch error: {e}");
            }
        }
    }
}