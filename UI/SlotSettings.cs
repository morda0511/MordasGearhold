using System;
using BepInEx.Configuration;
using MordasGearhold.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MordasGearhold.UI
{
    /// <summary>
    /// Window of the settings gear (SLOTS): how many trinket, utility, food, mead and quickslots the panel has, and the
    /// prefab names the three custom slots take. The values are the same config entries as in the config file; the
    /// panel follows at once. The window sits at the left edge so the panel stays in view.
    /// </summary>
    internal static class SlotSettings
    {
        private const float Width = 470f;
        private const float Height = 520f;

        private static InventoryGui s_gui;
        private static RectTransform s_window;
        private static readonly TMP_Text[] s_values = new TMP_Text[5];
        private static readonly TMP_Text[] s_customNames = new TMP_Text[Topology.CustomSlotCount];
        private static float s_nextRefresh;

        internal static bool IsOpen => s_window != null && s_window.gameObject.activeSelf;

        internal static void Open()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
            {
                return;
            }
            if (s_gui != gui || s_window == null)
            {
                Build(gui);
            }
            Refresh();
            s_window.SetAsLastSibling();
            s_window.gameObject.SetActive(true);
        }

        internal static void Close()
        {
            if (s_window != null)
            {
                s_window.gameObject.SetActive(false);
            }
            TypingGuard.End();
        }

        /// <summary>Called every frame by the panel: the window lives only while the inventory is open; names refresh now and then.</summary>
        internal static void Tick()
        {
            if (!IsOpen)
            {
                return;
            }
            if (!InventoryGui.IsVisible())
            {
                Close();
                return;
            }
            if (Time.unscaledTime >= s_nextRefresh)
            {
                s_nextRefresh = Time.unscaledTime + 0.5f;
                for (int i = 0; i < s_customNames.Length; i++)
                {
                    s_customNames[i].text = Topology.CustomPrefabs(GearSlot.Custom1 + i).Count == 0 ? "" : Topology.CustomLabel(GearSlot.Custom1 + i, null);
                }
            }
        }

        private static ConfigEntry<int> Entry(int row)
        {
            switch (row)
            {
                case 0: return Plugin.TrinketSlots;
                case 1: return Plugin.UtilitySlots;
                case 2: return Plugin.FoodSlots;
                case 3: return Plugin.MeadSlots;
                default: return Plugin.QuickslotCount;
            }
        }

        private static void Refresh()
        {
            for (int row = 0; row < s_values.Length; row++)
            {
                s_values[row].text = Entry(row).Value.ToString();
            }
        }

        private static void Build(InventoryGui gui)
        {
            s_gui = gui;
            RectTransform parent = (RectTransform)gui.m_player.parent;
            s_window = UiKit.Rect("MordasGearholdSlotSettings", parent);
            s_window.anchorMin = s_window.anchorMax = new Vector2(0f, 1f);
            s_window.pivot = new Vector2(0f, 1f);
            s_window.sizeDelta = new Vector2(Width, Height);
            s_window.anchoredPosition = new Vector2(20f, -60f);
            Image darken = UiKit.Named(gui.m_crafting, "Darken");
            if (darken != null)
            {
                UiKit.Fill(s_window, "Darken", darken, 24f);
            }
            Image board = UiKit.Fill(s_window, "Board", UiKit.Named(gui.m_crafting, "Bkg"), 0f);
            if (board.sprite == null)
            {
                board.color = UiKit.PanelFallback;
            }
            board.raycastTarget = true;

            TMP_Text title = UiKit.Text(s_window, gui.m_craftingStationName, "SLOTS", 22f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, Width / 2f, 28f, Width - 40f, 30f);

            string[] names = { "Trinket slots", "Utility slots", "Food slots", "Mead slots", "Quickslots" };
            for (int row = 0; row < names.Length; row++)
            {
                int r = row;
                float y = 78f + row * 40f;
                TMP_Text label = UiKit.Text(s_window, gui.m_recipeDecription, names[row], 16f, UiKit.Parchment, TextAlignmentOptions.Left);
                UiKit.Place(label.rectTransform, 110f, y, 170f, 24f);
                AddButton(gui, "-", 290f, y, 40f, () => Step(r, -1));
                s_values[row] = UiKit.Text(s_window, gui.m_recipeDecription, "", 18f, UiKit.Gold, TextAlignmentOptions.Center);
                UiKit.Place(s_values[row].rectTransform, 345f, y, 50f, 26f);
                AddButton(gui, "+", 400f, y, 40f, () => Step(r, 1));
            }

            TMP_Text custom = UiKit.Text(s_window, gui.m_craftingStationName, "CUSTOM SLOTS", 18f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(custom.rectTransform, Width / 2f, 300f, Width - 40f, 24f);
            TMP_Text hint = UiKit.Text(s_window, gui.m_recipeDecription, "Prefab names, comma separated (e.g. Wishbone,Demister). Empty = slot hidden.", 12f, UiKit.Dim, TextAlignmentOptions.Center);
            UiKit.Place(hint.rectTransform, Width / 2f, 324f, Width - 40f, 18f);
            for (int i = 0; i < Topology.CustomSlotCount; i++)
            {
                int n = i;
                float y = 360f + i * 46f;
                TMP_Text label = UiKit.Text(s_window, gui.m_recipeDecription, "Slot " + (i + 1), 16f, UiKit.Parchment, TextAlignmentOptions.Left);
                UiKit.Place(label.rectTransform, 62f, y - 8f, 70f, 22f);
                AddField(gui, 280f, y - 8f, 300f, 28f, Plugin.CustomSlotPrefabs[i].Value, value => Plugin.CustomSlotPrefabs[n].Value = value.Trim());
                s_customNames[i] = UiKit.Text(s_window, gui.m_recipeDecription, "", 12f, UiKit.Dim, TextAlignmentOptions.Left);
                UiKit.Place(s_customNames[i].rectTransform, 280f, y + 14f, 300f, 16f);
            }
            AddButton(gui, "DONE", Width / 2f, Height - 32f, 130f, Close);
            s_window.gameObject.SetActive(false);
        }

        private static void Step(int row, int delta)
        {
            ConfigEntry<int> entry = Entry(row);
            AcceptableValueRange<int> range = entry.Description.AcceptableValues as AcceptableValueRange<int>;
            int min = range != null ? range.MinValue : 0, max = range != null ? range.MaxValue : 8;
            entry.Value = Mathf.Clamp(entry.Value + delta, min, max);
            Refresh();
        }

        private static Button AddButton(InventoryGui gui, string label, float x, float y, float width, Action onClick)
        {
            Button button = UnityEngine.Object.Instantiate(gui.m_tabCraft.gameObject, s_window).GetComponent<Button>();
            button.gameObject.SetActive(true);
            button.interactable = true;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
            UiKit.Place((RectTransform)button.transform, x, y, width, 30f);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.richText = false;
                text.enableAutoSizing = true;
                text.fontSizeMin = 10f;
                text.fontSizeMax = 17f;
            }
            return button;
        }

        /// <summary>A one-line text box: click, type, Enter or click elsewhere keeps it. While typing, the game gets no key presses.</summary>
        private static void AddField(InventoryGui gui, float x, float y, float width, float height, string value, Action<string> commit)
        {
            RectTransform rect = UiKit.Rect("Field", s_window);
            UiKit.Place(rect, x, y, width, height);
            Image back = rect.gameObject.AddComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.4f);
            RectTransform viewport = UiKit.Rect("Viewport", rect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(8f, 0f);
            viewport.offsetMax = new Vector2(-8f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            TMP_Text text = UiKit.Text(viewport, gui.m_craftingStationName, value, 16f, UiKit.Gold, TextAlignmentOptions.Left);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            rect.gameObject.SetActive(false);
            TMP_InputField input = rect.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = text;
            input.targetGraphic = back;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 120;
            input.richText = false;
            input.text = value;
            input.onSelect.AddListener(_ => TypingGuard.Begin());
            input.onDeselect.AddListener(_ => TypingGuard.End());
            input.onEndEdit.AddListener(v => { TypingGuard.End(); commit(v); });
            rect.gameObject.SetActive(true);
        }
    }
}
