using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
  [HarmonyPatch]
  public static class BoarWallCleavePatch
  {
      // ===== TUNING =====
      // Direct hit multiplier applied in CalculateBlockDamage
      private const float GraceDirectMultiplier = 5f;
      private const float ZombieBoarDirectMultiplier = 2.25f;

      // Splash fractions applied to neighboring blocks after a successful hit
      private const float GraceSideFrac = 2f;
      private const float GraceUpFrac = 0.50f;
      private const float GraceDownFrac = 0.35f;

      private const float ZombieBoarSideFrac = 0.55f;
      private const float ZombieBoarUpFrac = 0.35f;
      private const float ZombieBoarDownFrac = 0.20f;

      // Optional minimum fallback if GetBlockDamage() is unavailable
      private const int GraceFallbackBaseDamage = 300;
      private const int ZombieBoarFallbackBaseDamage = 180;

      // Safety guards
      private const int MaxSplashPerNeighbor = 5000;
      private const bool DamageCenterForward = false; // set true later if you want even more destruction

      private static readonly Dictionary<int, int> LastBoostedDamageByEntity = new Dictionary<int, int>();

      [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.CalculateBlockDamage))]
      [HarmonyPostfix]
      public static void CalculateBlockDamage_Postfix(EntityAlive __instance, BlockDamage block, int defaultBlockDamage, ref int __result)
      {
          try
          {
              if (__instance == null || __result <= 0) return;
              if (block == null) return;

              BoarType boarType = GetBoarType(__instance);
              if (boarType == BoarType.None) return;

              float mult = GetDirectMultiplier(boarType);

              int boosted = Mathf.RoundToInt(__result * mult);
              if (boosted > __result)
                  __result = boosted;

              LastBoostedDamageByEntity[__instance.entityId] = __result;
          }
          catch (Exception ex)
          {
              Debug.Log($"[DeadAir][BoarWallCleave] CalculateBlockDamage_Postfix error: {ex}");
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

              BoarType boarType = GetBoarType(__instance);
              if (boarType == BoarType.None) return;

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

              int mainDamage = GetMainDamage(__instance, boarType);
              if (mainDamage <= 0) return;

              var pattern = GetImpactPattern(hitPos, __instance, world);

              ApplySplash(world, pattern.Left,  ClampSplash(mainDamage, GetSideFrac(boarType)), __instance.entityId);
              ApplySplash(world, pattern.Right, ClampSplash(mainDamage, GetSideFrac(boarType)), __instance.entityId);
              ApplySplash(world, pattern.Up,    ClampSplash(mainDamage, GetUpFrac(boarType)),   __instance.entityId);

              // Optional lower gouge for bigger "tear downward" feel
              ApplySplash(world, pattern.Down,  ClampSplash(mainDamage, GetDownFrac(boarType)), __instance.entityId);

              // Optional extra forward pressure
              if (DamageCenterForward && pattern.Forward != hitPos)
                  ApplySplash(world, pattern.Forward, ClampSplash(mainDamage, GetSideFrac(boarType) * 0.85f), __instance.entityId);
          }
          catch (Exception ex)
          {
              Debug.Log($"[DeadAir][BoarWallCleave] ExecuteDestroyBlockBehavior_Postfix error: {ex}");
          }
      }

      // ===== ENTITY IDENTIFICATION =====

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
          {
              Debug.Log($"[DeadAir][BoarPushDamage] Matched boar entity runtime={entity.GetType().FullName}");
              return BoarType.Grace;
          }
          if (runtimeName.Contains("zombieboar"))
          {
              Debug.Log($"[DeadAir][BoarPushDamage] Matched boar entity runtime={entity.GetType().FullName}"); 
              return BoarType.ZombieBoar;
          }

          string className = GetEntityClassName(entity).ToLowerInvariant();

          if (className == "animalbossgrace")
          {
            Debug.Log($"[DeadAir][BoarPushDamage] Matched boar entity runtime={entity.GetType().FullName}");
            return BoarType.Grace;
          }
          if (className == "animalzombieboar")
          {
            Debug.Log($"[DeadAir][BoarPushDamage] Matched boar entity runtime={entity.GetType().FullName}");
            return BoarType.ZombieBoar;
          }

          return BoarType.None;
      }

      private static readonly Dictionary<Type, string> CachedEntityClassNames = new Dictionary<Type, string>();

    private static string GetEntityClassName(EntityAlive entity)
    {
        return DeadAirReflectionHelpers.GetEntityClassLikeName(entity);
    }
      // ===== TUNING LOOKUPS =====

      private static float GetDirectMultiplier(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GraceDirectMultiplier,
              BoarType.ZombieBoar => ZombieBoarDirectMultiplier,
              _ => 1f
          };
      }

      private static float GetSideFrac(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GraceSideFrac,
              BoarType.ZombieBoar => ZombieBoarSideFrac,
              _ => 0f
          };
      }

      private static float GetUpFrac(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GraceUpFrac,
              BoarType.ZombieBoar => ZombieBoarUpFrac,
              _ => 0f
          };
      }

      private static float GetDownFrac(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GraceDownFrac,
              BoarType.ZombieBoar => ZombieBoarDownFrac,
              _ => 0f
          };
      }

      private static int GetFallbackBaseDamage(BoarType type)
      {
          return type switch
          {
              BoarType.Grace => GraceFallbackBaseDamage,
              BoarType.ZombieBoar => ZombieBoarFallbackBaseDamage,
              _ => 25
          };
      }

      private static int GetMainDamage(EntityAlive entity, BoarType type)
      {
          if (entity == null) return 0;

          if (LastBoostedDamageByEntity.TryGetValue(entity.entityId, out int cached) && cached > 0)
              return cached;

          int baseDmg = TryGetIntMethod(entity, "GetBlockDamage") ?? GetFallbackBaseDamage(type);
          return Mathf.RoundToInt(baseDmg * GetDirectMultiplier(type));
      }

      private static int ClampSplash(int mainDamage, float frac)
      {
          int value = Mathf.Max(1, Mathf.RoundToInt(mainDamage * frac));
          return Mathf.Min(value, MaxSplashPerNeighbor);
      }

      // ===== IMPACT SHAPE =====

      private struct ImpactPattern
      {
          public Vector3i Left;
          public Vector3i Right;
          public Vector3i Up;
          public Vector3i Down;
          public Vector3i Forward;
      }

      private static ImpactPattern GetImpactPattern(Vector3i hitPos, EntityAlive entity, World world)
      {
          // We derive left/right from the boar -> target block direction on the XZ plane.
          Vector3 dir = Vector3.zero;

          try
          {
              Vector3 hitCenter = new Vector3(hitPos.x + 0.5f, hitPos.y + 0.5f, hitPos.z + 0.5f);
              dir = (hitCenter - entity.position);
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

          Vector3 right = new Vector3(dir.z, 0f, -dir.x); // 90-degree rotate

          Vector3i forward = ToCardinal(dir);
          Vector3i rightCard = ToCardinal(right);

          // Prevent degenerate zero cardinals
          if (rightCard == Vector3i.zero)
              rightCard = new Vector3i(1, 0, 0);

          return new ImpactPattern
          {
              Left    = hitPos - rightCard,
              Right   = hitPos + rightCard,
              Up      = hitPos + Vector3i.up,
              Down    = hitPos + Vector3i.down,
              Forward = hitPos + forward
          };
      }

      private static Vector3i ToCardinal(Vector3 v)
      {
          if (Mathf.Abs(v.x) >= Mathf.Abs(v.z))
              return new Vector3i(v.x >= 0f ? 1 : -1, 0, 0);

          return new Vector3i(0, 0, v.z >= 0f ? 1 : -1);
      }

      // ===== BLOCK FILTERS =====

      private static bool ShouldSkipSplashBlock(Block block, Vector3i pos, World world)
      {
          try
          {
              if (block == null) return true;

              string name = block.GetBlockName()?.ToLowerInvariant() ?? "";
              if (string.IsNullOrEmpty(name)) return false;

              // Skip indestructible/special stuff if their names reveal it
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

      // ===== HELPERS =====

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

  public static class BoarWorldDamageShim
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
                  Type pt = p[i].ParameterType;

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
          catch (Exception ex)
          {
              Debug.Log($"[DeadAir][BoarWallCleave] BoarWorldDamageShim error: {ex}");
          }
      }

      private static void Find(World world)
      {
          if (_searched) return;
          _searched = true;

          Type t = world.GetType();
          MethodInfo[] methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

          _damageMethod =
              methods.FirstOrDefault(m => m.Name == "DamageBlock") ??
              methods.FirstOrDefault(m => m.Name == "damageBlock") ??
              methods.FirstOrDefault(m => m.Name.ToLowerInvariant().Contains("damageblock"));

          if (_damageMethod != null)
              _args = new object[_damageMethod.GetParameters().Length];
      }
  }
}