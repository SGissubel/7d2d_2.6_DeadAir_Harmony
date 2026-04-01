using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    public static class DeadAirWeaponRepairState
    {
        public const string BenchBlockName = "cntDeadAirWeaponRepairBench";
        public const string RepairKitName = "resourceWeaponRepairKit";

        private static float _lastRepairClickTime = -999f;

        public static bool TryBeginRepairClick(float minGapSeconds = 0.15f)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastRepairClickTime < minGapSeconds)
            {
                return false;
            }

            _lastRepairClickTime = now;
            return true;
        }
    }
}