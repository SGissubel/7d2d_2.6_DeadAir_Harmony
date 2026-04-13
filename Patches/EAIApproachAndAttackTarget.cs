using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch(typeof(EAIApproachAndAttackTarget), nameof(EAIApproachAndAttackTarget.GetMoveToLocation))]
    public static class Patch_EAIApproachAndAttackTarget_GetMoveToLocation_AntiLineup
    {
        private const bool DebugGetMoveToLocation = true;

        private static readonly FieldInfo FieldTheEntity =
            AccessTools.Field(typeof(EAIBase), "theEntity");

        private static readonly FieldInfo FieldEntityTarget =
            AccessTools.Field(typeof(EAIApproachAndAttackTarget), "entityTarget");

        private static readonly FieldInfo FieldEntityTargetVel =
            AccessTools.Field(typeof(EAIApproachAndAttackTarget), "entityTargetVel");

        private static readonly FieldInfo FieldIsTargetToEat =
            AccessTools.Field(typeof(EAIApproachAndAttackTarget), "isTargetToEat");

        public static void Postfix(EAIApproachAndAttackTarget __instance, float maxDist, ref Vector3 __result)
        {
            try
            {
                if (FieldTheEntity == null || FieldEntityTarget == null)
                {

                    return;
                }

                EntityAlive zombie = FieldTheEntity.GetValue(__instance) as EntityAlive;
                EntityAlive target = FieldEntityTarget.GetValue(__instance) as EntityAlive;

                if (zombie == null || target == null || target.IsDead())
                {
                    return;
                }

                bool isTargetToEat = false;
                if (FieldIsTargetToEat != null && FieldIsTargetToEat.GetValue(__instance) is bool eat)
                {
                    isTargetToEat = eat;
                }

                if (isTargetToEat)
                {
                    return;
                }

                Vector3 zombiePos = zombie.position;
                Vector3 targetPos = target.position;

                Vector3 toTarget = targetPos - zombiePos;
                float dy = toTarget.y;
                toTarget.y = 0f;

                float dist = toTarget.magnitude;
                if (toTarget.sqrMagnitude < 0.0001f)
                {
                    return;
                }

                Vector3 forward = toTarget.normalized;
                Vector3 baseGoal = __result;

                bool directChaseApplied = false;
                bool hardCloseApplied = false;


                // ------------------------------------------------------------
                // 1) Direct chase base goal
                //
                // If we have LOS to a player and are in a reasonable vertical/horizontal band,
                // prefer a grounded predicted player position as the BASE goal.
                //
                // This helps reduce the weird cardinal/stand-off feel without deleting spread,
                // because spread is applied AFTER this base goal is chosen.
                // ------------------------------------------------------------
                if (target is EntityPlayer &&
                    zombie.CanSee(target) &&
                    Mathf.Abs(dy) <= 2.5f &&
                    dist <= 12f)
                {
                    Vector3 vel = Vector3.zero;
                    if (FieldEntityTargetVel != null && FieldEntityTargetVel.GetValue(__instance) is Vector3 targetVel)
                    {
                        vel = targetVel;
                    }

                    Vector3 predictedGoal = target.position + vel * 6f;
                    predictedGoal = target.world.FindSupportingBlockPos(predictedGoal);

                    baseGoal = predictedGoal;
                    directChaseApplied = true;

                }

                // ------------------------------------------------------------
                // 2) Hard-close tightening
                //
                // If the zombie is in the near band and the player is actually moving away,
                // keep the goal tightly on the player instead of letting the close band feel floaty.
                // This does NOT remove spread; it only tightens the BASE goal first.
                // ------------------------------------------------------------
                if (target is EntityPlayer)
                {
                    Vector3 targetMotion = Vector3.zero;

                    if (FieldEntityTargetVel != null)
                    {
                        object velObj = FieldEntityTargetVel.GetValue(__instance);
                        if (velObj is Vector3 vel)
                            targetMotion = vel;
                    }

                    targetMotion.y = 0f;

                    float targetSpeed = targetMotion.magnitude;
                    float fleeDot = 0f;

                    if (targetSpeed > 0.001f)
                    {
                        fleeDot = Vector3.Dot(targetMotion.normalized, forward);
                    }

                    bool inHardCloseBand = dist >= .90f && dist <= 6.50f;
                    bool playerIsFleeing = targetSpeed >= 0.12f && fleeDot >= 0.15f;

                    if (inHardCloseBand && playerIsFleeing)
                    {
                        Vector3 hardCloseGoal = target.world.FindSupportingBlockPos(target.position);
                        baseGoal = hardCloseGoal;
                        hardCloseApplied = true;

                    }
                }

                // ------------------------------------------------------------
                // 3) Anti-lineup spread
                //
                // Spread is applied on top of the chosen BASE goal.
                // That means we keep the pack from stacking perfectly,
                // while still making them chase more directly overall.
                // ------------------------------------------------------------
                if (dist >= 1.35f && dist <= 10f)
                {
                    Vector3 lateral = new Vector3(-forward.z, 0f, forward.x);

                    float side = (zombie.entityId & 1) == 0 ? -1f : 1f;

                    int hash = zombie.entityId * 1103515245 + 12345;
                    float stable01 = ((hash & 0x7fffffff) % 1000) / 999f;

                    float lateralAmount = Mathf.Lerp(0.70f, 1.25f, stable01);
                    float distanceScale = Mathf.InverseLerp(1.35f, 8.5f, dist);
                    lateralAmount *= Mathf.Lerp(0.85f, 1.12f, distanceScale);

                    float radialJitter = Mathf.Lerp(-0.15f, 0.15f, stable01);

                    // When direct chase or hard-close is active, keep spread,
                    // but tone it down a bit so they don't look like they are
                    // making strange sideways runs instead of actually pressing you.
                    if (directChaseApplied)
                    {
                        lateralAmount *= 0.82f;
                        radialJitter *= 0.70f;
                    }

                    if (hardCloseApplied)
                    {
                        lateralAmount *= 0.25f;
                        radialJitter *= 0.10f;
                    }

                    Vector3 adjusted = baseGoal + lateral * (side * lateralAmount) + forward * radialJitter;
                    adjusted = target.world.FindSupportingBlockPos(adjusted);

                    baseGoal = adjusted;

                }


                __result = baseGoal;

            }
            catch (System.Exception ex)
            {
                CompatLog.Error($"[GetMoveToLocation] Postfix failed: {ex}");
            }
        }

        private static string FormatVec(Vector3 v)
        {
            return $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";
        }
    }
}