using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Standalone status-label updater for the DeadAir weapon bench.
    /// This file ONLY drives lblDeadAirRepairCountdown visibility/text.
    /// It does not alter repair logic, queue logic, or bench state.
    /// Safe for the reverted working branch because it does not depend on
    /// later helper methods like TryGetWeaponBenchWorkstationGroup.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "Update")]
    public static class DeadAirWeaponBenchCountdownLabelTest
    {
        const string StatusLabelId = "lblDeadAirRepairCountdown";
        const string TestText = "Refurbish Timer: 01:00";

        static void Postfix()
        {
            try
            {
                var player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
                var xui = player?.PlayerUI?.xui;
                if (xui == null)
                {
                    return;
                }

                if (!DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(xui))
                {
                    return;
                }

                var root = TryGetBenchRootController(xui);
                if (root == null)
                {
                    return;
                }

                var label = FindDescendantControllerById(root, StatusLabelId);
                if (label == null)
                {
                    return;
                }

                ApplyLabel(label, TestText, true);
            }
            catch (Exception ex)
            {
                CompatLog.Out($"[DeadAirRepair] CountdownLabelTest: {ex.Message}");
            }
        }

        static XUiController TryGetBenchRootController(XUi xui)
        {
            if (xui == null)
            {
                return null;
            }

            try
            {
                object wgObj = xui.FindWindowGroupByName(DeadAirWeaponBenchHelpers.BenchWindowGroupName);
                if (wgObj == null)
                {
                    return null;
                }

                if (wgObj is XUiController ctrl)
                {
                    return ctrl;
                }

                var tr = Traverse.Create(wgObj);

                try
                {
                    var p = tr.Property("Controller");
                    if (p.PropertyExists())
                    {
                        return p.GetValue() as XUiController;
                    }
                }
                catch
                {
                }

                try
                {
                    var f = tr.Field("controller");
                    if (f.FieldExists())
                    {
                        return f.GetValue() as XUiController;
                    }
                }
                catch
                {
                }

                try
                {
                    var f = tr.Field("Controller");
                    if (f.FieldExists())
                    {
                        return f.GetValue() as XUiController;
                    }
                }
                catch
                {
                }
            }
            catch
            {
            }

            return null;
        }

        static XUiController FindDescendantControllerById(XUiController root, string id)
        {
            if (root == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            var rootId = TryGetControllerId(root);
            if (!string.IsNullOrEmpty(rootId) &&
                string.Equals(rootId, id, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            foreach (var child in EnumerateChildControllers(root))
            {
                var found = FindDescendantControllerById(child, id);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static string TryGetControllerId(XUiController controller)
        {
            if (controller == null)
            {
                return null;
            }

            try
            {
                var tr = Traverse.Create(controller);

                foreach (var member in new[] { "ID", "id", "WindowId", "windowId", "controlName", "ControlName", "Name", "name" })
                {
                    try
                    {
                        var p = tr.Property(member);
                        if (p.PropertyExists())
                        {
                            var v = p.GetValue() as string;
                            if (!string.IsNullOrEmpty(v))
                            {
                                return v;
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
                            var v = f.GetValue() as string;
                            if (!string.IsNullOrEmpty(v))
                            {
                                return v;
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

            return null;
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

            if (childrenObj is IEnumerable enumerable)
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

        static void ApplyLabel(XUiController ctrl, string text, bool visible)
        {
            if (ctrl == null)
            {
                return;
            }

            try
            {
                var ctrlTr = Traverse.Create(ctrl);

                try
                {
                    var p = ctrlTr.Property("IsVisible");
                    if (p.PropertyExists())
                    {
                        p.SetValue(visible);
                    }
                }
                catch
                {
                }

                try
                {
                    var f = ctrlTr.Field("isVisible");
                    if (f.FieldExists())
                    {
                        f.SetValue(visible);
                    }
                }
                catch
                {
                }
            }
            catch
            {
            }

            var vc = ctrl.ViewComponent;
            if (vc == null)
            {
                return;
            }

            var tr = Traverse.Create(vc);

            try
            {
                var p = tr.Property("IsVisible");
                if (p.PropertyExists())
                {
                    p.SetValue(visible);
                }
            }
            catch
            {
            }

            try
            {
                tr.Property("Text").SetValue(text);
            }
            catch
            {
                try
                {
                    tr.Field("text").SetValue(text);
                }
                catch
                {
                }
            }
        }
    }
}