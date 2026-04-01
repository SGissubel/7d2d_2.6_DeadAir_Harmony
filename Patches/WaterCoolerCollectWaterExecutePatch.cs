namespace DeadAir_7LongDarkDays.Patches
{
    using System.Linq;
    using System.Reflection;
    using HarmonyLib;
    using UnityEngine;

    [HarmonyPatch]
    public static class WaterCoolerCollectWaterExecutePatch
    {
        static MethodBase TargetMethod()
        {
            return typeof(ItemActionCollectWater)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "ExecuteAction");
        }

        static bool Prefix(ItemActionCollectWater __instance, object[] __args)
        {
            if (__args == null || __args.Length < 2)
            {
                return true;
            }

            if (!(__args[0] is ItemActionData actionData))
            {
                return true;
            }

            if (!(__args[1] is bool isReleased))
            {
                return true;
            }

            if (!WaterCoolerActionHelpers.IsTargetingFullCooler(actionData))
            {
                return true;
            }

            // Mirror vanilla behavior: nothing happens until button release.
            if (!isReleased)
            {
                return false;
            }

            if (!WaterCoolerActionHelpers.TryGetPlayerAndWorld(actionData, out EntityPlayerLocal player, out World world))
            {
                return false;
            }

            Inventory inventory = player.inventory;
            if (inventory == null)
            {
                return false;
            }

            int slotIdx = actionData.invData.slotIdx;
            int holdingCount = inventory.holdingCount;

            ItemValue filledJarValue = ItemClass.GetItem("drinkJarRiverWater", false);
            ItemStack filledJarStack = new ItemStack(filledJarValue, 1);

            // One empty jar in hand: replace held slot directly.
            if (holdingCount <= 1)
            {
                inventory.SetItem(slotIdx, filledJarStack);
            }
            else
            {
                int addedSlot;
                bool added = inventory.AddItem(filledJarStack, out addedSlot);

                if (!added)
                {
                    return false;
                }

                inventory.DecHoldingItem(1);
            }

            actionData.lastUseTime = Time.time;
            player.RightArmAnimationUse = true;
            WaterCoolerActionHelpers.TrySwapCoolerToEmpty(actionData);
            // Optional sound
            if (!string.IsNullOrEmpty(__instance.soundStart))
            {
                player.PlayOneShot(__instance.soundStart);
            }

            return false;
        }
    }
}