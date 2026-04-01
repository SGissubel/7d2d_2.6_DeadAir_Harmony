namespace DeadAir_7LongDarkDays.Patches
{
    using System;
    using System.Linq;
    using System.Reflection;
    using HarmonyLib;

    [HarmonyPatch]
    public static class WaterCoolerCollectWaterTrace_OnHoldingUpdate
    {
        private static long _lastTicks;

        static MethodBase TargetMethod()
        {
            return typeof(ItemActionCollectWater)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                    m.Name == "OnHoldingUpdate");
        }

        static void Postfix(ItemActionCollectWater __instance, object[] __args)
        {
            if (__args == null || __args.Length == 0 || !(__args[0] is ItemActionData actionData))
            {
                return;
            }

            if (!ShouldLog())
            {
                return;
            }

            LogState("CollectWater.OnHoldingUpdate", __instance, actionData, null);
        }

        private static bool ShouldLog(int minMs = 400)
        {
            long now = DateTime.UtcNow.Ticks;
            long minTicks = TimeSpan.FromMilliseconds(minMs).Ticks;

            if (now - _lastTicks < minTicks)
            {
                return false;
            }

            _lastTicks = now;
            return true;
        }

        private static void LogState(string source, object instance, ItemActionData actionData, object extra)
        {
            string heldItem = actionData?.invData?.item?.Name ?? "<null>";
            bool isCooler = WaterCoolerActionHelpers.IsTargetingFullCooler(actionData);

            bool hitValid = false;
            string blockName = "<none>";
            string blockPos = "<none>";

            if (WaterCoolerActionHelpers.TryGetPlayerAndWorld(actionData, out EntityPlayerLocal player, out World world))
            {
                WorldRayHitInfo hitInfo = player.HitInfo;
                hitValid = hitInfo.bHitValid;

                if (hitInfo.bHitValid)
                {
                    Vector3i pos = hitInfo.hit.blockPos;
                    blockPos = pos.ToString();

                    BlockValue blockValue = world.GetBlock(pos);
                    Block block = Block.list[blockValue.type];
                    if (block != null)
                    {
                        blockName = block.GetBlockName() ?? "<null>";
                    }
                }
            }

            CompatLog.Out($"[DeadAir][CollectWaterTrace] {source} | heldItem={heldItem} hitValid={hitValid} block={blockName} pos={blockPos} isCooler={isCooler} extra={extra ?? "<null>"}");
        }
    }

    [HarmonyPatch]
    public static class WaterCoolerCollectWaterTrace_ExecuteAction
    {
        static MethodBase TargetMethod()
        {
            return typeof(ItemActionCollectWater)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                    m.Name == "ExecuteAction");
        }

        static void Prefix(ItemActionCollectWater __instance, object[] __args)
        {
            if (__args == null || __args.Length == 0 || !(__args[0] is ItemActionData actionData))
            {
                return;
            }

            object releasedArg = __args.Length > 1 ? __args[1] : "<missing>";
            LogState("CollectWater.ExecuteAction.Prefix", __instance, actionData, releasedArg);
        }

        static void Postfix(ItemActionCollectWater __instance, object[] __args)
        {
            if (__args == null || __args.Length == 0 || !(__args[0] is ItemActionData actionData))
            {
                return;
            }

            object releasedArg = __args.Length > 1 ? __args[1] : "<missing>";
            LogState("CollectWater.ExecuteAction.Postfix", __instance, actionData, releasedArg);
        }

        private static void LogState(string source, object instance, ItemActionData actionData, object extra)
        {
            string heldItem = actionData?.invData?.item?.Name ?? "<null>";
            bool isCooler = WaterCoolerActionHelpers.IsTargetingFullCooler(actionData);

            bool hitValid = false;
            string blockName = "<none>";
            string blockPos = "<none>";

            if (WaterCoolerActionHelpers.TryGetPlayerAndWorld(actionData, out EntityPlayerLocal player, out World world))
            {
                WorldRayHitInfo hitInfo = player.HitInfo;
                hitValid = hitInfo.bHitValid;

                if (hitInfo.bHitValid)
                {
                    Vector3i pos = hitInfo.hit.blockPos;
                    blockPos = pos.ToString();

                    BlockValue blockValue = world.GetBlock(pos);
                    Block block = Block.list[blockValue.type];
                    if (block != null)
                    {
                        blockName = block.GetBlockName() ?? "<null>";
                    }
                }
            }

            CompatLog.Out($"[DeadAir][CollectWaterTrace] {source} | heldItem={heldItem} hitValid={hitValid} block={blockName} pos={blockPos} isCooler={isCooler} extra={extra ?? "<null>"}");
        }
    }
}