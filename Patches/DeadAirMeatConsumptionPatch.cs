using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch(typeof(ItemActionEat))]
    public static class DeadAirMeatConsumptionPatch
    {
        private struct MeatConsumeData
        {
            public bool IsCustomMeat;
            public bool IsRawCarnivore;
            public bool IsCookedCarnivore;
            public bool IsHerbivoreRaw;
            public bool IsHerbivoreCooked;
            public int ExtraFood;
            public int ExtraHealth;
            public int ExtraWater;
            public int ExtraStamina;
        }
        private struct ConsumeGate
        {
            public float LastUseTime;
            public bool Processed;
        }
        private static readonly Dictionary<int, ConsumeGate> ConsumeGates = new Dictionary<int, ConsumeGate>();
        private static bool TryGetMeatData(ItemValue itemValue, out MeatConsumeData data)
        {
            data = default;
            if (itemValue?.ItemClass == null)
                return false;
            string itemName = itemValue.ItemClass.GetItemName();
            switch (itemName)
            {
                // =========================================================
                // HERBIVORE RAW MEATS
                // =========================================================
                case "foodRawMeatStag":
                case "foodRawMeatDoe":
                case "foodRawMeatBoar":
                case "foodRawMeatRabbit":
                case "foodRawMeatChicken":
                    data.IsCustomMeat = true;
                    data.IsHerbivoreRaw = true;
                    // Harmony path (Food Tier 0 removed in items.xml for these). ~75% of vanilla raw.
                    data.ExtraFood = 1;
                    data.ExtraHealth = -1;
                    data.ExtraStamina = -1;
                    data.ExtraWater = 0;
                    return true;
                case "foodCharredMeatHerbivore":
                    data.IsCustomMeat = true;
                    data.IsHerbivoreCooked = true;
                    data.ExtraFood = 8;
                    data.ExtraHealth = 3;
                    data.ExtraStamina = 5;
                    data.ExtraWater = -4;
                    return true;
                case "foodGrilledMeatHerbivore":
                    data.IsCustomMeat = true;
                    data.IsHerbivoreCooked = true;
                    data.ExtraFood = 10;
                    data.ExtraHealth = 3;
                    data.ExtraStamina = 8;
                    data.ExtraWater = 0;
                    return true;
                case "foodBoiledMeatHerbivore":
                    data.IsCustomMeat = true;
                    data.IsHerbivoreCooked = true;
                    data.ExtraFood = 10;
                    data.ExtraHealth = 8;
                    data.ExtraStamina = 8;
                    data.ExtraWater = 8;
                    return true;
                // =========================================================
                // RAW CARNIVORE MEATS
                // =========================================================
                case "foodRawMeatBear":
                case "foodRawMeatWolf":
                case "foodRawMeatMountainLion":
                case "foodRawMeatSnake":
                    data.IsCustomMeat = true;
                    data.IsRawCarnivore = true;
                    data.ExtraFood = 4;
                    data.ExtraHealth = 1;
                    data.ExtraWater = 0;
                    data.ExtraStamina = 4;
                    return true;
                // =========================================================
                // COOKED CARNIVORE MEATS
                // =========================================================
                case "foodCharredMeatCarnivore":
                    data.IsCustomMeat = true;
                    data.IsCookedCarnivore = true;
                    data.ExtraFood = 10;
                    data.ExtraHealth = 4;
                    data.ExtraWater = 0;
                    data.ExtraStamina = 6;
                    return true;
                case "foodGrilledMeatCarnivore":
                    data.IsCustomMeat = true;
                    data.IsCookedCarnivore = true;
                    data.ExtraFood = 15;
                    data.ExtraHealth = 6;
                    data.ExtraWater = 0;
                    data.ExtraStamina = 8;
                    return true;
                case "foodBoiledMeatCarnivore":
                    data.IsCustomMeat = true;
                    data.IsCookedCarnivore = true;
                    data.ExtraFood = 18;
                    data.ExtraHealth = 8;
                    data.ExtraWater = 12;
                    data.ExtraStamina = 8;
                    return true;
                default:
                    return false;
            }
        }
        private static bool TryGetConsumableKind(ItemValue itemValue, out bool isFood, out bool isDrink)
        {
            isFood = false;
            isDrink = false;
            if (itemValue?.ItemClass == null)
                return false;
            string itemName = itemValue.ItemClass.GetItemName();
            if (string.IsNullOrEmpty(itemName))
                return false;
            // Explicit custom drinks first
            switch (itemName)
            {
                case "drinkTeaParasiteCleanse":
                case "medicalAnthelminticTonic":
                case "drinkColonCleanseTonic":
                case "resourceEthanol":
                    isDrink = true;
                    return true;
            }
            // Broad drink heuristics
            if (itemName.StartsWith("drink"))
            {
                isDrink = true;
                return true;
            }
            if (itemName.Contains("Coffee") || itemName.Contains("coffee") ||
                itemName.Contains("Beer") || itemName.Contains("beer") ||
                itemName.Contains("Tea") || itemName.Contains("tea") ||
                itemName.Contains("Water") || itemName.Contains("water") ||
                itemName.Contains("Juice") || itemName.Contains("juice"))
            {
                isDrink = true;
                return true;
            }
            // Anything else consumed via ItemActionEat, treat as food
            isFood = true;
            return true;
        }
        private static bool ShouldProcessThisConsume(ItemActionData actionData, EntityAlive ent)
        {
            if (actionData?.invData?.item == null || ent == null)
                return false;
            int entityId = ent.entityId;
            float currentUseTime = actionData.lastUseTime;
            float threshold = 0.35f;
            int holdType = actionData.invData.item.HoldType.Value;
            if (AnimationDelayData.AnimationDelay != null &&
                holdType >= 0 &&
                holdType < AnimationDelayData.AnimationDelay.Length)
            {
                threshold = AnimationDelayData.AnimationDelay[holdType].RayCast;
            }
            float elapsed = Time.time - currentUseTime;
            if (!ConsumeGates.TryGetValue(entityId, out ConsumeGate gate))
            {
                gate = new ConsumeGate
                {
                    LastUseTime = currentUseTime,
                    Processed = false
                };
            }
            // New consume started
            if (!Mathf.Approximately(gate.LastUseTime, currentUseTime))
            {
                gate.LastUseTime = currentUseTime;
                gate.Processed = false;
            }
            // Not finished yet
            if (elapsed < threshold)
            {
                ConsumeGates[entityId] = gate;
                return false;
            }
            // Already processed this bite/use
            if (gate.Processed)
            {
                ConsumeGates[entityId] = gate;
                return false;
            }
            gate.Processed = true;
            ConsumeGates[entityId] = gate;
            return true;
        }
        private static void ApplyExtraBenefits(EntityAlive ent, ItemValue itemValue, MeatConsumeData data)
        {
            if (ent == null || itemValue == null)
                return;
            if (ent is EntityPlayer player)
            {
                if (data.ExtraFood != 0)
                {
                    float currentFoodAdd = ent.Buffs.GetCustomVar("$foodAmountAdd");
                    float foodDelta = EffectManager.GetValue(
                        data.ExtraFood >= 0 ? PassiveEffects.FoodGain : PassiveEffects.FoodLoss,
                        itemValue,
                        data.ExtraFood,
                        ent
                    );
                    ent.Buffs.SetCustomVar("$foodAmountAdd", currentFoodAdd + foodDelta, true);
                    ent.Buffs.AddBuff("buffProcessConsumables");
                }
                if (data.ExtraWater != 0)
                {
                    float waterDelta = EffectManager.GetValue(
                        data.ExtraWater >= 0 ? PassiveEffects.WaterGain : PassiveEffects.WaterLoss,
                        itemValue,
                        data.ExtraWater,
                        ent
                    );
                    player.AddWater((int)waterDelta);
                }
            }
            if (data.ExtraHealth != 0)
            {
                int healthDelta = (int)EffectManager.GetValue(
                    data.ExtraHealth >= 0 ? PassiveEffects.HealthGain : PassiveEffects.HealthLoss,
                    itemValue,
                    data.ExtraHealth,
                    ent
                );
                ent.AddHealth(healthDelta);
                if (healthDelta > 0)
                    ent.FireEvent(MinEventTypes.onSelfHealedSelf);
                else if (healthDelta < 0)
                    ent.FireEvent(MinEventTypes.onSelfDamagedSelf);
            }
            if (data.ExtraStamina != 0)
            {
                float staminaDelta = EffectManager.GetValue(
                    data.ExtraStamina >= 0 ? PassiveEffects.StaminaGain : PassiveEffects.StaminaLoss,
                    itemValue,
                    data.ExtraStamina,
                    ent
                );
                ent.AddStamina(staminaDelta);
            }
        }
        private static void EnsureParasiteMain(EntityAlive ent)
        {
            if (ent == null)
                return;
            if (!ent.Buffs.HasBuff("buffLD_ParasitesMain"))
            {
                ent.Buffs.AddBuff("buffLD_ParasitesMain");
                CompatLog.Out("[DeadAir Parasites] Added buffLD_ParasitesMain");
            }
            CompatLog.Out($"[DeadAir Parasites] buffLD_ParasitesMain active? {ent.Buffs.HasBuff("buffLD_ParasitesMain")}");
        }
        private static void StartParasites(EntityAlive ent)
        {
            if (ent == null)
                return;
            ent.Buffs.SetCustomVar("ldParasiteCounter", 1f, true);
            EnsureParasiteMain(ent);
            CompatLog.Out("[DeadAir Parasites] Starting parasites at 1% and ensuring main buff");
        }
        private static void IncreaseParasites(EntityAlive ent, float amount)
        {
            if (ent == null)
                return;
            float current = ent.Buffs.GetCustomVar("ldParasiteCounter");
            float updated = Mathf.Clamp(current + amount, 0f, 100f);
            ent.Buffs.SetCustomVar("ldParasiteCounter", updated, true);
            EnsureParasiteMain(ent);
        }
        private static void ApplyParasites(EntityAlive ent, MeatConsumeData data)
        {
            if (ent == null)
                return;
            float current = ent.Buffs.GetCustomVar("ldParasiteCounter");
            // Raw carnivore: always parasite-related
            if (data.IsRawCarnivore)
            {
                if (current <= 0f)
                    StartParasites(ent);
                else
                    IncreaseParasites(ent, 10f);
                return;
            }
            // Raw herbivore: items.xml ldParasiteChance ~30%
            if (data.IsHerbivoreRaw)
            {
                if (Random.value > 0.30f)
                    return;
                if (current <= 0f)
                    StartParasites(ent);
                else
                    IncreaseParasites(ent, 6f);
                return;
            }
            // Cooked carnivore: 30% chance, smaller worsening
            if (data.IsCookedCarnivore)
            {
                int roll = Random.Range(1, 101);
                if (roll > 30)
                    return;
                if (current <= 0f)
                    StartParasites(ent);
                else
                    IncreaseParasites(ent, 4f);
                return;
            }
            // Cooked herbivore: items.xml ldParasiteChance ~5%
            if (data.IsHerbivoreCooked)
            {
                if (Random.value > 0.05f)
                    return;
                if (current <= 0f)
                    StartParasites(ent);
                else
                    IncreaseParasites(ent, 4f);
            }
        }
        /// <summary>Matches vanilla foodRawMeat Food Tier 0 dysentery roll (12 + MetabolismResist vs 1–100).</summary>
        private static void ApplyHerbivoreRawDysenteryRoll(EntityAlive ent)
        {
            if (ent == null)
                return;
            float diseaseRoll = 12f + ent.Buffs.GetCustomVar("$MetabolismResist");
            int threshold = Mathf.Clamp(Mathf.RoundToInt(diseaseRoll), 0, 100);
            if (threshold <= 0)
                return;
            if (Random.Range(1, 101) <= threshold)
                ent.Buffs.AddBuff("buffDysenteryCatchFood");
        }
        private static int GetParasiteStage(EntityAlive ent)
        {
            if (ent == null)
                return 0;
            float counter = ent.Buffs.GetCustomVar("ldParasiteCounter");
            if (counter >= 50f)
                return 3;
            if (counter >= 25f)
                return 2;
            if (counter > 0f)
                return 1;
            return 0;
        }
        private static void ApplyParasiteVomit(EntityAlive ent, ItemValue itemValue)
        {
            if (!(ent is EntityPlayer player) || itemValue?.ItemClass == null)
                return;
            int stage = GetParasiteStage(ent);
            if (stage <= 0)
                return;
            if (!TryGetConsumableKind(itemValue, out bool isFood, out bool isDrink))
                return;
            float chance;
            if (isDrink)
            {
                chance =
                    stage == 1 ? 0.10f :
                    stage == 2 ? 0.20f :
                    0.35f;
            }
            else
            {
                chance =
                    stage == 1 ? 0.15f :
                    stage == 2 ? 0.30f :
                    0.50f;
            }
            if (Random.value > chance)
                return;
            int foodLoss;
            int waterLoss;
            if (isDrink)
            {
                if (stage == 3)
                {
                    foodLoss = 15;
                    waterLoss = 15;
                }
                else
                {
                    foodLoss = 8;
                    waterLoss = 10;
                }
            }
            else
            {
                if (stage == 3)
                {
                    foodLoss = 20;
                    waterLoss = 15;
                }
                else
                {
                    foodLoss = 15;
                    waterLoss = 8;
                }
            }

            float waterDelta = EffectManager.GetValue(PassiveEffects.WaterLoss, itemValue, -waterLoss, ent);
            player.AddWater((int)waterDelta);

            if (foodLoss > 0)
            {
                ent.Buffs.SetCustomVar("ldVomitFoodPenalty", foodLoss, true);
                ent.Buffs.AddBuff("buffLD_ParasiteVomitFoodDrain");
            }

            ent.Buffs.RemoveBuff("buffLD_ParasiteVomit");
            ent.Buffs.AddBuff("buffLD_ParasiteVomit");

            if (player is EntityPlayerLocal localPlayer)
            {
                GameManager.ShowTooltip(localPlayer, Localization.Get("ldMsgParasiteVomit"));
            }
            CompatLog.Out($"[DeadAir Parasites] Vomit triggered. Item={itemValue.ItemClass.GetItemName()} Stage={stage} IsDrink={isDrink} FoodSubtract={foodLoss} waterDelta={(int)waterDelta}");
        }
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ItemActionEat.OnHoldingUpdate))]
        public static void OnHoldingUpdate_Postfix(ItemActionData _actionData)
        {
            if (_actionData?.invData?.holdingEntity == null || _actionData.invData.itemValue == null)
                return;
            EntityAlive ent = _actionData.invData.holdingEntity;
            ItemValue itemValue = _actionData.invData.itemValue;
            if (!TryGetMeatData(itemValue, out MeatConsumeData data))
                return;
            if (!ShouldProcessThisConsume(_actionData, ent))
                return;
            ApplyExtraBenefits(ent, itemValue, data);
            if (data.IsHerbivoreRaw)
                ApplyHerbivoreRawDysenteryRoll(ent);
            ApplyParasites(ent, data);
            // Vomit must run in this postfix for mod meats: a second postfix cannot reuse ShouldProcessThisConsume (gate already consumed).
            if (ent.Buffs.HasBuff("buffLD_ParasitesMain"))
                ApplyParasiteVomit(ent, itemValue);
        }
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ItemActionEat.OnHoldingUpdate))]
        public static void OnHoldingUpdate_ParasiteVomitPostfix(ItemActionData _actionData)
        {
            if (_actionData?.invData?.holdingEntity == null || _actionData.invData.itemValue == null)
                return;
            EntityAlive ent = _actionData.invData.holdingEntity;
            ItemValue itemValue = _actionData.invData.itemValue;
            if (!ent.Buffs.HasBuff("buffLD_ParasitesMain"))
                return;
            if (TryGetMeatData(itemValue, out MeatConsumeData meatData) && meatData.IsCustomMeat)
                return;
            if (!ShouldProcessThisConsume(_actionData, ent))
                return;
            ApplyParasiteVomit(ent, itemValue);
        }
    }
}
