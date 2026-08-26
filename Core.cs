using ArcadeMachineComponent = Il2CppRAT.Arcade.ArcadeMachine;
using ArcadeMachineData = Il2CppRAT.Scriptables.Objects.ArcadeMachine;
using Il2CppRAT.Arcade;
using Il2CppRAT.Managers;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(ArcadeParadiseFreePlayMod.Core), "ArcadeParadiseFreePlayModv1.0.0", "1.0.0", "supermain", null)]
[assembly: MelonGame("Nosebleed Interactive", "Arcade Paradise")]

namespace ArcadeParadiseFreePlayMod
{
    public partial class Core : MelonMod
    {
        private const int FREE_PLAY_MACHINE_ID = 99002;
        private const string PREFS_KEY_PLACED = "freeplay_cabinet_placed";
        private const string PREFS_KEY_DELIVERY_X = "freeplay_delivery_pos_x";
        private const string PREFS_KEY_DELIVERY_Y = "freeplay_delivery_pos_y";
        private const string PREFS_KEY_DELIVERY_Z = "freeplay_delivery_pos_z";
        private const string PREFS_KEY_DELIVERY_ROT_Y = "freeplay_delivery_rot_y";
        private const string SAVE_KEY_STATE_VERSION = "apfreeplay_state_version";
        private const string SAVE_KEY_PLACED = "apfreeplay_cabinet_placed";
        private const string SAVE_KEY_POSITION_SAVED = "apfreeplay_position_saved";
        private const string SAVE_KEY_DELIVERY_SAVED = "apfreeplay_delivery_saved";
        private const string SAVE_KEY_POS_X = "apfreeplay_pos_x";
        private const string SAVE_KEY_POS_Y = "apfreeplay_pos_y";
        private const string SAVE_KEY_POS_Z = "apfreeplay_pos_z";
        private const string SAVE_KEY_ROT_Y = "apfreeplay_rot_y";
        private const string SAVE_KEY_DELIVERY_X = "apfreeplay_delivery_pos_x";
        private const string SAVE_KEY_DELIVERY_Y = "apfreeplay_delivery_pos_y";
        private const string SAVE_KEY_DELIVERY_Z = "apfreeplay_delivery_pos_z";
        private const string SAVE_KEY_DELIVERY_ROT_Y = "apfreeplay_delivery_rot_y";
        private const string SAVE_KEY_MACHINE_ORDER = "machine_99001";
       
        private const float FREE_PLAY_LOCATION_BONUS = 5f;
        private static Core _instance;
        private bool _spawned = false;
        private bool _shopInjected;
        private bool _saveStateReady;
        private bool _baseSceneLoaded;
        private static bool _freePlayPdaInputBlocked;
        private float _nextPersistenceCheck;
        private ArcadeGame _freePlayGame;
        private ArcadeMachineComponent _freePlayMachine;
        private ArcadeMachineData _persistedData;

        public override void OnInitializeMelon()
        {
            _instance = this;
            MelonLogger.Msg("Initialised.");

            var harmony = new HarmonyLib.Harmony("ArcadeParadiseFreePlayMod");
            harmony.PatchAll();
            MelonLogger.Msg("Harmony patches applied.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName != "BaseScene")
                return;

            _baseSceneLoaded = true;
            TryInjectFreePlayCabinet();
        }

        internal static void SetFreePlayPdaInputBlocked(bool blocked)
        {
            _freePlayPdaInputBlocked = blocked;
        }

        internal static bool IsFreePlayPdaInputBlocked => _freePlayPdaInputBlocked;

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (sceneName == "BaseScene")
            {
                _baseSceneLoaded = false;
                _freePlayPdaInputBlocked = false;
                _spawned = false;
                _shopInjected = false;
                _saveStateReady = false;
                _freePlayGame = null;
                _freePlayMachine = null;
                _persistedData = null;
            }
        }

        private void TryInjectFreePlayCabinet()
        {
            if (!_baseSceneLoaded)
                return;

            if (!_saveStateReady)
            {
                if (!TryInitialiseFreePlaySaveState())
                    return;
                _saveStateReady = true;
            }

            if (!_spawned)
            {
                if (ArcadeMachineManager.Instance == null)
                    return;

                MelonLogger.Msg("BaseScene ready: injecting FreePlay cabinet");
                SpawnFreePlayCabinet(activateCabinet: false);
            }
        }

        private void TryInjectFreePlayShop()
        {
            if (!_baseSceneLoaded || _persistedData == null)
                return;

            if (_shopInjected && IsFreePlayShopRegistered())
                return;

            if (UnityEngine.Object.FindObjectOfType<Il2CppRAT.UI.Computer.CUI_ArcadeMania>() == null)
                return;

            InjectFreePlayIntoShop(_persistedData);
        }

