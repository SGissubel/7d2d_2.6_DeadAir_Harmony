using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class DeadAirQuestTierSpawnUpgradePatch
    {
        private static readonly Dictionary<string, string[]> UpgradeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["zombieJoe"] = new[] { "zombieJoeRadiated", "zombieJoeCharged" },
            ["zombieDarlene"] = new[] { "zombieDarleneRadiated", "zombieDarleneCharged", "zombieDarleneInfernal" },
            ["zombieArlene"] = new[] { "zombieArleneRadiated", "zombieArleneCharged" },
            ["zombieMarlene"] = new[] { "zombieMarleneRadiated", "zombieMarleneCharged" },
            ["zombiePartyGirl"] = new[] { "zombiePartyGirlRadiated", "zombiePartyGirlCharged" },
            ["zombieBusinessMan"] = new[] { "zombieBusinessManRadiated", "zombieBusinessManCharged", "zombieBusinessManInfernal" },
            ["zombieSteve"] = new[] { "zombieSteveRadiated", "zombieSteveCharged" },
            ["zombieTomClark"] = new[] { "zombieTomClarkRadiated", "zombieTomClarkCharged" },
            ["zombieUtilityWorker"] = new[] { "zombieUtilityWorkerRadiated", "zombieUtilityWorkerCharged", "zombieUtilityWorkerInfernal" },
            ["zombieBurnt"] = new[] { "zombieBurntRadiated", "zombieBurntCharged", "zombieBurntInfernal" },
            ["zombieRancher"] = new[] { "zombieRancherRadiated", "zombieRancherCharged", "zombieRancherInfernal" },
            ["zombieNurse"] = new[] { "zombieNurseRadiated", "zombieNurseCharged" },
            ["zombieBiker"] = new[] { "zombieBikerRadiated", "zombieBikerInfernal" },
            ["zombieFatCop"] = new[] { "zombieFatCopRadiated", "zombieFatCopInfernal" },
            ["zombieSoldier"] = new[] { "zombieSoldierRadiated", "zombieSoldierInfernal" },
            ["zombieMaleHazmat"] = new[] { "zombieMaleHazmatRadiated", "zombieMaleHazmatCharged", "zombieMaleHazmatInfernal" }
        };

        private static readonly HashSet<int> ProcessedEntityIds = new HashSet<int>();
        private static readonly Dictionary<string, float> RecentSpawnKeys = new Dictionary<string, float>();
        private const float RepeatCooldownSeconds = 20f;

        private static bool _isSpawningReplacement = false;

        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("SleeperVolume");
            if (t == null) yield break;

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

        private static bool IsActiveQuestClear(World world)
        {
            Quest q = GetRelevantActiveQuest(world);
            if (q == null)
                return false;

            return q.QuestTags.Test_AnySet(QuestEventManager.clearTag);
        }

        static void Postfix(object __instance)
        {
            try
            {
                if (_isSpawningReplacement) return;
                if (__instance == null) return;

                var world = GameManager.Instance?.World;
                if (world == null) return;

                int actualQuestTier = TryGetActiveQuestTier(world);
                int effectiveQuestTier = DeadAirDebugQuestTier.GetEffectiveTier(actualQuestTier);

                bool debugMode = DeadAirDebugQuestTier.Enabled;

                bool isTargetQuest = debugMode || (IsActiveQuestInfested(world) && IsActiveQuestClear(world));

                if (!isTargetQuest)
                    return;

                if (effectiveQuestTier < 4)
                    return;

                Vector3? centerOpt = TryGetVolumeCenter(__instance);
                if (!centerOpt.HasValue)
                    return;

                Vector3 center = centerOpt.Value;

                if (!debugMode)
                {
                    if (!IsInsideActiveQuestPoi(center, world))
                        return;
                }

                // Only inspect nearby entities around the sleeper volume center
                var entities = world.Entities?.list;
                if (entities == null || entities.Count == 0)
                    return;

                float now = Time.realtimeSinceStartup;
                CleanupRecentSpawnKeys(now);

                for (int i = 0; i < entities.Count; i++)
                {
                    var entity = entities[i];
                    if (!(entity is EntityEnemy enemy)) continue;
                    if (enemy == null) continue;

                    // Keep this local to the volume that just spawned
                    if (Vector3.Distance(enemy.position, center) > 8f)
                        continue;

                    string baseName = enemy.EntityName;
                    if (string.IsNullOrEmpty(baseName)) continue;

                    if (baseName.IndexOf("Radiated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        baseName.IndexOf("Charged", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        baseName.IndexOf("Infernal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        baseName.IndexOf("Feral", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    if (ProcessedEntityIds.Contains(enemy.entityId))
                        continue;

                    string spawnKey = MakeSpawnKey(baseName, enemy.position);

                    if (RecentSpawnKeys.TryGetValue(spawnKey, out float lastTime))
                    {
                        if (now - lastTime < RepeatCooldownSeconds)
                            continue;
                    }

                    if (!UpgradeMap.TryGetValue(baseName, out var options) || options == null || options.Length == 0)
                        continue;

                    if (!ShouldUpgrade(effectiveQuestTier))
                        continue;

                    string replacement = PickReplacement(options, effectiveQuestTier);
                    if (string.IsNullOrEmpty(replacement) || replacement.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    ProcessedEntityIds.Add(enemy.entityId);
                    RecentSpawnKeys[spawnKey] = now;

                    if (ReplaceEntity(world, enemy, replacement))
                    {
                        CompatLog.Out($"[DeadAir] Sleeper upgraded {baseName} -> {replacement} | actualTier={actualQuestTier} effectiveTier={effectiveQuestTier} debug={debugMode}");
                    }
                }
            }
            catch (Exception e)
            {
                CompatLog.Out($"[DeadAir] QuestTierSpawnUpgradePatch error: {e}");
            }
        }

        private static Vector3? TryGetVolumeCenter(object sleeperVolume)
        {
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
            catch { }

            return null;
        }
        private static string MakeSpawnKey(string entityName, Vector3 pos)
        {
            int x = Mathf.RoundToInt(pos.x);
            int y = Mathf.RoundToInt(pos.y);
            int z = Mathf.RoundToInt(pos.z);
            return $"{entityName}@{x},{y},{z}";
        }

        private static void CleanupRecentSpawnKeys(float now)
        {
            if (RecentSpawnKeys.Count == 0) return;

            List<string> expired = null;

            foreach (var kvp in RecentSpawnKeys)
            {
                if (now - kvp.Value >= RepeatCooldownSeconds)
                {
                    expired ??= new List<string>();
                    expired.Add(kvp.Key);
                }
            }

            if (expired == null) return;

            for (int i = 0; i < expired.Count; i++)
                RecentSpawnKeys.Remove(expired[i]);
        }

        private static bool ShouldUpgrade(int tier)
        {
            float chance = tier switch
            {
                4 => 0.40f,
                5 => 0.80f,
                _ => 0.95f
            };

            return UnityEngine.Random.value < chance;
        }

        private static string PickReplacement(string[] options, int tier)
        {
            if (options == null || options.Length == 0)
                return null;

            if (tier == 4)
            {
                foreach (var opt in options)
                {
                    if (opt.IndexOf("Radiated", StringComparison.OrdinalIgnoreCase) >= 0)
                        return opt;
                }

                return options[0];
            }

            if (tier == 5)
            {
                var tier5 = new List<string>();
                foreach (var opt in options)
                {
                    if (opt.IndexOf("Radiated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        opt.IndexOf("Infernal", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        tier5.Add(opt);
                    }
                }

                if (tier5.Count > 0)
                    return tier5[UnityEngine.Random.Range(0, tier5.Count)];

                return options[UnityEngine.Random.Range(0, options.Length)];
            }

            // Tier 6+
            // Prefer Charged/Infernal over Radiated
            var tier6 = new List<string>();
            foreach (var opt in options)
            {
                if (opt.IndexOf("Charged", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    opt.IndexOf("Infernal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tier6.Add(opt);
                }
            }

            if (tier6.Count > 0)
                return tier6[UnityEngine.Random.Range(0, tier6.Count)];

            return options[UnityEngine.Random.Range(0, options.Length)];
        }

        private static bool ReplaceEntity(World world, EntityEnemy oldEnt, string replacementName)
        {
            Vector3 pos = oldEnt.position;
            Vector3 rot = oldEnt.rotation;

            var newEnt = TryCreateEntity(replacementName, pos);
            if (newEnt == null)
            {
                CompatLog.Out($"[DeadAir] FAILED replacement: {oldEnt.EntityName} -> {replacementName} at {pos}");
                return false;
            }

            try
            {
                _isSpawningReplacement = true;

                oldEnt.MarkToUnload();
                newEnt.rotation = rot;
                world.SpawnEntityInWorld(newEnt);
                return true;
            }
            finally
            {
                _isSpawningReplacement = false;
            }
        }

        private static Entity TryCreateEntity(string entityName, Vector3 pos)
        {
            try
            {
                var fromString = AccessTools.Method(typeof(EntityClass), "FromString", new[] { typeof(string) });
                if (fromString == null)
                {
                    CompatLog.Out($"[DeadAir] FromString method not found for: {entityName}");
                    return null;
                }

                object fromStringResult = fromString.Invoke(null, new object[] { entityName });
                if (fromStringResult == null)
                {
                    CompatLog.Out($"[DeadAir] FromString returned null for: {entityName}");
                    return null;
                }

                if (fromStringResult is EntityClass entityClassObj)
                {
                    var createByClass = AccessTools.Method(typeof(EntityFactory), "CreateEntity", new[] { entityClassObj.GetType(), typeof(Vector3) });
                    if (createByClass != null)
                    {
                        var ent = createByClass.Invoke(null, new object[] { entityClassObj, pos }) as Entity;
                        if (ent != null)
                            return ent;
                    }

                    int classId = -1;

                    var fld = AccessTools.Field(entityClassObj.GetType(), "entityClassId");
                    if (fld != null && fld.FieldType == typeof(int))
                        classId = (int)fld.GetValue(entityClassObj);
                    else
                    {
                        var prop = AccessTools.Property(entityClassObj.GetType(), "entityClassId");
                        if (prop != null && prop.PropertyType == typeof(int))
                            classId = (int)prop.GetValue(entityClassObj);
                    }

                    if (classId > 0)
                    {
                        var createById = AccessTools.Method(typeof(EntityFactory), "CreateEntity", new[] { typeof(int), typeof(Vector3) });
                        if (createById != null)
                        {
                            var ent = createById.Invoke(null, new object[] { classId, pos }) as Entity;
                            if (ent != null)
                                return ent;
                        }
                    }

                    CompatLog.Out($"[DeadAir] EntityClass path failed for: {entityName}");
                    return null;
                }

                if (fromStringResult is int id)
                {
                    var createById = AccessTools.Method(typeof(EntityFactory), "CreateEntity", new[] { typeof(int), typeof(Vector3) });
                    if (createById != null)
                    {
                        var ent = createById.Invoke(null, new object[] { id, pos }) as Entity;
                        if (ent != null)
                            return ent;
                    }

                    CompatLog.Out($"[DeadAir] Int path failed for: {entityName} (value={id})");
                    return null;
                }

                CompatLog.Out($"[DeadAir] Unsupported FromString result for: {entityName}");
                return null;
            }
            catch (Exception e)
            {
                CompatLog.Out($"[DeadAir] FAILED CreateEntity for: {entityName} | {e}");
                return null;
            }
        }

        private static Quest GetRelevantActiveQuest(World world)
        {
            EntityPlayerLocal epl = world.GetPrimaryPlayer();
            if (epl == null)
                return null;

            Quest q = epl.QuestJournal.ActiveQuest;
            if (q != null && q.RallyMarkerActivated)
                return q;

            // Fallback for cases where ActiveQuest was cleared late in quest flow
            var quests = epl.QuestJournal.quests;
            if (quests == null)
                return null;

            for (int i = 0; i < quests.Count; i++)
            {
                var candidate = quests[i];
                if (candidate == null) continue;
                if (!candidate.RallyMarkerActivated) continue;
                if (!candidate.Active) continue;

                return candidate;
            }

            return null;
        }

        private static bool IsActiveQuestInfested(World world)
        {
            Quest q = GetRelevantActiveQuest(world);
            if (q == null)
                return false;

            return q.QuestTags.Test_AnySet(QuestEventManager.infestedTag);
        }

        private static int TryGetActiveQuestTier(World world)
        {
            Quest q = GetRelevantActiveQuest(world);
            if (q == null)
                return 0;

            return q.QuestClass.DifficultyTier;
        }

        private static bool IsInsideActiveQuestPoi(Vector3 pos, World world)
        {
            Quest q = GetRelevantActiveQuest(world);
            if (q == null)
                return false;

            if (!q.GetPositionData(out Vector3 poiPos, Quest.PositionDataTypes.POIPosition) ||
                !q.GetPositionData(out Vector3 poiSize, Quest.PositionDataTypes.POISize))
            {
                return false;
            }

            Rect xz = new Rect(poiPos.x, poiPos.z, poiSize.x, poiSize.z);
            return xz.Contains(new Vector2(pos.x, pos.z));
        }
    }
}