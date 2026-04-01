namespace DeadAir_7LongDarkDays.Patches
{
    using System.Collections.Generic;

    public static class WaterCoolerActionHelpers
    {
        private static readonly HashSet<string> FullCoolerBlockNames = new HashSet<string>
        {
            "cntWaterCoolerFull",
            "cntWaterCoolerFullSideCentered",
            "cntWaterCoolerBottle",
            "cntWaterCoolerBottleSideCentered"
        };

        public static bool TryGetPlayerAndWorld(ItemActionData actionData, out EntityPlayerLocal player, out World world)
        {
            player = null;
            world = null;

            if (actionData?.invData == null)
            {
                return false;
            }

            world = actionData.invData.world;
            if (world == null)
            {
                return false;
            }

            player = actionData.invData.holdingEntity as EntityPlayerLocal;
            return player != null;
        }

        public static bool TryGetTargetedBlock(ItemActionData actionData, out BlockValue blockValue, out string blockName, out Vector3i blockPos)
        {
            blockValue = BlockValue.Air;
            blockName = null;
            blockPos = Vector3i.zero;

            if (!TryGetPlayerAndWorld(actionData, out EntityPlayerLocal player, out World world))
            {
                return false;
            }

            WorldRayHitInfo hitInfo = player.HitInfo;
            if (!hitInfo.bHitValid)
            {
                return false;
            }

            blockPos = hitInfo.hit.blockPos;
            blockValue = world.GetBlock(blockPos);

            Block block = Block.list[blockValue.type];
            if (block == null)
            {
                return false;
            }

            blockName = block.GetBlockName();
            return !string.IsNullOrEmpty(blockName);
        }


        public static bool TryGetEmptyCoolerBlockName(string fullBlockName, out string emptyBlockName)
        {
            switch (fullBlockName)
            {
                case "cntWaterCoolerFull":
                    emptyBlockName = "cntWaterCoolerFull_Empty";
                    return true;
                case "cntWaterCoolerFullSideCentered":
                    emptyBlockName = "cntWaterCoolerFullSideCentered_Empty";
                    return true;
                case "cntWaterCoolerBottle":
                    emptyBlockName = "cntWaterCoolerBottle_Empty";
                    return true;
                case "cntWaterCoolerBottleSideCentered":
                    emptyBlockName = "cntWaterCoolerBottleSideCentered_Empty";
                    return true;
                default:
                    emptyBlockName = null;
                    return false;
            }
        }
        public static bool IsTargetingFullCooler(ItemActionData actionData)
        {
            return TryGetTargetedBlock(actionData, out _, out string blockName, out _)
                && FullCoolerBlockNames.Contains(blockName);
        }
    }
}