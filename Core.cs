using ArcadeMachineComponent = Il2CppRAT.Arcade.ArcadeMachine;
using ArcadeMachineData = Il2CppRAT.Scriptables.Objects.ArcadeMachine;
using HarmonyLib;
using Il2CppRAT.Arcade;
using Il2CppRAT.Managers;
using Il2CppRAT.UI.Menu;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(ArcadeParadiseFreePlayMod.Core), "ArcadeParadiseFreePlayModv1.0.0", "1.0.0", "supermain", null)]
[assembly: MelonGame("Nosebleed Interactive", "Arcade Paradise")]

namespace ArcadeParadiseFreePlayMod
{
    public class Core : MelonMod
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

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (sceneName == "BaseScene")
            {
                _baseSceneLoaded = false;
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

            TryInjectFreePlayShop();
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

            bool shouldBeInteractable = !IsFreePlayPlaced() && !freePlayItem.m_ArcadeMachineData.m_OnDelivery;
            if (freePlayItem.m_Button.interactable != shouldBeInteractable)
                return false;

            bool inMachineItems = false;
            if (arcadeMania.m_arcadeMachineItems != null)
            {
                for (int i = 0; i < arcadeMania.m_arcadeMachineItems.Count; i++)
                {
                    if (arcadeMania.m_arcadeMachineItems[i] == freePlayItem)
                    {
                        inMachineItems = true;
                        break;
                    }
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
                        break;
                    }
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

        private static bool TryInitialiseFreePlaySaveState()
        {
            try
            {
                if (SaveDataManager.Instance == null)
                    return false;

                if (SaveDataManager.GetInt(SAVE_KEY_STATE_VERSION) != 1)
                {
                    bool migrateLegacyState = PlayerPrefs.HasKey(PREFS_KEY_PLACED) &&
                                               SaveDataManager.GetInt(SAVE_KEY_MACHINE_ORDER) > 0 &&
                                               HasLegacyFreePlayPosition();
                    SaveDataManager.SetInt(SAVE_KEY_PLACED, migrateLegacyState ? 1 : 0);
                    SaveDataManager.SetInt(SAVE_KEY_STATE_VERSION, 1);

                    if (migrateLegacyState)
                    {
                        MigrateLegacyFreePlayFloat(PREFS_KEY_DELIVERY_X, SAVE_KEY_DELIVERY_X);
                        MigrateLegacyFreePlayFloat(PREFS_KEY_DELIVERY_Y, SAVE_KEY_DELIVERY_Y);
                        MigrateLegacyFreePlayFloat(PREFS_KEY_DELIVERY_Z, SAVE_KEY_DELIVERY_Z);
                        MigrateLegacyFreePlayFloat(PREFS_KEY_DELIVERY_ROT_Y, SAVE_KEY_DELIVERY_ROT_Y);
                        MigrateLegacyFreePlayFloat("freeplay_pos_x", SAVE_KEY_POS_X);
                        MigrateLegacyFreePlayFloat("freeplay_pos_y", SAVE_KEY_POS_Y);
                        MigrateLegacyFreePlayFloat("freeplay_pos_z", SAVE_KEY_POS_Z);
                        MigrateLegacyFreePlayFloat("freeplay_rot_y", SAVE_KEY_ROT_Y);
                        if (HasLegacyFreePlayPosition())
                            SaveDataManager.SetInt(SAVE_KEY_POSITION_SAVED, 1);
                        if (HasLegacyFreePlayDeliveryPosition())
                            SaveDataManager.SetInt(SAVE_KEY_DELIVERY_SAVED, 1);
                        MelonLogger.Msg("Migrated legacy FreePlay state into the current save");
                    }

                    DeleteLegacyFreePlayPrefs();
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"FreePlay save state is not ready yet: {ex.Message}");
                return false;
            }
        }

        private static bool IsFreePlayPlaced()
        {
            return SaveDataManager.GetInt(SAVE_KEY_PLACED) == 1;
        }

        private static bool HasSavedFreePlayPosition()
        {
            return SaveDataManager.GetInt(SAVE_KEY_POSITION_SAVED) == 1;
        }

        private static bool HasLegacyFreePlayPosition()
        {
            return PlayerPrefs.HasKey("freeplay_pos_x") &&
                   PlayerPrefs.HasKey("freeplay_pos_y") &&
                   PlayerPrefs.HasKey("freeplay_pos_z") &&
                   PlayerPrefs.HasKey("freeplay_rot_y");
        }

        private static bool HasLegacyFreePlayDeliveryPosition()
        {
            return PlayerPrefs.HasKey(PREFS_KEY_DELIVERY_X) &&
                   PlayerPrefs.HasKey(PREFS_KEY_DELIVERY_Y) &&
                   PlayerPrefs.HasKey(PREFS_KEY_DELIVERY_Z) &&
                   PlayerPrefs.HasKey(PREFS_KEY_DELIVERY_ROT_Y);
        }

        private static bool IsAtFreePlayDeliveryPosition(Transform transform)
        {
            if (SaveDataManager.GetInt(SAVE_KEY_DELIVERY_SAVED) != 1)
                return false;

            return Mathf.Abs(transform.position.x - GetSavedFloat(SAVE_KEY_DELIVERY_X, 0f)) <= 0.01f &&
                   Mathf.Abs(transform.position.y - GetSavedFloat(SAVE_KEY_DELIVERY_Y, 0f)) <= 0.01f &&
                   Mathf.Abs(transform.position.z - GetSavedFloat(SAVE_KEY_DELIVERY_Z, 0f)) <= 0.01f &&
                   Mathf.Abs(Mathf.DeltaAngle(transform.rotation.eulerAngles.y,
                                              GetSavedFloat(SAVE_KEY_DELIVERY_ROT_Y, 0f))) <= 0.1f;
        }

        private static float GetSavedFloat(string key, float fallback)
        {
            try
            {
                if (SaveDataManager.GetInt(SAVE_KEY_POSITION_SAVED) != 1 &&
                    SaveDataManager.GetInt(SAVE_KEY_DELIVERY_SAVED) != 1)
                    return fallback;

                return BitConverter.Int32BitsToSingle(SaveDataManager.GetInt(key));
            }
            catch { return fallback; }
        }

        private static void SetSavedFloat(string key, float value)
        {
            SaveDataManager.SetInt(key, BitConverter.SingleToInt32Bits(value));
        }

        private static void SaveFreePlayPosition(Transform transform)
        {
            SetSavedFloat(SAVE_KEY_POS_X, transform.position.x);
            SetSavedFloat(SAVE_KEY_POS_Y, transform.position.y);
            SetSavedFloat(SAVE_KEY_POS_Z, transform.position.z);
            SetSavedFloat(SAVE_KEY_ROT_Y, transform.rotation.eulerAngles.y);
            SaveDataManager.SetInt(SAVE_KEY_POSITION_SAVED, 1);
        }

        private static void MigrateLegacyFreePlayFloat(string oldKey, string newKey)
        {
            if (PlayerPrefs.HasKey(oldKey))
                SetSavedFloat(newKey, PlayerPrefs.GetFloat(oldKey));
        }

        private static void DeleteLegacyFreePlayPrefs()
        {
            PlayerPrefs.DeleteKey(PREFS_KEY_PLACED);
            PlayerPrefs.DeleteKey("freeplay_pos_x");
            PlayerPrefs.DeleteKey("freeplay_pos_y");
            PlayerPrefs.DeleteKey("freeplay_pos_z");
            PlayerPrefs.DeleteKey("freeplay_rot_y");
            PlayerPrefs.DeleteKey(PREFS_KEY_DELIVERY_X);
            PlayerPrefs.DeleteKey(PREFS_KEY_DELIVERY_Y);
            PlayerPrefs.DeleteKey(PREFS_KEY_DELIVERY_Z);
            PlayerPrefs.DeleteKey(PREFS_KEY_DELIVERY_ROT_Y);
            PlayerPrefs.Save();
        }

        private static void ApplyFreePlayTexture(GameObject root)
        {
            string freePlayDir = Path.Combine(
                Path.GetDirectoryName(typeof(Core).Assembly.Location) ?? ".",
                "FreePlay");
            string pngPath = Path.Combine(freePlayDir, "cabinet.png");

            if (!File.Exists(pngPath))
            {
                MelonLogger.Msg($"[Core] No cabinet.png at {pngPath}: cabinet keeps GraffitiBallz textures");
                return;
            }

            var tex = new Texture2D(2, 2);
            UnityEngine.ImageConversion.LoadImage(tex, File.ReadAllBytes(pngPath));
            MelonLogger.Msg($"[Core] Loaded cabinet texture: {tex.width}x{tex.height}");

            Transform body = root.transform.Find("ArcadeMachine_GraffitiBallz");
            if (body == null)
            {
                foreach (Transform child in root.transform)
                {
                    if (child.GetComponent<MeshRenderer>() != null) { body = child; break; }
                }
                if (body == null)
                {
                    MelonLogger.Warning("[Core] No suitable child with MeshRenderer found: texture not swapped");
                    return;
                }
            }

            var mr = body.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null)
            {
                MelonLogger.Warning("[Core] MeshRenderer or sharedMaterial missing on body: texture not swapped");
                return;
            }

            var freePlayMat = new Material(mr.sharedMaterial.shader);
            freePlayMat.name = "FreePlayCabinetMat";
            freePlayMat.CopyPropertiesFromMaterial(mr.sharedMaterial);
            freePlayMat.mainTexture = tex;
            mr.material = freePlayMat;

            MelonLogger.Msg("[Core] Swapped texture on main cabinet body");
        }

        private void SpawnFreePlayCabinet(bool activateCabinet = true)
        {
            MelonLogger.Msg("Attempting to spawn FreePlay cabinet");

            var manager = ArcadeMachineManager.Instance;
            if (manager == null || manager.m_ArcadeMachines == null || manager.m_ArcadeMachines.Count == 0)
            {
                MelonLogger.Msg("Manager or machine list was null/empty.");
                return;
            }

            ArcadeMachineComponent template = null;
            foreach (var m in manager.m_ArcadeMachines)
            {
                if (m != null && m.name == "ArcadeMachine_GraffitiBallz")
                {
                    template = m;
                    break;
                }
            }
            if (template == null)
            {
                MelonLogger.Msg("GraffitiBallz not found, falling back to first machine");
                template = manager.m_ArcadeMachines[0];
            }
            MelonLogger.Msg($"Using template: {template.name}, Data={template.Data?.name}");

            var originalData = template.Data;
            if (originalData == null)
            {
                MelonLogger.Msg("Aborting: Template has no Data (ScriptableObject).");
                return;
            }

            ArcadeMachineData clonedData = null;
            if (_persistedData != null)
            {
                try { var _ = _persistedData.m_ID; }
                catch { _persistedData = null; }
            }
            if (_persistedData != null)
            {
                clonedData = _persistedData;
                MelonLogger.Msg($"Reusing existing datafile '{clonedData.name}' with ID {clonedData.m_ID}, m_Unlocked={clonedData.m_Unlocked}");
            }
            else
            {
                clonedData = UnityEngine.Object.Instantiate(originalData);
                clonedData.name = "FreePlayArcadeMachineData";
                clonedData.hideFlags = HideFlags.DontUnloadUnusedAsset;
                clonedData.m_ID = FREE_PLAY_MACHINE_ID;
                clonedData.m_StoreName = "Free Play";
                clonedData.m_TooltipTitle = "Free Play";
                clonedData.m_Price = 15000;
                clonedData.m_BasePopularity = 4f;
                clonedData.m_Popularity = 5f;
                clonedData.m_BaseTimePerPlay = 2f;
                clonedData.m_BaseHopperSize = 25000;
                clonedData.m_DaysToDeliver = 1;
                clonedData.m_Difficulty = Il2CppRAT.Scriptables.Objects.EArcadeMachineDifficulty.Medium;
                clonedData.m_PricePerPlay = Il2CppRAT.Scriptables.Objects.EArcadeMachinePlayPrice.c150;
                clonedData.m_AddedDemandPopularity = 30f;
                clonedData.m_Profitability = 1200;
                clonedData.m_Reliability = new Vector2(0.85f, 0.95f);

                bool wasPlaced = IsFreePlayPlaced();
                clonedData.m_Unlocked = wasPlaced;
                clonedData.m_OnDelivery = false;
                _persistedData = clonedData;
                MelonLogger.Msg($"Created new datafile '{clonedData.name}' with ID {clonedData.m_ID}, wasPlaced={wasPlaced}");
            }

            Vector3 spawnPos;
            Quaternion spawnRot;
            ArcadeMachineComponent slotMachine = null;
            int slotIndex = -1;

            for (int i = 0; i < manager.m_ArcadeMachines.Count; i++)
            {
                var m = manager.m_ArcadeMachines[i];
                if (m != null && m.gameObject != null && !m.gameObject.activeSelf && m != template)
                {
                    slotMachine = m;
                    slotIndex = i;
                    break;
                }
            }

            if (slotMachine != null)
            {
                spawnPos = slotMachine.transform.position;
                spawnRot = Quaternion.Euler(0f, slotMachine.transform.rotation.eulerAngles.y + 180f, 0f);
                spawnPos -= spawnRot * Vector3.forward * 0.4f;
                MelonLogger.Msg($"Placed at inactive slot: {slotMachine.name} @ {spawnPos}");
            }
            else
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    MelonLogger.Msg("Camera.main was null.");
                    return;
                }
                Vector3 cf = cam.transform.forward;
                cf.y = 0f;
                cf.Normalize();
                spawnPos = cam.transform.position + cf * 3f;
                spawnPos.y = template.transform.position.y;
                spawnRot = Quaternion.LookRotation(-cf, Vector3.up);
                MelonLogger.Msg("No inactive slot found: using camera-relative fallback");
            }

