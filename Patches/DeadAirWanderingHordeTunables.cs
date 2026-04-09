using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    // =============================================================================
    // Tunables — wandering horde (AIDirectorWanderingHordeComponent / AIWanderingHordeSpawner)
    // =============================================================================
    internal static class DeadAirWanderingHordeTunables
    {
        // --- Frequency (SetNextTime postfix) ---
        internal const bool SetNextTimePatchEnabled = true;

        /// <summary>
        /// Values > 1 shorten the remaining wait until HordeNextTime (e.g. 2 ≈ double frequency).
        /// </summary>
        internal const float HordeFrequencyMultiplier = 2f;

        /// <summary>
        /// Log every Horde SetNextTime adjustment (recommended while tuning).
        /// </summary>
        internal const bool SetNextTimeVerboseLog = true;

        // --- Direction (RandomPos postfix) ---
        internal const bool RandomPosPatchEnabled = true;

        /// <summary>
        /// 0 = vanilla; small values keep investigate bias conservative.
        /// </summary>
        internal const float InvestigateBiasTowardPlayer = 0.35f;

        /// <summary>
        /// Log each RandomPos bias application (can be noisy during an active horde).
        /// </summary>
        internal const bool RandomPosVerboseLog = true;

        // --- Count (SetupGroup postfix) ---
        /// <summary>
        /// Off by default: Config/gamestages.xml already scales WanderingHorde num.
        /// Enable only if you want a hard clamp on numToSpawn after SetupGroup.
        /// </summary>
        internal const bool SetupGroupPatchEnabled = false;

        internal const int WanderingHordeCountMin = 15;
        internal const int WanderingHordeCountMax = 30;

        /// <summary>
        /// Multiply xml-derived count before clamp (1 = clamp only).
        /// </summary>
        internal const float SetupGroupCountScale = 1f;

        internal const bool SetupGroupVerboseLog = true;
    }

    public static class DeadAirWanderingHordePatch
    {
        internal static string FormatMethod(MethodBase mb)
        {
            if (mb == null)
                return "(null)";

            var sb = new StringBuilder();
            sb.Append(mb.ReflectedType?.Name ?? mb.DeclaringType?.Name)
              .Append('.')
              .Append(mb.Name)
              .Append('(');

            var ps = mb.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");

                sb.Append(ps[i].ParameterType.Name)
                  .Append(' ')
                  .Append(ps[i].Name);
            }

            sb.Append(')');

            if (mb is MethodInfo mi && mi.ReturnType != typeof(void))
                sb.Append(" : ").Append(mi.ReturnType.Name);

            return sb.ToString();
        }
    }

    [HarmonyPatch(typeof(AIDirectorWanderingHordeComponent), nameof(AIDirectorWanderingHordeComponent.SetNextTime))]
    public static class DeadAirWanderingHordeSetNextTimePatch
    {
        private static readonly FieldInfo WorldWorldTimeField =
            typeof(World).GetField("worldTime", BindingFlags.Instance | BindingFlags.Public);

        private static MethodInfo _resolvedVanilla;

        public static bool Prepare()
        {
            if (!DeadAirWanderingHordeTunables.SetNextTimePatchEnabled)
                return false;

            _resolvedVanilla = AccessTools.DeclaredMethod(
                typeof(AIDirectorWanderingHordeComponent),
                nameof(AIDirectorWanderingHordeComponent.SetNextTime));

            if (_resolvedVanilla == null)
            {
                CompatLog.Warning("[DeadAir] WanderingHorde: SetNextTime not found; frequency patch disabled.");
                return false;
            }

            CompatLog.Out(
                "[DeadAir] WanderingHorde: SetNextTime patch ENABLED. Resolved vanilla signature: " +
                DeadAirWanderingHordePatch.FormatMethod(_resolvedVanilla));

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(AIDirectorWanderingHordeComponent __instance, SpawnType _spawnType)
        {
            if (__instance == null || _spawnType != SpawnType.Horde)
                return;

            if (DeadAirWanderingHordeTunables.HordeFrequencyMultiplier <= 1f)
                return;

            var world = GameManager.Instance?.World;
            if (world == null || WorldWorldTimeField == null)
            {
                if (DeadAirWanderingHordeTunables.SetNextTimeVerboseLog)
                    CompatLog.Warning("[DeadAir] WanderingHorde SetNextTime: skip (no world or worldTime field).");
                return;
            }

            try
            {
                ulong now = ToUInt64(WorldWorldTimeField.GetValue(world));

                // After vanilla SetNextTime body, this is the scheduled HordeNextTime.
                ulong vanillaHordeNext = __instance.HordeNextTime;

                if (vanillaHordeNext <= now)
                {
                    if (DeadAirWanderingHordeTunables.SetNextTimeVerboseLog)
                    {
                        CompatLog.Out(
                            $"[DeadAir] WanderingHorde SetNextTime: worldTime={now} vanillaHordeNext={vanillaHordeNext} " +
                            $"(not future; no compression). spawnType={_spawnType}");
                    }
                    return;
                }

                ulong span = vanillaHordeNext - now;
                ulong newSpan = (ulong)Math.Max(1d, span / DeadAirWanderingHordeTunables.HordeFrequencyMultiplier);
                ulong adjustedHordeNext = now + newSpan;

                __instance.HordeNextTime = adjustedHordeNext;

                if (DeadAirWanderingHordeTunables.SetNextTimeVerboseLog)
                {
                    CompatLog.Out(
                        $"[DeadAir] WanderingHorde SetNextTime: worldTime={now} vanillaHordeNext={vanillaHordeNext} " +
                        $"adjustedHordeNext={adjustedHordeNext} spanWas={span} spanNow={newSpan} " +
                        $"mult={DeadAirWanderingHordeTunables.HordeFrequencyMultiplier} spawnType={_spawnType}");
                }
            }
            catch (Exception ex)
            {
                CompatLog.Warning("[DeadAir] WanderingHorde SetNextTime postfix: " + ex);
            }
        }

        private static ulong ToUInt64(object value)
        {
            if (value == null)
                return 0UL;

            if (value is ulong u)
                return u;

            if (value is long l)
                return l < 0 ? 0UL : (ulong)l;

            return Convert.ToUInt64(value);
        }
    }

    [HarmonyPatch(typeof(AIDirectorGameStagePartySpawner), nameof(AIDirectorGameStagePartySpawner.SetupGroup))]
    public static class DeadAirWanderingHordeSetupGroupPatch
    {
        private static MethodInfo _resolvedVanilla;

        public static bool Prepare()
        {
            if (!DeadAirWanderingHordeTunables.SetupGroupPatchEnabled)
                return false;

            _resolvedVanilla = AccessTools.DeclaredMethod(
                typeof(AIDirectorGameStagePartySpawner),
                nameof(AIDirectorGameStagePartySpawner.SetupGroup));

            if (_resolvedVanilla == null)
            {
                CompatLog.Warning("[DeadAir] WanderingHorde: SetupGroup not found; count patch disabled.");
                return false;
            }

            CompatLog.Out(
                "[DeadAir] WanderingHorde: SetupGroup patch ENABLED. Resolved vanilla signature: " +
                DeadAirWanderingHordePatch.FormatMethod(_resolvedVanilla));

            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(AIDirectorGameStagePartySpawner __instance)
        {
            if (__instance == null)
                return;

            try
            {
                var def = __instance.def;
                if (def == null || !string.Equals(def.name, "WanderingHorde", StringComparison.Ordinal))
                    return;

                int before = __instance.numToSpawn;
                int scaled = Mathf.RoundToInt(before * DeadAirWanderingHordeTunables.SetupGroupCountScale);
                scaled = Mathf.Clamp(
                    scaled,
                    DeadAirWanderingHordeTunables.WanderingHordeCountMin,
                    DeadAirWanderingHordeTunables.WanderingHordeCountMax);

                if (scaled == before)
                {
                    if (DeadAirWanderingHordeTunables.SetupGroupVerboseLog)
                    {
                        CompatLog.Out(
                            $"[DeadAir] WanderingHorde SetupGroup: numToSpawn unchanged at {before} " +
                            "(def.name=WanderingHorde).");
                    }
                    return;
                }

                __instance.numToSpawn = scaled;

                if (DeadAirWanderingHordeTunables.SetupGroupVerboseLog)
                {
                    CompatLog.Out(
                        $"[DeadAir] WanderingHorde SetupGroup: numToSpawn {before} -> {scaled} " +
                        $"(clamp {DeadAirWanderingHordeTunables.WanderingHordeCountMin}-" +
                        $"{DeadAirWanderingHordeTunables.WanderingHordeCountMax}, " +
                        $"scale={DeadAirWanderingHordeTunables.SetupGroupCountScale}).");
                }
            }
            catch (Exception ex)
            {
                CompatLog.Warning("[DeadAir] WanderingHorde SetupGroup postfix: " + ex);
            }
        }
    }

    [HarmonyPatch]
    public static class DeadAirWanderingHordeRandomPosPatch
    {
        private static MethodInfo _resolved;

        public static bool Prepare()
        {
            if (!DeadAirWanderingHordeTunables.RandomPosPatchEnabled)
                return false;

            _resolved = ResolveRandomPos();
            if (_resolved == null)
            {
                CompatLog.Warning("[DeadAir] WanderingHorde: RandomPos could not be resolved (AIDirector vs AIDirectorWanderingHordeComponent); direction patch disabled.");
                return false;
            }

            CompatLog.Out(
                "[DeadAir] WanderingHorde: RandomPos patch ENABLED. Resolved vanilla signature: " +
                DeadAirWanderingHordePatch.FormatMethod(_resolved));

            return true;
        }

        public static MethodBase TargetMethod()
        {
            return _resolved;
        }

        [HarmonyPostfix]
        public static void Postfix(EntityPlayer target, ref Vector3 __result)
        {
            if (target == null || DeadAirWanderingHordeTunables.InvestigateBiasTowardPlayer <= 0f)
                return;

            try
            {
                Vector3 before = __result;
                Vector3 playerPos = target.position;

                __result = Vector3.Lerp(
                    before,
                    playerPos,
                    Mathf.Clamp01(DeadAirWanderingHordeTunables.InvestigateBiasTowardPlayer));

                if (!DeadAirWanderingHordeTunables.RandomPosVerboseLog)
                    return;

                bool moved = (before - __result).sqrMagnitude > 0.0001f;

                CompatLog.Out(
                    $"[DeadAir] WanderingHorde RandomPos: playerPos={playerPos} before={before} after={__result} " +
                    $"bias={DeadAirWanderingHordeTunables.InvestigateBiasTowardPlayer} moved={moved}");
            }
            catch (Exception ex)
            {
                CompatLog.Warning("[DeadAir] WanderingHorde RandomPos postfix: " + ex);
            }
        }

        private static MethodInfo ResolveRandomPos()
        {
            var t = typeof(AIWanderingHordeSpawner);

            foreach (var directorType in new[] { typeof(AIDirector), typeof(AIDirectorWanderingHordeComponent) })
            {
                var m = AccessTools.Method(
                    t,
                    nameof(AIWanderingHordeSpawner.RandomPos),
                    new[] { directorType, typeof(EntityPlayer), typeof(float) });

                if (m != null)
                    return m;
            }

            return null;
        }
    }
}