        private static bool IsFreePlayShopRegistered()
        {
            var arcadeMania = UnityEngine.Object.FindObjectOfType<Il2CppRAT.UI.Computer.CUI_ArcadeMania>();
            if (arcadeMania?.m_contentRoot == null)
                return false;

            Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem freePlayItem = null;
            for (int i = 0; i < arcadeMania.m_contentRoot.childCount; i++)
            {
                var item = arcadeMania.m_contentRoot.GetChild(i)
                    .GetComponent<Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem>();
                if (item?.m_ArcadeMachineData?.m_ID == FREE_PLAY_MACHINE_ID)
                {
                    freePlayItem = item;
                    break;
                }
            }

            if (freePlayItem == null || !freePlayItem.gameObject.activeSelf || freePlayItem.m_ComingSoon)
                return false;

            if (freePlayItem.m_Button == null)
                return false;

            if (!freePlayItem.m_Button.interactable)
                return false;

            bool inMachineItems = false;
            if (arcadeMania.m_arcadeMachineItems != null)
            {
                for (int i = 0; i < arcadeMania.m_arcadeMachineItems.Count; i++)
                {
                    if (arcadeMania.m_arcadeMachineItems[i] == freePlayItem)
                    {
                        inMachineItems = true;
                        break;        }
    }
}


            var scrollView = arcadeMania.m_ScrollView;
            bool inWebpageItems = false;
            if (scrollView?.m_WebpageItems != null)
            {
                for (int i = 0; i < scrollView.m_WebpageItems.Count; i++)
                {
                    if (scrollView.m_WebpageItems[i]?.TryCast<Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem>() == freePlayItem)
                    {
                        inWebpageItems = true;
                        break;        }
    }
}


            return inMachineItems && inWebpageItems;
        }

        public override void OnUpdate()
        {
            TryInjectFreePlayCabinet();

            if (Time.time >= _nextPersistenceCheck)
            {
                _nextPersistenceCheck = Time.time + 2f;

                try
                {
                    if (_saveStateReady && IsFreePlayPlaced())
                    {
                        var mgr = ArcadeMachineManager.Instance;

                        if (mgr?.m_arcadeMachineDictionaryByID != null
                            && !mgr.m_arcadeMachineDictionaryByID.ContainsKey(FREE_PLAY_MACHINE_ID))
                        {
                            MelonLogger.Msg("Cabinet missing from scene: respawning from save");
                            SpawnFreePlayCabinet();
                        }

                        // a placed cabinet must never be hidden as the stock game can deactivate it
                        // (e.g. the CancelLoadGame coroutine after a failed ROM load)
                        if (mgr?.m_arcadeMachineDictionaryByID != null
                            && mgr.m_arcadeMachineDictionaryByID.TryGetValue(FREE_PLAY_MACHINE_ID, out var hiddenMachine)
                            && hiddenMachine != null && hiddenMachine.gameObject != null
                            && !hiddenMachine.gameObject.activeSelf)
                        {
                            hiddenMachine.gameObject.SetActive(true);
                            MelonLogger.Msg("FreePlay cabinet was inactive: reactivated");
                        }

                        if (mgr?.m_arcadeMachineDictionaryByID != null
                            && mgr.m_arcadeMachineDictionaryByID.TryGetValue(FREE_PLAY_MACHINE_ID, out var mc)
                            && mc != null && mc.gameObject != null && mc.gameObject.activeSelf)
                        {
                            var t = mc.transform;
                            float px = t.position.x, py = t.position.y, pz = t.position.z;
                            bool hasSavedPosition = HasSavedFreePlayPosition();
                            bool atDeliveryPosition = IsAtFreePlayDeliveryPosition(t);
                            float savedX = GetSavedFloat(SAVE_KEY_POS_X, 0f);
                            float savedY = GetSavedFloat(SAVE_KEY_POS_Y, 0f);
                            float savedZ = GetSavedFloat(SAVE_KEY_POS_Z, 0f);
                            float savedRotY = GetSavedFloat(SAVE_KEY_ROT_Y, t.rotation.eulerAngles.y);

                            bool skipPositionSave = (!hasSavedPosition && atDeliveryPosition) ||
                                                     (hasSavedPosition &&
                                                      Mathf.Abs(px - savedX) <= 0.01f &&
                                                      Mathf.Abs(py - savedY) <= 0.01f &&
                                                      Mathf.Abs(pz - savedZ) <= 0.01f &&
                                                      Mathf.Abs(Mathf.DeltaAngle(t.rotation.eulerAngles.y, savedRotY)) <= 0.1f);

                            if (!skipPositionSave &&
                                (!hasSavedPosition ||
                                 Mathf.Abs(px - savedX) > 0.01f ||
                                 Mathf.Abs(py - savedY) > 0.01f ||
                                 Mathf.Abs(pz - savedZ) > 0.01f ||
                                 Mathf.Abs(Mathf.DeltaAngle(t.rotation.eulerAngles.y, savedRotY)) > 0.1f))
                            {
                                SaveFreePlayPosition(t);
                            }
                        }
                    }
                }
                catch { }
            }

            // pump the libretro core every frame while the cabinet is active
            if (_freePlayMachine != null && _freePlayMachine.gameObject != null &&
                _freePlayMachine.gameObject.activeInHierarchy)
            {
                if (_freePlayGame is EmulatorArcadeManager emu)
                {
                    emu.StartAttractMode();
                    emu.EmuUpdate();
                }
            }
            else if (_freePlayMachine == null)
            {
                _freePlayGame = null;
            }
        }
    }
}