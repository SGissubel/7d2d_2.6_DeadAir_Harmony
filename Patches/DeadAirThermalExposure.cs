using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch(typeof(EntityPlayerLocal), "LateUpdate")]
    public static class DeadAirThermalExposurePatch
    {
        private const float UpdateIntervalSeconds = 1.0f;
        private static readonly Dictionary<int, float> NextUpdateByEntityId = new Dictionary<int, float>();
        /// <summary>
        /// Seconds spent in buffElementFreezing or buffElementSweltering since last HP tick (1 Hz, reset when safe).
        /// </summary>
        private static readonly Dictionary<int, int> ExtremeThermalSecondsByEntityId = new Dictionary<int, int>();
        /// <summary>
        /// Seconds spent soaked + cold (not yet freezing) since last HP tick.
        /// </summary>
        private static readonly Dictionary<int, int> WetColdSecondsByEntityId = new Dictionary<int, int>();
        private const int ExtremeThermalHealthLossSeconds = 30;
        private const int WetColdHealthLossSeconds = 40;
        private static readonly string[] ManagedBuffs =
        {
            "buffDeadAirWetShockOutdoor1",
            "buffDeadAirWetShockOutdoor2",
            "buffDeadAirWetShockOutdoor3",
            "buffDeadAirWetResidual1",
            "buffDeadAirWetResidual2",
            "buffDeadAirColdEmergency"
        };
        public static void Postfix(EntityPlayerLocal __instance)
        {
            if (__instance == null || __instance.Buffs == null || __instance.IsDead())
            {
                return;
            }
            int entityId = __instance.entityId;
            float now = Time.time;
            if (NextUpdateByEntityId.TryGetValue(entityId, out float nextUpdate) && now < nextUpdate)
            {
                return;
            }
            NextUpdateByEntityId[entityId] = now + UpdateIntervalSeconds;
            try
            {
                UpdateThermalState(__instance);
                ApplyExtremeThermalHealthDrain(__instance);
                ApplyWetColdHealthDrain(__instance);
            }
            catch (Exception ex)
            {
                CompatLog.Warning($"[DeadAir Thermal] Exception updating thermal state: {ex}");
            }
        }
        /// <summary>
        /// While core temp is in freezing or sweltering range (game buffs buffElementFreezing / buffElementSweltering),
        /// lose 1 HP every 30 seconds of real exposure time (throttled by the same 1 Hz tick as this patch).
        /// </summary>
        private static void ApplyExtremeThermalHealthDrain(EntityPlayerLocal player)
        {
            int entityId = player.entityId;
            bool extreme = player.Buffs.HasBuff("buffElementFreezing") || player.Buffs.HasBuff("buffElementSweltering");
            if (!extreme)
            {
                ExtremeThermalSecondsByEntityId.Remove(entityId);
                return;
            }
            if (!ExtremeThermalSecondsByEntityId.TryGetValue(entityId, out int seconds))
            {
                seconds = 0;
            }
            seconds++;
            if (seconds < ExtremeThermalHealthLossSeconds)
            {
                ExtremeThermalSecondsByEntityId[entityId] = seconds;
                return;
            }
            ExtremeThermalSecondsByEntityId[entityId] = 0;
            if (player.IsDead())
            {
                return;
            }
            player.AddHealth(-1);
            player.FireEvent(MinEventTypes.onSelfDamagedSelf);
        }
        /// <summary>
        /// Soaked + cold (core temp still in "cold" band, not freezing): steady HP loss. Slightly slower tick than true freezing.
        /// </summary>
        private static void ApplyWetColdHealthDrain(EntityPlayerLocal player)
        {
            int entityId = player.entityId;
            bool isWet = player.Buffs.HasBuff("buffElementWet");
            bool isCold = player.Buffs.HasBuff("buffElementCold");
            bool isFreezing = player.Buffs.HasBuff("buffElementFreezing");
            bool wetColdHypothermia = isWet && isCold && !isFreezing;
            if (!wetColdHypothermia)
            {
                WetColdSecondsByEntityId.Remove(entityId);
                return;
            }
            if (!WetColdSecondsByEntityId.TryGetValue(entityId, out int seconds))
            {
                seconds = 0;
            }
            seconds++;
            if (seconds < WetColdHealthLossSeconds)
            {
                WetColdSecondsByEntityId[entityId] = seconds;
                return;
            }
            WetColdSecondsByEntityId[entityId] = 0;
            if (player.IsDead())
            {
                return;
            }
            player.AddHealth(-1);
            player.FireEvent(MinEventTypes.onSelfDamagedSelf);
        }
        private static void UpdateThermalState(EntityPlayerLocal player)
        {
            bool isWet = player.Buffs.HasBuff("buffElementWet");
            bool isCold = player.Buffs.HasBuff("buffElementCold");
            bool isFreezing = player.Buffs.HasBuff("buffElementFreezing");
            // If not wet, clear the supplemental wet/cold model entirely.
            if (!isWet)
            {
                RemoveManagedBuffs(player);
                return;
            }
            bool isSheltered = IsSheltered(player);
            float heldHeat = GetHeldHeatBonus(player);
            float nearbyHeat = GetNearbyHeatBonus(player);
            float totalHeat = heldHeat + nearbyHeat;
            // Base shock stage from current condition.
            // This approximates "effective ambient is immediately worse when wet"
            // without having to directly rewrite the game's internal temp evaluator.
            int outdoorShockStage = 0;
            int residualStage = 0;
            bool emergency = false;
            if (!isSheltered)
            {
                if (isFreezing)
                {
                    outdoorShockStage = 3;
                }
                else if (isCold)
                {
                    outdoorShockStage = 2;
                }
                else
                {
                    outdoorShockStage = 0;
                }
            }
            else
            {
                // Even inside, being wet should keep a lingering chill until dry.
                if (isFreezing)
                {
                    residualStage = 2;
                }
                else if (isCold)
                {
                    residualStage = 2;
                }
                else
                {
                    residualStage = 0;
                }
            }
            // Heat offsets: torch helps, fire helps a lot more, but neither should fully erase wet cold outside.
            // Reduction thresholds are intentionally conservative.
            int reduction = GetStageReduction(totalHeat, isSheltered);
            outdoorShockStage = Math.Max(0, outdoorShockStage - reduction);
            residualStage = Math.Max(0, residualStage - reduction);
            // True emergency drain only for soaked + freezing + outdoors + weak heat support.
            emergency = isWet && isFreezing && !isSheltered && totalHeat < 10f;
            ApplyManagedBuffs(player, outdoorShockStage, residualStage, emergency);
        }
        private static void ApplyManagedBuffs(EntityPlayerLocal player, int outdoorShockStage, int residualStage, bool emergency)
        {
            // Clear all first so we never end up with overlapping supplemental states.
            RemoveManagedBuffs(player);
            switch (outdoorShockStage)
            {
                case 1:
                    player.Buffs.AddBuff("buffDeadAirWetShockOutdoor1");
                    break;
                case 2:
                    player.Buffs.AddBuff("buffDeadAirWetShockOutdoor2");
                    break;
                case 3:
                    player.Buffs.AddBuff("buffDeadAirWetShockOutdoor3");
                    break;
            }
            switch (residualStage)
            {
                case 1:
                    player.Buffs.AddBuff("buffDeadAirWetResidual1");
                    break;
                case 2:
                    player.Buffs.AddBuff("buffDeadAirWetResidual2");
                    break;
            }
            if (emergency)
            {
                player.Buffs.AddBuff("buffDeadAirColdEmergency");
            }
        }
        private static void RemoveManagedBuffs(EntityPlayerLocal player)
        {
            foreach (string buffName in ManagedBuffs)
            {
                if (player.Buffs.HasBuff(buffName))
                {
                    player.Buffs.RemoveBuff(buffName);
                }
            }
        }
        private static int GetStageReduction(float totalHeat, bool isSheltered)
        {
            // Torch alone should help, but not cure being soaked outdoors.
            // Fire should matter more. Indoors + decent fire can offset more.
            if (isSheltered)
            {
                if (totalHeat >= 18f) return 2;
                if (totalHeat >= 8f) return 1;
                return 0;
            }
            // Outdoors is harsher: even with heat, wet exposure still bites.
            if (totalHeat >= 20f) return 1;
            return 0;
        }
        private static float GetHeldHeatBonus(EntityPlayerLocal player)
        {
            try
            {
                ItemValue heldItem = player.inventory?.holdingItemItemValue ?? ItemValue.None;
                if (!heldItem.IsEmpty())
                {
                    string itemName = heldItem.ItemClass?.Name ?? string.Empty;
                    // Tune this list to your actual item names.
                    if (itemName.IndexOf("torch", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return 5f;
                    }
                    if (itemName.IndexOf("lantern", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return 3f;
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Warning($"[DeadAir Thermal] Error checking held heat item: {ex}");
            }
            return 0f;
        }
        private static float GetNearbyHeatBonus(EntityPlayerLocal player)
        {
            try
            {
                World world = GameManager.Instance.World;
                Vector3i center = World.worldToBlockPos(player.position);
                float bestHeat = 0f;
                const int radius = 6;
                for (int x = -radius; x <= radius; x++)
                {
                    for (int y = -2; y <= 2; y++)
                    {
                        for (int z = -radius; z <= radius; z++)
                        {
                            Vector3i pos = new Vector3i(center.x + x, center.y + y, center.z + z);
                            BlockValue blockValue = world.GetBlock(pos);
                            if (blockValue.isair || blockValue.Block == null)
                            {
                                continue;
                            }
                            string blockName = blockValue.Block.GetBlockName();
                            if (string.IsNullOrEmpty(blockName))
                            {
                                continue;
                            }
                            float distance = Vector3.Distance(player.position, pos.ToVector3());
                            // Campfire / fireplace / burning barrel style heat sources.
                            if (blockName.IndexOf("campfire", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                bestHeat = Math.Max(bestHeat, DistanceWeightedHeat(12f, distance, 6f));
                            }
                            else if (blockName.IndexOf("fireplace", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                bestHeat = Math.Max(bestHeat, DistanceWeightedHeat(14f, distance, 6f));
                            }
                            else if (blockName.IndexOf("barrelBurning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     blockName.IndexOf("burningBarrel", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                bestHeat = Math.Max(bestHeat, DistanceWeightedHeat(10f, distance, 5f));
                            }
                            else if (blockName.IndexOf("forge", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                bestHeat = Math.Max(bestHeat, DistanceWeightedHeat(10f, distance, 5f));
                            }
                        }
                    }
                }
                return bestHeat;
            }
            catch (Exception ex)
            {
                CompatLog.Warning($"[DeadAir Thermal] Error checking nearby heat: {ex}");
                return 0f;
            }
        }
        private static float DistanceWeightedHeat(float maxHeat, float distance, float maxDistance)
        {
            if (distance >= maxDistance)
            {
                return 0f;
            }
            float t = 1f - (distance / maxDistance);
            return maxHeat * t;
        }
        private static bool IsSheltered(EntityPlayerLocal player)
        {
            try
            {
                World world = GameManager.Instance.World;
                Vector3i pos = World.worldToBlockPos(player.position);
                // Check a reasonable distance above the player for a solid overhead block.
                // We want "roof / ceiling / cave overhead", not perfect sky simulation.
                const int maxCheckUp = 12;
                for (int y = 2; y <= maxCheckUp; y++)
                {
                    Vector3i checkPos = new Vector3i(pos.x, pos.y + y, pos.z);
                    BlockValue blockValue = world.GetBlock(checkPos);
                    if (blockValue.isair || blockValue.Block == null)
                    {
                        continue;
                    }
                    string blockName = blockValue.Block.GetBlockName();
                    if (string.IsNullOrEmpty(blockName))
                    {
                        // Unknown non-air block overhead: count it as shelter
                        return true;
                    }
                    string lower = blockName.ToLowerInvariant();
                    // Ignore foliage-ish overhead so trees don't count as proper shelter
                    if (lower.Contains("tree") ||
                        lower.Contains("leaf") ||
                        lower.Contains("leaves") ||
                        lower.Contains("pine") ||
                        lower.Contains("oak") ||
                        lower.Contains("bush") ||
                        lower.Contains("plant"))
                    {
                        continue;
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Warning($"[DeadAir Thermal] Error checking shelter: {ex}");
                return false;
            }
        }
    }
}
