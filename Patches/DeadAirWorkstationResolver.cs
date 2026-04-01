using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    public static class DeadAirWorkstationResolver
    {
        public static TileEntityWorkstation TryResolveTileEntity(object obj)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            return TryResolveTileEntityRecursive(obj, 0, 4, visited);
        }

        static TileEntityWorkstation TryResolveTileEntityRecursive(object obj, int depth, int maxDepth, HashSet<object> visited)
        {
            if (obj == null || depth > maxDepth)
            {
                return null;
            }

            if (!obj.GetType().IsValueType)
            {
                if (!visited.Add(obj))
                {
                    return null;
                }
            }

            if (obj is TileEntityWorkstation tew)
            {
                return tew;
            }

            if (obj is TileEntity te && te is TileEntityWorkstation tew2)
            {
                return tew2;
            }

            // First: direct known members on common UI objects
            foreach (var name in new[]
            {
                "WorkstationData",
                "workstationData",
                "toolWindow",
                "outputWindow",
                "currentWorkstationToolGrid",
                "currentWorkstationOutputGrid",
                "lootContainer",
                "LootContainer",
                "tileEntity",
                "TileEntity",
                "tileEntityWorkstation",
                "TileEntityWorkstation",
                "workstation",
                "Workstation",
                "m_tileEntity",
                "m_workstation"
            })
            {
                var child = TryGetMemberValue(obj, name);
                var found = TryResolveTileEntityRecursive(child, depth + 1, maxDepth, visited);
                if (found != null)
                {
                    return found;
                }
            }

            // Then: follow obvious controller links
            if (obj is XUiController ctrl)
            {
                foreach (var child in new object[]
                {
                    ctrl.windowGroup,
                    ctrl.windowGroup?.Controller,
                    ctrl.xui
                })
                {
                    var found = TryResolveTileEntityRecursive(child, depth + 1, maxDepth, visited);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            // Finally: shallow scan of interesting fields/properties only
            var type = obj.GetType();
            while (type != null)
            {
                foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!LooksRelevant(f.Name, f.FieldType))
                    {
                        continue;
                    }

                    object val = null;
                    try
                    {
                        val = f.GetValue(obj);
                    }
                    catch
                    {
                    }

                    var found = TryResolveTileEntityRecursive(val, depth + 1, maxDepth, visited);
                    if (found != null)
                    {
                        return found;
                    }
                }

                foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    if (!LooksRelevant(p.Name, p.PropertyType))
                    {
                        continue;
                    }

                    object val = null;
                    try
                    {
                        val = p.GetValue(obj);
                    }
                    catch
                    {
                    }

                    var found = TryResolveTileEntityRecursive(val, depth + 1, maxDepth, visited);
                    if (found != null)
                    {
                        return found;
                    }
                }

                type = type.BaseType;
            }

            return null;
        }

        static object TryGetMemberValue(object obj, string name)
        {
            if (obj == null)
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

            // Also support compiler-generated backing field names like <currentWorkstationToolGrid>k__BackingField
            try
            {
                var backing = tr.Field($"<{name}>k__BackingField");
                if (backing.FieldExists())
                {
                    return backing.GetValue();
                }
            }
            catch
            {
            }

            return null;
        }

        static bool LooksRelevant(string memberName, Type memberType)
        {
            string n = memberName ?? string.Empty;
            string t = memberType?.FullName ?? string.Empty;

            return ContainsAny(n, "tile", "entity", "workstation", "loot", "tool", "output", "grid", "container")
                || ContainsAny(t, "TileEntity", "Workstation", "Loot", "XUiM_Workstation", "XUiC_Workstation");
        }

        static bool ContainsAny(string source, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (source.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
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
    }
}