            var cloneGO = UnityEngine.Object.Instantiate(
                template.gameObject, spawnPos, spawnRot, template.transform.parent);
            cloneGO.name = "FreePlayArcadeCabinet";

            var machineComp = cloneGO.GetComponent<ArcadeMachineComponent>();
            if (machineComp == null)
            {
                MelonLogger.Msg("Aborting: Cloned GameObject has no ArcadeMachine component.");
                return;
            }

            MelonLogger.Msg($"m_Screen={machineComp.m_Screen?.name ?? "NULL"},  m_NoScreenGame={machineComp.m_NoScreenGame}");

            machineComp.m_ArcadeMachineDatafile = clonedData;

            var oldGame = machineComp.m_Game;
            if (oldGame != null)
            {
                if (oldGame.gameObject == cloneGO || oldGame.transform.IsChildOf(cloneGO.transform))
                    UnityEngine.Object.Destroy(oldGame);
                else
                    machineComp.m_Game = null;
            }

            var emuGame = cloneGO.AddComponent<EmulatorArcadeManager>();
            machineComp.m_Game = emuGame;
            _freePlayGame = emuGame;
            _freePlayMachine = machineComp;
            MelonLogger.Msg("FreePlay cabinet spawned with EmulatorArcadeManager");

            MelonLogger.Msg($"Spawned '{cloneGO.name}' at {spawnPos}, parent={cloneGO.transform.parent?.name}");

