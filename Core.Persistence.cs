using Il2CppRAT.Managers;
using MelonLoader;
using UnityEngine;

namespace ArcadeParadiseFreePlayMod
{
    public partial class Core
    {
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
    }
}
