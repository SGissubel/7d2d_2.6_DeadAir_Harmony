using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch]
    public static class WaterCoolerDrinkPromptPatch
    {
        static MethodBase TargetMethod()
        {
            return typeof(ItemActionEat)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                    m.Name == "IsValidConditions" &&
                    m.ReturnType == typeof(bool));
        }

        static void Postfix(object[] __args, ref bool __result)
        {
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

            __result = true;
            WaterCoolerActionHelpers.RememberDrinkCoolerTarget(actionData);
        }
    }
}