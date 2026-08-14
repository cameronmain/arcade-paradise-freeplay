using ArcadeMachineData = Il2CppRAT.Scriptables.Objects.ArcadeMachine;
using MelonLoader;
using UnityEngine;

namespace ArcadeParadiseFreePlayMod
{
    public partial class Core
    {
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
                    if (existing.m_Button != null)
                        existing.m_Button.interactable = true;
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
            newItem.gameObject.SetActive(true);
            if (newItem.m_Button != null)
                newItem.m_Button.interactable = true;

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
    }
}
