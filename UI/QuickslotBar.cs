using System;
using BepInEx.Configuration;
using MordasGearhold.Core;
using MordasGearhold.Patches;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MordasGearhold.UI
{
    /// <summary>
    /// A HUD bar of Gearhold slots: every OCCUPIED slot of the active set with its item, amount and its current
    /// hotkey (read from the config each frame, so a key changed in the panel shows up at once). Built from the
    /// vanilla hotbar element; the key sits above each slot on one line, so long keys like "Alt+1" stay readable.
    /// There are three bars, each with its own position (QuickslotBarEditor): the 8 quickslots, the 3 food slots
    /// and the 3 mead slots.
    /// </summary>
    internal sealed class QuickslotBar
    {
        private const float KeyHeight = 20f;

        internal static readonly QuickslotBar Quick = new QuickslotBar("Quickslots", GearSlot.Quick1, Topology.QuickslotCount,
            () => Plugin.QuickslotBarX, () => Plugin.QuickslotBarY);
        internal static readonly QuickslotBar Food = new QuickslotBar("Food", GearSlot.Food1, Topology.FoodSlotCount,
            () => Plugin.FoodBarX, () => Plugin.FoodBarY);
        internal static readonly QuickslotBar Mead = new QuickslotBar("Mead", GearSlot.Mead1, Topology.MeadSlotCount,
            () => Plugin.MeadBarX, () => Plugin.MeadBarY);

        internal static readonly QuickslotBar[] All = { Quick, Food, Mead };

        private sealed class Element
        {
            public GameObject Go;
            public Image Icon;
            public TMP_Text Amount;
            public TMP_Text Key;
            public GuiBar Durability;
            public GameObject Equipped;
        }

        internal readonly string Name;
        private readonly GearSlot m_first;
        private readonly Element[] m_elements;
        private readonly Func<ConfigEntry<float>> m_x;
        private readonly Func<ConfigEntry<float>> m_y;
        private Hud m_hud;
        private RectTransform m_root;
        private float m_spacing = 70f;
        private bool m_failed;

        private QuickslotBar(string name, GearSlot first, int count, Func<ConfigEntry<float>> x, Func<ConfigEntry<float>> y)
        {
            Name = name;
            m_first = first;
            m_elements = new Element[count];
            m_x = x;
            m_y = y;
        }

        internal ConfigEntry<float> X => m_x();
        internal ConfigEntry<float> Y => m_y();

        internal static void TickAll(Hud hud, Player player)
        {
            foreach (QuickslotBar bar in All)
            {
                bar.Tick(hud, player);
            }
        }

        private void Tick(Hud hud, Player player)
        {
            if (m_failed)
            {
                return;
            }
            try
            {
                if (m_hud != hud || m_root == null)
                {
                    Build(hud);
                }
                if (m_root == null)
                {
                    return;
                }
                // while the position window is open the bars show on top of the inventory screen, all slots
                bool preview = QuickslotBarEditor.IsOpen;
                bool show = player != null && GearholdState.IsReady(player) && hud.m_rootObject.activeInHierarchy
                    && (preview || (Plugin.ShowQuickslotBar.Value && !InventoryGui.IsVisible()));
                if (show)
                {
                    show = Refresh(player, preview) > 0;
                }
                if (m_root.gameObject.activeSelf != show)
                {
                    m_root.gameObject.SetActive(show);
                }
            }
            catch (Exception ex)
            {
                m_failed = true;
                if (m_root != null)
                {
                    m_root.gameObject.SetActive(false);
                }
                Plugin.Log.LogError("Gearhold " + Name + " bar failed, bar disabled until restart: " + ex);
            }
        }

        private void Build(Hud hud)
        {
            m_hud = hud;
            HotkeyBar bar = hud.GetComponentInChildren<HotkeyBar>(true);
            if (bar == null || bar.m_elementPrefab == null)
            {
                return;
            }
            m_spacing = bar.m_elementSpace;
            m_root = UiKit.Rect("MordasGearhold" + Name + "Bar", hud.m_rootObject.transform);
            m_root.anchorMin = m_root.anchorMax = new Vector2(0.5f, 0f);
            m_root.pivot = new Vector2(0.5f, 0f);
            m_root.sizeDelta = new Vector2(m_spacing * m_elements.Length, m_spacing + KeyHeight);

            for (int i = 0; i < m_elements.Length; i++)
            {
                GameObject go = UnityEngine.Object.Instantiate(bar.m_elementPrefab, m_root);
                go.name = Name + (i + 1);
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
                keyRect.sizeDelta = new Vector2(m_spacing + 10f, KeyHeight);
                keyRect.anchoredPosition = new Vector2(0f, 1f);
                key.enableAutoSizing = true;
                key.fontSizeMin = 10f;
                key.fontSizeMax = 16f;
                key.outlineWidth = 0.2f;
                key.outlineColor = Color.black;

                m_elements[i] = new Element
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
            m_root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Lays out the occupied slots centred at the configured position; returns how many there are. In the
        /// position window (preview) all slots show, empty ones without an item, so the bar is always visible.
        /// </summary>
        private int Refresh(Player player, bool preview)
        {
            m_root.localScale = Vector3.one * Plugin.QuickslotBarScale.Value;
            m_root.anchoredPosition = new Vector2(X.Value, Y.Value);
            int shown = 0;
            for (int i = 0; i < m_elements.Length; i++)
            {
                if (preview || Equipment.ItemIn(player, m_first + i) != null)
                {
                    shown++;
                }
            }
            if (shown == 0)
            {
                return 0;
            }
            float x = -(shown - 1) * m_spacing / 2f;
            for (int i = 0; i < m_elements.Length; i++)
            {
                Element e = m_elements[i];
                GearSlot slot = m_first + i;
                ItemDrop.ItemData item = Equipment.ItemIn(player, slot);
                if (item == null && !preview)
                {
                    if (e.Go.activeSelf) e.Go.SetActive(false);
                    continue;
                }
                if (!e.Go.activeSelf) e.Go.SetActive(true);
                RectTransform rect = (RectTransform)e.Go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                // the vanilla element's pivot is not its centre: place it by its visual centre
                rect.anchoredPosition = new Vector2(x, 0f) - rect.rect.center;
                x += m_spacing;

                e.Key.text = Plugin.KeyLabel(slot);
                if (e.Icon != null)
                {
                    e.Icon.gameObject.SetActive(item != null);
                    if (item != null) e.Icon.sprite = item.GetIcon();
                }
                if (e.Amount != null)
                {
                    bool stack = item != null && item.m_shared.m_maxStackSize > 1;
                    e.Amount.gameObject.SetActive(stack);
                    if (stack) e.Amount.text = item.m_stack.ToString();
                }
                if (e.Equipped != null)
                {
                    e.Equipped.SetActive(item != null && item.m_equipped);
                }
                if (e.Durability != null)
                {
                    bool worn = item != null && item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability();
                    e.Durability.gameObject.SetActive(worn);
                    if (worn) e.Durability.SetValue(item.m_durability <= 0f ? 1f : item.GetDurabilityPercentage());
                }
            }
            return shown;
        }
    }
}
