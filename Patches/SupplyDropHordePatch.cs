using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class SupplyDropHordePatch
    {
        private const float InnerMin = 4f;
        private const float InnerMax = 9f;
        private const float MidMin = 9f;
        private const float MidMax = 14f;
        private const float OuterMin = 14f;
        private const float OuterMax = 20f;
        private const float MinSpawnDistance = 4f;

        private static readonly string[] LowTierPool =
        {
            "zombieArlene", "zombieJoe", "zombieSteve", "zombieBusinessMan", "zombieUtilityWorker", "zombieNurse"
        };

        private static readonly string[] MidTierPool =
        {
            "zombieBiker", "zombieFatCop", "zombieSoldier", "zombieMoe", "zombieSkateboarder", "zombieHazmat"
        };

        private static readonly string[] HighTierPool =
        {
            "zombieArleneRadiated", "zombieJoeRadiated", "zombieBikerRadiated", "zombieFatCopRadiated",
            "zombieSoldierRadiated", "zombieHazmatRadiated"
        };

        private static readonly HashSet<int> TriggeredCrateIds = new HashSet<int>();

        private static MethodInfo _cachedGameStageGetter;
        private static FieldInfo _cachedGameStageField;
        private static bool _gameStageResolverTried;
        private static bool _gameStageMissingLogged;

        private static bool _isSpawningFromSupplyDrop;

        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("EntitySupplyCrate");
            if (t == null)
                yield break;

            string[] candidates =
            {
                "OnAddedToWorld",
                "OnEntityLoaded",
                "PostInit"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                var method = AccessTools.DeclaredMethod(t, candidates[i]);
                if (method != null)
                    yield return method;
            }
        }

        static void Postfix(EntitySupplyCrate __instance)
        {
            if (__instance == null)
                return;

            if (_isSpawningFromSupplyDrop)
                return;

            try
            {
                var world = GameManager.Instance?.World;
                if (world == null)
                    return;

                int crateId = __instance.entityId;
                if (crateId <= 0)
                    return;

                if (!TriggeredCrateIds.Add(crateId))
                    return;

                Vector3 cratePos = __instance.position;
                int gameStage = GetPrimaryPlayerGameStage(world);
                HordePlan plan = BuildPlan(gameStage);

                _isSpawningFromSupplyDrop = true;
                try
                {
                    SpawnRing(world, cratePos, plan.InnerCount, InnerMin, InnerMax, plan);
                    SpawnRing(world, cratePos, plan.MidCount, MidMin, MidMax, plan);
                    SpawnRing(world, cratePos, plan.OuterCount, OuterMin, OuterMax, plan);
                }
                finally
                {
                    _isSpawningFromSupplyDrop = false;
                }

                CompatLog.Out($"[DeadAir] SupplyDropHordePatch spawned {plan.TotalCount} zombies around supply crate id={crateId} at {cratePos} | gs={gameStage}");
            }
            catch (Exception ex)
            {
                _isSpawningFromSupplyDrop = false;
                CompatLog.Out($"[DeadAir] SupplyDropHordePatch error: {ex}");
            }
        }

        private static int GetPrimaryPlayerGameStage(World world)
        {
            try
            {
                EntityPlayerLocal player = world.GetPrimaryPlayer();
                if (player == null)
                    return 0;

                EnsureGameStageResolver(player.GetType());

                if (_cachedGameStageGetter != null)
                {
                    object value = _cachedGameStageGetter.Invoke(player, null);
                    return ConvertToInt(value);
                }

                if (_cachedGameStageField != null)
                {
                    object value = _cachedGameStageField.GetValue(player);
                    return ConvertToInt(value);
                }

                if (!_gameStageMissingLogged)
                {
                    _gameStageMissingLogged = true;
                    CompatLog.Out("[DeadAir] SupplyDropHordePatch could not resolve a game-stage member on EntityPlayerLocal. Falling back to 0.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAir] SupplyDropHordePatch could not read player game stage: {ex.Message}");
                return 0;
            }
        }

        private static void EnsureGameStageResolver(Type playerType)
        {
            if (_gameStageResolverTried || playerType == null)
                return;

            _gameStageResolverTried = true;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            string[] propertyNames =
            {
                "GameStage",
                "PartyGameStage",
                "HighestPartyGameStage"
            };

            for (int i = 0; i < propertyNames.Length; i++)
            {
                var prop = playerType.GetProperty(propertyNames[i], Flags);
                var getter = prop?.GetGetMethod(true);
                if (getter != null)
                {
                    _cachedGameStageGetter = getter;
                    CompatLog.Out($"[DeadAir] SupplyDropHordePatch using {playerType.Name}.{propertyNames[i]} for game stage scaling.");
                    return;
                }
            }

            string[] fieldNames =
            {
                "GameStage",
                "gameStage",
                "PartyGameStage",
                "HighestPartyGameStage"
            };

            for (int i = 0; i < fieldNames.Length; i++)
            {
                var field = playerType.GetField(fieldNames[i], Flags);
                if (field != null)
                {
                    _cachedGameStageField = field;
                    CompatLog.Out($"[DeadAir] SupplyDropHordePatch using {playerType.Name}.{fieldNames[i]} for game stage scaling.");
                    return;
                }
            }
        }

        private static int ConvertToInt(object value)
        {
            if (value == null)
                return 0;

            if (value is int i)
                return i;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static HordePlan BuildPlan(int gameStage)
        {
            if (gameStage < 30)
                return new HordePlan(inner: 10, mid: 5, outer: 3, midChance: 0.15f, highChance: 0.00f);

            if (gameStage < 60)
                return new HordePlan(inner: 14, mid: 7, outer: 4, midChance: 0.30f, highChance: 0.02f);

            if (gameStage < 100)
                return new HordePlan(inner: 18, mid: 9, outer: 5, midChance: 0.45f, highChance: 0.07f);

            if (gameStage < 160)
                return new HordePlan(inner: 22, mid: 11, outer: 6, midChance: 0.60f, highChance: 0.15f);

            return new HordePlan(inner: 26, mid: 13, outer: 7, midChance: 0.75f, highChance: 0.28f);
        }

        private static void SpawnRing(World world, Vector3 center, int count, float minRadius, float maxRadius, HordePlan plan)
        {
            for (int i = 0; i < count; i++)
            {
                string entityName = PickEntityName(plan);
                if (string.IsNullOrEmpty(entityName))
                    continue;

                Vector3 spawnPos = FindSpawnPositionNear(world, center, minRadius, maxRadius);
                Entity ent = CreateEntity(entityName, spawnPos);
                if (ent == null)
                    continue;

                world.SpawnEntityInWorld(ent);
            }
        }

        private static string PickEntityName(HordePlan plan)
        {
            float roll = UnityEngine.Random.value;
            if (plan.HighChance > 0f && roll < plan.HighChance)
                return HighTierPool[UnityEngine.Random.Range(0, HighTierPool.Length)];

            if (roll < plan.HighChance + plan.MidChance)
                return MidTierPool[UnityEngine.Random.Range(0, MidTierPool.Length)];

            return LowTierPool[UnityEngine.Random.Range(0, LowTierPool.Length)];
        }

        private static Vector3 FindSpawnPositionNear(World world, Vector3 center, float minRadius, float maxRadius)
        {
            Vector3 fallback = center + new Vector3(maxRadius, 0f, 0f);

            for (int attempt = 0; attempt < 24; attempt++)
            {
                float dist = UnityEngine.Random.Range(
                    Mathf.Max(MinSpawnDistance, minRadius),
                    Mathf.Max(minRadius + 0.1f, maxRadius));

                float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float x = center.x + Mathf.Cos(angle) * dist;
                float z = center.z + Mathf.Sin(angle) * dist;
                float y = GetTerrainHeight(world, x, z, center.y);

                Vector3 candidate = new Vector3(x, y + 0.20f, z);
                fallback = candidate;

                if (IsValidSpawnPosition(world, candidate))
                    return candidate;
            }

            return fallback;
        }

        private static bool IsValidSpawnPosition(World world, Vector3 pos)
        {
            try
            {
                Vector3i feet = World.worldToBlockPos(pos);
                Vector3i body = feet + Vector3i.up;
                Vector3i below = feet + Vector3i.down;

                BlockValue feetBlock = world.GetBlock(feet);
                BlockValue bodyBlock = world.GetBlock(body);
                BlockValue belowBlock = world.GetBlock(below);

                if (!feetBlock.isair)
                    return false;
                if (!bodyBlock.isair)
                    return false;
                if (belowBlock.isair)
                    return false;

                return true;
            }
            catch
            {
                return true;
            }
        }

        private static float GetTerrainHeight(World world, float x, float z, float fallbackY)
        {
            try
            {
                return world.GetHeightAt(Mathf.FloorToInt(x), Mathf.FloorToInt(z));
            }
            catch
            {
                return fallbackY;
            }
        }

        private static Entity CreateEntity(string entityName, Vector3 pos)
        {
            try
            {
                int entityClassId = EntityClass.FromString(entityName);
                if (entityClassId <= 0)
                    return null;

                return EntityFactory.CreateEntity(entityClassId, pos);
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAir] SupplyDropHordePatch could not create entity '{entityName}': {ex.Message}");
                return null;
            }
        }

        private readonly struct HordePlan
        {
            public readonly int InnerCount;
            public readonly int MidCount;
            public readonly int OuterCount;
            public readonly float MidChance;
            public readonly float HighChance;
            public int TotalCount => InnerCount + MidCount + OuterCount;

            public HordePlan(int inner, int mid, int outer, float midChance, float highChance)
            {
                InnerCount = inner;
                MidCount = mid;
                OuterCount = outer;
                MidChance = Mathf.Clamp01(midChance);
                HighChance = Mathf.Clamp01(highChance);
            }
        }
    }
}