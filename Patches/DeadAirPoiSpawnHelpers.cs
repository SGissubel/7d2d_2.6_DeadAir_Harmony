using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    internal static class DeadAirPoiSpawnHelpers
    {
        internal static IEnumerable<MethodBase> TargetSleeperVolumeMethods()
        {
            var t = AccessTools.TypeByName("SleeperVolume");
            if (t == null)
                yield break;

            var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SpawnEntities",
                "SpawnEntity",
                "Spawn",
                "SpawnSleepers"
            };

            foreach (var m in AccessTools.GetDeclaredMethods(t))
            {
                if (candidateNames.Contains(m.Name))
                    yield return m;
            }
        }

        internal static Vector3? TryGetVolumeCenter(object sleeperVolume)
        {
            if (sleeperVolume == null)
                return null;

            try
            {
                var t = sleeperVolume.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var name in new[] { "center", "Center", "position", "pos", "Pos" })
                {
                    var prop = t.GetProperty(name, flags);
                    if (prop != null && prop.PropertyType == typeof(Vector3))
                        return (Vector3)prop.GetValue(sleeperVolume, null);

                    var field = t.GetField(name, flags);
                    if (field != null && field.FieldType == typeof(Vector3))
                        return (Vector3)field.GetValue(sleeperVolume);
                }
            }
            catch (Exception e)
            {
                CompatLog.Out($"[DeadAir] TryGetVolumeCenter failed: {e.Message}");
            }

            return null;
        }

        internal static string MakeLocalSpawnKey(string prefix, object sleeperVolume, Vector3 center)
        {
            int objKey = sleeperVolume != null ? sleeperVolume.GetHashCode() : 0;
            int x = Mathf.RoundToInt(center.x);
            int y = Mathf.RoundToInt(center.y);
            int z = Mathf.RoundToInt(center.z);
            return $"{prefix}:{objKey}:{x},{y},{z}";
        }

        internal static Entity TryCreateEntity(string entityName, Vector3 pos)
        {
            try
            {
                int entityClassId = EntityClass.FromString(entityName);
                if (entityClassId <= 0)
                    return null;

                return EntityFactory.CreateEntity(entityClassId, pos);
            }
            catch (Exception e)
            {
                CompatLog.Out($"[DeadAir] TryCreateEntity failed for '{entityName}': {e.Message}");
                return null;
            }
        }

        internal static bool TrySpawnEntity(World world, string entityName, Vector3 pos)
        {
            if (world == null || string.IsNullOrEmpty(entityName))
                return false;

            var ent = TryCreateEntity(entityName, pos);
            if (ent == null)
                return false;

            world.SpawnEntityInWorld(ent);
            return true;
        }

        internal static Vector3 RandomPointAroundCenter(Vector3 center, float minRadius, float maxRadius)
        {
            float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = UnityEngine.Random.Range(minRadius, maxRadius);
            return new Vector3(center.x + Mathf.Cos(angle) * dist, center.y, center.z + Mathf.Sin(angle) * dist);
        }

        internal static Vector3 PointOppositePlayerFromCenter(Vector3 center, Vector3 playerPos, float minRadius, float maxRadius)
        {
            Vector3 awayFromPlayer = center - playerPos;
            awayFromPlayer.y = 0f;

            if (awayFromPlayer.sqrMagnitude < 0.001f)
                awayFromPlayer = Vector3.forward;

            awayFromPlayer.Normalize();
            float jitter = UnityEngine.Random.Range(-40f, 40f);
            Vector3 dir = Quaternion.Euler(0f, jitter, 0f) * awayFromPlayer;
            float dist = UnityEngine.Random.Range(minRadius, maxRadius);

            return new Vector3(center.x + dir.x * dist, center.y, center.z + dir.z * dist);
        }
    }
}