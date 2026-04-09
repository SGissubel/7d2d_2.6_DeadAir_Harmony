using System.Linq;
using System.Reflection;
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class WildBreachGroupBreach
    {
        // =========================================================
        // TUNING
        // =========================================================

        // Direct hit pressure on the primary struck block.
        private const float BaseMultiplier = 4f;
        private const float PerZombieBonus = 1.85f;
    

        private const int SurgeThreshold = 3;
        private const int RageThreshold = 5;

        // Group escalation thresholds.
        private const float SurgeBonus = 2f;
        private const float RageBonus = 2.50f;
        private const float EntranceDirectBonus = 2.50f;

        private const float NearbyRadius = 4.25f;

        // Final hard cap after ALL bonuses.
        // Kept intentionally high because the goal is violent breach pressure.
        private const float MaxFinalMultiplier = 35.82f;

        // Spread tuning.
        // Surge = strong widening, but not quite full-copy.
        private const float SurgeSpreadFrac = 0.90f;

        // Rage = full-copy brutal spread.
        private const float RageSpreadFrac = 1.00f;

        // Extra tear-out only on destroy behavior, to widen beyond the initial face.
        private const float DestroyFollowThroughFrac = 1.50f;

        // Cache the last boosted direct damage for the entity so the destroy hook
        // can use the same brutality level when a block actually gives way.
        private static readonly Dictionary<int, int> LastBoostedDamageByEntity = new Dictionary<int, int>();

        // =========================================================
        // PRIMARY HOT PATH: ITEMACTIONATTACK.HIT
        // =========================================================

        [HarmonyPatch(typeof(ItemActionAttack), "Hit")]
        [HarmonyPrefix]
        public static void Hit_Prefix(
            WorldRayHitInfo hitInfo,
            int _attackerEntityId,
            ref float _blockDamage,
            ItemActionAttack.AttackHitInfo _attackDetails)
        {
            try
            {
                World world = GameManager.Instance?.World;
                if (world == null) return;

                Entity entity = world.GetEntity(_attackerEntityId);
                if (!(entity is EntityZombie zombie)) return;
                if (!zombie.IsAlive()) return;

                if (_attackDetails == null) return;
                if (!_attackDetails.bBlockHit) return;

                Vector3i hitPos = GetHitPos(hitInfo, _attackDetails);
                if (hitPos == Vector3i.zero) return;

                // Tight doorway-only targeting for the direct rage logic.
                if (!IsDirectBreachTarget(hitPos)) return;

                int nearbyZeds = CountNearbyZombiesAt(hitPos, NearbyRadius);

                float mult = BaseMultiplier + Mathf.Max(0, nearbyZeds - 1) * PerZombieBonus;

                if (IsEntranceBlock(world, hitPos))
                    mult *= EntranceDirectBonus;

                if (nearbyZeds >= SurgeThreshold)
                    mult *= SurgeBonus;

                if (nearbyZeds >= RageThreshold)
                    mult *= RageBonus;

                // Clamp AFTER all bonuses.
                mult = Mathf.Min(mult, MaxFinalMultiplier);

                float boosted = _blockDamage * mult;
                if (boosted > _blockDamage)
                    _blockDamage = boosted;

                int boostedInt = Mathf.Max(1, Mathf.RoundToInt(_blockDamage));
                LastBoostedDamageByEntity[_attackerEntityId] = boostedInt;

                // WIDENING IN THE HOT PATH:
                // This is intentionally additive. We do NOT split the damage budget.
                // The struck block gets full direct damage from Hit itself.
                // Neighbor blocks get their own additional damage copies via WorldDamageShim.
                ImpactPattern pattern = GetImpactPattern(hitPos, zombie);

                if (nearbyZeds >= RageThreshold)
                {
                    int rageDamage = Mathf.Max(1, Mathf.RoundToInt(_blockDamage * RageSpreadFrac));

                    // Full-copy brutal spread across the doorway face.
                    ApplySplash(world, pattern.Left,    rageDamage, _attackerEntityId, hitPos);
                    ApplySplash(world, pattern.Right,   rageDamage, _attackerEntityId, hitPos);
                    ApplySplash(world, pattern.Up,      rageDamage, _attackerEntityId, hitPos);
                    ApplySplash(world, pattern.Forward, rageDamage, _attackerEntityId, hitPos);
                }
                else if (nearbyZeds >= SurgeThreshold)
                {
                    int surgeDamage = Mathf.Max(1, Mathf.RoundToInt(_blockDamage * SurgeSpreadFrac));

                    // Still nasty, but a little less absurd until full rage.
                    ApplySplash(world, pattern.Left,  surgeDamage, _attackerEntityId, hitPos);
                    ApplySplash(world, pattern.Right, surgeDamage, _attackerEntityId, hitPos);
                    ApplySplash(world, pattern.Up,    surgeDamage, _attackerEntityId, hitPos);
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAir][WildBreach] Hit_Prefix error: {ex}");
            }
        }

        // =========================================================
        // SECONDARY HOOK: EXECUTEDESTROYBLOCKBEHAVIOR
        // Used only as follow-through to rip the opening wider once things fail.
        // =========================================================

        [HarmonyPatch(typeof(EntityHuman), "ExecuteDestroyBlockBehavior",
            new[] { typeof(EntityAlive.DestroyBlockBehavior), typeof(ItemActionAttack.AttackHitInfo) })]
        [HarmonyPostfix]
        public static void ExecuteDestroyBlockBehavior_Postfix(
            EntityHuman __instance,
            EntityAlive.DestroyBlockBehavior behavior,
            ItemActionAttack.AttackHitInfo attackHitInfo,
            ref bool __result)
        {
            try
            {
                if (!__result) return;
                if (__instance == null) return;
                if (!(__instance is EntityZombie zombie)) return;
                if (!zombie.IsAlive()) return;
                if (attackHitInfo == null) return;

                World world = GameManager.Instance?.World;
                if (world == null) return;

                Vector3i hitPos = attackHitInfo.hitPosition;
                if (hitPos == Vector3i.zero) return;

                if (!IsDirectBreachTarget(hitPos)) return;

                int nearbyZeds = CountNearbyZombiesAt(hitPos, NearbyRadius);
                if (nearbyZeds < SurgeThreshold) return;

                int mainDamage = 25;
                if (LastBoostedDamageByEntity.TryGetValue(__instance.entityId, out int cached) && cached > 0)
                    mainDamage = cached;

                ImpactPattern pattern = GetImpactPattern(hitPos, zombie);

                // This hook is not the main damage engine anymore.
                // It is used to widen beyond the initial face once blocks start failing.
                if (nearbyZeds >= RageThreshold)
                {
                    int rageFollowThrough = Mathf.Max(1, Mathf.RoundToInt(mainDamage * DestroyFollowThroughFrac));

                    ApplySplash(world, pattern.LeftForward,  rageFollowThrough, __instance.entityId, hitPos);
                    ApplySplash(world, pattern.RightForward, rageFollowThrough, __instance.entityId, hitPos);
                    ApplySplash(world, pattern.Down,         rageFollowThrough, __instance.entityId, hitPos);
                }
                else
                {
                    int surgeFollowThrough = Mathf.Max(1, Mathf.RoundToInt(mainDamage * 0.75f));

                    ApplySplash(world, pattern.LeftForward,  surgeFollowThrough, __instance.entityId, hitPos);
                    ApplySplash(world, pattern.RightForward, surgeFollowThrough, __instance.entityId, hitPos);
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAir][WildBreach] ExecuteDestroyBlockBehavior error: {ex}");
            }
        }

        // =========================================================
        // IMPACT SHAPE
        // =========================================================

        private struct ImpactPattern
        {
            public Vector3i Left;
            public Vector3i Right;
            public Vector3i Up;
            public Vector3i Down;
            public Vector3i Forward;
            public Vector3i LeftForward;
            public Vector3i RightForward;
        }

        private static ImpactPattern GetImpactPattern(Vector3i hitPos, EntityAlive entity)
        {
            Vector3 hitCenter = new Vector3(hitPos.x + 0.5f, hitPos.y + 0.5f, hitPos.z + 0.5f);
            Vector3 dir = hitCenter - entity.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f)
                dir = entity.GetForwardVector();

            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.forward;

            dir.Normalize();

            Vector3 right = new Vector3(dir.z, 0f, -dir.x);

            Vector3i forward = ToCardinal(dir);
            Vector3i rightCard = ToCardinal(right);

            if (rightCard == Vector3i.zero)
                rightCard = new Vector3i(1, 0, 0);

            return new ImpactPattern
            {
                Left = hitPos - rightCard,
                Right = hitPos + rightCard,
                Up = hitPos + Vector3i.up,
                Down = hitPos + Vector3i.down,
                Forward = hitPos + forward,
                LeftForward = hitPos - rightCard + forward,
                RightForward = hitPos + rightCard + forward
            };
        }

        private static Vector3i ToCardinal(Vector3 v)
        {
            if (Mathf.Abs(v.x) >= Mathf.Abs(v.z))
                return new Vector3i(v.x >= 0f ? 1 : -1, 0, 0);

            return new Vector3i(0, 0, v.z >= 0f ? 1 : -1);
        }

        // =========================================================
        // POSITION / TARGETING HELPERS
        // =========================================================

        private static Vector3i GetHitPos(WorldRayHitInfo hitInfo, ItemActionAttack.AttackHitInfo attackHitInfo)
        {
            try
            {
                if (attackHitInfo != null && attackHitInfo.hitPosition != Vector3i.zero)
                    return attackHitInfo.hitPosition;

                if (hitInfo.hit.blockPos != Vector3i.zero)
                    return hitInfo.hit.blockPos;

                if (hitInfo.fmcHit.blockPos != Vector3i.zero)
                    return hitInfo.fmcHit.blockPos;
            }
            catch
            {
            }

            return Vector3i.zero;
        }

        // Tight doorway-only target check for the direct Hit hook.
        // Only the entrance block itself or immediate one-block frame neighbors qualify.
        private static bool IsDirectBreachTarget(Vector3i pos)
        {
            World world = GameManager.Instance?.World;
            if (world == null) return false;

            if (IsEntranceBlock(world, pos))
                return true;

            Vector3i[] immediate =
            {
                new Vector3i( 1, 0, 0), new Vector3i(-1, 0, 0),
                new Vector3i( 0, 1, 0), new Vector3i( 0,-1, 0),
                new Vector3i( 0, 0, 1), new Vector3i( 0, 0,-1)
            };

            for (int i = 0; i < immediate.Length; i++)
            {
                if (IsEntranceBlock(world, pos + immediate[i]))
                    return true;
            }

            return false;
        }

        private static bool IsEntranceBlock(World world, Vector3i pos)
        {
            try
            {
                if (world == null) return false;

                BlockValue bv = world.GetBlock(pos);
                if (bv.isair) return false;

                Block b = Block.list[bv.type];
                if (b == null) return false;

                string n = b.GetBlockName()?.ToLowerInvariant() ?? string.Empty;

                return n.Contains("door")
                    || n.Contains("hatch")
                    || n.Contains("bars")
                    || n.Contains("gate");
            }
            catch
            {
                return false;
            }
        }

        // =========================================================
        // ENTITY / DAMAGE HELPERS
        // =========================================================

        private static int CountNearbyZombiesAt(Vector3 pos, float radius)
        {
            World world = GameManager.Instance?.World;
            if (world?.Entities?.list == null)
                return 1;

            int count = 0;
            float radiusSq = radius * radius;

            var entities = world.Entities.list;
            for (int i = 0; i < entities.Count; i++)
            {
                if (!(entities[i] is EntityZombie zombie)) continue;
                if (!zombie.IsAlive()) continue;

                Vector3 delta = zombie.position - pos;
                if (delta.sqrMagnitude <= radiusSq)
                    count++;
            }

            return Mathf.Max(1, count);
        }

        private static void ApplySplash(World world, Vector3i pos, int damage, int entityId, Vector3i originalHitPos)
        {
            try
            {
                if (world == null || damage <= 0) return;
                if (pos == originalHitPos) return;

                // Keep widening tightly scoped to doorway / immediate frame.
                if (!IsDirectBreachTarget(pos)) return;

                BlockValue bv = world.GetBlock(pos);
                if (bv.isair) return;

                Block b = Block.list[bv.type];
                if (b == null) return;

                WorldDamageShim.DamageBlock(world, pos, damage, entityId);
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAir][WildBreach] ApplySplash error: {ex}");
            }
        }
    }

    public static class WorldDamageShim
    {
        private static bool _searched;
        private static MethodInfo _damageMethod;
        private static object[] _args;

        public static void DamageBlock(World world, Vector3i pos, int damage, int entityId)
        {
            if (world == null) return;
            Find(world);

            if (_damageMethod == null) return;

            try
            {
                var p = _damageMethod.GetParameters();

                for (int i = 0; i < p.Length; i++)
                {
                    var pt = p[i].ParameterType;

                    if (pt == typeof(Vector3i)) _args[i] = pos;
                    else if (pt == typeof(BlockValue)) _args[i] = world.GetBlock(pos);
                    else if (pt == typeof(bool)) _args[i] = false;
                    else if (pt == typeof(int))
                    {
                        string n = p[i].Name?.ToLowerInvariant() ?? "";

                        if (n.Contains("damage")) _args[i] = damage;
                        else if (n.Contains("entity")) _args[i] = entityId;
                        else _args[i] = damage;
                    }
                    else _args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                }

                _damageMethod.Invoke(world, _args);
            }
            catch
            {
            }
        }

        private static void Find(World world)
        {
            if (_searched) return;
            _searched = true;

            var t = world.GetType();
            var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            _damageMethod =
                methods.FirstOrDefault(m => m.Name == "DamageBlock") ??
                methods.FirstOrDefault(m => m.Name == "damageBlock") ??
                methods.FirstOrDefault(m => m.Name.ToLowerInvariant().Contains("damageblock"));

            if (_damageMethod != null)
                _args = new object[_damageMethod.GetParameters().Length];
        }
    }
}