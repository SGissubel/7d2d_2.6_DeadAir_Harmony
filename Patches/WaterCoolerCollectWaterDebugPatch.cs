// using System.Linq;
// using System.Reflection;
// using HarmonyLib;

// namespace DeadAir_7LongDarkDays.Patches
// {
//     [HarmonyPatch]
//     public static class WaterCoolerCollectWaterDebugPatch
//     {
//         static MethodBase TargetMethod()
//         {
//             return typeof(ItemActionCollectWater)
//                 .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
//                 .FirstOrDefault(m => m.Name == "ExecuteAction");
//         }
//     }
// }
