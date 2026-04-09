// using System;
// using System.Linq;
// using System.Reflection;
// using HarmonyLib;

// namespace DeadAir_7LongDarkDays.Patches
// {

//     public static class WaterCoolerJarPromptState
//     {
//         public static bool IsAimingAtCooler;
//         public static long LastSeenTicks;

//         public static void Update(bool isAimingAtCooler)
//         {
//             IsAimingAtCooler = isAimingAtCooler;
//             LastSeenTicks = DateTime.UtcNow.Ticks;
//         }

//         public static bool IsActive(int maxAgeMs = 250)
//         {
//             long age = DateTime.UtcNow.Ticks - LastSeenTicks;
//             return IsAimingAtCooler && age <= TimeSpan.FromMilliseconds(maxAgeMs).Ticks;
//         }
//     }

//     [HarmonyPatch]
//     public static class WaterCoolerJarPromptStatePatch
//     {
//         static MethodBase TargetMethod()
//         {
//             return typeof(ItemActionCollectWater)
//                 .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
//                 .FirstOrDefault(m => m.Name == "OnHoldingUpdate");
//         }

//         static void Postfix(object[] __args)
//         {
//             if (__args == null || __args.Length == 0 || !(__args[0] is ItemActionData actionData))
//             {
//                 WaterCoolerJarPromptState.Update(false);
//                 return;
//             }

//             WaterCoolerJarPromptState.Update(
//                 WaterCoolerActionHelpers.IsTargetingFullCooler(actionData));
//         }
//     }

//     [HarmonyPatch]
//     public static class WaterCoolerJarDescriptionPatch
//     {
//         static MethodBase TargetMethod()
//         {
//             return typeof(ItemAction)
//                 .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
//                 .FirstOrDefault(m =>
//                     m.Name == "GetDescription" &&
//                     m.ReturnType == typeof(string));
//         }

//         static void Postfix(ItemAction __instance, ref string __result)
//         {
//             if (!(__instance is ItemActionCollectWater))
//             {
//                 return;
//             }

//             if (!WaterCoolerJarPromptState.IsActive())
//             {
//                 return;
//             }

//             __result = "ldContextFillJarFromCooler";
//         }
//     }
// }