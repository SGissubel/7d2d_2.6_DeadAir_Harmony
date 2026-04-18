using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class ParkourHeightCheesePatch
    {
        private static float _nextCheckTime;
        private static float _heightAdvantageStart = -1f;
        private static float _lastTriggerTime = -999f;
        private static bool _warningPlayed;
        private static int _lastReinforcementClassId = -1;

        private const float CheckInterval = 0.35f;
        private const float RequiredHeight = 2.75f;
        private const float MaxHorizontalDistance = 6.0f;
        private const float WarningTime = 1.0f;
        private const float RequiredDuration = 1.5f;
        private const float TriggerCooldown = 15.0f;
        private const int MinQualifiedZombies = 3;
        private const float MaxAllowedHeight = 3.5f;
        private const int ReinforcementCount = 4;
        private const float MinSpawnDistance = 9f;
        private const float MaxSpawnDistance = 12f;
        private const int SpawnAttemptsPerZombie = 8;
        private const string ReinforcementGroup = "ZombiesAll";

        public static MethodBase TargetMethod()
        {
            MethodBase method =
                AccessTools.Method(typeof(EntityPlayerLocal), "OnUpdateLive") ??
                AccessTools.Method(typeof(EntityPlayerLocal), "OnUpdateEntity") ??
                AccessTools.Method(typeof(EntityPlayerLocal), "Update") ??
                AccessTools.Method(typeof(EntityAlive), "OnUpdateLive");

            if (method != null)
                CompatLog.Out($"[DeadAir Parkour Rage] TargetMethod selected: {method.DeclaringType.FullName}.{method.Name}");
            else
                CompatLog.Out("[DeadAir Parkour Rage] TargetMethod failed: no valid method found");

            return method;
        }

        public static bool Prepare()
        {
            CompatLog.Out("[DeadAir Parkour Rage] Harmony Prepare() called");
            return true;
        }

        public static void Postfix(object __instance)
        {
            EntityPlayerLocal player = null;

            if (__instance is EntityPlayerLocal localPlayer)
                player = localPlayer;
            else if (__instance is EntityAlive alive && alive is EntityPlayerLocal alivePlayer)
                player = alivePlayer;

            if (player == null || player.world == null || !player.IsAlive())
                return;

            float now = Time.time;

            if (now < _nextCheckTime)
                return;

            _nextCheckTime = now + CheckInterval;

            if (now < _lastTriggerTime + TriggerCooldown)
                return;

            List<EntityZombie> qualified = GetQualifiedZombies(player);

            if (qualified.Count >= MinQualifiedZombies)
            {
                if (_heightAdvantageStart < 0f)
                {
                    _heightAdvantageStart = now;
                    _warningPlayed = false;
                }

                float elapsed = now - _heightAdvantageStart;

                if (!_warningPlayed && elapsed >= WarningTime)
                {
                    PlayHeightRageWarning(player);
                    _warningPlayed = true;
                }

                if (elapsed >= RequiredDuration)
                {
                    TriggerRage(player, qualified);
                    _lastTriggerTime = now;
                    _heightAdvantageStart = -1f;
                    _warningPlayed = false;
                }
            }
            else
            {
                _heightAdvantageStart = -1f;
                _warningPlayed = false;
            }
        }

        private static void PlayHeightRageWarning(EntityPlayerLocal player)
        {
            if (player == null || player.Buffs == null)
                return;

            if (!player.Buffs.HasBuff("buffDeadAirHeightRageWarning"))
                player.Buffs.AddBuff("buffDeadAirHeightRageWarning");
        }

        private static List<EntityZombie> GetQualifiedZombies(EntityPlayerLocal player)
        {
            var result = new List<EntityZombie>();

            if (player?.world?.Entities?.list == null)
                return result;

            Vector3 playerPos = player.position;
            Vector2 playerXZ = new Vector2(playerPos.x, playerPos.z);
            var entities = player.world.Entities.list;

            for (int i = 0; i < entities.Count; i++)
            {
                if (!(entities[i] is EntityZombie zombie))
                    continue;

                if (!zombie.IsAlive())
                    continue;

                if (zombie.Buffs == null)
                    continue;

                if (zombie.Buffs.HasBuff("buffDeadAirHeightRage"))
                    continue;

                // Only punish obvious "I'm perched above you and you can see me" cheese.
                // This avoids triggering through floors/walls when the player is simply nearby above.
                if (!zombie.CanSee(player))
                    continue;

                Vector3 zombiePos = zombie.position;

                float verticalDiff = playerPos.y - zombiePos.y;
                if (verticalDiff < RequiredHeight)
                    continue;

                if (verticalDiff > MaxAllowedHeight)
                    continue;

                Vector2 zombieXZ = new Vector2(zombiePos.x, zombiePos.z);
                float horizontalDist = Vector2.Distance(playerXZ, zombieXZ);

                if (horizontalDist > MaxHorizontalDistance)
                    continue;

                result.Add(zombie);
            }

            return result;
        }

        private static void TriggerRage(EntityPlayerLocal player, List<EntityZombie> zombies)
        {
            if (player?.Buffs != null && !player.Buffs.HasBuff("buffDeadAirHeightRageActive"))
                player.Buffs.AddBuff("buffDeadAirHeightRageActive");

            for (int i = 0; i < zombies.Count; i++)
            {
                EntityZombie zombie = zombies[i];
                if (zombie == null || !zombie.IsAlive() || zombie.Buffs == null)
                    continue;

                zombie.Buffs.AddBuff("buffDeadAirHeightRage");
            }

            SpawnReinforcements(player, ReinforcementCount);
        }

        private static void SpawnReinforcements(EntityPlayerLocal player, int count)
        {
            if (player == null || player.world == null || count <= 0)
                return;

            World world = player.world;
            Vector3 playerPos = player.position;

            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos;
                if (!TryFindSpawnPosition(world, playerPos, out spawnPos))
                {
                    CompatLog.Out("[DeadAir Parkour Rage] Failed to find reinforcement spawn position");
                    continue;
                }

                int entityID = EntityGroups.GetRandomFromGroup(ReinforcementGroup, ref _lastReinforcementClassId, world.rand);
                if (entityID <= 0)
                {
                    CompatLog.Out($"[DeadAir Parkour Rage] Failed to get entityID from group {ReinforcementGroup}");
                    continue;
                }

                Entity spawnEntity = EntityFactory.CreateEntity(entityID, spawnPos);
                if (spawnEntity == null)
                {
                    CompatLog.Out("[DeadAir Parkour Rage] EntityFactory.CreateEntity returned null");
                    continue;
                }

                spawnEntity.SetSpawnerSource(EnumSpawnerSource.Dynamic);
                spawnEntity.SetPosition(spawnPos);
                world.SpawnEntityInWorld(spawnEntity);

                CompatLog.Out($"[DeadAir Parkour Rage] Spawned reinforcement entityId={spawnEntity.entityId} at {spawnPos}");
            }
        }

        private static bool TryFindSpawnPosition(World world, Vector3 playerPos, out Vector3 spawnPos)
        {
            for (int attempt = 0; attempt < SpawnAttemptsPerZombie; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle.normalized;
                if (circle == Vector2.zero)
                    circle = Vector2.right;

                float distance = Random.Range(MinSpawnDistance, MaxSpawnDistance);

                int x = Mathf.RoundToInt(playerPos.x + circle.x * distance);
                int z = Mathf.RoundToInt(playerPos.z + circle.y * distance);

                int y = (int)world.GetHeightAt(x, z);
                if (y <= 0)
                    continue;

                spawnPos = new Vector3(x + 0.5f, y + 1f, z + 0.5f);
                return true;
            }

            spawnPos = playerPos;
            return false;
        }
    }
}
