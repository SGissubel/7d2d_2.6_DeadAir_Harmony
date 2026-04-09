using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DeadAir_7LongDarkDays.Patches
{

    public static class WaterCoolerActionHelpers
    {
        private struct RecentDrinkTarget
        {
            public Vector3i BlockPos;
            public int ClrIdx;
            public long TicksUtc;
        }

        private static readonly Dictionary<int, RecentDrinkTarget> RecentDrinkTargets = new Dictionary<int, RecentDrinkTarget>();

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

        public static bool TryGetTargetedBlock(ItemActionData actionData, out BlockValue blockValue, out string blockName, out Vector3i blockPos, out int clrIdx)
        {
            blockValue = BlockValue.Air;
            blockName = null;
            blockPos = Vector3i.zero;
            clrIdx = 0;

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
            clrIdx = hitInfo.hit.clrIdx;
            blockValue = world.GetBlock(clrIdx, blockPos);

            Block block = blockValue.Block ?? Block.list[blockValue.type];
            if (block == null)
            {
                return false;
            }

            blockName = block.GetBlockName();
            return !string.IsNullOrEmpty(blockName);
        }

        public static bool IsTargetingFullCooler(ItemActionData actionData)
        {
            return TryGetTargetedBlock(actionData, out _, out string blockName, out _, out _)
                && FullCoolerBlockNames.Contains(blockName);
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

        public static void RememberDrinkCoolerTarget(ItemActionData actionData)
        {
            if (!TryGetPlayerAndWorld(actionData, out EntityPlayerLocal player, out _))
            {
                return;
            }

            if (!TryGetTargetedBlock(actionData, out _, out string blockName, out Vector3i blockPos, out int clrIdx))
            {
                return;
            }

            if (!FullCoolerBlockNames.Contains(blockName))
            {
                return;
            }

            RecentDrinkTargets[player.entityId] = new RecentDrinkTarget
            {
                BlockPos = blockPos,
                ClrIdx = clrIdx,
                TicksUtc = DateTime.UtcNow.Ticks
            };
        }

        public static bool TrySwapRememberedDrinkCooler(EntityPlayerLocal player, World world, int maxAgeMs = 3000)
        {
            if (player == null || world == null)
            {
                return false;
            }

            if (!RecentDrinkTargets.TryGetValue(player.entityId, out RecentDrinkTarget target))
            {
                return false;
            }

            long ageTicks = DateTime.UtcNow.Ticks - target.TicksUtc;
            if (ageTicks > TimeSpan.FromMilliseconds(maxAgeMs).Ticks)
            {
                return false;
            }

            BlockValue current = world.GetBlock(target.ClrIdx, target.BlockPos);
            Block currentBlock = current.Block ?? Block.list[current.type];
            if (currentBlock == null)
            {
                return false;
            }

            string currentName = currentBlock.GetBlockName();
            if (!TryGetEmptyCoolerBlockName(currentName, out string emptyName))
            {
                return false;
            }

            return TrySetBlockByName(world, target.ClrIdx, target.BlockPos, emptyName);
        }

        public static bool TrySwapTargetedCoolerToEmpty(ItemActionData actionData)
        {
            if (!TryGetPlayerAndWorld(actionData, out _, out World world))
            {
                return false;
            }

            if (!TryGetTargetedBlock(actionData, out _, out string blockName, out Vector3i blockPos, out int clrIdx))
            {
                return false;
            }

            if (!TryGetEmptyCoolerBlockName(blockName, out string emptyName))
            {
                return false;
            }

            return TrySetBlockByName(world, clrIdx, blockPos, emptyName);
        }

        private static bool TrySetBlockByName(World world, int clrIdx, Vector3i blockPos, string blockName)
        {
            if (world == null || string.IsNullOrEmpty(blockName))
            {
                return false;
            }

            BlockValue current = world.GetBlock(clrIdx, blockPos);

            if (!TryResolveBlockTypeId(blockName, out int newTypeId))
            {
                CompatLog.Out($"[DeadAir][WaterCooler] Unknown block name for swap: '{blockName}'.");
                return false;
            }

            // Preserve facing/orientation/meta from the current placed block.
            BlockValue newValue = current;
            newValue.type = newTypeId;

            MethodInfo setBlockRpc = typeof(World)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "SetBlockRPC")
                    {
                        return false;
                    }

                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 3
                        && p[0].ParameterType == typeof(int)
                        && p[1].ParameterType == typeof(Vector3i)
                        && p[2].ParameterType == typeof(BlockValue);
                });

            if (setBlockRpc != null)
            {
                setBlockRpc.Invoke(world, new object[] { clrIdx, blockPos, newValue });
                return true;
            }

            MethodInfo setBlock = typeof(World)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "SetBlock")
                    {
                        return false;
                    }

                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 3
                        && p[0].ParameterType == typeof(int)
                        && p[1].ParameterType == typeof(Vector3i)
                        && p[2].ParameterType == typeof(BlockValue);
                });

            if (setBlock != null)
            {
                setBlock.Invoke(world, new object[] { clrIdx, blockPos, newValue });
                return true;
            }

            CompatLog.Out("[DeadAir][WaterCooler] Could not find a compatible World.SetBlockRPC/SetBlock overload.");
            return false;
        }

        private static bool TryResolveBlockTypeId(string blockName, out int typeId)
        {
            typeId = -1;

            for (int i = 0; i < Block.list.Length; i++)
            {
                Block block = Block.list[i];
                if (block != null && block.GetBlockName() == blockName)
                {
                    typeId = i;
                    return true;
                }
            }

            return false;
        }
        public static bool HasRecentDrinkCoolerTarget(EntityPlayerLocal player, int maxAgeMs = 3000)
        {
            if (player == null)
            {
                return false;
            }

            if (!RecentDrinkTargets.TryGetValue(player.entityId, out RecentDrinkTarget target))
            {
                return false;
            }

            long ageTicks = DateTime.UtcNow.Ticks - target.TicksUtc;
            return ageTicks <= TimeSpan.FromMilliseconds(maxAgeMs).Ticks;
        }
        private static bool TryBuildBlockValue(string blockName, out BlockValue blockValue)
        {
            blockValue = BlockValue.Air;

            int typeId = -1;

            for (int i = 0; i < Block.list.Length; i++)
            {
                Block block = Block.list[i];
                if (block != null && block.GetBlockName() == blockName)
                {
                    typeId = i;
                    break;
                }
            }

            if (typeId < 0)
            {
                return false;
            }

            ConstructorInfo ctor = typeof(BlockValue)
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(c =>
                {
                    ParameterInfo[] p = c.GetParameters();
                    return p.Length == 1 && IsIntegral(p[0].ParameterType);
                });

            if (ctor == null)
            {
                CompatLog.Out("[DeadAir][WaterCooler] Could not find 1-arg BlockValue ctor.");
                return false;
            }

            Type paramType = ctor.GetParameters()[0].ParameterType;
            object converted = Convert.ChangeType(typeId, paramType);
            object boxed = ctor.Invoke(new[] { converted });
            blockValue = (BlockValue)boxed;
            return true;
        }

        private static bool IsIntegral(Type t)
        {
            return t == typeof(byte)
                || t == typeof(sbyte)
                || t == typeof(short)
                || t == typeof(ushort)
                || t == typeof(int)
                || t == typeof(uint);
        }
    }
}