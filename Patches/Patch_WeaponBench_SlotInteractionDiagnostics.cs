using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Diagnostic-only patch.
    /// Prefix-only, no __result, no mutation, no blocking.
    /// Safe for mixed void/non-void methods.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_WeaponBench_SlotInteractionDiagnostics
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();

            // Workstation tool-grid methods that actually exist
            foreach (var name in new[]
            {
                "SetItem",
                "SetItemStack",
                "GetItem",
                "GetItemStack",
                "RemoveItem",
                "TakeItem",
                "HandleSlotChanged",
                "OnSlotChanged",
                "OnItemChanged"
            })
            {
                var mi = AccessTools.Method(typeof(XUiC_WorkstationToolGrid), name);
                if (mi != null && seen.Add(mi))
                {
                    CompatLog.Out($"[DeadAirDiag] Targeting XUiC_WorkstationToolGrid.{name}");
                    yield return mi;
                }
            }

            // TileEntityWorkstation methods that actually exist
            foreach (var name in new[]
            {
                "SetToolStacks",
                "GetToolStacks",
                "SetToolInSlot",
                "GetToolBySlot",
                "SetItem",
                "RemoveItem"
            })
            {
                var mi = AccessTools.Method(typeof(TileEntityWorkstation), name);
                if (mi != null && seen.Add(mi))
                {
                    CompatLog.Out($"[DeadAirDiag] Targeting TileEntityWorkstation.{name}");
                    yield return mi;
                }
            }

            // Only target Inventory methods that actually exist
            foreach (var mi in AccessTools.GetDeclaredMethods(typeof(Inventory)))
            {
                if (mi == null)
                {
                    continue;
                }

                if (mi.Name == "SetItem" || mi.Name == "RemoveItem" || mi.Name == "RemoveItems" || mi.Name == "AddItem" || mi.Name == "AddItems")
                {
                    if (seen.Add(mi))
                    {
                        CompatLog.Out($"[DeadAirDiag] Targeting Inventory.{mi.Name}({DescribeParams(mi)})");
                        yield return mi;
                    }
                }
            }
        }

        static void Prefix(MethodBase __originalMethod, object __instance, object[] __args)
        {
            try
            {
                if (!ShouldLog(__instance, __args))
                {
                    return;
                }

                CompatLog.Out(
                    $"[DeadAirDiag] CALL {__originalMethod?.DeclaringType?.Name}.{__originalMethod?.Name} " +
                    $"instance={FormatObject(__instance)} args={FormatArgs(__args)}");
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirDiag] Prefix exception: {ex.Message}");
            }
        }

        static bool ShouldLog(object instance, object[] args)
        {
            try
            {
                var player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
                var xui = player?.PlayerUI?.xui;

                if (xui != null && DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
                {
                    return true;
                }

                if (instance is XUiC_WorkstationToolGrid)
                {
                    return true;
                }

                if (instance is TileEntityWorkstation te)
                {
                    return true;
                }

                if (args != null)
                {
                    foreach (var a in args)
                    {
                        if (a is TileEntityWorkstation)
                        {
                            return true;
                        }

                        if (a is ItemStack stack && !stack.IsEmpty())
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        static string DescribeParams(MethodBase method)
        {
            try
            {
                var ps = method.GetParameters();
                if (ps == null || ps.Length == 0)
                {
                    return "";
                }

                var parts = new List<string>();
                foreach (var p in ps)
                {
                    parts.Add($"{p.ParameterType.Name} {p.Name}");
                }

                return string.Join(", ", parts);
            }
            catch
            {
                return "?";
            }
        }

        static string FormatArgs(object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return "[]";
            }

            var parts = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                parts.Add($"{i}:{FormatObject(args[i])}");
            }

            return "[" + string.Join(" | ", parts) + "]";
        }

        static string FormatObject(object obj)
        {
            if (obj == null)
            {
                return "<null>";
            }

            try
            {
                if (obj is ItemStack stack)
                {
                    return $"ItemStack(name={GetStackNameSafe(stack)}, count={stack.count})";
                }

                if (obj is ItemValue iv)
                {
                    return $"ItemValue(name={iv.ItemClass?.Name ?? "<null>"}, useTimes={GetUseTimesSafe(iv)})";
                }

                if (obj is TileEntityWorkstation te)
                {
                    return $"TileEntityWorkstation({GetBenchPosSafe(te)})";
                }

                return $"{obj.GetType().Name}";
            }
            catch
            {
                return obj.GetType().Name;
            }
        }

        static string GetStackNameSafe(ItemStack stack)
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

        static string GetBenchPosSafe(TileEntityWorkstation te)
        {
            try
            {
                var tr = Traverse.Create(te);

                foreach (var member in new[] { "ToWorldPos", "BlockPos", "blockPos", "LocalChunkPos", "localChunkPos" })
                {
                    try
                    {
                        var p = tr.Property(member);
                        if (p.PropertyExists())
                        {
                            var v = p.GetValue();
                            if (v != null)
                            {
                                return v.ToString();
                            }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        var f = tr.Field(member);
                        if (f.FieldExists())
                        {
                            var v = f.GetValue();
                            if (v != null)
                            {
                                return v.ToString();
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return "?";
        }
    }
}