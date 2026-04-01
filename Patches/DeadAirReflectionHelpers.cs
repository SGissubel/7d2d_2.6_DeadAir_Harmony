using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    internal static class DeadAirReflectionHelpers
    {
        private static readonly Dictionary<Type, string> CachedEntityClassNames = new Dictionary<Type, string>();
        private static readonly string[] HitPosNames = { "hitBlockPos", "HitBlockPos", "blockPos", "BlockPos", "hitPos", "HitPos" };
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static string GetEntityClassLikeName(EntityAlive entity)
        {
            if (entity == null)
                return string.Empty;

            Type runtimeType = entity.GetType();
            if (CachedEntityClassNames.TryGetValue(runtimeType, out string cached))
                return cached;

            string result = string.Empty;

            try
            {
                if (!string.IsNullOrEmpty(entity.EntityName))
                    result = entity.EntityName;
            }
            catch { }

            if (string.IsNullOrEmpty(result))
            {
                try
                {
                    object ec = GetMemberValue(runtimeType, entity, "entityClass") ??
                                GetMemberValue(runtimeType, entity, "EntityClass");

                    if (ec != null && !(ec is int) && !(ec is string))
                    {
                        Type ect = ec.GetType();
                        object val = GetMemberValue(ect, ec, "entityClassName") ??
                                     GetMemberValue(ect, ec, "EntityClassName") ??
                                     GetMemberValue(ect, ec, "name") ??
                                     GetMemberValue(ect, ec, "Name");

                        if (val is string s && !string.IsNullOrEmpty(s))
                            result = s;
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(result))
                result = runtimeType.Name ?? string.Empty;

            CachedEntityClassNames[runtimeType] = result;
            return result;
        }

        internal static bool HasAttackTarget(EntityAlive entity)
        {
            if (entity == null)
                return false;

            try
            {
                return GetMemberValue(entity.GetType(), entity, "attackTarget") != null ||
                       GetMemberValue(entity.GetType(), entity, "AttackTarget") != null;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetBoolProperty(object obj, string propName, out bool value)
        {
            value = false;
            if (obj == null || string.IsNullOrEmpty(propName))
                return false;

            try
            {
                object val = GetPropertyValue(obj.GetType(), obj, propName);
                if (val is bool b)
                {
                    value = b;
                    return true;
                }
            }
            catch { }

            return false;
        }

        internal static int? TryGetIntMethod(object obj, string methodName)
        {
            if (obj == null || string.IsNullOrEmpty(methodName))
                return null;

            try
            {
                var m = obj.GetType().GetMethod(methodName, Flags, null, Type.EmptyTypes, null);
                if (m == null)
                    return null;

                object val = m.Invoke(obj, Array.Empty<object>());
                if (val is int i)
                    return i;
            }
            catch { }

            return null;
        }

        internal static Vector3i GetVector3iFromAttackHitInfo(object attackHitInfo)
        {
            if (attackHitInfo == null)
                return Vector3i.zero;

            try
            {
                Type t = attackHitInfo.GetType();
                for (int i = 0; i < HitPosNames.Length; i++)
                {
                    object val = GetMemberValue(t, attackHitInfo, HitPosNames[i]);
                    if (val is Vector3i v)
                        return v;
                }
            }
            catch { }

            return Vector3i.zero;
        }

        private static object GetMemberValue(Type t, object instance, string name)
        {
            return GetFieldValue(t, instance, name) ?? GetPropertyValue(t, instance, name);
        }

        private static object GetFieldValue(Type t, object instance, string name)
        {
            var f = t.GetField(name, Flags);
            return f?.GetValue(instance);
        }

        private static object GetPropertyValue(Type t, object instance, string name)
        {
            var p = t.GetProperty(name, Flags);
            return p?.GetValue(instance, null);
        }
    }
}