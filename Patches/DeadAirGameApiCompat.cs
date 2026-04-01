using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

/// <summary>Reflection-safe helpers for vanilla API differences across game builds.</summary>
namespace DeadAir_7LongDarkDays.Patches
{
    internal static class DeadAirGameApiCompat
    {
        internal static bool ItemClassRepairToolsMentions(ItemClass ic, string toolName)
        {
            if (ic == null || string.IsNullOrEmpty(toolName))
                return false;

            var p = typeof(ItemClass).GetProperty("RepairTools", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var val = p?.GetValue(ic);
            if (val == null)
                return false;

            return ObjectMentionsToolName(val, toolName);
        }

        internal static bool ItemClassHasAnyRepairTool(ItemClass ic)
        {

            if (ic == null)
                return false;

            CompatLog.Out($"[DeadAirRepair] Compat check item IC={ic}");
            var p = typeof(ItemClass).GetProperty("RepairTools", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            CompatLog.Out($"[DeadAirRepair] Compat check item P={p}");
            var val = p?.GetValue(ic);
            CompatLog.Out($"[DeadAirRepair] Compat check item VAL={val}");
            if (val == null)
                return false;

            if (val is string s)
                return s.Length > 0;

            foreach (string unused in EnumerateRepairToolTokens(val))
                return true;

            return false;
        }

        static bool ObjectMentionsToolName(object val, string toolName)
        {
            if (val is string str)
                return str.IndexOf(toolName, StringComparison.OrdinalIgnoreCase) >= 0;

            foreach (string token in EnumerateRepairToolTokens(val))
            {
                if (!string.IsNullOrEmpty(token) &&
                    token.IndexOf(toolName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            try
            {
                var ts = val.ToString();
                return !string.IsNullOrEmpty(ts) &&
                    ts.IndexOf(toolName, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        static IEnumerable<string> EnumerateRepairToolTokens(object val)
        {
            if (val == null)
                yield break;

            var t = val.GetType();

            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object fv = null;
                bool ok = false;

                try
                {
                    fv = f.GetValue(val);
                    ok = true;
                }
                catch
                {
                }

                if (!ok || fv == null)
                    continue;

                if (fv is string fvs)
                {
                    if (!string.IsNullOrEmpty(fvs))
                        yield return fvs;
                }
                else if (fv is string[] arr)
                {
                    foreach (var e in arr)
                    {
                        if (!string.IsNullOrEmpty(e))
                            yield return e;
                    }
                }
                else if (fv is IEnumerable enField && !(fv is string))
                {
                    foreach (var item in enField)
                    {
                        foreach (var tok in TokensFromPiece(item))
                            yield return tok;
                    }
                }
            }

            foreach (var pr in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!pr.CanRead || pr.GetIndexParameters().Length > 0)
                    continue;

                object pv = null;
                bool ok = false;

                try
                {
                    pv = pr.GetValue(val);
                    ok = true;
                }
                catch
                {
                }

                if (!ok || pv == null)
                    continue;

                if (pv is string pvs)
                {
                    if (!string.IsNullOrEmpty(pvs))
                        yield return pvs;
                }
                else if (pv is string[] arr2)
                {
                    foreach (var e in arr2)
                    {
                        if (!string.IsNullOrEmpty(e))
                            yield return e;
                    }
                }
                else if (pv is IEnumerable enProp && !(pv is string))
                {
                    foreach (var item in enProp)
                    {
                        foreach (var tok in TokensFromPiece(item))
                            yield return tok;
                    }
                }
            }
        }

        static IEnumerable<string> TokensFromPiece(object item)
        {
            if (item == null)
                yield break;

            if (item is string st)
            {
                if (!string.IsNullOrEmpty(st))
                    yield return st;
                yield break;
            }

            var it = item.GetType();

            foreach (var f in it.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType != typeof(string))
                    continue;

                string sf = null;
                bool ok = false;

                try
                {
                    sf = f.GetValue(item) as string;
                    ok = true;
                }
                catch
                {
                }

                if (ok && !string.IsNullOrEmpty(sf))
                    yield return sf;
            }

            foreach (var p in it.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.PropertyType != typeof(string) || !p.CanRead || p.GetIndexParameters().Length > 0)
                    continue;

                string sp = null;
                bool ok = false;

                try
                {
                    sp = p.GetValue(item) as string;
                    ok = true;
                }
                catch
                {
                }

                if (ok && !string.IsNullOrEmpty(sp))
                    yield return sp;
            }
        }

        internal static string TryGetXUiControlName(object ctrl)
        {
            if (ctrl == null)
                return null;

            var tr = Traverse.Create(ctrl);
            foreach (var key in new[] { "ID", "id", "Name", "name", "m_ID", "m_id", "m_Name", "m_name", "controlName", "ControlName" })
            {
                try
                {
                    var p = tr.Property(key);
                    if (p.PropertyExists())
                    {
                        if (p.GetValue() is string s && s.Length > 0)
                            return s;
                    }
                }
                catch
                {
                }

                try
                {
                    var f = tr.Field(key);
                    if (f.FieldExists())
                    {
                        if (f.GetValue() is string s2 && s2.Length > 0)
                            return s2;
                    }
                }
                catch
                {
                }
            }

            try
            {
                object vc = null;
                var vp = tr.Property("ViewComponent");
                if (vp.PropertyExists())
                    vc = vp.GetValue();

                if (vc == null)
                {
                    var vf = tr.Field("viewComponent");
                    if (!vf.FieldExists())
                        vf = tr.Field("ViewComponent");

                    if (vf.FieldExists())
                        vc = vf.GetValue();
                }

                if (vc != null)
                {
                    var nested = TryGetXUiControlName(vc);
                    if (!string.IsNullOrEmpty(nested))
                        return nested;
                }
            }
            catch
            {
            }

            return null;
        }

        internal static void TryMarkTileEntityDirty(TileEntity te)
        {
            if (te == null)
                return;

            var t = te.GetType();
            while (t != null)
            {
                MethodInfo m = t.GetMethod("MarkDirty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (m != null)
                {
                    try
                    {
                        m.Invoke(te, null);
                        return;
                    }
                    catch
                    {
                        return;
                    }
                }

                m = t.GetMethod("MarkDirty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
                if (m != null)
                {
                    try
                    {
                        m.Invoke(te, new object[] { true });
                        return;
                    }
                    catch
                    {
                        return;
                    }
                }

                t = t.BaseType;
            }

            t = te.GetType();
            while (t != null)
            {
                foreach (var mn in new[] { "SetModified", "MarkForUpdate", "markForUpdate" })
                {
                    var m = t.GetMethod(mn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (m != null)
                    {
                        try
                        {
                            m.Invoke(te, null);
                            return;
                        }
                        catch
                        {
                            return;
                        }
                    }
                }

                t = t.BaseType;
            }
        }
    }
}