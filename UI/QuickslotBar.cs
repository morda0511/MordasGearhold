using System;
using MordasGearhold.Core;
using MordasGearhold.Patches;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MordasGearhold.UI
{
    /// <summary>
    /// HUD bar at the bottom centre: every OCCUPIED quickslot of the active set with its item, amount and its
    /// current hotkey (read from the config each frame, so a key changed in the panel shows up at once).
    /// Built from the vanilla hotbar element; the key sits above each slot on one line, so long keys like
    /// "Alt+1" stay readable.
    /// </summary>
    internal static class QuickslotBar
    {
        private const float KeyHeight = 20f;

        private sealed class Element
        {
            public GameObject Go;
            public Image Icon;
            public TMP_Text Amount;
            public TMP_Text Key;
            public GuiBar Durability;
            public GameObject Equipped;
        }

        private static Hud s_hud;
        private static RectTransform s_root;
        private static readonly Element[] s_elements = new Element[Topology.QuickslotCount];
        private static float s_spacing = 70f;
        private static bool s_failed;

        internal static void Tick(Hud hud, Player player)
        {
            if (s_failed)
            {
                return;
            }
            try
            {
                if (s_hud != hud || s_root == null)
                {
                    Build(hud);
                }
                if (s_root == null)
                {
                    return;
                }
                bool show = Plugin.ShowQuickslotBar.Value && player != null && GearholdState.IsReady(player)
                    && hud.m_rootObject.activeInHierarchy && !InventoryGui.IsVisible();
                if (show)
                {
                    show = Refresh(player) > 0;
                }
                if (s_root.gameObject.activeSelf != show)
                {
                    s_root.gameObject.SetActive(show);
                }
            }
            catch (Exception ex)
            {
                s_failed = true;
                if (s_root != null)
                {
                    s_root.gameObject.SetActive(false);
                }
                Plugin.Log.LogError("Gearhold quickslot bar failed, bar disabled until restart: " + ex);
            }
        }

        private static void Build(Hud hud)
        {
            s_hud = hud;
            HotkeyBar bar = hud.GetComponentInChildren<HotkeyBar>(true);
            if (bar == null || bar.m_elementPrefab == null)
            {
                return;
            }
            s_spacing = bar.m_elementSpace;
            s_root = UiKit.Rect("MordasGearholdQuickslots", hud.m_rootObject.transform);
            s_root.anchorMin = s_root.anchorMax = new Vector2(0.5f, 0f);
            s_root.pivot = new Vector2(0.5f, 0f);
            s_root.sizeDelta = new Vector2(s_spacing * Topology.QuickslotCount, s_spacing + KeyHeight);
            s_root.anchoredPosition = new Vector2(0f, 100f);

            for (int i = 0; i < s_elements.Length; i++)
            {
                GameObject go = UnityEngine.Object.Instantiate(bar.m_elementPrefab, s_root);
                go.name = "Quickslot" + (i + 1);
                Transform t = go.transform;
                TMP_Text vanillaBinding = t.Find("binding")?.GetComponent<TMP_Text>();
                if (vanillaBinding != null)
                {
                    vanillaBinding.enabled = false;
                }
                // own key label above the slot: one line, never wrapped
                TMP_Text key = UiKit.Text(t, vanillaBinding, "", 16f, UiKit.Gold, TextAlignmentOptions.Bottom);
                RectTransform keyRect = key.rectTransform;
                keyRect.anchorMin = new Vector2(0.5f, 1f);
                keyRect.anchorMax = new Vector2(0.5f, 1f);
                keyRect.pivot = new Vector2(0.5f, 0f);
                keyRect.sizeDelta = new Vector2(s_spacing + 10f, KeyHeight);
                keyRect.anchoredPosition = new Vector2(0f, 1f);
                key.enableAutoSizing = true;
                key.fontSizeMin = 10f;
                key.fontSizeMax = 16f;
                key.outlineWidth = 0.2f;
                key.outlineColor = Color.black;

                s_elements[i] = new Element
                {
                    Go = go,
                    Icon = t.Find("icon")?.GetComponent<Image>(),
                    Amount = t.Find("amount")?.GetComponent<TMP_Text>(),
                    Key = key,
                    Durability = t.Find("durability")?.GetComponent<GuiBar>(),
                    Equipped = t.Find("equiped")?.gameObject,
                };
                t.Find("queued")?.gameObject.SetActive(false);
                t.Find("selected")?.gameObject.SetActive(false);
                go.SetActive(false);
            }
            s_root.gameObject.SetActive(false);
        }

        /// <summary>Lays out the occupied slots centred; returns how many there are.</summary>
        private static int Refresh(Player player)
        {
            s_root.localScale = Vector3.one * Plugin.QuickslotBarScale.Value;
            int occupied = 0;
            for (int i = 0; i < s_elements.Length; i++)
            {
                if (Equipment.ItemIn(player, Topology.QuickSlot(i)) != null)
                {
                    occupied++;
                }
            }
            if (occupied == 0)
            {
                return 0;
            }
            float x = -(occupied - 1) * s_spacing / 2f;
            for (int i = 0; i < s_elements.Length; i++)
            {
                Element e = s_elements[i];
                ItemDrop.ItemData item = Equipment.ItemIn(player, Topology.QuickSlot(i));
                if (item == null)
                {
                    if (e.Go.activeSelf) e.Go.SetActive(false);
                    continue;
                }
                if (!e.Go.activeSelf) e.Go.SetActive(true);
                RectTransform rect = (RectTransform)e.Go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                // the vanilla element's pivot is not its centre: place it by its visual centre
                rect.anchoredPosition = new Vector2(x, 0f) - rect.rect.center;
                x += s_spacing;

                if (e.Icon != null)
                {
                    e.Icon.gameObject.SetActive(true);
                    e.Icon.sprite = item.GetIcon();
                }
                e.Key.text = Plugin.KeyLabel(i);
                if (e.Amount != null)
                {
                    bool stack = item.m_shared.m_maxStackSize > 1;
                    e.Amount.gameObject.SetActive(stack);
                    if (stack) e.Amount.text = item.m_stack.ToString();
                }
                if (e.Equipped != null)
                {
                    e.Equipped.SetActive(item.m_equipped);
                }
                if (e.Durability != null)
                {
                    bool worn = item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability();
                    e.Durability.gameObject.SetActive(worn);
                    if (worn) e.Durability.SetValue(item.m_durability <= 0f ? 1f : item.GetDurabilityPercentage());
                }
            }
            return occupied;
        }
    }
}
