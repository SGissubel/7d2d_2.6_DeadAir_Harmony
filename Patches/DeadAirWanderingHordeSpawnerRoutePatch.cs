using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    /// <summary>
    /// Postfix on <see cref="AIWanderingHordeSpawner.Update"/>:
    /// after vanilla advances the wandering runner, nudge the public route anchors
    /// <see cref="AIWanderingHordeSpawner.endPos"/> and <see cref="AIWanderingHordeSpawner.pitStopPos"/>
    /// toward the primary player (with optional velocity lead).
    /// Metadata (v2.6): instance methods
    /// <see cref="AIWanderingHordeSpawner.Update"/>(World world, float _deltaTime),
    /// <see cref="AIWanderingHordeSpawner.UpdateSpawn"/>(World _world, float _deltaTime),
    /// <see cref="AIWanderingHordeSpawner.UpdateHorde"/>(float dt).
    /// </summary>
    [HarmonyPatch(typeof(AIWanderingHordeSpawner), nameof(AIWanderingHordeSpawner.Update))]
    public static class DeadAirWanderingHordeSpawnerRoutePatch
    {
        private static MethodInfo _resolvedVanilla;
        private static float _logAccum;

        public static bool Prepare()
        {
            if (!DeadAirWanderingHordeTunables.SpawnerRoutePatchEnabled)
                return false;

            _resolvedVanilla = AccessTools.DeclaredMethod(
                typeof(AIWanderingHordeSpawner),
                nameof(AIWanderingHordeSpawner.Update));

            if (_resolvedVanilla == null)
            {
                CompatLog.Warning("[DeadAir] WanderingHorde: AIWanderingHordeSpawner.Update not found; route patch disabled.");
                return false;
            }

            CompatLog.Out(
                "[DeadAir] WanderingHorde: route patch ENABLED (Update postfix). Resolved vanilla signature: " +
                DeadAirWanderingHordePatch.FormatMethod(_resolvedVanilla));

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(AIWanderingHordeSpawner __instance, World world, float _deltaTime)
        {
            if (__instance == null || world == null || _deltaTime <= 0f)
                return;

            string spawnTypeName = __instance.spawnType.ToString();
            if (!string.Equals(spawnTypeName, "Horde", StringComparison.OrdinalIgnoreCase))
                return;

            var player = world.GetPrimaryPlayer();
            if (player == null || player.IsDead())
                return;

            try
            {
                Vector3 aim = ComputeAimPoint(player);
                float k = 1f - Mathf.Exp(-DeadAirWanderingHordeTunables.RoutePullPerSecond * _deltaTime);

                Vector3 endBefore = __instance.endPos;
                Vector3 pitBefore = __instance.pitStopPos;

                __instance.endPos = PullHorizontal(endBefore, aim, k);
                __instance.pitStopPos = PullHorizontal(pitBefore, aim, k * 0.72f);

                if (!DeadAirWanderingHordeTunables.SpawnerRouteVerboseLog)
                    return;

                _logAccum += _deltaTime;
                if (_logAccum < DeadAirWanderingHordeTunables.SpawnerRouteLogIntervalSeconds)
                    return;

                _logAccum = 0f;

                float dEnd = HorizontalDistance(player.position, endBefore);
                float dEndAfter = HorizontalDistance(player.position, __instance.endPos);

                CompatLog.Out(
                    $"[DeadAir] WanderingHorde Route: aim={aim} player={player.position} " +
                    $"endBefore={endBefore} endAfter={__instance.endPos} pitBefore={pitBefore} pitAfter={__instance.pitStopPos} " +
                    $"distPlayerToEndBefore={dEnd:0.0} distPlayerToEndAfter={dEndAfter:0.0} kFrame={k:0.000}");
            }
            catch (Exception ex)
            {
                CompatLog.Warning("[DeadAir] WanderingHorde route postfix: " + ex);
            }
        }

        private static Vector3 ComputeAimPoint(EntityPlayer player)
        {
            Vector3 p = player.position;
            Vector3 v = player.GetVelocityPerSecond();
            v.y = 0f;

            if (v.sqrMagnitude < 0.0004f)
                return p;

            Vector3 lead = v * DeadAirWanderingHordeTunables.RouteLeadSeconds;
            if (lead.magnitude > DeadAirWanderingHordeTunables.RouteLeadMaxHorizontal && lead.sqrMagnitude > 0.0001f)
                lead = lead.normalized * DeadAirWanderingHordeTunables.RouteLeadMaxHorizontal;

            return p + lead;
        }

        /// <summary>Blend horizontally toward aim; preserve terrain height from the source anchor.</summary>
        private static Vector3 PullHorizontal(Vector3 from, Vector3 aim, float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f)
                return from;

            Vector3 a = from;
            a.y = 0f;
            Vector3 b = aim;
            b.y = 0f;
            Vector3 m = Vector3.Lerp(a, b, t);
            return new Vector3(m.x, from.y, m.z);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
