using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir_7LongDarkDays.Patches
{
    public static class DeadAirWeaponBenchDebug
    {
        public const bool EnableLogs = true;

        public static void Log(string message)
        {
            if (EnableLogs)
            {
                CompatLog.Out($"[DeadAirWeaponBench] {message}");
            }
        }

        public static void Warn(string message)
        {
            CompatLog.Warning($"[DeadAirWeaponBench] {message}");
        }

        public static void Error(string message)
        {
            CompatLog.Error($"[DeadAirWeaponBench] {message}");
        }
        
    }

    public static class DeadAirWeaponBenchHelpers
    {
        public const string BenchBlockName = "cntDeadAirWeaponRepairBench";
        public const string BenchWindowGroupName = "workstation_cntDeadAirWeaponRepairBench";

        private static readonly HashSet<string> SupportedWeapons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Vanilla
            "gunHandgunT1Pistol",
            "gunHandgunT3SMG5",
            "gunHandgunT2Magnum44",
            "gunHandgunT3DesertVulture",
            "gunShotgunT1DoubleBarrel",
            "gunShotgunT2PumpShotgun",
            "gunShotgunT3AutoShotgun",
            "gunRifleT1HuntingRifle",
            "gunRifleT2LeverActionRifle",
            "gunRifleT3SniperRifle",
            "gunMGT1AK47",
            "gunMGT2TacticalAR",
            "gunMGT3M60",
            "gunExplosivesT3RocketLauncher",
            "gunBowT1WoodenBow",
            "gunBowT3CompoundBow",
            "gunBowT1IronCrossbow",
            "gunBowT3CompoundCrossbow",

            // IZY curated subset
            "IZYgunT0PistolLiberatorImprovisedPistol",
            "IZYgunT0SMGPIPEsubmachinegun",
            "IZYgunT1Pistol9Makarov",
            "IZYgunT145TACSMGMAC10",
            "IZYgunT2Pistol45M1911A1",
            "IZYgunT3Pistol45USP45",
            "IZYgunT445TACSMGKrissSuperV",
            "IZYgunT4SMGCZScorpionEVO3",

            "IZYgunT1SNIPERRIFLE762Mosinnagant",
            "IZYgunT3MarksManRifle762HKPSG1",
            "IZYgunT3MarksmanRifleMMRM1Carbine45",
            "IZYgunT4MarksmanRifle45MMRKrissVectorCarbine",

            "IZYgunT2ARSTG44N",
            "IZYgunT3BattleRifleHKG3",
            "IZYgunT3BULLPUPHellionVHS2",
            "IZYgunT4ARCabineHK416",

            "IZYgunT2TradshotgunLeveractionShotgunModel1887",
            "IZYgunT4TACshotgunM1014",

            "IZYgunT2LGLChinalakeGrenadeLauncher"
        };

        public static bool IsSupportedWeapon(string itemName)
        {
            return !string.IsNullOrEmpty(itemName) && SupportedWeapons.Contains(itemName);
        }

        /// <summary>Supported ranged weapon that is a firearm.</summary>
        public static bool IsFirearmSupportedWeapon(string itemName)
        {
            return IsSupportedWeapon(itemName);
        }

        public static string GetRepairPartName(string itemName)
        {
            switch (itemName)
            {
                // -------------------------
                // Handguns / SMGs / pistol-family IZY
                // -------------------------
                case "gunHandgunT1Pistol":
                case "gunHandgunT3SMG5":
                case "gunHandgunT2Magnum44":
                case "gunHandgunT3DesertVulture":
                case "IZYgunT0PistolLiberatorImprovisedPistol":
                case "IZYgunT1Pistol9Makarov":
                
                case "IZYgunT2Pistol45M1911A1":
                case "IZYgunT3Pistol45USP45":              
                    return "gunHandgunT1PistolParts";

                // -------------------------
                // Primitive / improvised IZY handgun-family
                // -------------------------
                
                case "IZYgunT0SMGPIPEsubmachinegun":
                    return "resourceMetalPipe";

                // -------------------------
                // Shotguns
                // -------------------------
                case "gunShotgunT1DoubleBarrel":
                case "gunShotgunT2PumpShotgun":
                case "gunShotgunT3AutoShotgun":

                case "IZYgunT2TradshotgunLeveractionShotgunModel1887":
                case "IZYgunT4TACshotgunM1014":
                    return "gunShotgunT1DoubleBarrelParts";

                // -------------------------
                // Rifles / marksman rifles
                // -------------------------
                case "gunRifleT1HuntingRifle":
                case "gunRifleT2LeverActionRifle":
                case "gunRifleT3SniperRifle":

                case "IZYgunT1SNIPERRIFLE762Mosinnagant":
                case "IZYgunT3MarksManRifle762HKPSG1":
                case "IZYgunT3MarksmanRifleMMRM1Carbine45":
                case "IZYgunT4MarksmanRifle45MMRKrissVectorCarbine":
                    return "gunRifleT1HuntingRifleParts";

                // -------------------------
                // AR / MG family
                // -------------------------
                case "gunMGT1AK47":
                case "gunMGT2TacticalAR":
                case "gunMGT3M60":
                case "IZYgunT145TACSMGMAC10":
                case "IZYgunT445TACSMGKrissSuperV":
                case "IZYgunT4SMGCZScorpionEVO3":

                case "IZYgunT2ARSTG44N":
                case "IZYgunT3BattleRifleHKG3":
                case "IZYgunT3BULLPUPHellionVHS2":
                case "IZYgunT4ARCabineHK416":
                    return "gunMGT1AK47Parts";

                // -------------------------
                // Explosive launcher family
                // -------------------------
                case "gunExplosivesT3RocketLauncher":
                case "IZYgunT2LGLChinalakeGrenadeLauncher":
                    return "gunExplosivesT3RocketLauncherParts";

                // -------------------------
                // Bows / crossbows
                // -------------------------
                case "gunBowT1WoodenBow":
                case "gunBowT3CompoundBow":
                case "gunBowT1IronCrossbow":
                case "gunBowT3CompoundCrossbow":
                    return "resourceForgedIron";

                default:
                    return null;
            }
        }

        public static int GetQualityScaledPartCount(ItemValue itemValue)
        {
            try
            {
                int quality = itemValue.Quality;
                if (quality < 1) quality = 1;
                if (quality > 6) quality = 6;
                return quality;
            }
            catch
            {
                return 1;
            }
        }

        public static bool IsWeaponRepairBench(XUiC_WorkstationWindowGroup group)
        {
            if (group == null)
            {
                return false;
            }

            try
            {
                if (!string.IsNullOrEmpty(group.Workstation) &&
                    string.Equals(group.Workstation, BenchBlockName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (group.WindowGroup != null &&
                    !string.IsNullOrEmpty(group.WindowGroup.ID) &&
                    string.Equals(group.WindowGroup.ID, BenchWindowGroupName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"IsWeaponRepairBench(XUiC_WorkstationWindowGroup) failed: {ex}");
            }

            return false;
        }

        public static bool IsWeaponRepairBench(XUiController controller)
        {
            if (controller == null)
            {
                return false;
            }

            try
            {
                if (controller is XUiC_WorkstationWindowGroup directGroup)
                {
                    return IsWeaponRepairBench(directGroup);
                }

                XUiController rootController = controller.windowGroup?.Controller;
                if (rootController is XUiC_WorkstationWindowGroup rootGroup)
                {
                    return IsWeaponRepairBench(rootGroup);
                }
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"IsWeaponRepairBench(XUiController) failed: {ex}");
            }

            return false;
        }

        public static bool IsWeaponBenchOpen(XUi xui)
        {
            try
            {
                if (xui == null)
                {
                    return false;
                }

                object benchWindowGroup = xui.FindWindowGroupByName(BenchWindowGroupName);
                if (benchWindowGroup == null)
                {
                    return false;
                }

                return Traverse.Create(benchWindowGroup).Property("IsOpen").GetValue<bool>();
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"IsWeaponBenchOpen failed: {ex.Message}");
                return false;
            }
        }

        public static void HideController(XUiController controller, bool makeDormant)
        {
            if (controller == null)
            {
                return;
            }

            try
            {
                if (controller.ViewComponent != null)
                {
                    controller.ViewComponent.IsVisible = false;
                }

                controller.IsDirty = false;

                if (makeDormant)
                {
                    controller.IsDormant = true;
                }

                if (controller.ViewComponent != null)
                {
                    var tr = Traverse.Create(controller.ViewComponent);

                    try
                    {
                        var go = tr.Property("gameObject").GetValue() as GameObject
                            ?? tr.Field("gameObject").GetValue() as GameObject;

                        if (go != null)
                        {
                            go.SetActive(false);
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    try
                    {
                        var tf = tr.Property("transform").GetValue() as Transform
                            ?? tr.Field("transform").GetValue() as Transform;

                        if (tf != null && tf.gameObject != null)
                        {
                            tf.gameObject.SetActive(false);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"HideController failed for {controller.GetType().FullName}: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(XUiC_RecipeList), "OnPressRecipe", new Type[] { typeof(XUiController), typeof(int) })]
    public static class DeadAirWeaponBench_BlockRecipeSelection
    {
        public static bool Prefix(XUiC_RecipeList __instance)
        {
            try
            {
                if (!DeadAirWeaponBenchHelpers.IsWeaponRepairBench(__instance))
                {
                    return true;
                }

                GameManager.ShowTooltip(__instance.xui.playerUI.entityPlayer, "This station does not use recipe crafting.");
                return false;
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"BlockRecipeSelection failed, allowing vanilla behavior: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(XUiC_CraftingWindowGroup), "AddItemToQueue", new Type[] { typeof(Recipe) })]
    public static class DeadAirWeaponBench_BlockAddItemToQueue_Recipe
    {
        public static bool Prefix(XUiC_CraftingWindowGroup __instance, ref bool __result)
        {
            try
            {
                if (!DeadAirWeaponBenchHelpers.IsWeaponRepairBench(__instance))
                {
                    return true;
                }

                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"BlockAddItemToQueue(Recipe) failed, allowing vanilla behavior: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(XUiC_CraftingWindowGroup), "AddItemToQueue", new Type[] { typeof(Recipe), typeof(int) })]
    public static class DeadAirWeaponBench_BlockAddItemToQueue_RecipeCount
    {
        public static bool Prefix(XUiC_CraftingWindowGroup __instance, ref bool __result)
        {
            try
            {
                if (!DeadAirWeaponBenchHelpers.IsWeaponRepairBench(__instance))
                {
                    return true;
                }

                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"BlockAddItemToQueue(Recipe,int) failed, allowing vanilla behavior: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(XUiC_CraftingWindowGroup), "AddItemToQueue", new Type[] { typeof(Recipe), typeof(int), typeof(float) })]
    public static class DeadAirWeaponBench_BlockAddItemToQueue_RecipeCountTime
    {
        public static bool Prefix(XUiC_CraftingWindowGroup __instance, ref bool __result)
        {
            try
            {
                if (!DeadAirWeaponBenchHelpers.IsWeaponRepairBench(__instance))
                {
                    return true;
                }

                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"BlockAddItemToQueue(Recipe,int,float) failed, allowing vanilla behavior: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(XUiC_EmptyInfoWindow), "OnOpen")]
    public static class DeadAirWeaponBench_HideEmptyInfo_OnOpen
    {
        public static void Postfix(XUiC_EmptyInfoWindow __instance)
        {
            try
            {
                if (__instance?.xui == null || !DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(__instance.xui))
                {
                    return;
                }

                DeadAirWeaponBenchHelpers.HideController(__instance, true);
                DeadAirWeaponBenchDebug.Log("Suppressed EmptyInfoWindow OnOpen while weapon bench is open.");
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"HideEmptyInfo_OnOpen failed: {ex}");
            }
        }
    }
    [HarmonyPatch(typeof(XUiC_ItemInfoWindow), "OnOpen")]

    [HarmonyPatch(typeof(XUiController), "OnOpen")]
    public static class DeadAirWeaponBench_HideItemInfoViaBaseOnOpen
    {
        public static void Postfix(XUiController __instance)
        {
            try
            {
                if (__instance == null || !(__instance is XUiC_ItemInfoWindow))
                {
                    return;
                }

                if (__instance.xui == null || !DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(__instance.xui))
                {
                    return;
                }

                DeadAirWeaponBenchHelpers.HideController(__instance, true);
                DeadAirWeaponBenchDebug.Log("Suppressed ItemInfoWindow via XUiController.OnOpen while weapon bench is open.");
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"HideItemInfoViaBaseOnOpen failed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(XUiController), "OnOpen")]
    public static class DeadAirWeaponBench_LogOpenedControllers_OnOpen
    {
        static readonly HashSet<string> LoggedThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Postfix(XUiController __instance)
        {
            try
            {
                if (__instance?.xui == null)
                {
                    return;
                }

                if (!DeadAirWeaponBenchHelpers.IsWeaponBenchOpen(__instance.xui))
                {
                    return;
                }

                var typeName = __instance.GetType().FullName ?? __instance.GetType().Name ?? "<unknown>";
                var controllerName = TryGetControllerName(__instance);
                var controllerId = TryGetControllerId(__instance);
                var groupId = __instance.windowGroup?.ID ?? "<null>";
                var groupType = __instance.windowGroup?.GetType().FullName ?? "<null>";

                // Keep this targeted: log only likely center/info/inventory-related windows,
                // plus anything with suspicious names that may be our inspect blocker.
                var combined = $"{typeName} | name={controllerName} | id={controllerId} | group={groupId}";
                if (!LooksRelevant(combined))
                {
                    return;
                }

                var key = $"{typeName}|{controllerName}|{controllerId}|{groupId}";
                if (!LoggedThisSession.Add(key))
                {
                    return;
                }

                DeadAirWeaponBenchDebug.Log(
                    $"OPEN controller: type={typeName}, name={controllerName}, id={controllerId}, groupId={groupId}, groupType={groupType}");
            }
            catch (Exception ex)
            {
                DeadAirWeaponBenchDebug.Warn($"LogOpenedControllers_OnOpen failed: {ex}");
            }
        }

        static bool LooksRelevant(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return text.IndexOf("info", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("inspect", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("craft", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("backpack", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("inventory", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string TryGetControllerName(XUiController controller)
        {
            try
            {
                var tr = Traverse.Create(controller);

                foreach (var member in new[] { "WindowName", "windowName", "Name", "name" })
                {
                    try
                    {
                        var p = tr.Property(member);
                        if (p.PropertyExists())
                        {
                            var v = p.GetValue() as string;
                            if (!string.IsNullOrEmpty(v))
                            {
                                return v;
                            }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        var f = tr.Field(member);
                        if (f.FieldExists())
                        {
                            var v = f.GetValue() as string;
                            if (!string.IsNullOrEmpty(v))
                            {
                                return v;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return "<null>";
        }

        static string TryGetControllerId(XUiController controller)
        {
            try
            {
                var tr = Traverse.Create(controller);

                foreach (var member in new[] { "ID", "id", "WindowId", "windowId", "controlName", "ControlName" })
                {
                    try
                    {
                        var p = tr.Property(member);
                        if (p.PropertyExists())
                        {
                            var v = p.GetValue() as string;
                            if (!string.IsNullOrEmpty(v))
                            {
                                return v;
                            }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        var f = tr.Field(member);
                        if (f.FieldExists())
                        {
                            var v = f.GetValue() as string;
                            if (!string.IsNullOrEmpty(v))
                            {
                                return v;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return "<null>";
        }
    }
}