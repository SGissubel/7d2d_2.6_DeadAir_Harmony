using System;
using HarmonyLib;

/// <summary>
/// Buffs all entity classes whose name starts with <c>zombieWight</c> (feral, radiated, charged, infernal, and future variants).
/// </summary>
internal static class WightEntityPatches
{
    /// <summary>Multiplies walk/crouch and aggro move speeds from GetMoveSpeed*.</summary>
    internal const float MoveSpeedMultiplier = 1.35f;

    /// <summary>
    /// Fraction of knockdown buildup (StunProne / StunKnee) kept when the attacker uses a weapon with the <c>ranged</c> item tag.
    /// Lower = harder to knock down with guns. Melee and other weapons are unchanged.
    /// </summary>
    internal const float RangedKnockdownBuildupKept = 0.22f;

    internal static bool IsZombieWightLineage(EntityAlive entity)
    {
        if (entity == null)
        {
            return false;
        }

        if (!EntityClass.list.TryGetValue(entity.entityClass, out EntityClass ec))
        {
            return false;
        }

        return ec.entityClassName != null
            && ec.entityClassName.StartsWith("zombieWight", StringComparison.Ordinal);
    }

    /// <summary>True if the attacking item is a ranged weapon (item tag "ranged"), e.g. firearms and typical bow/crossbow.</summary>
    internal static bool IsRangedWeaponDamage(DamageResponse dm)
    {
        ItemClass ic = dm.Source.ItemClass;
        return ic != null && ic.HasAnyTags(DismembermentManager.rangedTags);
    }

    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.GetMoveSpeed))]
    [HarmonyPostfix]
    private static void GetMoveSpeed_Postfix(EntityAlive __instance, ref float __result)
    {
        if (!IsZombieWightLineage(__instance))
        {
            return;
        }

        __result *= MoveSpeedMultiplier;
    }

    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.GetMoveSpeedAggro))]
    [HarmonyPostfix]
    private static void GetMoveSpeedAggro_Postfix(EntityAlive __instance, ref float __result)
    {
        if (!IsZombieWightLineage(__instance))
        {
            return;
        }

        __result *= MoveSpeedMultiplier;
    }

    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.GetMoveSpeedPanic))]
    [HarmonyPostfix]
    private static void GetMoveSpeedPanic_Postfix(EntityAlive __instance, ref float __result)
    {
        if (!IsZombieWightLineage(__instance))
        {
            return;
        }

        __result *= MoveSpeedMultiplier;
    }

    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.ProcessDamageResponseLocal))]
    [HarmonyPrefix]
    private static void ProcessDamageResponseLocal_Prefix(EntityAlive __instance, out int __stateProne, out int __stateKnee)
    {
        __stateProne = __instance.bodyDamage.StunProne;
        __stateKnee = __instance.bodyDamage.StunKnee;
    }

    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.ProcessDamageResponseLocal))]
    [HarmonyPostfix]
    private static void ProcessDamageResponseLocal_Postfix(
        EntityAlive __instance,
        DamageResponse _dmResponse,
        int __stateProne,
        int __stateKnee)
    {
        if (!IsZombieWightLineage(__instance))
        {
            return;
        }

        if (!_dmResponse.Source.CanStun)
        {
            return;
        }

        if (!IsRangedWeaponDamage(_dmResponse))
        {
            return;
        }

        int dProne = __instance.bodyDamage.StunProne - __stateProne;
        int dKnee = __instance.bodyDamage.StunKnee - __stateKnee;
        if (dProne <= 0 && dKnee <= 0)
        {
            return;
        }

        if (dProne > 0)
        {
            __instance.bodyDamage.StunProne = __stateProne + (int)Math.Floor(dProne * (double)RangedKnockdownBuildupKept);
        }

        if (dKnee > 0)
        {
            __instance.bodyDamage.StunKnee = __stateKnee + (int)Math.Floor(dKnee * (double)RangedKnockdownBuildupKept);
        }
    }
}
