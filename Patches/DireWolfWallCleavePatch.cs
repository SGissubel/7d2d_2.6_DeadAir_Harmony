using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
  [HarmonyPatch]
  public static class DireWolfWallCleavePatch
  {
      // ===== DIRE WOLF ONLY =====
      // Mirrors the successful Grace/Bear architecture,
      // but tuned to ~75% of Bear/Grace destructive strength.

      private const float DireWolfDirectMultiplier = 3.75f;

      private const float DireWolfSideFrac = 1.5f;
      private const float DireWolfUpFrac = 0.375f;
      private const float DireWolfDownFrac = 0.2625f;

      private const int DireWolfFallbackBaseDamage = 225;

      private const int MaxSplashPerNeighbor = 5000;
      private const bool DamageCenterForward = false;

      private static readonly Dictionary<int, int> LastBoostedDamageByEntity = new Dictionary<int, int>();

      [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.CalculateBlockDamage))]
      [HarmonyPostfix]
      public static void CalculateBlockDamage_Postfix(EntityAlive __instance, BlockDamage block, int defaultBlockDamage, ref int __result)
      {
          try
          {
              if (__instance == null || __result <= 0) return;
              if (block == null) return;
              if (!IsDireWolf(__instance)) return;

              int boosted = Mathf.RoundToInt(__result * DireWolfDirectMultiplier);
              if (boosted > __result)
                  __result = boosted;

              LastBoostedDamageByEntity[__instance.entityId] = __result;
          }
          catch (Exception ex)
          {
              Debug.Log($"[DeadAir][DireWolfWallCleave] CalculateBlockDamage_Postfix error: {ex}");
          }
      }

      [HarmonyPatch(typeof(EntityAlive), "ExecuteDestroyBlockBehavior")]
      [HarmonyPostfix]
      public static void ExecuteDestroyBlockBehavior_Postfix(EntityAlive __instance, object behavior, object attackHitInfo, ref bool __result)
      {
          try
          {
              if (!__result) return;
              if (__instance == null) return;
              if (!IsDireWolf(__instance)) return;
              if (!GetBoolProperty(__instance, "IsBreakingBlocks")) return;

              Vector3i hitPos = GetVector3iFromAttackHitInfo(attackHitInfo);
              if (hitPos == Vector3i.zero) return;

              World world = GameManager.Instance?.World;
              if (world == null) return;

              BlockValue hitBV = world.GetBlock(hitPos);
              if (hitBV.isair) return;

              Block hitBlock = Block.list[hitBV.type];
              if (hitBlock == null) return;
              if (ShouldSkipSplashBlock(hitBlock, hitPos, world)) return;

              int mainDamage = GetMainDamage(__instance);
              if (mainDamage <= 0) return;

              var pattern = GetImpactPattern(hitPos, __instance);

              ApplySplash(world, pattern.Left,  ClampSplash(mainDamage, DireWolfSideFrac), __instance.entityId);
              ApplySplash(world, pattern.Right, ClampSplash(mainDamage, DireWolfSideFrac), __instance.entityId);
              ApplySplash(world, pattern.Up,    ClampSplash(mainDamage, DireWolfUpFrac),   __instance.entityId);
              ApplySplash(world, pattern.Down,  ClampSplash(mainDamage, DireWolfDownFrac), __instance.entityId);

              if (DamageCenterForward && pattern.Forward != hitPos)
                  ApplySplash(world, pattern.Forward, ClampSplash(mainDamage, DireWolfSideFrac * 0.85f), __instance.entityId);
          }
          catch (Exception ex)
          {
              Debug.Log($"[DeadAir][DireWolfWallCleave] ExecuteDestroyBlockBehavior_Postfix error: {ex}");
          }
      }

      private static bool IsDireWolf(EntityAlive entity)
      {
          if (entity == null) return false;

          string runtimeName = entity.GetType().Name.ToLowerInvariant();
          if (runtimeName.Contains("direwolf"))
              return true;

          string className = GetEntityClassName(entity).ToLowerInvariant();
          return className == "animaldirewolf";
      }

      private static readonly Dictionary<Type, string> CachedEntityClassNames = new Dictionary<Type, string>();


      private static string GetEntityClassName(EntityAlive entity)
      {
          return DeadAirReflectionHelpers.GetEntityClassLikeName(entity);
      }

      private static int GetMainDamage(EntityAlive entity)
      {
          if (entity == null) return 0;

          if (LastBoostedDamageByEntity.TryGetValue(entity.entityId, out int cached) && cached > 0)
              return cached;

          int baseDmg = TryGetIntMethod(entity, "GetBlockDamage") ?? DireWolfFallbackBaseDamage;
          return Mathf.RoundToInt(baseDmg * DireWolfDirectMultiplier);
      }

      private static int ClampSplash(int mainDamage, float frac)
      {
          int value = Mathf.Max(1, Mathf.RoundToInt(mainDamage * frac));
          return Mathf.Min(value, MaxSplashPerNeighbor);
      }

      private struct ImpactPattern
      {
          public Vector3i Left;
          public Vector3i Right;
          public Vector3i Up;
          public Vector3i Down;
          public Vector3i Forward;
      }

      private static ImpactPattern GetImpactPattern(Vector3i hitPos, EntityAlive entity)
      {
          Vector3 dir = Vector3.zero;

          try
          {
              Vector3 hitCenter = new Vector3(hitPos.x + 0.5f, hitPos.y + 0.5f, hitPos.z + 0.5f);
              dir = hitCenter - entity.position;
              dir.y = 0f;

              if (dir.sqrMagnitude < 0.0001f)
                  dir = entity.GetForwardVector();
          }
          catch
          {
              dir = Vector3.forward;
          }

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
              Forward = hitPos + forward
          };
      }

      private static Vector3i ToCardinal(Vector3 v)
      {
          if (Mathf.Abs(v.x) >= Mathf.Abs(v.z))
              return new Vector3i(v.x >= 0f ? 1 : -1, 0, 0);

          return new Vector3i(0, 0, v.z >= 0f ? 1 : -1);
      }

      private static bool ShouldSkipSplashBlock(Block block, Vector3i pos, World world)
      {
          try
          {
              if (block == null) return true;

              string name = block.GetBlockName()?.ToLowerInvariant() ?? "";
              if (string.IsNullOrEmpty(name)) return false;

              if (name.Contains("bedrock")) return true;
              if (name.Contains("terrain")) return true;
              if (name.Contains("air")) return true;
          }
          catch { }

          return false;
      }

      private static void ApplySplash(World world, Vector3i pos, int damage, int entityId)
      {
          try
          {
              if (world == null || damage <= 0) return;

              BlockValue bv = world.GetBlock(pos);
              if (bv.isair) return;

              Block b = Block.list[bv.type];
              if (b == null) return;
              if (ShouldSkipSplashBlock(b, pos, world)) return;

              BoarWorldDamageShim.DamageBlock(world, pos, damage, entityId);
          }
          catch { }
      }

      private static bool GetBoolProperty(object obj, string propName)
      {
          return DeadAirReflectionHelpers.TryGetBoolProperty(obj, propName, out bool value) && value;
      }
      private static int? TryGetIntMethod(object obj, string methodName)
      {
          return DeadAirReflectionHelpers.TryGetIntMethod(obj, methodName);
      }
      private static Vector3i GetVector3iFromAttackHitInfo(object attackHitInfo)
      {
          return DeadAirReflectionHelpers.GetVector3iFromAttackHitInfo(attackHitInfo);
      }
  }
}