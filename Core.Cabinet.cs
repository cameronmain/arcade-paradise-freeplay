using ArcadeMachineComponent = Il2CppRAT.Arcade.ArcadeMachine;
using ArcadeMachineData = Il2CppRAT.Scriptables.Objects.ArcadeMachine;
using Il2CppRAT.Arcade;
using Il2CppRAT.Managers;
using MelonLoader;
using UnityEngine;

namespace ArcadeParadiseFreePlayMod
{
    public partial class Core
    {
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
            NeutraliseBorrowedGlassOverlay(cloneGO);

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
    }
}
