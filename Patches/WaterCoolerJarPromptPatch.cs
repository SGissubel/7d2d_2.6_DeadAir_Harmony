using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;


namespace DeadAir_7LongDarkDays.Patches
{

    [HarmonyPatch]
    public static class WaterCoolerJarPromptPatch
    {
        static MethodBase TargetMethod()
        {
            return typeof(ItemAction)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                    m.Name == "CanInteract" &&
                    m.ReturnType == typeof(string));
        }

        static void Postfix(ItemAction __instance, object[] __args, ref string __result)
        {
            if (!(__instance is ItemActionCollectWater))
            {
                return;
            }

            if (__args == null || __args.Length == 0)
            {
                return;
            }

            if (!(__args[0] is ItemActionData actionData))
            {
                return;
            }

            if (!WaterCoolerActionHelpers.IsTargetingFullCooler(actionData))
            {
                return;
            }

            __result = Localization.Get("ldContextFillJarFromCooler");
        }
    }
}
