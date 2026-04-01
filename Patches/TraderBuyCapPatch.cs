using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Audio;

namespace DeadAir_7LongDarkDays.Patches
{
    [HarmonyPatch(typeof(ItemActionEntrySell), nameof(ItemActionEntrySell.OnActivated))]
    public static class TraderBuyCapPatch
    {
        private const int BUY_CAP_PER_CYCLE = 5000;

        // Store per-trader cycle state
        private class TraderCycleState
        {
            public ulong LastSeenNextResetTime;
            public int PaidOutThisCycle;
        }

        private static readonly Dictionary<string, TraderCycleState> StateByTrader = new();

        static bool Prefix(ItemActionEntrySell __instance)
        {
            try
            {
                // Skip vending-machine-owner path (stocking/collecting)
                bool isOwner = (bool)AccessTools.Field(typeof(ItemActionEntrySell), "isOwner").GetValue(__instance);
                if (isOwner) return true;

                var itemController = AccessTools.Property(typeof(BaseItemActionEntry), "ItemController")
                    ?.GetValue(__instance) as XUiController;
                if (itemController is not XUiC_ItemStack itemStackController) return true;

                var xui = itemController.xui;
                if (xui?.Trader?.Trader == null) return true;

                int countToSell = itemStackController.InfoWindow.BuySellCounter.Count;
                if (countToSell <= 0) return true;

                ItemStack stack = itemStackController.ItemStack;
                if (stack == null || stack.IsEmpty()) return true;

                ItemValue itemValue = stack.itemValue;
                ItemClass itemClass = ItemClass.GetForId(itemValue.type);
                if (itemClass == null) return true;

                int sellPrice = XUiM_Trader.GetSellPrice(xui, itemValue, countToSell, itemClass);
                if (sellPrice <= 0) return true;

                // Identify trader + get its current cycle marker
                string traderKey = GetTraderKey(xui);
                ulong nextResetTime = xui.Trader.Trader.NextResetTime; // <-- KEY IDEA

                // Get/Create state
                if (!StateByTrader.TryGetValue(traderKey, out var state))
                {
                    state = new TraderCycleState
                    {
                        LastSeenNextResetTime = nextResetTime,
                        PaidOutThisCycle = 0
                    };
                    StateByTrader[traderKey] = state;
                }

                // AUTO RESET when trader restocks (NextResetTime changes)
                if (state.LastSeenNextResetTime != nextResetTime)
                {
                    state.LastSeenNextResetTime = nextResetTime;
                    state.PaidOutThisCycle = 0;
                }

                // Enforce cap
                if (state.PaidOutThisCycle + sellPrice > BUY_CAP_PER_CYCLE)
                {
                    EntityPlayerLocal player = xui.playerUI.entityPlayer;
                    int remaining = Math.Max(0, BUY_CAP_PER_CYCLE - state.PaidOutThisCycle);

                    GameManager.ShowTooltip(player,
                        $"Trader buy limit reached.\nRemaining this restock: {remaining} dukes.");

                    Manager.PlayInsidePlayerHead("ui_denied");
                    return false;
                }

                // Optional: don’t consume budget if the sale will fail due to space
                ItemValue currency = ItemClass.GetItem(TraderInfo.CurrencyItem);
                ItemStack dukesStack = new ItemStack(currency.Clone(), sellPrice);
                var playerInv = xui.PlayerInventory;

                int slotCount = itemStackController.xui.PlayerInventory.Backpack.SlotCount;
                int slotNumber = itemStackController.SlotNumber
                                 + ((itemStackController.StackLocation == XUiC_ItemStack.StackLocationTypes.ToolBelt) ? slotCount : 0);

                if (!playerInv.CanSwapItems(stack.Clone(), dukesStack, slotNumber))
                    return true;

                // Consume budget
                state.PaidOutThisCycle += sellPrice;
                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log($"[YourMod] TraderBuyCapPatch error: {e}");
                return true;
            }
        }

        private static string GetTraderKey(XUi xui)
        {
            if (xui.Trader.TraderEntity != null)
                return $"TraderEntity:{xui.Trader.TraderEntity.EntityName}";

            if (xui.Trader.TraderTileEntity != null)
                return $"TraderTile:{xui.Trader.TraderTileEntity}";

            return "Trader:Unknown";
        }
    }
}
