using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class PoiAmbushBehindYouPatch
    {
        private static readonly HashSet<string> TriggeredVolumes = new HashSet<string>();

        private static readonly string[] AmbushPool =
        {
            "zombieJoe",
            "zombieSteve",
            "zombieArlene",
            "zombieBusinessMan",
            "zombieUtilityWorker",
            "zombieBiker",
            "zombieSoldier"
        };

        private const float AmbushTriggerChance = 0.35f;
        private const int MinAmbushCount = 1;
        private const int MaxAmbushCount = 2;
        private const float MinSpawnRadius = 4.0f;
        private const float MaxSpawnRadius = 8.0f;

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

                var player = world.GetPrimaryPlayer();
                if (player == null)
                    return;

                Vector3? centerOpt = DeadAirPoiSpawnHelpers.TryGetVolumeCenter(__instance);
                if (!centerOpt.HasValue)
                    return;

                if (UnityEngine.Random.value > AmbushTriggerChance)
                    return;

                Vector3 center = centerOpt.Value;
                string volumeKey = DeadAirPoiSpawnHelpers.MakeLocalSpawnKey("ambush", __instance, center);
                if (!TriggeredVolumes.Add(volumeKey))
                    return;

                int spawnCount = UnityEngine.Random.Range(MinAmbushCount, MaxAmbushCount + 1);
                for (int i = 0; i < spawnCount; i++)
                {
                    string entityName = AmbushPool[UnityEngine.Random.Range(0, AmbushPool.Length)];
                    Vector3 spawnPos = DeadAirPoiSpawnHelpers.PointOppositePlayerFromCenter(center, player.position, MinSpawnRadius, MaxSpawnRadius);
                    DeadAirPoiSpawnHelpers.TrySpawnEntity(world, entityName, spawnPos);
                }
            }
            catch (Exception e)
            {
                CompatLog.Out($"[DeadAir] PoiAmbushBehindYouPatch error: {e}");
            }
        }
    }
}