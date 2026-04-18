using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// One-minute refurbish delay for the DeadAir weapon bench. Materials are removed when Refurbish is pressed;
    /// durability is applied when the timer completes. Does not touch hide/show or other bench UI logic.
    /// </summary>
    public static class DeadAirWeaponBenchRepairQueue
    {
        public const float RepairDurationSeconds = 60f;

        internal const string StatusLabelId = "lblDeadAirRepairCountdown";

        sealed class Pending
        {
            public int BenchEntityId = -1;
            public Vector3i BenchBlockWorldPos;
            public int PlayerEntityId = -1;
            public float EndRealtime;
            public string ExpectedWeaponName;
            public string RequiredPartsName;
            public int PartsCount;
        }

        static readonly Dictionary<string, Pending> PendingByBenchKey = new Dictionary<string, Pending>(StringComparer.Ordinal);

        static string MakeKey(TileEntityWorkstation te)
        {
            int id = GetTileEntityIdSafe(te);
            if (id >= 0)
            {
                return "id:" + id;
            }

            var p = TryGetBlockWorldPosition(te);
            return $"b:{p.x},{p.y},{p.z}";
        }

        static Vector3i TryGetBlockWorldPosition(TileEntity te)
        {
            if (te == null)
            {
                return Vector3i.zero;
            }

            try
            {
                var m = te.GetType().GetMethod("ToWorldPos", Type.EmptyTypes);
                if (m != null && m.ReturnType == typeof(Vector3i))
                {
                    return (Vector3i)m.Invoke(te, null);
                }
            }
            catch
            {
            }

            try
            {
                var tr = Traverse.Create(te);
                var f = tr.Field("worldPosInt");
                if (f.FieldExists())
                {
                    return f.GetValue<Vector3i>();
                }
            }
            catch
            {
            }

            return Vector3i.zero;
        }

        public static bool IsRepairInProgress(TileEntityWorkstation te)
        {
            if (te == null)
            {
                return false;
            }

            return PendingByBenchKey.ContainsKey(MakeKey(te));
        }

        public static bool TryGetRemainingSeconds(TileEntityWorkstation te, out int secondsRemaining)
        {
            secondsRemaining = 0;
            if (te == null)
            {
                return false;
            }

            if (!PendingByBenchKey.TryGetValue(MakeKey(te), out var p))
            {
                return false;
            }

            float left = p.EndRealtime - Time.realtimeSinceStartup;
            secondsRemaining = Mathf.CeilToInt(Mathf.Max(0f, left));
            return true;
        }

        public static bool TryGetPendingWeaponNameForBench(TileEntityWorkstation te, out string expectedWeaponName)
        {
            expectedWeaponName = null;
            if (te == null)
            {
                return false;
            }

            if (!PendingByBenchKey.TryGetValue(MakeKey(te), out var p) || p == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(p.ExpectedWeaponName))
            {
                return false;
            }

            expectedWeaponName = p.ExpectedWeaponName;
            return true;
        }

        public static bool TryGetPendingWeaponNameForPlayer(int playerEntityId, out string expectedWeaponName)
        {
            expectedWeaponName = null;
            if (playerEntityId < 0)
            {
                return false;
            }

            foreach (var kvp in PendingByBenchKey)
            {
                var pending = kvp.Value;
                if (pending == null || pending.PlayerEntityId != playerEntityId)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(pending.ExpectedWeaponName))
                {
                    continue;
                }

                expectedWeaponName = pending.ExpectedWeaponName;
                return true;
            }

            return false;
        }

        /// <summary>Returns false if this bench already has a job or scheduling failed.</summary>
        public static bool TryScheduleRepair(
            TileEntityWorkstation te,
            EntityPlayerLocal player,
            string expectedWeaponName,
            string requiredPartsName,
            int partsCount)
        {
            if (te == null || player == null)
            {
                return false;
            }

            string key = MakeKey(te);
            if (PendingByBenchKey.ContainsKey(key))
            {
                return false;
            }

            var p = new Pending
            {
                BenchEntityId = GetTileEntityIdSafe(te),
                BenchBlockWorldPos = TryGetBlockWorldPosition(te),
                PlayerEntityId = GetPlayerEntityIdSafe(player),
                EndRealtime = Time.realtimeSinceStartup + RepairDurationSeconds,
                ExpectedWeaponName = expectedWeaponName,
                RequiredPartsName = requiredPartsName,
                PartsCount = partsCount,
            };

            PendingByBenchKey[key] = p;
            CompatLog.Out($"[DeadAirRepair] Scheduled refurbish: key={key} ends in {RepairDurationSeconds}s");
            return true;
        }

        static int GetPlayerEntityIdSafe(EntityAlive e)
        {
            if (e == null)
            {
                return -1;
            }

            try
            {
                var tr = Traverse.Create(e);
                var p = tr.Property("entityId");
                if (p.PropertyExists())
                {
                    return p.GetValue<int>();
                }
            }
            catch
            {
            }

            try
            {
                return Traverse.Create(e).Field<int>("entityId").Value;
            }
            catch
            {
            }

            return -1;
        }

        internal static void CancelForBench(TileEntityWorkstation te)
        {
            if (te == null)
            {
                return;
            }

            PendingByBenchKey.Remove(MakeKey(te));
        }

        static TileEntity TryResolveTileEntity(World world, Pending p)
        {
            if (world == null)
            {
                return null;
            }

            if (p.BenchEntityId >= 0)
            {
                var byId = TryInvokeGetTileEntity(world, p.BenchEntityId);
                if (byId != null)
                {
                    return byId;
                }
            }

            if (p.BenchBlockWorldPos.x != 0 || p.BenchBlockWorldPos.y != 0 || p.BenchBlockWorldPos.z != 0)
            {
                return TryInvokeGetTileEntity(world, p.BenchBlockWorldPos);
            }

            return null;
        }

        static TileEntity TryInvokeGetTileEntity(World world, int entityId)
        {
            try
            {
                var mi = typeof(World).GetMethod(
                    "GetTileEntity",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(int) },
                    null);

                if (mi != null)
                {
                    return mi.Invoke(world, new object[] { entityId }) as TileEntity;
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] GetTileEntity(int) reflection: {ex.Message}");
            }

            return null;
        }

        static TileEntity TryInvokeGetTileEntity(World world, Vector3i blockPos)
        {
            try
            {
                var mi = typeof(World).GetMethod(
                    "GetTileEntity",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(Vector3i) },
                    null);

                if (mi != null)
                {
                    return mi.Invoke(world, new object[] { blockPos }) as TileEntity;
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] GetTileEntity(Vector3i) reflection: {ex.Message}");
            }

            return null;
        }

        public static void Tick()
        {
            if (PendingByBenchKey.Count == 0)
            {
                return;
            }

            var gm = GameManager.Instance;
            var world = gm?.World;
            if (world == null)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            var canceledKeys = new List<string>();
            var doneKeys = new List<string>();

            foreach (var kv in PendingByBenchKey)
            {
                if (TryShouldCancelActiveRepair(world, kv.Value, out _))
                {
                    canceledKeys.Add(kv.Key);
                    continue;
                }

                if (kv.Value.EndRealtime <= now)
                {
                    doneKeys.Add(kv.Key);
                }
            }

            foreach (var key in canceledKeys)
            {
                if (!PendingByBenchKey.TryGetValue(key, out var p))
                {
                    continue;
                }

                PendingByBenchKey.Remove(key);

                var player = ResolvePlayer(world, p.PlayerEntityId);
                if (player != null)
                {
                    RefundBenchConsumedMaterials(
                        player,
                        DeadAirWeaponRepairState.RepairKitName,
                        p.RequiredPartsName,
                        p.PartsCount);
                    DeadAirNotify.Msg(player, "deadairRepairInterrupted");
                    TryRefreshPlayerBackpackUi(player.PlayerUI?.xui);
                }
            }

            foreach (var key in doneKeys)
            {
                if (!PendingByBenchKey.TryGetValue(key, out var p))
                {
                    continue;
                }

                PendingByBenchKey.Remove(key);

                var rawTe = TryResolveTileEntity(world, p);
                var te = rawTe as TileEntityWorkstation;

                if (te == null)
                {
                    CompatLog.Out($"[DeadAirRepair] Timer: bench tile entity not resolved for key={key}; refunding.");
                    var refundPlayer = ResolvePlayer(world, p.PlayerEntityId);
                    if (refundPlayer != null)
                    {
                        RefundBenchConsumedMaterials(
                            refundPlayer,
                            DeadAirWeaponRepairState.RepairKitName,
                            p.RequiredPartsName,
                            p.PartsCount);
                        DeadAirNotify.Msg(refundPlayer, "deadairRepairInterrupted");
                        TryRefreshPlayerBackpackUi(refundPlayer.PlayerUI?.xui);
                    }

                    continue;
                }

                CompletePendingWeaponBenchRepair(
                    te,
                    world,
                    p.PlayerEntityId,
                    p.ExpectedWeaponName,
                    p.RequiredPartsName,
                    p.PartsCount);
            }
        }

        static bool TryShouldCancelActiveRepair(World world, Pending p, out string reason)
        {
            reason = null;
            if (world == null || p == null)
            {
                return false;
            }

            var rawTe = TryResolveTileEntity(world, p);
            var te = rawTe as TileEntityWorkstation;
            if (te == null)
            {
                return false;
            }

            var tools = DeadAirWeaponRepairActions.TryGetToolStacks(te);
            if (tools == null || tools.Length < 1)
            {
                reason = "tool stacks missing";
                CompatLog.Out($"[DeadAirRepair] Active refurbish canceled: {reason}. expectedWeapon={p.ExpectedWeaponName ?? "<null>"}");
                return true;
            }

            var weapon = tools[0];
            if (weapon.IsEmpty() || weapon.itemValue?.ItemClass == null)
            {
                reason = "weapon slot cleared";
                CompatLog.Out($"[DeadAirRepair] Active refurbish canceled: {reason}. expectedWeapon={p.ExpectedWeaponName ?? "<null>"}");
                return true;
            }

            string actualWeaponName = weapon.itemValue.ItemClass.Name;
            if (!string.Equals(actualWeaponName, p.ExpectedWeaponName, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"weapon changed actual={actualWeaponName} expected={p.ExpectedWeaponName}";
                CompatLog.Out($"[DeadAirRepair] Active refurbish canceled: {reason}");
                return true;
            }

            return false;
        }

        static EntityPlayerLocal ResolvePlayer(World world, int playerEntityId)
        {
            EntityPlayerLocal player = null;
            try
            {
                var ent = world.GetEntity(playerEntityId);
                player = ent as EntityPlayerLocal;
            }
            catch
            {
            }

            if (player == null)
            {
                try
                {
                    player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
                }
                catch
                {
                }
            }

            return player;
        }

        static int GetTileEntityIdSafe(TileEntity te)
        {
            if (te == null)
            {
                return -1;
            }

            try
            {
                var tr = Traverse.Create(te);
                var p = tr.Property("entityId");
                if (p.PropertyExists())
                {
                    return p.GetValue<int>();
                }
            }
            catch
            {
            }

            try
            {
                return Traverse.Create(te).Field<int>("entityId").Value;
            }
            catch
            {
            }

            return -1;
        }

        static void RefundBenchConsumedMaterials(EntityPlayerLocal player, string repairKitName, string requiredPartsName, int partsNeeded)
        {
            if (player == null)
            {
                return;
            }

            try
            {
                var xui = player.PlayerUI?.xui;
                var playerInventory = xui?.PlayerInventory;
                if (playerInventory == null)
                {
                    return;
                }

                Recipe recipe = new Recipe();

                var kitClass = ResolveItemClassByName(repairKitName);
                if (kitClass != null)
                {
                    recipe.ingredients.Add(new ItemStack(new ItemValue(kitClass.Id), 1));
                }

                if (partsNeeded > 0 && !string.IsNullOrEmpty(requiredPartsName))
                {
                    var partClass = ResolveItemClassByName(requiredPartsName);
                    if (partClass != null)
                    {
                        recipe.ingredients.Add(new ItemStack(new ItemValue(partClass.Id), partsNeeded));
                    }
                }

                playerInventory.AddItems(recipe.ingredients.ToArray());
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] RefundBenchConsumedMaterials: {ex.Message}");
            }
        }

        static ItemClass ResolveItemClassByName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
            {
                return null;
            }

            try
            {
                var mi = typeof(ItemClass).GetMethod(
                    "GetItemClass",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);

                if (mi != null)
                {
                    var result = mi.Invoke(null, new object[] { itemName }) as ItemClass;
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            catch
            {
            }

            try
            {
                var mi = typeof(ItemClass).GetMethod(
                    "GetItemClass",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(bool) },
                    null);

                if (mi != null)
                {
                    var result = mi.Invoke(null, new object[] { itemName, false }) as ItemClass;
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            catch
            {
            }

            try
            {
                var mi = typeof(ItemClass).GetMethod(
                    "GetForIdOrName",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);

                if (mi != null)
                {
                    var result = mi.Invoke(null, new object[] { itemName }) as ItemClass;
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        static void TryRefreshPlayerBackpackUi(XUi xui)
        {
            if (xui == null)
            {
                return;
            }

            try
            {
                object backpackGroupObj = xui.FindWindowGroupByName("backpack");
                if (backpackGroupObj == null)
                {
                    return;
                }

                XUiController rootController = backpackGroupObj as XUiController;
                if (rootController == null)
                {
                    var tr = Traverse.Create(backpackGroupObj);

                    try
                    {
                        var p = tr.Property("Controller");
                        if (p.PropertyExists())
                        {
                            rootController = p.GetValue() as XUiController;
                        }
                    }
                    catch
                    {
                    }

                    if (rootController == null)
                    {
                        try
                        {
                            var f = tr.Field("controller");
                            if (f.FieldExists())
                            {
                                rootController = f.GetValue() as XUiController;
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                if (rootController == null)
                {
                    return;
                }

                RefreshControllerRecursive(rootController);
            }
            catch
            {
            }
        }

        static void RefreshControllerRecursive(XUiController controller)
        {
            if (controller == null)
            {
                return;
            }

            try
            {
                controller.IsDirty = true;
            }
            catch
            {
            }

            foreach (var methodName in new[] { "Refresh", "RefreshData", "UpdateData", "RefreshBindings", "ForceRefreshItemStack" })
            {
                try
                {
                    var mi = controller.GetType().GetMethod(
                        methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (mi != null)
                    {
                        mi.Invoke(controller, null);
                        break;
                    }
                }
                catch
                {
                }
            }

            foreach (var child in EnumerateChildControllers(controller))
            {
                RefreshControllerRecursive(child);
            }
        }

        static bool IsFullyRepaired(ItemValue itemValue)
        {
            if (itemValue == null)
            {
                return false;
            }

            try
            {
                return itemValue.UseTimes <= 0f;
            }
            catch
            {
                return false;
            }
        }

        static void ApplyFullRepair(ref ItemValue itemValue)
        {
            if (itemValue == null)
            {
                return;
            }

            itemValue.UseTimes = 0f;

            try
            {
                var pct = typeof(ItemValue).GetProperty("Percentage", BindingFlags.Public | BindingFlags.Instance);
                if (pct != null && pct.CanWrite)
                {
                    pct.SetValue(itemValue, 1f, null);
                }
            }
            catch
            {
            }
        }

        static string GetStackName(ItemStack stack)
        {
            try
            {
                if (stack == null || stack.IsEmpty() || stack.itemValue?.ItemClass == null)
                {
                    return "<empty>";
                }

                return stack.itemValue.ItemClass.Name;
            }
            catch
            {
                return "<unknown>";
            }
        }

        static float GetUseTimesSafe(ItemValue itemValue)
        {
            try
            {
                return itemValue?.UseTimes ?? -1f;
            }
            catch
            {
                return -1f;
            }
        }

        static bool TrySetToolStacks(TileEntityWorkstation te, ItemStack[] tools)
        {
            if (te == null || tools == null)
            {
                return false;
            }

            try
            {
                var mi = te.GetType().GetMethod(
                    "SetToolStacks",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(ItemStack[]) },
                    null);

                if (mi != null)
                {
                    mi.Invoke(te, new object[] { tools });
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                var tr = Traverse.Create(te);

                foreach (var name in new[] { "tools", "toolStacks", "m_tools", "m_toolStacks" })
                {
                    var f = tr.Field(name);
                    if (f.FieldExists())
                    {
                        f.SetValue(tools);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        static void TryRefreshBenchUiForPlayer(EntityPlayerLocal player, TileEntityWorkstation te, ItemStack[] tools)
        {
            if (player?.PlayerUI?.xui == null)
            {
                return;
            }

            var xui = player.PlayerUI.xui;

            try
            {
                object currentToolGrid = GetMemberValue(xui, "currentWorkstationToolGrid")
                                    ?? GetMemberValue(xui, "<currentWorkstationToolGrid>k__BackingField");

                if (currentToolGrid != null)
                {
                    TryPushStacksToToolGridItemStackControllers(currentToolGrid, tools);
                }
            }
            catch
            {
            }

            try
            {
                if (te != null)
                {
                    foreach (var methodName in new[] { "SetModified", "SetDirty", "MarkChanged", "Changed" })
                    {
                        TryInvokeNoArg(te, methodName);
                    }
                }
            }
            catch
            {
            }
        }

        static void TryPushStacksToToolGridItemStackControllers(object currentToolGrid, ItemStack[] tools)
        {
            if (currentToolGrid == null || tools == null || tools.Length < 1)
            {
                return;
            }

            try
            {
                var gridCtrl = currentToolGrid as XUiController;
                if (gridCtrl == null)
                {
                    return;
                }

                var itemStacks = new List<object>();

                try
                {
                    var mi = gridCtrl.GetType().GetMethod(
                        "GetChildrenByType",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (mi != null)
                    {
                        var generic = mi.MakeGenericMethod(typeof(XUiC_ItemStack));
                        if (generic.Invoke(gridCtrl, null) is IEnumerable found)
                        {
                            foreach (var o in found)
                            {
                                if (o != null)
                                {
                                    itemStacks.Add(o);
                                }
                            }
                        }
                    }
                }
                catch
                {
                }

                if (itemStacks.Count == 0)
                {
                    foreach (var child in EnumerateChildControllers(gridCtrl))
                    {
                        if (child is XUiC_ItemStack)
                        {
                            itemStacks.Add(child);
                        }
                    }
                }

                if (itemStacks.Count > 0)
                {
                    TryInvokeSetItemOnItemStackController(itemStacks[0], tools[0]);
                }
            }
            catch
            {
            }
        }

        static void TryInvokeSetItemOnItemStackController(object ctrl, ItemStack stack)
        {
            if (ctrl == null || stack == null)
            {
                return;
            }

            var tr = Traverse.Create(ctrl);

            try
            {
                var p = tr.Property("ItemStack");
                if (p.PropertyExists())
                {
                    p.SetValue(stack);
                    return;
                }
            }
            catch
            {
            }

            try
            {
                var mi = ctrl.GetType().GetMethod(
                    "ForceRefreshItemStack",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                if (mi != null)
                {
                    mi.Invoke(ctrl, null);
                }
            }
            catch
            {
            }
        }

        static IEnumerable<XUiController> EnumerateChildControllers(XUiController controller)
        {
            if (controller == null)
            {
                yield break;
            }

            object childrenObj = null;

            try
            {
                childrenObj = Traverse.Create(controller).Property("Children").GetValue();
            }
            catch
            {
            }

            if (childrenObj == null)
            {
                try
                {
                    childrenObj = Traverse.Create(controller).Field("children").GetValue();
                }
                catch
                {
                }
            }

            if (childrenObj is System.Collections.IEnumerable enumerable)
            {
                foreach (var obj in enumerable)
                {
                    if (obj is XUiController child)
                    {
                        yield return child;
                    }
                }
            }
        }

        static object GetMemberValue(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            var type = obj.GetType();

            while (type != null && type != typeof(object))
            {
                try
                {
                    var f = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (f != null)
                    {
                        return f.GetValue(obj);
                    }
                }
                catch
                {
                }

                try
                {
                    var p = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                    {
                        return p.GetValue(obj, null);
                    }
                }
                catch
                {
                }

                type = type.BaseType;
            }

            return null;
        }

        static void TryInvokeNoArg(object obj, string methodName)
        {
            if (obj == null || string.IsNullOrEmpty(methodName))
            {
                return;
            }

            try
            {
                var mi = obj.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                mi?.Invoke(obj, null);
            }
            catch
            {
            }
        }

        static void CompletePendingWeaponBenchRepair(
            TileEntityWorkstation te,
            World world,
            int playerEntityId,
            string expectedWeaponName,
            string requiredPartsName,
            int partsNeeded)
        {
           if (te == null || world == null)
            {
                CompatLog.Out("[DeadAirRepair] CompletePending: bench missing");
                return;
            }

            EntityPlayerLocal player = ResolvePlayer(world, playerEntityId);

            var tools = DeadAirWeaponRepairActions.TryGetToolStacks(te);
            if (tools == null || tools.Length < 1)
            {
                if (player != null)
                {
                    RefundBenchConsumedMaterials(player, DeadAirWeaponRepairState.RepairKitName, requiredPartsName, partsNeeded);
                    DeadAirNotify.Msg(player, "deadairRepairInterrupted");
                    TryRefreshPlayerBackpackUi(player.PlayerUI?.xui);
                }

                return;
            }

            var weapon = tools[0];
            if (weapon.IsEmpty() || weapon.itemValue?.ItemClass == null)
            {
                if (player != null)
                {
                    RefundBenchConsumedMaterials(player, DeadAirWeaponRepairState.RepairKitName, requiredPartsName, partsNeeded);
                    DeadAirNotify.Msg(player, "deadairRepairInterrupted");
                    TryRefreshPlayerBackpackUi(player.PlayerUI?.xui);
                }

                return;
            }

            var wname = weapon.itemValue.ItemClass.Name;
            if (!string.Equals(wname, expectedWeaponName, StringComparison.OrdinalIgnoreCase))
            {
                if (player != null)
                {
                    RefundBenchConsumedMaterials(player, DeadAirWeaponRepairState.RepairKitName, requiredPartsName, partsNeeded);
                    DeadAirNotify.Msg(player, "deadairRepairInterrupted");
                    TryRefreshPlayerBackpackUi(player.PlayerUI?.xui);
                }

                return;
            }

            if (IsFullyRepaired(weapon.itemValue))
            {
                if (player != null)
                {
                    RefundBenchConsumedMaterials(player, DeadAirWeaponRepairState.RepairKitName, requiredPartsName, partsNeeded);
                    DeadAirNotify.Msg(player, "deadairRepairInterrupted");
                    TryRefreshPlayerBackpackUi(player.PlayerUI?.xui);
                }

                return;
            }

            var repaired = weapon.itemValue;
            ApplyFullRepair(ref repaired);
            weapon.itemValue = repaired;
            tools[0] = weapon;

            if (tools.Length > 1)
            {
                tools[1].Clear();
            }

            if (tools.Length > 2)
            {
                tools[2].Clear();
            }

            bool wroteBack = TrySetToolStacks(te, tools);
            CompatLog.Out($"[DeadAirRepair] CompletePending AFTER weapon={GetStackName(tools[0])} useTimes={GetUseTimesSafe(tools[0].itemValue)} writeBack={wroteBack}");

            TryRefreshBenchUiForPlayer(player, te, tools);

            if (player != null)
            {
                TryRefreshPlayerBackpackUi(player.PlayerUI?.xui);
                DeadAirNotify.Msg(player, "deadairRepairSuccess");
            }
        }
    }
    

    [HarmonyPatch(typeof(GameManager), "Update")]
    public static class DeadAirWeaponBenchRepairQueue_GameManagerUpdate
    {
        static void Postfix()
        {
            try
            {
                DeadAirWeaponBenchRepairQueue.Tick();
                DeadAirWeaponBenchRepairStatusUi.RefreshOpenBenchStatusLabel();
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] GameManager.Update postfix: {ex.Message}");
            }
        }
    }
}
