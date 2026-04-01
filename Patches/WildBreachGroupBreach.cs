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
    public static class WildBreachGroupBreach
    {
        private const float BaseMultiplier      = 2.10f;
        private const float PerZombieBonus      = 0.45f;
        private const float MaxMultiplier       = 6.00f;
        private const float NearbyRadius        = 3.75f;

        private const float SplashDamageFrac    = 0.65f;

        private const float EntranceDirectBonus = 1.35f;
        private const int   GroupThreshold      = 4;
        private const float GroupSurgeBonus     = 1.40f;

        private const int   RageThreshold       = 6;
        private const float RageBonus           = 1.75f;

        private static readonly string[] EntranceTags =
        {
            "door", "hatch", "bars", "bar", "windowBars", "windowbar", "gate"
        };

        private static readonly Dictionary<int, int> LastBoostedDamageByEntity = new Dictionary<int, int>();

        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.CalculateBlockDamage))]
        [HarmonyPostfix]
        public static void CalculateBlockDamage_Postfix(EntityAlive __instance, BlockDamage block, int defaultBlockDamage, ref int __result)
        {
            try
            {
                if (__instance == null || __result <= 0) return;
                if (!(__instance is EntityZombie)) return;
                if (block == null) return;

                Vector3i hitPos = GetBlockPosFromBlockDamage(block);

                if (hitPos != Vector3i.zero)
                {
                    if (!IsBreachRelevantPosition(hitPos)) return;
                }
                else
                {
                    if (!IsEntranceBlock(block)) return;
                }

                int nearbyZeds = hitPos != Vector3i.zero
                    ? CountNearbyZombiesAt(hitPos, NearbyRadius)
                    : CountNearbyZombies(__instance, NearbyRadius);

                float mult = BaseMultiplier + Math.Max(0, nearbyZeds - 1) * PerZombieBonus;
                mult = Mathf.Min(mult, MaxMultiplier);

                if (nearbyZeds >= GroupThreshold)
                    mult *= GroupSurgeBonus;

                if (nearbyZeds >= RageThreshold)
                    mult *= RageBonus;

                if (hitPos != Vector3i.zero && IsEntranceBlock(GameManager.Instance?.World, hitPos))
                    mult *= EntranceDirectBonus;

                int boosted = Mathf.RoundToInt(__result * mult);
                if (boosted > __result) __result = boosted;

                LastBoostedDamageByEntity[__instance.entityId] = __result;
            }
            catch { }
        }

        [HarmonyPatch(typeof(EntityAlive), "ExecuteDestroyBlockBehavior")]
        [HarmonyPostfix]
        public static void ExecuteDestroyBlockBehavior_Postfix(EntityAlive __instance, object behavior, object attackHitInfo, ref bool __result)
        {
            try
            {
                if (!__result) return;
                if (__instance == null) return;
                if (!(__instance is EntityZombie)) return;
                if (!GetBoolProperty(__instance, "IsBreakingBlocks")) return;

                Vector3i hitPos = GetVector3iFromAttackHitInfo(attackHitInfo);
                if (hitPos == Vector3i.zero) return;

                var world = GameManager.Instance?.World;
                if (world == null) return;

                BlockValue hitBV = world.GetBlock(hitPos);
                if (hitBV.isair) return;

                Block hitBlock = Block.list[hitBV.type];
                if (hitBlock == null) return;
                if (!IsBreachRelevantPosition(hitPos)) return;

                int nearbyZeds = CountNearbyZombiesAt(hitPos, NearbyRadius);

                int mainDamage;
                if (!LastBoostedDamageByEntity.TryGetValue(__instance.entityId, out mainDamage) || mainDamage <= 0)
                {
                    float mult = BaseMultiplier + Math.Max(0, nearbyZeds - 1) * PerZombieBonus;
                    mult = Mathf.Min(mult, MaxMultiplier);

                    if (nearbyZeds >= GroupThreshold)
                        mult *= GroupSurgeBonus;

                    if (nearbyZeds >= RageThreshold)
                        mult *= RageBonus;

                    if (IsEntranceBlock(world, hitPos))
                        mult *= EntranceDirectBonus;

                    int baseDmg = TryGetIntMethod(__instance, "GetBlockDamage") ?? 25;
                    mainDamage = Mathf.RoundToInt(baseDmg * mult);
                }

                Vector3i anchorPos = FindNearestEntranceAnchor(hitPos);
                List<Vector3i> cluster = anchorPos != Vector3i.zero
                    ? GetDoorwayCluster(anchorPos)
                    : GetFallbackCluster(hitPos);

                if (cluster.Count == 0) return;

                // In rage mode, hit more of the doorway face and hit it harder.
                int clusterHits;
                float clusterFrac;

                if (nearbyZeds >= RageThreshold)
                {
                    clusterHits = 6;
                    clusterFrac = 1.10f;
                }
                else if (nearbyZeds >= 4)
                {
                    clusterHits = 3;
                    clusterFrac = SplashDamageFrac;
                }
                else
                {
                    clusterHits = 1;
                    clusterFrac = 0.50f;
                }

                int clusterDamage = Mathf.Max(1, Mathf.RoundToInt(mainDamage * clusterFrac));

                int applied = 0;
                for (int i = 0; i < cluster.Count && applied < clusterHits; i++)
                {
                    Vector3i p = cluster[i];

                    // Avoid double-applying to the exact same block that was just hit naturally.
                    if (p == hitPos) continue;

                    BlockValue bv = world.GetBlock(p);
                    if (bv.isair) continue;

                    Block b = Block.list[bv.type];
                    if (b == null) continue;

                    WorldDamageShim.DamageBlock(world, p, clusterDamage, __instance.entityId);
                    applied++;
                }
            }
            catch { }
        }

        private static bool IsBreachRelevantPosition(Vector3i pos)
        {
            try
            {
                var world = GameManager.Instance?.World;
                if (world == null) return false;

                if (IsEntranceBlock(world, pos)) return true;

                var offsets = new[]
                {
                    new Vector3i( 1, 0, 0), new Vector3i(-1, 0, 0),
                    new Vector3i( 0, 1, 0), new Vector3i( 0,-1, 0),
                    new Vector3i( 0, 0, 1), new Vector3i( 0, 0,-1),

                    new Vector3i( 1, 1, 0), new Vector3i(-1, 1, 0),
                    new Vector3i( 1,-1, 0), new Vector3i(-1,-1, 0),

                    new Vector3i( 0, 1, 1), new Vector3i( 0, 1,-1),
                    new Vector3i( 0,-1, 1), new Vector3i( 0,-1,-1),
                };

                foreach (var o in offsets)
                {
                    if (IsEntranceBlock(world, pos + o))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static Vector3i FindNearestEntranceAnchor(Vector3i origin)
        {
            try
            {
                var world = GameManager.Instance?.World;
                if (world == null) return Vector3i.zero;

                var search = new List<Vector3i>
                {
                    origin,

                    origin + new Vector3i( 1, 0, 0), origin + new Vector3i(-1, 0, 0),
                    origin + new Vector3i( 0, 1, 0), origin + new Vector3i( 0,-1, 0),
                    origin + new Vector3i( 0, 0, 1), origin + new Vector3i( 0, 0,-1),

                    origin + new Vector3i( 1, 1, 0), origin + new Vector3i(-1, 1, 0),
                    origin + new Vector3i( 1,-1, 0), origin + new Vector3i(-1,-1, 0),

                    origin + new Vector3i( 0, 1, 1), origin + new Vector3i( 0, 1,-1),
                    origin + new Vector3i( 0,-1, 1), origin + new Vector3i( 0,-1,-1),
                };

                foreach (var p in search)
                {
                    if (IsEntranceBlock(world, p))
                        return p;
                }
            }
            catch { }

            return Vector3i.zero;
        }

        private static List<Vector3i> GetDoorwayCluster(Vector3i anchor)
        {
            // Ordered on purpose:
            // anchor first, then header, then lower sides, then mid sides, then upper sides/below.
            var raw = new[]
            {
                anchor,
                anchor + new Vector3i( 0, 1, 0),   // above door / header
                anchor + new Vector3i(-1, 0, 0),   // mid left
                anchor + new Vector3i( 1, 0, 0),   // mid right
                anchor + new Vector3i(-1,-1, 0),   // bottom left
                anchor + new Vector3i( 1,-1, 0),   // bottom right
                anchor + new Vector3i(-1, 1, 0),   // top left
                anchor + new Vector3i( 1, 1, 0),   // top right
                anchor + new Vector3i( 0,-1, 0),   // below door
            };

            return FilterUniqueSolidPositions(raw);
        }

        private static List<Vector3i> GetFallbackCluster(Vector3i center)
        {
            var raw = new[]
            {
                center,
                center + new Vector3i( 0, 1, 0),
                center + new Vector3i(-1, 0, 0),
                center + new Vector3i( 1, 0, 0),
                center + new Vector3i(-1,-1, 0),
                center + new Vector3i( 1,-1, 0),
                center + new Vector3i( 0,-1, 0),
            };

            return FilterUniqueSolidPositions(raw);
        }

        private static List<Vector3i> FilterUniqueSolidPositions(IEnumerable<Vector3i> positions)
        {
            var result = new List<Vector3i>();
            var seen = new HashSet<string>();
            var world = GameManager.Instance?.World;
            if (world == null) return result;

            foreach (var p in positions)
            {
                string key = $"{p.x},{p.y},{p.z}";
                if (!seen.Add(key)) continue;

                BlockValue bv = world.GetBlock(p);
                if (bv.isair) continue;

                Block b = Block.list[bv.type];
                if (b == null) continue;

                result.Add(p);
            }

            return result;
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

                return IsEntranceBlock(b);
            }
            catch { }

            return false;
        }

        private static bool IsEntranceBlock(BlockDamage bd)
        {
            try
            {
                var t = bd.GetType();
                var prop =
                    AccessTools.Property(t, "blockName") ??
                    AccessTools.Property(t, "BlockName") ??
                    AccessTools.Property(t, "name");

                if (prop != null)
                {
                    string n = (prop.GetValue(bd, null) as string) ?? "";
                    return LooksLikeEntranceName(n);
                }
            }
            catch { }

            return false;
        }

        private static Vector3i GetBlockPosFromBlockDamage(BlockDamage bd)
        {
            try
            {
                var t = bd.GetType();
                string[] names = { "blockPos", "BlockPos", "pos", "Pos", "hitPos", "HitPos" };

                foreach (var n in names)
                {
                    var f = AccessTools.Field(t, n);
                    if (f != null && f.GetValue(bd) is Vector3i fv) return fv;

                    var p = AccessTools.Property(t, n);
                    if (p != null && p.GetValue(bd, null) is Vector3i pv) return pv;
                }
            }
            catch { }

            return Vector3i.zero;
        }

        private static bool HasTagSafe(Block block, string tagName)
        {
            try
            {
                if (block == null || string.IsNullOrEmpty(tagName)) return false;

                var blockTagsType = AccessTools.TypeByName("BlockTags");
                if (blockTagsType == null || !blockTagsType.IsEnum) return false;

                object enumValue = Enum.Parse(blockTagsType, tagName, ignoreCase: true);

                var hasTagMethod = AccessTools.Method(block.GetType(), "HasTag", new Type[] { blockTagsType });
                if (hasTagMethod == null) return false;

                var result = hasTagMethod.Invoke(block, new object[] { enumValue });
                return result is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEntranceBlock(Block b)
        {
            try
            {
                foreach (var tag in EntranceTags)
                    if (HasTagSafe(b, tag)) return true;

                return LooksLikeEntranceName(b.GetBlockName());
            }
            catch { }

            return false;
        }

        private static bool LooksLikeEntranceName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            n = n.ToLowerInvariant();
            return n.Contains("door") || n.Contains("hatch") || n.Contains("bars") || n.Contains("bar") || n.Contains("gate");
        }

        private static int CountNearbyZombies(EntityAlive self, float radius)
        {
            try
            {
                if (self == null) return 1;
                return CountNearbyZombiesAt(self.position, radius);
            }
            catch { }

            return 1;
        }

        private static int CountNearbyZombiesAt(Vector3 pos, float radius)
        {
            try
            {
                var world = GameManager.Instance?.World;
                if (world == null) return 1;

                var bounds = new Bounds(pos, new Vector3(radius * 2f, radius * 2f, radius * 2f));

                var m = AccessTools.Method(world.GetType(), "GetEntitiesInBounds");
                if (m == null) return 1;

                var pars = m.GetParameters();

                if (pars.Length == 2 && pars[0].ParameterType == typeof(Type) && pars[1].ParameterType == typeof(Bounds))
                {
                    var list = m.Invoke(world, new object[] { typeof(EntityZombie), bounds }) as IList;
                    if (list == null) return 1;

                    int count = 0;
                    foreach (var o in list)
                        if (o is EntityZombie) count++;

                    return Math.Max(1, count);
                }

                if (pars.Length == 2 && pars[0].ParameterType == typeof(Bounds))
                {
                    object listObj = Activator.CreateInstance(pars[1].ParameterType);
                    m.Invoke(world, new object[] { bounds, listObj });

                    int count = 0;
                    if (listObj is IEnumerable enumerable)
                    {
                        foreach (var o in enumerable)
                            if (o is EntityZombie) count++;
                    }

                    return Math.Max(1, count);
                }
            }
            catch { }

            return 1;
        }

        private static bool GetBoolProperty(object obj, string propName)
        {
            try
            {
                if (obj == null) return false;
                var p = AccessTools.Property(obj.GetType(), propName);
                if (p == null) return false;
                return p.GetValue(obj, null) is bool b && b;
            }
            catch { }
            return false;
        }

        private static int? TryGetIntMethod(object obj, string methodName)
        {
            try
            {
                if (obj == null) return null;
                var m = AccessTools.Method(obj.GetType(), methodName);
                if (m == null) return null;
                var val = m.Invoke(obj, Array.Empty<object>());
                if (val is int i) return i;
            }
            catch { }
            return null;
        }

        private static Vector3i GetVector3iFromAttackHitInfo(object attackHitInfo)
        {
            if (attackHitInfo == null) return Vector3i.zero;

            try
            {
                var t = attackHitInfo.GetType();
                string[] names = { "hitBlockPos", "HitBlockPos", "blockPos", "BlockPos", "hitPos", "HitPos" };

                foreach (var n in names)
                {
                    var f = AccessTools.Field(t, n);
                    if (f != null && f.GetValue(attackHitInfo) is Vector3i fv) return fv;

                    var p = AccessTools.Property(t, n);
                    if (p != null && p.GetValue(attackHitInfo, null) is Vector3i pv) return pv;
                }
            }
            catch { }

            return Vector3i.zero;
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
            catch { }
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