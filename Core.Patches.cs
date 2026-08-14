using ArcadeMachineComponent = Il2CppRAT.Arcade.ArcadeMachine;
using ArcadeMachineData = Il2CppRAT.Scriptables.Objects.ArcadeMachine;
using HarmonyLib;
using Il2CppRAT.Managers;
using Il2CppRAT.UI.Menu;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

namespace ArcadeParadiseFreePlayMod
{
    public partial class Core
    {
        [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.HandleMenuToggle))]
        private static class BlockFreePlayMenuTogglePatch
        {
            private static bool Prefix()
            {
                return !IsFreePlayPdaInputBlocked;
            }
        }

        [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.ToggleMenus))]
        private static class BlockFreePlayToggleMenusPatch
        {
            private static bool Prefix()
            {
                return !IsFreePlayPdaInputBlocked;
            }
        }

        [HarmonyPatch(typeof(Il2CppRAT.UI.Computer.CUI_ArcadeMania), "Open")]
        private static class ArcadeManiaOpenPatch
        {
            private static void Postfix() { _instance?.TryInjectFreePlayShop(); }
        }

        [HarmonyPatch(typeof(Il2CppRAT.UI.Computer.CUI_ArcadeMania), "UpdateArcadeMachineItems")]
        private static class ArcadeManiaRefreshPatch
        {
            private static void Postfix() { _instance?.TryInjectFreePlayShop(); }
        }

        [HarmonyPatch(typeof(Il2CppRAT.UI.Computer.CUI_ArcadeMachineConfirm), "ConfirmOrder")]
        private static class ConfirmOrderPatch
        {
            private static int _lastFrame;

            private static bool Prefix(Il2CppRAT.UI.Computer.CUI_ArcadeMachineConfirm __instance)
            {
                var data = __instance.m_ArcadeMachineData;
                if (data == null || data.m_ID != FREE_PLAY_MACHINE_ID)
                    return true;

                if (_lastFrame == Time.frameCount) return false;
                _lastFrame = Time.frameCount;

                float price = data.m_Price;
                int days = __instance.m_DeliveryDays;

                MelonLogger.Msg($"[ConfirmOrder] Processing FreePlay purchase: price={price}, days={days}");

                try { Il2CppRAT.Managers.CurrencyManager.SpendCash(price, (Il2CppRAT.Managers.CurrencyManager.CashSpendings)1); }
                catch (Exception ex) { MelonLogger.Error($"[ConfirmOrder] SpendCash failed: {ex.Message}"); return false; }

                try { data.m_OnDelivery = true; }
                catch (Exception ex) { MelonLogger.Warning($"[ConfirmOrder] m_OnDelivery set failed: {ex.Message}"); }

                try { SaveDataManager.SetInt($"machine_{FREE_PLAY_MACHINE_ID}", days); }
                catch (Exception ex) { MelonLogger.Warning($"[ConfirmOrder] SaveDataManager.SetInt failed: {ex.Message}"); }

                try
                {
                    var deliveryDate = new Il2CppSystem.ValueTuple<int, int>(days, FREE_PLAY_MACHINE_ID);
                    DeliveryManager.AddDelivery(deliveryDate);
                    MelonLogger.Msg($"[ConfirmOrder] Scheduled delivery: day={days}, machineID={FREE_PLAY_MACHINE_ID}");
                }
                catch (Exception ex) { MelonLogger.Warning($"[ConfirmOrder] AddDelivery failed: {ex.Message}"); }

                try { __instance.m_ParentWindow?.TryCast<Il2CppRAT.UI.Computer.CUI_ArcadeMachineDescription>()?.CheckMachine(); }
                catch (Exception ex) { MelonLogger.Warning($"[ConfirmOrder] CheckMachine failed: {ex.Message}"); }

                var status = __instance.m_PurchaseStatus;
                if (status != null) status._IsSuccess_k__BackingField = true;

                try { Il2CppRAT.Managers.ComputerManager.CloseCurrentWindow(); }
                catch (Exception ex) { MelonLogger.Warning($"[ConfirmOrder] CloseCurrentWindow failed: {ex.Message}"); }

                MelonLogger.Msg("[ConfirmOrder] FreePlay cabinet purchased! Delivery box arrives tomorrow.");
                return false;
            }
        }

        [HarmonyPatch(typeof(Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem), "UpdateItemDetails")]
        private static class UpdateItemDetailsPatch
        {
            private static void Postfix(Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem __instance)
            {
                if (__instance.m_ArcadeMachineData?.m_ID != FREE_PLAY_MACHINE_ID) return;
                try { if (__instance.m_Title != null) __instance.m_Title.text = "Free Play"; }
                catch (Exception ex) { MelonLogger.Warning($"[Harmony] Title fallback failed: {ex.Message}"); }
                try { if (__instance.m_Description != null) __instance.m_Description.text = "Load your own arcade ROMs and play classic coin-op games!"; }
                catch (Exception ex) { MelonLogger.Warning($"[Harmony] Description fallback failed: {ex.Message}"); }
            }
        }

        [HarmonyPatch(typeof(SaveDataManager), "Save")]
        private static class SavePatch
        {
            private static void Prefix()
            {
                try
                {
                    var mgr = ArcadeMachineManager.Instance;
                    if (mgr?.m_arcadeMachineDictionaryByID != null &&
                        mgr.m_arcadeMachineDictionaryByID.TryGetValue(FREE_PLAY_MACHINE_ID, out var machine) &&
                        machine != null && machine.gameObject != null && machine.gameObject.activeSelf &&
                        IsFreePlayPlaced() &&
                        (HasSavedFreePlayPosition() || !IsAtFreePlayDeliveryPosition(machine.transform)))
                    {
                        SaveFreePlayPosition(machine.transform);
                    }
                }
                catch (Exception ex) { MelonLogger.Warning($"[Save] FreePlay position capture failed: {ex.Message}"); }
            }
        }

        [HarmonyPatch(typeof(ArcadeMachineManager), "UnlockMachine")]
        private static class UnlockMachinePatch
        {
            private static void Postfix(int id)
            {
                if (id != FREE_PLAY_MACHINE_ID) return;

                MelonLogger.Msg("[Harmony] FreePlay cabinet activated via delivery box");

                try
                {
                    var mgr = ArcadeMachineManager.Instance;
                    if (mgr?.m_arcadeMachineDictionaryByID != null &&
                        mgr.m_arcadeMachineDictionaryByID.TryGetValue(FREE_PLAY_MACHINE_ID, out var machine) &&
                        machine != null && machine.transform != null)
                    {
                        var t = machine.transform;
                        SetSavedFloat(SAVE_KEY_DELIVERY_X, t.position.x);
                        SetSavedFloat(SAVE_KEY_DELIVERY_Y, t.position.y);
                        SetSavedFloat(SAVE_KEY_DELIVERY_Z, t.position.z);
                        SetSavedFloat(SAVE_KEY_DELIVERY_ROT_Y, t.rotation.eulerAngles.y);
                        SaveDataManager.SetInt(SAVE_KEY_DELIVERY_SAVED, 1);
                    }
                }
                catch (Exception ex) { MelonLogger.Warning($"[Harmony] Delivery position capture failed: {ex.Message}"); }

                SaveDataManager.SetInt(SAVE_KEY_PLACED, 1);
                SaveDataManager.SetInt(SAVE_KEY_STATE_VERSION, 1);
            }
        }

        [HarmonyPatch(typeof(Il2CppRAT.UI.Computer.CUI_ArcadeMachineDescription), "LoadDescriptionData")]
        private static class LoadDescriptionDataPatch
        {
            private static void Postfix(Il2CppRAT.UI.Computer.CUI_ArcadeMachineDescription __instance)
            {
                if (__instance.m_ArcadeMachineData?.m_ID != FREE_PLAY_MACHINE_ID) return;
                try { if (__instance.m_Title != null) __instance.m_Title.text = "Free Play"; }
                catch (Exception ex) { MelonLogger.Warning($"[Harmony] Desc title override failed: {ex.Message}"); }
                try { if (__instance.m_Description != null) __instance.m_Description.text = "Load your own arcade ROMs and play classic coin-op games!"; }
                catch (Exception ex) { MelonLogger.Warning($"[Harmony] Desc description override failed: {ex.Message}"); }
            }
        }

        [HarmonyPatch(typeof(ArcadeMachineData), "get_IsBroken")]
        private static class IsBrokenPatch
        {
            private static void Postfix(ArcadeMachineData __instance, ref bool __result)
            {
                if (__instance.m_ID == FREE_PLAY_MACHINE_ID) __result = false;
            }
        }

        [HarmonyPatch(typeof(ArcadeMachineData), "CalcPurePopularity")]
        private static class LocationBonusPatch
        {
            private static void Postfix(ArcadeMachineData __instance, ref float __result)
            {
                if (__instance.m_ID == FREE_PLAY_MACHINE_ID)
                    __result += FREE_PLAY_LOCATION_BONUS;
            }
        }

        [HarmonyPatch(typeof(MUI_MachineItem), "SetUpTexts")]
        private static class PDAMachineItemPatch
        {
            private static void Postfix(MUI_MachineItem __instance)
            {
                var data = __instance.m_Machine?.m_ArcadeMachineDatafile;
                if (data == null || data.m_ID != FREE_PLAY_MACHINE_ID) return;
                try
                {
                    var nameTmp = __instance.m_NameText?.GetComponent<TextMeshProUGUI>();
                    if (nameTmp != null) nameTmp.text = "Free Play";
                }
                catch (Exception ex) { MelonLogger.Warning($"[PDA] Name override failed: {ex.Message}"); }
            }
        }

        [HarmonyPatch(typeof(ArcadeMachineComponent), nameof(ArcadeMachineComponent.OnInteract))]
        private static class OnInteractPatch
        {
            private static int _lastFrame;
            private static ArcadeMachineComponent _lastMachine;

            private static void Prefix(ArcadeMachineComponent __instance)
            {
                if (__instance.m_ArcadeMachineDatafile?.m_ID != FREE_PLAY_MACHINE_ID) return;

                SetFreePlayPdaInputBlocked(true);
                if (__instance.m_Game is EmulatorArcadeManager emu)
                    emu.ReApplyScreenMaterial();
            }

            private static void Postfix(ArcadeMachineComponent __instance)
            {
                if (__instance.m_ArcadeMachineDatafile?.m_ID != FREE_PLAY_MACHINE_ID) return;

                if (__instance.m_Game is EmulatorArcadeManager screenGame)
                    screenGame.ReApplyScreenMaterial();

                if (_lastMachine == __instance && _lastFrame == Time.frameCount) return;
                _lastMachine = __instance;
                _lastFrame = Time.frameCount;

                var game = __instance.m_Game;

                if (game != null && game.IsIdle)
                {
                    MelonLogger.Msg("[Harmony] Forcing StartGame on FreePlay cabinet");
                    game.StartGame();
                }

                if (game is EmulatorArcadeManager emu)
                {
                    emu.OnPlayerInteract();
                    // stock interaction path can restore the templates Graffiti Ballz screen material so must replace it before render
                    emu.ReApplyScreenMaterial();
                }

                NeutraliseBorrowedGlassOverlay(__instance.gameObject);

                try
                {
                    GamesManager.m_ActiveGame = game;
                    GamesManager.m_ActiveMachine = __instance;
                }
                catch (Exception ex) { MelonLogger.Warning($"[Harmony] GamesManager registration failed: {ex.Message}"); }
            }
        }

        [HarmonyPatch(typeof(ArcadeMachineComponent), nameof(ArcadeMachineComponent.OnDisconnect))]
        private static class OnDisconnectPatch
        {
            private static void Postfix(ArcadeMachineComponent __instance)
            {
                if (__instance.m_ArcadeMachineDatafile?.m_ID != FREE_PLAY_MACHINE_ID) return;
                SetFreePlayPdaInputBlocked(false);
                __instance.CancelInvoke("SwapToAttractMode");
                MelonLogger.Msg("[Harmony] OnDisconnect completed: cancelled attract screen swap");
            }

            private static Exception Finalizer(ArcadeMachineComponent __instance, Exception __exception)
            {
                if (__instance.m_ArcadeMachineDatafile?.m_ID == FREE_PLAY_MACHINE_ID)
                    return null;
                return __exception;
            }
        }
    }
}
