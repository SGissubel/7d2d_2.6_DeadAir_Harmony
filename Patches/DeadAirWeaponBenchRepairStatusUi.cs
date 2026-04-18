using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Updates only the dedicated status label on windowDeadAirWeaponBenchInputs.
    /// Reverted-branch safe: does not depend on TryGetWeaponBenchWorkstationGroup,
    /// ResolveBenchFromWorkstationGroup, or the standalone label-test patch.
    /// </summary>
    public static class DeadAirWeaponBenchRepairStatusUi
    {
        public static void RefreshOpenBenchStatusLabel()
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

                var wg = TryGetBenchWorkstationGroup(xui);
                if (wg == null)
                {
                    return;
                }


                var te = TryResolveBenchFromWorkstationGroup(wg);
                if (te == null)
                {
                    return;
                }


                var labelCtrl = FindStatusLabelController(wg);
                if (labelCtrl == null)
                {
                    return;
                }


                if (DeadAirWeaponBenchRepairQueue.TryGetRemainingSeconds(te, out int sec))
                {
                    int m = sec / 60;
                    int s = sec % 60;

                    string line;
                    try
                    {
                        string fmt = Localization.Get("deadairRepairStatusLine");
                        if (string.IsNullOrEmpty(fmt) || string.Equals(fmt, "deadairRepairStatusLine", StringComparison.OrdinalIgnoreCase))
                        {
                            line = $"Refurbish Timer: {m}:{s:00}";
                        }
                        else
                        {
                            line = string.Format(fmt, m, s.ToString("00"));
                        }
                    }
                    catch
                    {
                        line = $"Refurbish Timer: {m}:{s:00}";
                    }

                    ApplyLabel(labelCtrl, line, true);
                }
                else
                {
                    ApplyLabel(labelCtrl, string.Empty, false);
                }
            }
            catch
            {
            }
        }

        static XUiC_WorkstationWindowGroup TryGetBenchWorkstationGroup(XUi xui)
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

                if (wgObj is XUiC_WorkstationWindowGroup direct)
                {
                    return direct;
                }

                if (wgObj is XUiController ctrl && ctrl is XUiC_WorkstationWindowGroup asGroup)
                {
                    return asGroup;
                }

                var tr = Traverse.Create(wgObj);

                try
                {
                    var p = tr.Property("Controller");
                    if (p.PropertyExists())
                    {
                        return p.GetValue() as XUiC_WorkstationWindowGroup;
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
                        return f.GetValue() as XUiC_WorkstationWindowGroup;
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
                        return f.GetValue() as XUiC_WorkstationWindowGroup;
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

        static TileEntityWorkstation TryResolveBenchFromWorkstationGroup(XUiC_WorkstationWindowGroup wg)
        {
            if (wg == null)
            {
                return null;
            }

            try
            {
                var workstationData = GetMemberValue(wg, "WorkstationData")
                                   ?? GetMemberValue(wg, "workstationData");

                if (workstationData != null)
                {
                    var directTileEntity = GetMemberValue(workstationData, "tileEntity")
                                        ?? GetMemberValue(workstationData, "TileEntity");

                    if (directTileEntity is TileEntityWorkstation te)
                    {
                        return te;
                    }

                    if (directTileEntity is TileEntity baseTe)
                    {
                        return baseTe as TileEntityWorkstation;
                    }
                }
            }
            catch
            {
            }

            return null;
        }


        static XUiController FindDescendantControllerByViewName(XUiController root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            if (ViewLooksLikeTarget(root, targetName))
            {
                return root;
            }

            foreach (var child in EnumerateChildControllers(root))
            {
                var found = FindDescendantControllerByViewName(child, targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static bool ViewLooksLikeTarget(XUiController ctrl, string targetName)
        {
            if (ctrl?.ViewComponent == null || string.IsNullOrEmpty(targetName))
            {
                return false;
            }

            try
            {
                var vc = ctrl.ViewComponent;
                var tr = Traverse.Create(vc);

                foreach (var member in new[] { "ID", "Id", "id", "Name", "name" })
                {
                    try
                    {
                        var p = tr.Property(member);
                        if (p.PropertyExists())
                        {
                            var v = p.GetValue() as string;
                            if (!string.IsNullOrEmpty(v) &&
                                string.Equals(v, targetName, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
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
                            if (!string.IsNullOrEmpty(v) &&
                                string.Equals(v, targetName, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    var go = tr.Property("gameObject").GetValue() as UnityEngine.GameObject
                        ?? tr.Field("gameObject").GetValue() as UnityEngine.GameObject;

                    if (go != null &&
                        string.Equals(go.name, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }
            catch
            {
            }

            return false;
        }
        static XUiController FindStatusLabelController(XUiC_WorkstationWindowGroup wg)
        {
            string id = DeadAirWeaponBenchRepairQueue.StatusLabelId;

            var win = TryInvokeGetChildById(wg, "windowDeadAirWeaponBenchInputs");
            if (win != null)
            {
                var onWin = FindDescendantControllerById(win, id);
                if (onWin != null)
                {
                    return onWin;
                }

                var byViewName = FindDescendantControllerByViewName(win, id);
                if (byViewName != null)
                {
                    return byViewName;
                }
            }

            var onRoot = FindDescendantControllerById(wg, id);
            if (onRoot != null)
            {
                return onRoot;
            }

            var byRootViewName = FindDescendantControllerByViewName(wg, id);
            if (byRootViewName != null)
            {
                return byRootViewName;
            }

            return null;
        }

        static XUiController TryInvokeGetChildById(XUiController root, string childId)
        {
            if (root == null || string.IsNullOrEmpty(childId))
            {
                return null;
            }

            try
            {
                for (var t = root.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    var mi = t.GetMethod(
                        "GetChildById",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(string) },
                        null);

                    if (mi != null)
                    {
                        return mi.Invoke(root, new object[] { childId }) as XUiController;
                    }
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