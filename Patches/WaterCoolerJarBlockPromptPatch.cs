using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{

    [HarmonyPatch]
    public static class WaterCoolerJarBlockPromptPatch_HasCommands
    {
        static MethodBase TargetMethod()
        {
            return typeof(Block)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                    m.Name == "HasBlockActivationCommands" &&
                    m.ReturnType == typeof(bool));
        }

        static void Postfix(object __instance, object[] __args, ref bool __result)
        {
            if (__args == null || __args.Length == 0)
            {
                return;
            }

            EntityPlayerLocal player = __args.OfType<EntityPlayerLocal>().FirstOrDefault();
            if (player == null)
            {
                return;
            }

            BlockValue blockValue = BlockValue.Air;
            bool foundBlockValue = false;

            foreach (object arg in __args)
            {
                if (arg is BlockValue bv)
                {
                    blockValue = bv;
                    foundBlockValue = true;
                    break;
                }
            }

            if (!foundBlockValue)
            {
                return;
            }

            Block block = Block.list[blockValue.type];
            if (block == null)
            {
                return;
            }

            string blockName = block.GetBlockName();
            if (string.IsNullOrEmpty(blockName))
            {
                return;
            }

            if (blockName != "cntWaterCoolerFull"
                && blockName != "cntWaterCoolerFullSideCentered"
                && blockName != "cntWaterCoolerBottle"
                && blockName != "cntWaterCoolerBottleSideCentered")
            {
                return;
            }

            ItemValue holdingItem = player.inventory?.holdingItemItemValue ?? ItemValue.None;
            string heldName = holdingItem.IsEmpty() ? null : holdingItem.ItemClass?.Name;
            bool emptyHands = holdingItem.IsEmpty() || heldName == "meleeHandPlayer";

            if (heldName == "drinkJarEmpty" || !emptyHands)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class WaterCoolerJarBlockPromptPatch_GetText
    {
        static MethodBase TargetMethod()
        {
            return typeof(Block)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                    m.Name == "GetActivationText" &&
                    m.ReturnType == typeof(string));
        }

        static void Postfix(object __instance, object[] __args, ref string __result)
        {
            if (__args == null || __args.Length == 0)
            {
                return;
            }

            EntityPlayerLocal player = __args.OfType<EntityPlayerLocal>().FirstOrDefault();
            if (player == null)
            {
                return;
            }

            BlockValue blockValue = BlockValue.Air;
            bool foundBlockValue = false;

            foreach (object arg in __args)
            {
                if (arg is BlockValue bv)
                {
                    blockValue = bv;
                    foundBlockValue = true;
                    break;
                }
            }

            if (!foundBlockValue)
            {
                return;
            }

            Block block = Block.list[blockValue.type];
            if (block == null)
            {
                return;
            }

            string blockName = block.GetBlockName();
            if (string.IsNullOrEmpty(blockName))
            {
                return;
            }

            if (blockName != "cntWaterCoolerFull"
                && blockName != "cntWaterCoolerFullSideCentered"
                && blockName != "cntWaterCoolerBottle"
                && blockName != "cntWaterCoolerBottleSideCentered")
            {
                return;
            }

            ItemValue holdingItem = player.inventory?.holdingItemItemValue ?? ItemValue.None;
            string heldName = holdingItem.IsEmpty() ? null : holdingItem.ItemClass?.Name;
            bool emptyHands = holdingItem.IsEmpty() || heldName == "meleeHandPlayer";

            if (heldName == "drinkJarEmpty")
            {
                __result = "{0} Fill Jar from " + block.GetLocalizedBlockName();
                return;
            }

            if (emptyHands)
            {
                return;
            }

            // Anything else in hand: show guidance
            __result = "{0} Empty your hands to drink or hold an empty jar to fill from " + block.GetLocalizedBlockName();
        }
    }
}