            if (slotIndex >= 0 && slotIndex < manager.m_ArcadeMachines.Count)
            {
                manager.m_ArcadeMachines[slotIndex] = machineComp;
                MelonLogger.Msg($"Replaced slot [{slotIndex}] ({slotMachine?.name}) with FreePlay cabinet");
            }
            else
            {
                manager.m_ArcadeMachines.Add(machineComp);
                MelonLogger.Msg($"Appended FreePlay cabinet (count now {manager.m_ArcadeMachines.Count})");
            }

            var dict = manager.m_arcadeMachineDictionaryByID;
            if (dict != null)
            {
                dict[FREE_PLAY_MACHINE_ID] = machineComp;
                MelonLogger.Msg($"Registered with m_arcadeMachineDictionaryByID (ID={FREE_PLAY_MACHINE_ID})");
            }

            _spawned = true;
            ApplyFreePlayTexture(cloneGO);

            bool alreadyPlaced = IsFreePlayPlaced();
            if (alreadyPlaced)
            {
                try
                {
                    if (HasSavedFreePlayPosition())
                    {
                        float px = GetSavedFloat(SAVE_KEY_POS_X, spawnPos.x);
                        float py = GetSavedFloat(SAVE_KEY_POS_Y, spawnPos.y);
                        float pz = GetSavedFloat(SAVE_KEY_POS_Z, spawnPos.z);
                        float ry = GetSavedFloat(SAVE_KEY_ROT_Y, spawnRot.eulerAngles.y);
                        cloneGO.transform.position = new Vector3(px, py, pz);
                        cloneGO.transform.rotation = Quaternion.Euler(0f, ry, 0f);
                    }
                }
                catch { }
                cloneGO.SetActive(true);
                MelonLogger.Msg("FreePlay cabinet respawned from save (already placed)");
                TryInjectFreePlayShop();
            }
            else
            {
                TryInjectFreePlayShop();

                if (activateCabinet)
                {
                    cloneGO.SetActive(true);
                    MelonLogger.Msg("FreePlay cabinet active (dev mode: shop may not be available)");
                }
                else
                {
                    cloneGO.SetActive(false);
                    MelonLogger.Msg("FreePlay cabinet registered but inactive until purchased");
                }
            }
        }

        private bool InjectFreePlayIntoShop(ArcadeMachineData freePlayData)
        {
            var arcadeMania = UnityEngine.Object.FindObjectOfType<Il2CppRAT.UI.Computer.CUI_ArcadeMania>();
            if (arcadeMania == null) return false;

            var contentRoot = arcadeMania.m_contentRoot;
            if (contentRoot == null) return false;

            for (int i = 0; i < contentRoot.childCount; i++)
            {
                var child = contentRoot.GetChild(i);
                var existing = child.GetComponent<Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem>();
                if (existing != null && existing.m_ArcadeMachineData?.m_ID == FREE_PLAY_MACHINE_ID)
                {
                    existing.Initialise(freePlayData);
                    existing.UpdateItemDetails();
                    existing.ComingSoon(false);
                    existing.gameObject.SetActive(true);
                    existing.m_PageButton = existing.m_Button;
                    if (existing.m_Button != null)
                        existing.m_Button.interactable = !IsFreePlayPlaced() && !freePlayData.m_OnDelivery;
                    EnsureFreePlayShopListRegistration(arcadeMania, existing);
                    _shopInjected = true;
                    MelonLogger.Msg("[ShopInject] FreePlay already in shop: reinitialised existing item");
                    return true;
                }
            }

            Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem template = null;
            for (int i = 0; i < contentRoot.childCount; i++)
            {
                template = contentRoot.GetChild(i).GetComponent<Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem>();
                if (template != null) break;
            }
            if (template == null) template = arcadeMania.m_arcadeMachineItemPrefab;
            if (template == null) return false;

            var newItem = UnityEngine.Object.Instantiate(template, contentRoot, false);
            newItem.name = "FreePlayShopItem";
            newItem.Initialise(freePlayData);
            newItem.UpdateItemDetails();
            newItem.ComingSoon(false);

            try { if (newItem.m_Title != null) newItem.m_Title.text = "Free Play"; }
            catch (Exception ex) { MelonLogger.Warning($"[ShopInject] Title override failed: {ex.Message}"); }
            try { if (newItem.m_Description != null) newItem.m_Description.text = "Load your own arcade ROMs and play classic coin-op games!"; }
            catch (Exception ex) { MelonLogger.Warning($"[ShopInject] Description override failed: {ex.Message}"); }
            newItem.m_PageButton = newItem.m_Button;
            newItem.gameObject.SetActive(true);
            if (newItem.m_Button != null)
                newItem.m_Button.interactable = !IsFreePlayPlaced() && !freePlayData.m_OnDelivery;

            EnsureFreePlayShopListRegistration(arcadeMania, newItem);
            _shopInjected = true;
            MelonLogger.Msg("[ShopInject] Injected FreePlay cabinet into ArcadeMania shop");
            return true;
        }

        private static void EnsureFreePlayShopListRegistration(
            Il2CppRAT.UI.Computer.CUI_ArcadeMania arcadeMania,
            Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem item)
        {
            try
            {
                bool inMachineItems = false;
                if (arcadeMania.m_arcadeMachineItems != null)
                {
                    for (int i = 0; i < arcadeMania.m_arcadeMachineItems.Count; i++)
                    {
                        if (arcadeMania.m_arcadeMachineItems[i] == item)
                        { inMachineItems = true; break; }
                    }
                    if (!inMachineItems)
                        arcadeMania.m_arcadeMachineItems.Add(item);
                }

                var scrollView = arcadeMania.m_ScrollView;
                bool inWebpageItems = false;
                if (scrollView?.m_WebpageItems != null)
                {
                    for (int i = 0; i < scrollView.m_WebpageItems.Count; i++)
                    {
                        if (scrollView.m_WebpageItems[i]?.TryCast<Il2CppRAT.UI.Computer.CUI_ArcadeMachineItem>() == item)
                        { inWebpageItems = true; break; }
                    }
                    if (!inWebpageItems)
                        scrollView.m_WebpageItems.Add(item);
                    scrollView.UpdateNavigation();
                }

                MelonLogger.Msg($"[ShopInject] FreePlay item registered: active={item.gameObject.activeSelf}, " +
                                $"machineItems={inMachineItems || arcadeMania.m_arcadeMachineItems == null}, " +
                                $"webpageItems={inWebpageItems || scrollView?.m_WebpageItems == null}");
            }
            catch (Exception ex) { MelonLogger.Warning($"[ShopInject] List registration failed: {ex.Message}"); }
        }

        // ── Harmony patches ──────────────────────────────────────

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

            private static void Postfix(ArcadeMachineComponent __instance)
            {
                if (__instance.m_ArcadeMachineDatafile?.m_ID != FREE_PLAY_MACHINE_ID) return;

                if (_lastMachine == __instance && _lastFrame == Time.frameCount) return;
                _lastMachine = __instance;
                _lastFrame = Time.frameCount;

                var game = __instance.m_Game;

                if (game != null && game.IsIdle)
                {
                    MelonLogger.Msg("[Harmony] Forcing StartGame on FreePlay cabinet");
                    game.StartGame();
                }

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