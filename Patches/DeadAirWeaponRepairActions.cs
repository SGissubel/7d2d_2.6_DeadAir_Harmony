using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    public static class DeadAirWeaponRepairActions
    {
      static void ReturnLegacyHiddenToolSlotsToPlayer(EntityPlayerLocal player, ItemStack[] tools)
      {
          if (tools == null || tools.Length < 2 || player == null)
          {
              return;
          }

          for (int i = 1; i < tools.Length; i++)
          {
              try
              {
                  if (tools[i].IsEmpty())
                  {
                      continue;
                  }

                  var copy = new ItemStack(tools[i].itemValue.Clone(), tools[i].count);
                  if (TryAddStackToPlayer(player, copy))
                  {
                      CompatLog.Out($"[DeadAirRepair] Returned legacy hidden tool slot item to player from slot index={i}: {GetStackName(copy)} x{copy.count}");
                      tools[i].Clear();
                  }
                  else
                  {
                      CompatLog.Out($"[DeadAirRepair] Could not return legacy hidden tool slot item to player from slot index={i}");
                  }
              }
              catch (Exception ex)
              {
                  CompatLog.Out($"[DeadAirRepair] ReturnLegacyHiddenToolSlotsToPlayer slot={i}: {ex.Message}");
              }
          }
      }

      /// <summary>Preferred: uses live <see cref="Inventory"/> slots. Falls back to reflection scan if needed.</summary>
      internal static int CountPlayerItem(EntityPlayerLocal player, string itemName)
      {
          return DeadAirInventoryItemRemoval.CountNamedItem(player, itemName);
      }

      /// <summary>Fallback when <see cref="Inventory.GetItem"/> is unavailable.</summary>
      internal static int CountPlayerItemLegacy(EntityPlayerLocal player, string itemName)
      {
          if (player == null || string.IsNullOrEmpty(itemName))
          {
              return 0;
          }

          int total = 0;
          foreach (var arr in EnumeratePlayerItemStackArrays(player))
          {
              if (arr == null)
              {
                  continue;
              }

              for (int i = 0; i < arr.Length; i++)
              {
                  try
                  {
                      var s = arr[i];
                      if (s.IsEmpty() || s.itemValue.ItemClass == null)
                      {
                          continue;
                      }

                      if (string.Equals(s.itemValue.ItemClass.Name, itemName, StringComparison.OrdinalIgnoreCase))
                      {
                          total += s.count;
                      }
                  }
                  catch
                  {
                  }
              }
          }

          return total;
      }

      internal static bool RemovePlayerItems(EntityPlayerLocal player, string itemName, int count)
      {
          return DeadAirInventoryItemRemoval.TryRemoveNamedItem(player, itemName, count);
      }

      internal static bool RemovePlayerItemsLegacy(EntityPlayerLocal player, string itemName, int count)
      {
          if (player == null || string.IsNullOrEmpty(itemName) || count <= 0)
          {
              return false;
          }

          int remaining = count;

          foreach (var arr in EnumeratePlayerItemStackArrays(player))
          {
              if (arr == null)
              {
                  continue;
              }

              for (int i = 0; i < arr.Length && remaining > 0; i++)
              {
                  try
                  {
                      var s = arr[i];
                      if (s.IsEmpty() || s.itemValue.ItemClass == null)
                      {
                          continue;
                      }

                      if (!string.Equals(s.itemValue.ItemClass.Name, itemName, StringComparison.OrdinalIgnoreCase))
                      {
                          continue;
                      }

                      int take = s.count < remaining ? s.count : remaining;
                      s.count -= take;
                      remaining -= take;

                      if (s.count <= 0)
                      {
                          s.Clear();
                      }

                      arr[i] = s;
                  }
                  catch
                  {
                  }
              }

              if (remaining <= 0)
              {
                  break;
              }
          }

          if (remaining > 0)
          {
              CompatLog.Out($"[DeadAirRepair] RemovePlayerItems incomplete. requested={count}, removed={count - remaining}, item={itemName}");
              return false;
          }

          TryMarkPlayerContainersDirty(player);
          return true;
      }

      static IEnumerable<ItemStack[]> EnumeratePlayerItemStackArrays(EntityPlayerLocal player)
      {
          if (player == null)
          {
              yield break;
          }

          var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

          foreach (var rootName in new[] { "inventory", "bag", "backpack", "lootContainer", "equipment" })
          {
              object root = null;

              try
              {
                  root = GetMemberValue(player, rootName);
              }
              catch
              {
              }

              if (root == null)
              {
                  continue;
              }

              foreach (var arr in EnumerateItemStackArraysRecursive(root, 0, 4, seen))
              {
                  yield return arr;
              }
          }
      }

      static IEnumerable<ItemStack[]> EnumerateItemStackArraysRecursive(object obj, int depth, int maxDepth, HashSet<object> seen)
      {
          if (obj == null || depth > maxDepth)
          {
              yield break;
          }

          if (!obj.GetType().IsValueType)
          {
              if (!seen.Add(obj))
              {
                  yield break;
              }
          }

          if (obj is ItemStack[] direct)
          {
              yield return direct;
              yield break;
          }

          var type = obj.GetType();

          while (type != null && type != typeof(object))
          {
              foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
              {
                  object val = null;

                  try
                  {
                      val = f.GetValue(obj);
                  }
                  catch
                  {
                  }

                  if (val == null)
                  {
                      continue;
                  }

                  if (val is ItemStack[] arr)
                  {
                      yield return arr;
                      continue;
                  }

                  if (LooksLikePlayerContainerMember(f.Name, f.FieldType))
                  {
                      foreach (var childArr in EnumerateItemStackArraysRecursive(val, depth + 1, maxDepth, seen))
                      {
                          yield return childArr;
                      }
                  }
              }

              foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
              {
                  if (!p.CanRead || p.GetIndexParameters().Length > 0)
                  {
                      continue;
                  }

                  object val = null;

                  try
                  {
                      val = p.GetValue(obj, null);
                  }
                  catch
                  {
                  }

                  if (val == null)
                  {
                      continue;
                  }

                  if (val is ItemStack[] arr)
                  {
                      yield return arr;
                      continue;
                  }

                  if (LooksLikePlayerContainerMember(p.Name, p.PropertyType))
                  {
                      foreach (var childArr in EnumerateItemStackArraysRecursive(val, depth + 1, maxDepth, seen))
                      {
                          yield return childArr;
                      }
                  }
              }

              type = type.BaseType;
          }
      }

      static bool LooksLikePlayerContainerMember(string memberName, Type memberType)
      {
          string n = memberName ?? string.Empty;
          string t = memberType?.FullName ?? string.Empty;

          return ContainsIgnoreCase(n, "slot", "item", "stack", "bag", "backpack", "inventory", "toolbelt", "container")
              || ContainsIgnoreCase(t, "ItemStack", "ItemValue", "Inventory", "Bag", "Backpack", "Container");
      }

      static void TryMarkPlayerContainersDirty(EntityPlayerLocal player)
      {
          if (player == null)
          {
              return;
          }

          foreach (var rootName in new[] { "inventory", "bag", "backpack" })
          {
              object root = null;

              try
              {
                  root = GetMemberValue(player, rootName);
              }
              catch
              {
              }

              if (root == null)
              {
                  continue;
              }

              foreach (var methodName in new[] { "SetModified", "SetDirty", "MarkChanged", "OnChanged", "Changed" })
              {
                  TryInvokeNoArg(root, methodName);
              }
          }
      }

      static bool TryAddStackToPlayer(EntityPlayerLocal player, ItemStack stack)
      {
          if (player == null || stack.IsEmpty())
          {
              return false;
          }

          try
          {
              var inv = GetMemberValue(player, "inventory");
              if (inv != null)
              {
                  foreach (var name in new[] { "AddItem", "AddItemStack", "AddItemNotEmpty" })
                  {
                      var mi = inv.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(ItemStack) }, null);
                      if (mi != null)
                      {
                          mi.Invoke(inv, new object[] { stack });
                          return true;
                      }
                  }
              }
          }
          catch
          {
          }

          try
          {
              var bag = GetMemberValue(player, "bag");
              if (bag != null)
              {
                  var mi = bag.GetType().GetMethod("AddItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(ItemStack) }, null);
                  if (mi != null)
                  {
                      mi.Invoke(bag, new object[] { stack });
                      return true;
                  }
              }
          }
          catch
          {
          }

          return false;
      }

      sealed class ReferenceEqualityComparer : IEqualityComparer<object>
      {
          public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

          public new bool Equals(object x, object y)
          {
              return ReferenceEquals(x, y);
          }

          public int GetHashCode(object obj)
          {
              return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
          }
      }
      public static void TryRepairFromUiButton(XUiC_SimpleButton button)
      {
          var btnId = DeadAirGameApiCompat.TryGetXUiControlName(button);
          if (button == null || !string.Equals(btnId, "btnDeadAirWeaponRepair", StringComparison.OrdinalIgnoreCase))
          {
              return;
          }

          CompatLog.Out("[DeadAirRepair] Repair button hit via XUiC_SimpleButton.Btn_OnPress");

          if (!DeadAirWeaponRepairState.TryBeginRepairClick())
          {
              return;
          }

          var player = button?.xui?.playerUI?.entityPlayer as EntityPlayerLocal
              ?? GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
          if (player == null)
          {
              CompatLog.Out("[DeadAirRepair] FAIL: no local player from bench UI / primary player");
              return;
          }

          var te = ResolveBenchFromButton(button);
          if (te == null)
          {
              CompatLog.Out("[DeadAirRepair] FAIL: no active weapon repair bench resolved from button context");
              DeadAirNotify.Msg(player, "deadairRepairNoBench");
              return;
          }

          if (!IsWeaponBenchButtonContext(button))
          {
              CompatLog.Out("[DeadAirRepair] FAIL: button context is not the DeadAir weapon repair bench");
              DeadAirNotify.Msg(player, "deadairRepairNoBench");
              return;
          }

          var tools = TryGetToolStacks(te);
          if (tools == null || tools.Length < 1)
          {
              CompatLog.Out("[DeadAirRepair] FAIL: tool slots missing");
              DeadAirNotify.Msg(player, "deadairRepairBadSlots");
              return;
          }

          var weapon = tools[0];

          CompatLog.Out($"[DeadAirRepair] BEFORE weapon={GetStackName(weapon)} useTimes={GetUseTimesSafe(weapon.itemValue)} count={weapon.count}");

          if (weapon.IsEmpty() || weapon.itemValue.ItemClass == null)
          {
              CompatLog.Out("[DeadAirRepair] FAIL: weapon slot empty");
              DeadAirNotify.Msg(player, "deadairRepairNeedWeapon");
              return;
          }

          var weaponClass = weapon.itemValue.ItemClass;
          CompatLog.Out($"[DeadAirRepair] weaponClass={weaponClass?.Name}");

          if (weaponClass == null || !DeadAirWeaponBenchHelpers.IsSupportedWeapon(weaponClass.Name))
          {
              CompatLog.Out("[DeadAirRepair] FAIL: unsupported or invalid weapon");
              DeadAirNotify.Msg(player, "deadairRepairNotWeapon");
              return;
          }

          if (!DeadAirWeaponBenchHelpers.IsFirearmSupportedWeapon(weaponClass.Name))
          {
              CompatLog.Out("[DeadAirRepair] FAIL: bench refurb is for firearms only (not bows/crossbows)");
              DeadAirNotify.Msg(player, "deadairRepairBenchFirearmsOnly");
              return;
          }

          var requiredPartsName = DeadAirWeaponBenchHelpers.GetRepairPartName(weaponClass.Name);
          if (string.IsNullOrEmpty(requiredPartsName))
          {
              CompatLog.Out($"[DeadAirRepair] FAIL: no required parts mapping for weapon={weaponClass.Name}");
              DeadAirNotify.Msg(player, "deadairRepairWrongParts");
              return;
          }

          int need = DeadAirWeaponBenchHelpers.GetQualityScaledPartCount(weapon.itemValue);

          if (IsFullyRepaired(weapon.itemValue))
          {
              CompatLog.Out("[DeadAirRepair] FAIL: weapon already fully repaired");
              DeadAirNotify.Msg(player, "deadairRepairAlreadyFull");
              return;
          }

          ReturnLegacyHiddenToolSlotsToPlayer(player, tools);

          int availableKits = CountPlayerItem(player, DeadAirWeaponRepairState.RepairKitName);
          if (availableKits < 1)
          {
              CompatLog.Out($"[DeadAirRepair] FAIL: repair kit missing in player inventory. have={availableKits}");
              DeadAirNotify.Msg(player, "deadairRepairNeedKit");
              return;
          }

          int availableParts = CountPlayerItem(player, requiredPartsName);
          if (availableParts < need)
          {
              CompatLog.Out($"[DeadAirRepair] FAIL: not enough matching parts in player inventory. need={need}, have={availableParts}, part={requiredPartsName}");
              DeadAirNotify.Msg(player, "deadairRepairNotEnoughParts");
              return;
          }

          if (!RemovePlayerItems(player, DeadAirWeaponRepairState.RepairKitName, 1))
          {
              CompatLog.Out("[DeadAirRepair] FAIL: could not remove repair kit from player inventory");
              DeadAirNotify.Msg(player, "deadairRepairNeedKit");
              return;
          }

          if (!RemovePlayerItems(player, requiredPartsName, need))
          {
              CompatLog.Out("[DeadAirRepair] FAIL: could not remove required parts from player inventory after validation");
              DeadAirNotify.Msg(player, "deadairRepairNotEnoughParts");
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

          CompatLog.Out($"[DeadAirRepair] AFTER weapon={GetStackName(tools[0])} useTimes={GetUseTimesSafe(tools[0].itemValue)} count={tools[0].count}");

          bool wroteBack = TrySetToolStacks(te, tools);
          CompatLog.Out($"[DeadAirRepair] writeBack={wroteBack}");

          TrySyncUiWorkstationModel(button, tools);
          TryRefreshBenchState(te, button);
          LogPostWriteToolStacksDiagnostics(te, button);

          CompatLog.Out($"[DeadAirRepair] SUCCESS: weapon={weaponClass.Name}, quality={weapon.itemValue.Quality}, partsUsed={need}, source=playerInventory");
          DeadAirNotify.Msg(player, "deadairRepairSuccess");
      }
        static bool IsWeaponBenchButtonContext(XUiC_SimpleButton button)
        {
            try
            {
                var wgCtrl = button?.windowGroup?.Controller as XUiC_WorkstationWindowGroup;
                if (wgCtrl == null)
                {
                    CompatLog.Out("[DeadAirRepair] IsWeaponBenchButtonContext: no workstation window group controller");
                    return false;
                }

                var workstationNameObj = GetMemberValue(wgCtrl, "workstation")
                                      ?? GetMemberValue(wgCtrl, "Workstation");

                var workstationName = workstationNameObj as string;

                CompatLog.Out($"[DeadAirRepair] IsWeaponBenchButtonContext: workstationName={workstationName ?? "<null>"} expected={DeadAirWeaponRepairState.BenchBlockName}");

                return string.Equals(workstationName, DeadAirWeaponRepairState.BenchBlockName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] IsWeaponBenchButtonContext exception: {ex.Message}");
                return false;
            }
        }

        public static bool IsOurBench(TileEntityWorkstation te)
        {
            var resolvedBlockName = GetBlockName(te);
            CompatLog.Out($"[DeadAirRepair] IsOurBench check. resolvedBlockName={resolvedBlockName ?? "<null>"} expected={DeadAirWeaponRepairState.BenchBlockName}");

            return te != null &&
                   string.Equals(resolvedBlockName, DeadAirWeaponRepairState.BenchBlockName, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetBlockName(TileEntity te)
        {
            CompatLog.Out($"[DeadAirRepair] te => GetBlockName={te}");

            if (te == null)
            {
                return null;
            }

            try
            {
                var bv = Traverse.Create(te).Field<BlockValue>("blockValue").Value;
                CompatLog.Out($"[DeadAirRepair] bv => GetBlockName={bv}");
                var b = Block.list[bv.type];
                CompatLog.Out($"[DeadAirRepair] b => GetBlockName={b}");
                return b?.blockName;
            }
            catch
            {
                return null;
            }
        }

        public static TileEntityWorkstation ResolveBenchFromButton(XUiC_SimpleButton button)
        {
            if (button == null)
            {
                return null;
            }

            var wgCtrl = button.windowGroup?.Controller as XUiC_WorkstationWindowGroup;
            if (wgCtrl == null)
            {
                CompatLog.Out("[DeadAirRepair] ResolveBenchFromButton: no workstation window group controller");
                return null;
            }

            var workstationData = GetMemberValue(wgCtrl, "WorkstationData")
                               ?? GetMemberValue(wgCtrl, "workstationData");

            if (workstationData != null)
            {
                CompatLog.Out($"[DeadAirRepair] WorkstationData type={workstationData.GetType().FullName}");

                var directTileEntity = GetMemberValue(workstationData, "tileEntity")
                                    ?? GetMemberValue(workstationData, "TileEntity");

                if (directTileEntity is TileEntityWorkstation directBench)
                {
                    CompatLog.Out($"[DeadAirRepair] Direct WorkstationData tileEntity found. blockName={GetBlockName(directBench) ?? "<null>"}");
                    return directBench;
                }

                if (directTileEntity is TileEntity baseTe)
                {
                    var benchTe = baseTe as TileEntityWorkstation;
                    if (benchTe != null)
                    {
                        CompatLog.Out($"[DeadAirRepair] Direct WorkstationData TileEntity cast worked. blockName={GetBlockName(benchTe) ?? "<null>"}");
                        return benchTe;
                    }
                }

                DumpInterestingMembers(workstationData, "WorkstationData");
            }

            var xui = button.xui;
            var currentToolGrid = GetMemberValue(xui, "currentWorkstationToolGrid")
                               ?? GetMemberValue(xui, "<currentWorkstationToolGrid>k__BackingField");

            if (currentToolGrid != null)
            {
                CompatLog.Out($"[DeadAirRepair] currentWorkstationToolGrid type={currentToolGrid.GetType().FullName}");

                var te = DeadAirWorkstationResolver.TryResolveTileEntity(currentToolGrid);
                if (te != null)
                {
                    CompatLog.Out($"[DeadAirRepair] Resolved bench from currentWorkstationToolGrid: {GetBlockName(te) ?? "<null>"}");
                    return te;
                }
            }

            {
                var te = DeadAirWorkstationResolver.TryResolveTileEntity(wgCtrl);
                if (te != null)
                {
                    CompatLog.Out($"[DeadAirRepair] Resolved bench from workstation window controller: {GetBlockName(te) ?? "<null>"}");
                    return te;
                }
            }

            CompatLog.Out("[DeadAirRepair] ResolveBenchFromButton: no bench found from button context");
            return null;
        }

        public static ItemStack[] TryGetToolStacks(TileEntityWorkstation te)
        {
            if (te == null)
            {
                return null;
            }

            // Prefer engine API: GetToolStacks() / ToolStacks is the source of truth for sync + UI.
            // Field reflection can return a stale array or a copy the UI does not observe.
            var fromApi = TryReadToolStacksFromInstance(te);
            if (fromApi != null)
            {
                CompatLog.Out("[DeadAirRepair] TryGetToolStacks: using GetToolStacks() from type hierarchy (TileEntityWorkstation)");
                return fromApi;
            }

            var wd = GetTileEntityWorkstationDataObject(te);
            if (wd != null)
            {
                fromApi = TryReadToolStacksFromInstance(wd);
                if (fromApi != null)
                {
                    CompatLog.Out($"[DeadAirRepair] TryGetToolStacks: using GetToolStacks() from type hierarchy ({wd.GetType().Name})");
                    return fromApi;
                }
            }

            var tr = Traverse.Create(te);
            foreach (var name in new[] { "tools", "toolStacks", "m_tools", "m_toolStacks" })
            {
                var f = tr.Field(name);
                if (f.FieldExists())
                {
                    var v = f.GetValue();
                    if (v is ItemStack[] arr)
                    {
                        CompatLog.Out("[DeadAirRepair] TryGetToolStacks: fallback to field on TileEntityWorkstation");
                        return arr;
                    }
                }
            }

            if (wd != null)
            {
                var wtr = Traverse.Create(wd);
                foreach (var name in new[] { "tools", "toolStacks", "m_tools", "m_toolStacks" })
                {
                    var f = wtr.Field(name);
                    if (f.FieldExists())
                    {
                        var v = f.GetValue();
                        if (v is ItemStack[] arr)
                        {
                            CompatLog.Out("[DeadAirRepair] TryGetToolStacks: fallback to field on workstationData");
                            return arr;
                        }
                    }
                }
            }

            return null;
        }

        static object GetTileEntityWorkstationDataObject(TileEntityWorkstation te)
        {
            if (te == null)
            {
                return null;
            }

            var tr = Traverse.Create(te);
            foreach (var wn in new[] { "workstationData", "WorkstationData", "m_workstationData" })
            {
                var wf = tr.Field(wn);
                if (wf.FieldExists())
                {
                    var wd = wf.GetValue();
                    if (wd != null)
                    {
                        return wd;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// GetToolStacks / SetToolStacks are often private and declared on a base class.
        /// typeof(TileEntityWorkstation).GetMethod(...) does not find those — walk the hierarchy with DeclaredOnly.
        /// </summary>
        static MethodInfo FindDeclaredMethodInHierarchy(Type startType, string name, Type[] parameterTypes)
        {
            if (startType == null)
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var t = startType; t != null && t != typeof(object); t = t.BaseType)
            {
                MethodInfo mi;
                if (parameterTypes == null || parameterTypes.Length == 0)
                {
                    mi = t.GetMethod(name, flags, null, Type.EmptyTypes, null);
                }
                else
                {
                    mi = t.GetMethod(name, flags, null, parameterTypes, null);
                }

                if (mi != null)
                {
                    return mi;
                }
            }

            return null;
        }

        static ItemStack[] CoerceToItemStackArray(object result)
        {
            if (result == null)
            {
                return null;
            }

            if (result is ItemStack[] direct)
            {
                return direct;
            }

            if (result is Array a && a.Length >= 0)
            {
                if (a.Length == 0)
                {
                    return Array.Empty<ItemStack>();
                }

                if (a.GetValue(0) is ItemStack)
                {
                    var copy = new ItemStack[a.Length];
                    Array.Copy(a, copy, a.Length);
                    return copy;
                }
            }

            return null;
        }

        static ItemStack[] TryReadToolStacksFromInstance(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            try
            {
                var mi = FindDeclaredMethodInHierarchy(instance.GetType(), "GetToolStacks", Type.EmptyTypes);
                if (mi != null)
                {
                    var result = mi.Invoke(instance, null);
                    var arr = CoerceToItemStackArray(result);
                    if (arr != null)
                    {
                        return arr;
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] TryReadToolStacksFromInstance GetToolStacks: {ex.Message}");
            }

            try
            {
                for (var t = instance.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    var pi = t.GetProperty("ToolStacks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (pi != null && pi.GetIndexParameters().Length == 0)
                    {
                        var result = pi.GetValue(instance, null);
                        var arr = CoerceToItemStackArray(result);
                        if (arr != null)
                        {
                            return arr;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] TryReadToolStacksFromInstance ToolStacks prop: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Read tool stacks from any object (TE, XUiM_Workstation, XUiC_WorkstationToolGrid, etc.) for diagnostics.
        /// </summary>
        static ItemStack[] TryReadToolStacksUniversal(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            if (instance is TileEntityWorkstation te)
            {
                return TryGetToolStacks(te);
            }

            var fromApi = TryReadToolStacksFromInstance(instance);
            if (fromApi != null)
            {
                return fromApi;
            }

            return TryReadToolStacksFromFieldsOnly(instance);
        }

        static ItemStack[] TryReadToolStacksFromFieldsOnly(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            var tr = Traverse.Create(instance);
            foreach (var name in new[] { "tools", "toolStacks", "m_tools", "m_toolStacks" })
            {
                var f = tr.Field(name);
                if (f.FieldExists())
                {
                    var v = f.GetValue();
                    if (v is ItemStack[] arr)
                    {
                        return arr;
                    }
                }
            }

            return null;
        }

        static void LogPostWriteToolStacksDiagnostics(TileEntityWorkstation te, XUiC_SimpleButton button)
        {
            if (button == null)
            {
                return;
            }

            object wdUi = null;
            object grid = null;

            try
            {
                var wg = button.windowGroup?.Controller as XUiC_WorkstationWindowGroup;
                wdUi = wg != null ? (GetMemberValue(wg, "WorkstationData") ?? GetMemberValue(wg, "workstationData")) : null;
                var xui = button.xui;
                grid = xui != null
                    ? (GetMemberValue(xui, "currentWorkstationToolGrid") ?? GetMemberValue(xui, "<currentWorkstationToolGrid>k__BackingField"))
                    : null;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] POST-WRITE compare: could not resolve WorkstationData/tool grid: {ex.Message}");
            }

            CompatLog.Out("[DeadAirRepair] POST-WRITE compare (read back slot 0/1/2 after write + UI sync + refresh):");

            var stacksTe = te != null ? TryGetToolStacks(te) : null;
            LogSlotsLine("TileEntityWorkstation", stacksTe);

            var stacksWd = TryReadToolStacksUniversal(wdUi);
            LogSlotsLine("WorkstationData (XUiM)", stacksWd);

            var stacksGrid = TryReadToolStacksUniversal(grid);
            LogSlotsLine("currentWorkstationToolGrid", stacksGrid);

            if (stacksGrid == null && grid != null)
            {
                LogDeclaredFieldsItemStackArrayScan(grid, "currentWorkstationToolGrid (no tools[] — scan fields)");
                LogItemStackSlotsFromGridChildren(grid, "currentWorkstationToolGrid (XUiC_ItemStack children)");
            }

            if (stacksTe != null && stacksWd != null)
            {
                CompatLog.Out($"[DeadAirRepair]   refEquals TE array == WorkstationData array: {ReferenceEquals(stacksTe, stacksWd)}");
            }

            if (stacksTe != null && stacksGrid != null)
            {
                CompatLog.Out($"[DeadAirRepair]   refEquals TE array == toolGrid array: {ReferenceEquals(stacksTe, stacksGrid)}");
            }

            if (stacksWd != null && stacksGrid != null)
            {
                CompatLog.Out($"[DeadAirRepair]   refEquals WorkstationData array == toolGrid array: {ReferenceEquals(stacksWd, stacksGrid)}");
            }
        }

        static void LogSlotsLine(string label, ItemStack[] stacks)
        {
            if (stacks == null)
            {
                CompatLog.Out($"[DeadAirRepair]   {label}: <null>");
                return;
            }

            if (stacks.Length < 1)
            {
                CompatLog.Out($"[DeadAirRepair]   {label}: len={stacks.Length} (need >= 1)");
                return;
            }

            var slot0 = FormatSlot(stacks[0]);
            var slot1 = stacks.Length > 1 ? FormatSlot(stacks[1]) : "<n/a>";
            var slot2 = stacks.Length > 2 ? FormatSlot(stacks[2]) : "<n/a>";

            CompatLog.Out($"[DeadAirRepair]   {label}: [0] {slot0} | [1] {slot1} | [2] {slot2}");
        }

        static string FormatSlot(ItemStack s)
        {
            try
            {
                if (s.IsEmpty())
                {
                    return "empty";
                }

                var name = s.itemValue?.ItemClass?.Name ?? "?";
                return $"{name} useTimes={GetUseTimesSafe(s.itemValue)} count={s.count}";
            }
            catch
            {
                return "<error>";
            }
        }

        /// <summary>
        /// Open bench UI reads from XUiM_Workstation / tool grid, not only TileEntityWorkstation.tools.
        /// Mirror stacks into the model and poke the grid so slots repaint.
        /// </summary>
        static void TrySyncUiWorkstationModel(XUiC_SimpleButton button, ItemStack[] tools)
        {
            if (button == null || tools == null)
            {
                return;
            }

            try
            {
                var wg = button.windowGroup?.Controller as XUiC_WorkstationWindowGroup;
                if (wg == null)
                {
                    return;
                }

                var wdUi = GetMemberValue(wg, "WorkstationData") ?? GetMemberValue(wg, "workstationData");
                if (wdUi == null)
                {
                    return;
                }

                var modelType = wdUi.GetType();
                CompatLog.Out($"[DeadAirRepair] UI sync: WorkstationData type={modelType.FullName}");

                bool didSomething = false;

                var setModel = FindDeclaredMethodInHierarchy(modelType, "SetToolStacks", new[] { typeof(ItemStack[]) });
                if (setModel != null)
                {
                    setModel.Invoke(wdUi, new object[] { tools });
                    CompatLog.Out("[DeadAirRepair] UI sync: SetToolStacks(ItemStack[]) on WorkstationData (XUi model)");
                    didSomething = true;
                }

                if (!didSomething)
                {
                    var getModel = FindDeclaredMethodInHierarchy(modelType, "GetToolStacks", Type.EmptyTypes);
                    if (getModel != null)
                    {
                        var existing = CoerceToItemStackArray(getModel.Invoke(wdUi, null));
                        if (existing != null && existing.Length >= tools.Length)
                        {
                            for (int i = 0; i < tools.Length && i < existing.Length; i++)
                            {
                                existing[i] = tools[i];
                            }

                            CompatLog.Out("[DeadAirRepair] UI sync: copied stacks into WorkstationData.GetToolStacks() array");
                            didSomething = true;
                        }
                    }
                }

                if (!didSomething)
                {
                    if (TrySetToolStacksOnObject(wdUi, tools))
                    {
                        CompatLog.Out("[DeadAirRepair] UI sync: wrote tools fields on WorkstationData");
                        didSomething = true;
                    }
                }

                foreach (var refreshName in new[] { "RefreshData", "Refresh", "UpdateFromTileEntity", "NotifyListeners" })
                {
                    TryInvokeNoArg(wdUi, refreshName);
                }

                var xui = button.xui;
                var grid = xui != null
                    ? (GetMemberValue(xui, "currentWorkstationToolGrid") ?? GetMemberValue(xui, "<currentWorkstationToolGrid>k__BackingField"))
                    : null;

                if (grid != null)
                {
                    CompatLog.Out($"[DeadAirRepair] UI sync: currentWorkstationToolGrid type={grid.GetType().FullName}");

                    var setGrid = FindDeclaredMethodInHierarchy(grid.GetType(), "SetToolStacks", new[] { typeof(ItemStack[]) });
                    if (setGrid != null)
                    {
                        setGrid.Invoke(grid, new object[] { tools });
                        CompatLog.Out("[DeadAirRepair] UI sync: SetToolStacks on tool grid");
                    }
                    else
                    {
                        var getGrid = FindDeclaredMethodInHierarchy(grid.GetType(), "GetToolStacks", Type.EmptyTypes);
                        if (getGrid != null)
                        {
                            var existing = CoerceToItemStackArray(getGrid.Invoke(grid, null));
                            if (existing != null && existing.Length >= tools.Length)
                            {
                                for (int i = 0; i < tools.Length && i < existing.Length; i++)
                                {
                                    existing[i] = tools[i];
                                }

                                CompatLog.Out("[DeadAirRepair] UI sync: copied into tool grid GetToolStacks array");
                            }
                        }
                    }

                    foreach (var methodName in new[]
                             {
                                 "WorkStation_OnToolsOrFuelChanged",
                                 "OnToolsOrFuelChanged",
                                 "Refresh",
                                 "RefreshData",
                                 "UpdateSlots",
                                 "UpdateToolSlots",
                                 "RefreshToolSlots",
                                 "RecreateTools",
                             })
                    {
                        TryInvokeNoArg(grid, methodName);
                    }

                    TryInvokeWorkstationToolsChangedEvent(grid);

                    // Tool grid does not mirror TE.tools[]; repeated RequiredItemStack children hold what you see.
                    TryPushStacksToToolGridItemStackControllers(grid, tools);
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] TrySyncUiWorkstationModel: {ex.Message}");
            }
        }

        /// <summary>
        /// windows.xml uses repeat_content + RequiredItemStack — visible slots are XUiC_ItemStack children, not a tools[] on the grid.
        /// </summary>
        static void TryPushStacksToToolGridItemStackControllers(object grid, ItemStack[] tools)
        {
            if (grid == null || tools == null)
            {
                return;
            }

            try
            {
                var children = InvokeGetChildrenByTypeAsArray(grid, typeof(XUiC_ItemStack));
                if (children == null || children.Length == 0)
                {
                    children = CollectAllItemStackDescendants(grid);
                    if (children != null && children.Length > 0)
                    {
                        CompatLog.Out($"[DeadAirRepair] TryPushStacksToToolGridItemStackControllers: nested scan found {children.Length} XUiC_ItemStack (GetChildrenByType had none)");
                    }
                }

                if (children == null || children.Length == 0)
                {
                    CompatLog.Out("[DeadAirRepair] TryPushStacksToToolGridItemStackControllers: no XUiC_ItemStack controllers (direct or nested)");
                    return;
                }

                int n = tools.Length < children.Length ? tools.Length : children.Length;
                for (int i = 0; i < n; i++)
                {
                    if (!(children[i] is XUiC_ItemStack slot))
                    {
                        continue;
                    }

                    if (TryInvokeSetItemOnItemStackController(slot, tools[i]))
                    {
                        CompatLog.Out($"[DeadAirRepair] UI sync: pushed ItemStack to tool grid slot index={i}");
                    }
                    else
                    {
                        CompatLog.Out($"[DeadAirRepair] UI sync: FAILED to set tool grid slot index={i} (no matching setter)");
                    }

                    TryInvokeNoArg(slot, "ForceRefreshItemStack");
                    TryInvokeNoArg(slot, "Refresh");
                    TryInvokeNoArg(slot, "RefreshBindings");
                    TryInvokeNoArg(slot, "UpdateData");
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] TryPushStacksToToolGridItemStackControllers: {ex.Message}");
            }
        }

        /// <summary>
        /// GetChildrenByType only returns immediate children; repeated RequiredItemStack cells live under inner grids.
        /// </summary>
        static XUiC_ItemStack[] CollectAllItemStackDescendants(object root)
        {
            if (root == null)
            {
                return null;
            }

            var found = new List<XUiC_ItemStack>();

            void Visit(object node, int depth)
            {
                if (node == null || depth > 64)
                {
                    return;
                }

                if (node is XUiC_ItemStack slot)
                {
                    found.Add(slot);
                }

                foreach (var child in EnumerateXUiChildControllers(node))
                {
                    Visit(child, depth + 1);
                }
            }

            try
            {
                Visit(root, 0);
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] CollectAllItemStackDescendants: {ex.Message}");
            }

            if (found.Count == 0)
            {
                var getById = FindDeclaredMethodInHierarchy(root.GetType(), "GetChildById", new[] { typeof(string) });
                if (getById != null)
                {
                    foreach (var sid in new[]
                             {
                                 "0", "1", "2", "inventory", "inventory_0", "inventory_1", "inventory_2", "0_0", "0_1", "0_2",
                             })
                    {
                        object ch = null;

                        try
                        {
                            ch = getById.Invoke(root, new object[] { sid });
                        }
                        catch
                        {
                            // ignore
                        }

                        if (ch == null)
                        {
                            continue;
                        }

                        if (ch is XUiC_ItemStack s)
                        {
                            found.Add(s);
                            continue;
                        }

                        try
                        {
                            Visit(ch, 1);
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
            }

            if (found.Count == 0)
            {
                int direct = 0;
                foreach (var _ in EnumerateXUiChildControllers(root))
                {
                    direct++;
                }

                CompatLog.Out($"[DeadAirRepair] CollectAllItemStackDescendants: 0 item slots under grid; direct child count={direct} (if 0, Children/GetChild API may differ on this build)");
            }

            return found.Count > 0 ? found.ToArray() : null;
        }

        static IEnumerable EnumerateXUiChildControllers(object controller)
        {
            if (controller == null)
            {
                yield break;
            }

            var v = GetMemberValue(controller, "Children")
                 ?? GetMemberValue(controller, "children")
                 ?? GetMemberValue(controller, "m_children");

            if (v is IEnumerable en && v is not string)
            {
                foreach (var o in en)
                {
                    if (o != null)
                    {
                        yield return o;
                    }
                }

                yield break;
            }

            int count = -1;
            var cc = GetMemberValue(controller, "ChildCount") ?? GetMemberValue(controller, "childCount");
            if (cc is int ci)
            {
                count = ci;
            }

            if (count <= 0)
            {
                yield break;
            }

            MethodInfo getChild = null;
            for (var t = controller.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                getChild = t.GetMethod(
                    "GetChild",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    new[] { typeof(int) },
                    null);

                if (getChild != null)
                {
                    break;
                }
            }

            if (getChild == null)
            {
                yield break;
            }

            for (int i = 0; i < count; i++)
            {
                object ch = null;

                try
                {
                    ch = getChild.Invoke(controller, new object[] { i });
                }
                catch
                {
                    // ignore
                }

                if (ch != null)
                {
                    yield return ch;
                }
            }
        }

        static bool TryInvokeSetItemOnItemStackController(XUiC_ItemStack slot, ItemStack stack)
        {
            if (slot == null)
            {
                return false;
            }

            try
            {
                // 1) Writable ItemStack property (XUi often binds MVC through this)
                for (var t = slot.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    var pi = t.GetProperty(
                        "ItemStack",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                    if (pi != null && pi.CanWrite && pi.GetIndexParameters().Length == 0 &&
                        string.Equals(pi.Name, "ItemStack", StringComparison.Ordinal))
                    {
                        pi.SetValue(slot, stack, null);
                        CompatLog.Out($"[DeadAirRepair] TryInvokeSetItemOnItemStackController: set property ItemStack on {t.Name}");
                        return true;
                    }
                }

                // 2) Single-arg ItemStack methods
                foreach (var name in new[] { "SetItem", "SetItemStack", "ForceSetItemStack" })
                {
                    var mi = FindDeclaredMethodInHierarchy(slot.GetType(), name, new[] { typeof(ItemStack) });
                    if (mi != null)
                    {
                        mi.Invoke(slot, new object[] { stack });
                        CompatLog.Out($"[DeadAirRepair] TryInvokeSetItemOnItemStackController: {slot.GetType().Name}.{name}(ItemStack)");
                        return true;
                    }
                }

                // 3) SetItem(ItemValue, int) — common in 7DTD item stack controllers
                var miIv = FindDeclaredMethodInHierarchy(slot.GetType(), "SetItem", new[] { typeof(ItemValue), typeof(int) });
                if (miIv != null)
                {
                    miIv.Invoke(slot, new object[] { stack.itemValue, stack.count });
                    CompatLog.Out("[DeadAirRepair] TryInvokeSetItemOnItemStackController: SetItem(ItemValue, int)");
                    return true;
                }

                // 4) SetItem(ItemValue, int, bool) optional third parameter false
                var miIv3 = FindSetItemMethodItemValueIntBool(slot.GetType());
                if (miIv3 != null)
                {
                    var ps = miIv3.GetParameters();
                    if (ps.Length == 3 && ps[2].ParameterType == typeof(bool))
                    {
                        miIv3.Invoke(slot, new object[] { stack.itemValue, stack.count, false });
                        CompatLog.Out("[DeadAirRepair] TryInvokeSetItemOnItemStackController: SetItem(ItemValue, int, bool)");
                        return true;
                    }
                }

                // 5) Nested XUi model object
                foreach (var memberName in new[] { "ItemStackData", "ItemStackViewModel", "ViewModel", "itemStackModel", "ItemStackController" })
                {
                    var model = GetMemberValue(slot, memberName);
                    if (model == null)
                    {
                        continue;
                    }

                    foreach (var name in new[] { "SetItem", "SetItemStack", "ForceSetItemStack" })
                    {
                        var mm = FindDeclaredMethodInHierarchy(model.GetType(), name, new[] { typeof(ItemStack) });
                        if (mm != null)
                        {
                            mm.Invoke(model, new object[] { stack });
                            CompatLog.Out($"[DeadAirRepair] TryInvokeSetItemOnItemStackController: {model.GetType().Name}.{name}(ItemStack) via {memberName}");
                            return true;
                        }
                    }

                    var pm = FindDeclaredMethodInHierarchy(model.GetType(), "SetItem", new[] { typeof(ItemValue), typeof(int) });
                    if (pm != null)
                    {
                        pm.Invoke(model, new object[] { stack.itemValue, stack.count });
                        CompatLog.Out($"[DeadAirRepair] TryInvokeSetItemOnItemStackController: nested SetItem(ItemValue, int) on {memberName}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] TryInvokeSetItemOnItemStackController: {ex.Message}");
            }

            return false;
        }

        static MethodInfo FindSetItemMethodItemValueIntBool(Type startType)
        {
            for (var t = startType; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != "SetItem")
                    {
                        continue;
                    }

                    var ps = m.GetParameters();
                    if (ps.Length == 3 && ps[0].ParameterType == typeof(ItemValue) && ps[1].ParameterType == typeof(int) && ps[2].ParameterType == typeof(bool))
                    {
                        return m;
                    }
                }
            }

            return null;
        }

        static XUiC_ItemStack[] InvokeGetChildrenByTypeAsArray(object controller, Type childType)
        {
            if (controller == null || childType == null)
            {
                return null;
            }

            object raw = null;

            for (var t = controller.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                var m = t.GetMethod(
                    "GetChildrenByType",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null,
                    new[] { typeof(Type) },
                    null);

                if (m == null)
                {
                    continue;
                }

                try
                {
                    raw = m.Invoke(controller, new object[] { childType });
                    if (raw != null)
                    {
                        break;
                    }
                }
                catch
                {
                    // try next declaring type
                }
            }

            if (raw == null)
            {
                return null;
            }

            if (raw is XUiC_ItemStack[] typed)
            {
                return typed;
            }

            if (raw is Array arr)
            {
                var list = new List<XUiC_ItemStack>();
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr.GetValue(i) is XUiC_ItemStack s)
                    {
                        list.Add(s);
                    }
                }

                return list.Count > 0 ? list.ToArray() : null;
            }

            if (raw is IEnumerable enumerable)
            {
                var list = new List<XUiC_ItemStack>();
                foreach (var o in enumerable)
                {
                    if (o is XUiC_ItemStack s)
                    {
                        list.Add(s);
                    }
                }

                return list.Count > 0 ? list.ToArray() : null;
            }

            return null;
        }

        static void LogDeclaredFieldsItemStackArrayScan(object obj, string label)
        {
            if (obj == null)
            {
                return;
            }

            try
            {
                CompatLog.Out($"[DeadAirRepair] {label}: scanning for ItemStack[] fields on {obj.GetType().FullName}");
                for (var t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType != typeof(ItemStack[]))
                        {
                            continue;
                        }

                        try
                        {
                            var v = f.GetValue(obj) as ItemStack[];
                            if (v == null || v.Length < 3)
                            {
                                continue;
                            }

                            CompatLog.Out($"[DeadAirRepair]   found {t.Name}.{f.Name} len={v.Length} [0]={FormatSlot(v[0])}");
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] LogDeclaredFieldsItemStackArrayScan: {ex.Message}");
            }
        }

        static void LogItemStackSlotsFromGridChildren(object grid, string label)
        {
            if (grid == null)
            {
                return;
            }

            try
            {
                var children = InvokeGetChildrenByTypeAsArray(grid, typeof(XUiC_ItemStack));
                if (children == null || children.Length == 0)
                {
                    children = CollectAllItemStackDescendants(grid);
                }

                if (children == null || children.Length == 0)
                {
                    CompatLog.Out($"[DeadAirRepair] {label}: <no XUiC_ItemStack (direct or nested)>");
                    return;
                }

                CompatLog.Out($"[DeadAirRepair] {label}: found {children.Length} item stack controller(s)");

                for (int i = 0; i < children.Length && i < 3; i++)
                {
                    var s = TryReadItemStackFromItemStackController(children[i]);
                    CompatLog.Out($"[DeadAirRepair] {label}: [{i}] {FormatSlot(s)}");
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] LogItemStackSlotsFromGridChildren: {ex.Message}");
            }
        }

        static ItemStack TryReadItemStackFromItemStackController(XUiC_ItemStack slot)
        {
            if (slot == null)
            {
                return default;
            }

            try
            {
                foreach (var propName in new[] { "ItemStack", "itemStack", "Stack", "stack" })
                {
                    var pi = slot.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (pi != null && pi.GetIndexParameters().Length == 0)
                    {
                        var v = pi.GetValue(slot, null);
                        if (v is ItemStack st)
                        {
                            return st;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return default;
        }

        static void TryInvokeWorkstationToolsChangedEvent(object grid)
        {
            if (grid == null)
            {
                return;
            }

            try
            {
                foreach (var m in grid.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    if (string.Equals(m.Name, "WorkStation_OnToolsOrFuelChanged", StringComparison.Ordinal) ||
                        string.Equals(m.Name, "OnWorkstationToolsChanged", StringComparison.Ordinal))
                    {
                        m.Invoke(grid, null);
                        CompatLog.Out($"[DeadAirRepair] UI sync: invoked {grid.GetType().Name}.{m.Name}()");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] TryInvokeWorkstationToolsChangedEvent: {ex.Message}");
            }
        }

        static bool TrySetToolStacks(TileEntityWorkstation te, ItemStack[] tools)
        {
            if (te == null || tools == null)
            {
                return false;
            }

            if (TryWriteToolStacksViaApi(te, tools))
            {
                return true;
            }

            bool wroteAny = TrySetToolStacksOnObject(te, tools);

            var wd = GetTileEntityWorkstationDataObject(te);
            if (wd != null)
            {
                wroteAny |= TrySetToolStacksOnObject(wd, tools);
            }

            return wroteAny;
        }

        static bool TryWriteToolStacksViaApi(TileEntityWorkstation te, ItemStack[] tools)
        {
            if (te == null || tools == null)
            {
                return false;
            }

            bool ok = false;

            try
            {
                var mi = FindDeclaredMethodInHierarchy(te.GetType(), "SetToolStacks", new[] { typeof(ItemStack[]) });
                if (mi != null)
                {
                    mi.Invoke(te, new object[] { tools });
                    CompatLog.Out("[DeadAirRepair] SetToolStacks(ItemStack[]) invoked on TileEntityWorkstation (type hierarchy)");
                    ok = true;
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] SetToolStacks on TileEntityWorkstation failed: {ex.Message}");
            }

            var wd = GetTileEntityWorkstationDataObject(te);
            if (wd != null)
            {
                try
                {
                    var mi = FindDeclaredMethodInHierarchy(wd.GetType(), "SetToolStacks", new[] { typeof(ItemStack[]) });
                    if (mi != null)
                    {
                        mi.Invoke(wd, new object[] { tools });
                        CompatLog.Out($"[DeadAirRepair] SetToolStacks(ItemStack[]) invoked on {wd.GetType().FullName} (type hierarchy)");
                        ok = true;
                    }
                }
                catch (Exception ex)
                {
                    CompatLog.Out($"[DeadAirRepair] SetToolStacks on workstationData failed: {ex.Message}");
                }
            }

            if (ok)
            {
                return true;
            }

            if (TrySetToolSlotsViaSetToolInSlot(te, tools))
            {
                return true;
            }

            return false;
        }

        static bool TrySetToolSlotsViaSetToolInSlot(TileEntityWorkstation te, ItemStack[] tools)
        {
            if (te == null || tools == null)
            {
                return false;
            }

            var match = FindDeclaredMethodInHierarchy(te.GetType(), "SetToolInSlot", new[] { typeof(int), typeof(ItemStack) });

            if (match == null)
            {
                return false;
            }

            try
            {
                for (int i = 0; i < tools.Length; i++)
                {
                    match.Invoke(te, new object[] { i, tools[i] });
                }

                CompatLog.Out("[DeadAirRepair] SetToolInSlot(int, ItemStack) invoked for each tool slot (type hierarchy)");
                return true;
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] SetToolInSlot failed: {ex.Message}");
                return false;
            }
        }

        static bool TrySetToolStacksOnObject(object obj, ItemStack[] tools)
        {
            if (obj == null || tools == null)
            {
                return false;
            }

            bool wrote = false;
            var tr = Traverse.Create(obj);

            foreach (var name in new[] { "tools", "toolStacks", "m_tools", "m_toolStacks" })
            {
                try
                {
                    var field = tr.Field(name);
                    if (field.FieldExists())
                    {
                        field.SetValue(tools);
                        CompatLog.Out($"[DeadAirRepair] WROTE tool stack field '{name}' on {obj.GetType().FullName}");
                        wrote = true;
                    }
                }
                catch (Exception ex)
                {
                    CompatLog.Out($"[DeadAirRepair] Failed writing field '{name}' on {obj.GetType().FullName}: {ex.Message}");
                }

                try
                {
                    var prop = tr.Property(name);
                    if (prop.PropertyExists())
                    {
                        prop.SetValue(tools);
                        CompatLog.Out($"[DeadAirRepair] WROTE tool stack property '{name}' on {obj.GetType().FullName}");
                        wrote = true;
                    }
                }
                catch (Exception ex)
                {
                    CompatLog.Out($"[DeadAirRepair] Failed writing property '{name}' on {obj.GetType().FullName}: {ex.Message}");
                }
            }

            return wrote;
        }

        static void TryRefreshBenchState(TileEntityWorkstation te, XUiC_SimpleButton button)
        {
            try
            {
                DeadAirGameApiCompat.TryMarkTileEntityDirty(te);
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] TryMarkTileEntityDirty failed: {ex.Message}");
            }

            TryInvokeNoArg(te, "SetModified");
            TryInvokeNoArg(te, "SetDirty");
            TryInvokeNoArg(te, "MarkChanged");
            TryInvokeNoArg(te, "UpdateSlots");

            try
            {
                var wg = button?.windowGroup?.Controller;
                if (wg != null)
                {
                    wg.IsDirty = true;
                    if (wg.ViewComponent != null)
                    {
                        wg.ViewComponent.IsVisible = true;
                    }

                    // Workstation window binds to XUiM_Workstation; refresh so tool slots re-read tile entity.
                    var wdUi = GetMemberValue(wg, "WorkstationData") ?? GetMemberValue(wg, "workstationData");
                    if (wdUi != null)
                    {
                        var tn = wdUi.GetType().FullName ?? string.Empty;
                        if (tn.IndexOf("XUiM_Workstation", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            TryInvokeNoArg(wdUi, "RefreshData");
                            TryInvokeNoArg(wdUi, "Refresh");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] UI dirty refresh failed: {ex.Message}");
            }

            try
            {
                var xui = button?.xui;
                if (xui != null)
                {
                    var playerUi = xui.playerUI;
                    if (playerUi != null)
                    {
                        CompatLog.Out("[DeadAirRepair] XUI/playerUI refresh path reached.");
                    }
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] XUI dirty refresh failed: {ex.Message}");
            }
        }

        static void ApplyFullRepair(ref ItemValue iv)
        {
            iv.UseTimes = 0;

            try
            {
                var pct = typeof(ItemValue).GetProperty("Percentage", BindingFlags.Public | BindingFlags.Instance);
                if (pct != null && pct.CanWrite)
                {
                    pct.SetValue(iv, 1f, null);
                }
            }
            catch
            {
            }
        }

        static bool IsFullyRepaired(ItemValue iv)
        {
            try
            {
                return iv.UseTimes <= 0;
            }
            catch
            {
                return false;
            }
        }

        static void ConsumeKit(ref ItemStack kit)
        {
            kit.count--;
            if (kit.count <= 0)
            {
                kit.Clear();
            }
        }

        static void ConsumeParts(ref ItemStack parts, int need)
        {
            parts.count -= need;
            if (parts.count <= 0)
            {
                parts.Clear();
            }
        }

        static object GetMemberValue(object obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var tr = Traverse.Create(obj);

            try
            {
                var p = tr.Property(name);
                if (p.PropertyExists())
                {
                    return p.GetValue();
                }
            }
            catch
            {
            }

            try
            {
                var f = tr.Field(name);
                if (f.FieldExists())
                {
                    return f.GetValue();
                }
            }
            catch
            {
            }

            return null;
        }

        static void DumpInterestingMembers(object obj, string label)
        {
            try
            {
                if (obj == null)
                {
                    CompatLog.Out($"[DeadAirRepair] {label}: null");
                    return;
                }

                var t = obj.GetType();
                CompatLog.Out($"[DeadAirRepair] Dumping members for {label}: {t.FullName}");

                while (t != null)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        string n = f.Name;
                        string ft = f.FieldType.FullName ?? f.FieldType.Name;

                        if (!LooksInteresting(n, ft))
                        {
                            continue;
                        }

                        object val = null;
                        string vt = "null";

                        try
                        {
                            val = f.GetValue(obj);
                            vt = val?.GetType().FullName ?? "null";
                        }
                        catch
                        {
                            vt = "<unreadable>";
                        }

                        CompatLog.Out($"[DeadAirRepair]   field {n}: declared={ft}, valueType={vt}");
                    }

                    foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!p.CanRead || p.GetIndexParameters().Length > 0)
                        {
                            continue;
                        }

                        string n = p.Name;
                        string pt = p.PropertyType.FullName ?? p.PropertyType.Name;

                        if (!LooksInteresting(n, pt))
                        {
                            continue;
                        }

                        object val = null;
                        string vt = "null";

                        try
                        {
                            val = p.GetValue(obj, null);
                            vt = val?.GetType().FullName ?? "null";
                        }
                        catch
                        {
                            vt = "<unreadable>";
                        }

                        CompatLog.Out($"[DeadAirRepair]   prop  {n}: declared={pt}, valueType={vt}");
                    }

                    t = t.BaseType;
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] DumpInterestingMembers failed for {label}: {ex.Message}");
            }
        }

        static bool LooksInteresting(string name, string typeName)
        {
            return ContainsIgnoreCase(name, "tile", "entity", "workstation", "loot", "tool", "grid", "container", "block", "position")
                || ContainsIgnoreCase(typeName, "TileEntity", "Workstation", "Loot", "BlockValue", "Vector3i");
        }

        static bool ContainsIgnoreCase(string source, params string[] parts)
        {
            source = source ?? string.Empty;

            foreach (var p in parts)
            {
                if (source.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        static void TryInvokeNoArg(object obj, string methodName)
        {
            if (obj == null || string.IsNullOrEmpty(methodName))
            {
                return;
            }

            try
            {
                var m = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (m != null)
                {
                    m.Invoke(obj, null);
                    CompatLog.Out($"[DeadAirRepair] Invoked {obj.GetType().FullName}.{methodName}()");
                }
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] Failed invoking {obj.GetType().FullName}.{methodName}(): {ex.Message}");
            }
        }

        static string GetStackName(ItemStack stack)
        {
            try
            {
                return stack.IsEmpty() ? "<empty>" : (stack.itemValue.ItemClass?.Name ?? "<nullItemClass>");
            }
            catch
            {
                return "<error>";
            }
        }

        static float GetUseTimesSafe(ItemValue iv)
        {
            try
            {
                return iv.UseTimes;
            }
            catch
            {
                return -1f;
            }
        }
    }
}