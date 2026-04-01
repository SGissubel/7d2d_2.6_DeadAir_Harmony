using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
  [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnUpdateLive))]
  public static class BoarPushDamagePatch
  {
      // ===== TUNING =====
      private const float GraceTickSeconds = 999f;
      private const float ZombieBoarTickSeconds = 999f;

      private const int GracePushDamage = 1000;
      private const int GracePushDamageUpper = 1000;
      private const int GracePushDamageLower = 1000;
      private const int GracePushDamageSide = 1000;

      private const int ZombieBoarPushDamage = 1000;
      private const int ZombieBoarPushDamageUpper = 1000;
      private const int ZombieBoarPushDamageLower = 1000;
      private const int ZombieBoarPushDamageSide = 1000;

      private const float MinMoveIntent = 10f;
      private const float MaxHorizontalSpeedToCountAsStuck = 15f;

      private const bool DamageUpperForward = true;
      private const bool DamageLowerForward = true;

      private static readonly Dictionary<int, float> NextPushTimeByEntity = new Dictionary<int, float>();

      [HarmonyPostfix]
      public static void Postfix(EntityAlive __instance)
      {
          try
          {
              if (__instance == null) return;
              if (__instance.isEntityRemote) return;
              if (__instance.IsDead()) return;

              BoarType boarType = GetBoarType(__instance);
              if (boarType == BoarType.None) return;

              float now = Time.time;

              if (!NextPushTimeByEntity.TryGetValue(__instance.entityId, out float nextTime))
                  nextTime = 0f;

              if (now < nextTime) return;
              NextPushTimeByEntity[__instance.entityId] = now + GetTickRate(boarType);

              if (!ShouldApplyPushDamage(__instance)) return;

              World world = GameManager.Instance?.World;
              if (world == null) return;

              Vector3i forward = GetForwardCardinal(__instance);
              if (forward == Vector3i.zero) return;

              var candidates = GetFrontFaceCandidates(__instance, forward);
              if (candidates.Count == 0) return;

              int hitsToApply = boarType == BoarType.Grace ? 5 : 4;
              int applied = 0;

              for (int i = 0; i < candidates.Count && applied < hitsToApply; i++)
              {
                  Vector3i p = candidates[i];

                  BlockValue bv = world.GetBlock(p);
                  if (bv.isair) continue;

                  Block block = Block.list[bv.type];
                  if (block == null) continue;
                  if (ShouldSkipPushBlock(block)) continue;

                  int damage = GetPushDamageForCandidate(boarType, i);
                  if (damage <= 0) continue;

                  WorldDamageShim.DamageBlock(world, p, damage, __instance.entityId);
                  applied++;
              }

          }
          catch (Exception ex)
          {
              Debug.Log($"[DeadAir][BoarPushDamage] error: {ex}");
          }
      }

      // ===== CORE LOGIC =====

      private static bool ShouldApplyPushDamage(EntityAlive entity)
      {
          if (entity == null) return false;

          // Must be in an aggressive / block-breaking state
          bool hasTarget = HasAttackTarget(entity);
          bool isBreakingBlocks = GetBoolProperty(entity, "IsBreakingBlocks");

          if (!hasTarget && !isBreakingBlocks)
              return false;

          // Must be trying to move forward
          Vector3 forward = GetFlatForward(entity);
          if (forward.sqrMagnitude < 0.0001f)
              return false;

          Vector3 horizontalVelocity = entity.motion;
          horizontalVelocity.y = 0f;

          float actualSpeed = horizontalVelocity.magnitude;

          // "move intent" = velocity direction relative to forward direction
          float moveIntent = 0f;
          if (horizontalVelocity.sqrMagnitude > 0.0001f)
              moveIntent = Vector3.Dot(horizontalVelocity.normalized, forward);

          // Two acceptable stuck cases:
          // 1) breaking blocks and barely moving
          // 2) has target, near-zero progress, still facing into obstacle space
          bool stuck = actualSpeed <= MaxHorizontalSpeedToCountAsStuck;

          // If motion is near zero, entity may still be pushing via AI pathing.
          // So allow IsBreakingBlocks alone to qualify even if moveIntent can't be inferred.
          if (isBreakingBlocks && stuck)
              return true;

          // If there is some motion, require forward intent and low progress.
          if (hasTarget && stuck && moveIntent >= MinMoveIntent)
              return true;

          return false;
      }

      private static void TryDamagePushBlock(World world, Vector3i pos, int damage, int entityId)
      {
          try
          {
              if (world == null || damage <= 0) return;

              BlockValue bv = world.GetBlock(pos);
              if (bv.isair) return;

              Block block = Block.list[bv.type];
              if (block == null) return;
              if (ShouldSkipPushBlock(block)) return;

              WorldDamageShim.DamageBlock(world, pos, damage, entityId);
          }
          catch { }
      }

      // ===== POSITIONING =====

      private static int GetPushDamageForCandidate(BoarType type, int index)
      {
          bool isGrace = type == BoarType.Grace;

          // index mapping from GetFrontFaceCandidates priority order
          switch (index)
          {
              case 0: return isGrace ? 950 : 475; // mid-center
              case 1: return isGrace ? 750 : 300; // upper-center
              case 2:
              case 3: return isGrace ? 650 : 240; // mid-left/right
              case 4: return isGrace ? 500 : 180; // low-center
              default: return isGrace ? 350 : 120; // corners / lower side cleanup
          }
      }

      private static Vector3i GetFrontChestBlock(EntityAlive entity, Vector3i forward)
      {
          // Sample around chest-ish height and slightly forward from center.
          Vector3 pos = entity.position;

          // Tweak these if needed after first live test:
          // 0.9f forward = helps with large boar body
          // 1.0f up = chest/head-ish contact point
          Vector3 sample = pos
              + new Vector3(0.5f, 1.0f, 0.5f)
              + new Vector3(forward.x, forward.y, forward.z) * 0.9f;

          return World.worldToBlockPos(sample);
      }

      private static Vector3i GetForwardCardinal(EntityAlive entity)
      {
          Vector3 fwd = GetFlatForward(entity);
          if (fwd.sqrMagnitude < 0.0001f)
              return Vector3i.zero;

          if (Mathf.Abs(fwd.x) >= Mathf.Abs(fwd.z))
              return new Vector3i(fwd.x >= 0f ? 1 : -1, 0, 0);

          return new Vector3i(0, 0, fwd.z >= 0f ? 1 : -1);
      }

      private static Vector3i GetRightCardinal(Vector3i forward)
      {
          if (forward == Vector3i.zero) return Vector3i.zero;

          // rotate 90 degrees around Y
          return new Vector3i(forward.z, 0, -forward.x);
      }

      private static Vector3 GetFlatForward(EntityAlive entity)
      {
          try
          {
              Vector3 fwd = entity.GetForwardVector();
              fwd.y = 0f;

              if (fwd.sqrMagnitude < 0.0001f)
                  return Vector3.zero;

              return fwd.normalized;
          }
          catch
          {
              return Vector3.zero;
          }
      }

      // ===== ENTITY FILTER =====

      private enum BoarType
      {
          None,
          ZombieBoar,
          Grace
      }

      private static BoarType GetBoarType(EntityAlive entity)
      {
          if (entity == null) return BoarType.None;

          string runtimeName = entity.GetType().Name.ToLowerInvariant();
          if (runtimeName.Contains("grace"))
              return BoarType.Grace;
          if (runtimeName.Contains("zombieboar"))
              return BoarType.ZombieBoar;

          string className = GetEntityClassName(entity).ToLowerInvariant();

          if (className == "animalbossgrace")
              return BoarType.Grace;
          if (className == "animalzombieboar")
              return BoarType.ZombieBoar;

          return BoarType.None;
      }

      private static readonly Dictionary<Type, string> CachedEntityClassNames = new Dictionary<Type, string>();
      private static List<Vector3i> GetFrontFaceCandidates(EntityAlive entity, Vector3i forward)
      {
          var list = new List<Vector3i>();
          if (entity == null || forward == Vector3i.zero) return list;

          Vector3i right = GetRightCardinal(forward);
          if (right == Vector3i.zero)
              right = new Vector3i(1, 0, 0);

          Vector3 pos = entity.position;

          // front-center anchor, slightly farther forward than before
          Vector3 anchor = pos
              + new Vector3(0.5f, 0f, 0.5f)
              + new Vector3(forward.x, 0f, forward.z) * 1.05f;

          Vector3i centerLow = World.worldToBlockPos(anchor + new Vector3(0f, 0.55f, 0f));
          Vector3i centerMid = World.worldToBlockPos(anchor + new Vector3(0f, 1.05f, 0f));
          Vector3i centerUp  = World.worldToBlockPos(anchor + new Vector3(0f, 1.55f, 0f));

          // Preferred order: center first, then shoulders, then lower cleanup
          list.Add(centerMid);
          list.Add(centerUp);
          list.Add(centerMid - right);
          list.Add(centerMid + right);
          list.Add(centerLow);

          list.Add(centerUp - right);
          list.Add(centerUp + right);
          list.Add(centerLow - right);
          list.Add(centerLow + right);

          // remove duplicates
          var deduped = new List<Vector3i>();
          foreach (var p in list)
          {
              if (!deduped.Contains(p))
                  deduped.Add(p);
          }

          return deduped;
      }
      private static string GetEntityClassName(EntityAlive entity)
      {
          return DeadAirReflectionHelpers.GetEntityClassLikeName(entity);
      }

      // ===== TUNING LOOKUPS =====

      private static float GetTickRate(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GraceTickSeconds,
              BoarType.ZombieBoar => ZombieBoarTickSeconds,
              _ => 0.5f
          };
      }

      private static int GetPushDamage(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GracePushDamage,
              BoarType.ZombieBoar => ZombieBoarPushDamage,
              _ => 0
          };
      }

      private static int GetPushDamageUpper(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GracePushDamageUpper,
              BoarType.ZombieBoar => ZombieBoarPushDamageUpper,
              _ => 0
          };
      }

      private static int GetPushDamageLower(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GracePushDamageLower,
              BoarType.ZombieBoar => ZombieBoarPushDamageLower,
              _ => 0
          };
      }

      private static int GetPushDamageSide(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GracePushDamageSide,
              BoarType.ZombieBoar => ZombieBoarPushDamageSide,
              _ => 0
          };
      }

      // ===== HELPERS =====

      private static bool HasAttackTarget(EntityAlive entity)
      {
          return DeadAirReflectionHelpers.HasAttackTarget(entity);
      }

      private static bool GetBoolProperty(object obj, string propName)
      {
          return DeadAirReflectionHelpers.TryGetBoolProperty(obj, propName, out bool value) && value;
      }

      private static bool ShouldSkipPushBlock(Block block)
      {
          try
          {
              if (block == null) return true;

              string name = block.GetBlockName()?.ToLowerInvariant() ?? "";

              if (name.Contains("air")) return true;
              if (name.Contains("bedrock")) return true;
              if (name.Contains("terrain")) return true;
          }
          catch { }

          return false;
      }
  }
}