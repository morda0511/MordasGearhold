using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MordasGearhold.UI
{
    /// <summary>
    /// Small window (HUD BAR button in the panel) with two sliders that move the quickslot bar on the HUD:
    /// left/right and up/down. While it is open the Gearhold panel steps aside and the bar shows all eight slots
    /// on top of the inventory screen, so every move is visible at once. The values are the config entries
    /// QuickslotBarX / QuickslotBarY, so they are saved right away.
    /// </summary>
    internal static class QuickslotBarEditor
    {
        internal const float RangeX = 900f;
        internal const float RangeY = 900f;
        private const float Width = 460f;
        private const float Height = 214f;

        private static InventoryGui s_gui;
        private static RectTransform s_window;
        private static Slider s_x;
        private static Slider s_y;
        private static TMP_Text s_xValue;
        private static TMP_Text s_yValue;

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
            s_x.SetValueWithoutNotify(Plugin.QuickslotBarX.Value);
            s_y.SetValueWithoutNotify(Plugin.QuickslotBarY.Value);
            ShowValues();
            s_window.SetAsLastSibling();
            s_window.gameObject.SetActive(true);
        }

        internal static void Close()
        {
            if (s_window != null)
            {
                s_window.gameObject.SetActive(false);
            }
        }

        /// <summary>Called every frame by the panel: the window lives only while the inventory is open.</summary>
        internal static void Tick()
        {
            if (IsOpen && !InventoryGui.IsVisible())
            {
                Close();
            }
        }

        private static void ShowValues()
        {
            s_xValue.text = Mathf.RoundToInt(Plugin.QuickslotBarX.Value).ToString();
            s_yValue.text = Mathf.RoundToInt(Plugin.QuickslotBarY.Value).ToString();
        }

        private static void Build(InventoryGui gui)
        {
            s_gui = gui;
            RectTransform parent = (RectTransform)gui.m_player.parent;
            s_window = UiKit.Rect("MordasGearholdBarPosition", parent);
            s_window.anchorMin = s_window.anchorMax = new Vector2(0.5f, 1f);
            s_window.pivot = new Vector2(0.5f, 1f);
            s_window.sizeDelta = new Vector2(Width, Height);
            s_window.anchoredPosition = new Vector2(0f, -60f);
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

            TMP_Text title = UiKit.Text(s_window, gui.m_craftingStationName, "QUICKSLOT BAR", 22f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, Width / 2f, 28f, Width - 40f, 30f);

            s_x = AddSlider(gui, "Left / right", 76f, -RangeX, RangeX, out s_xValue, v =>
            {
                Plugin.QuickslotBarX.Value = Mathf.Round(v);
                ShowValues();
            });
            s_y = AddSlider(gui, "Up / down", 118f, 0f, RangeY, out s_yValue, v =>
            {
                Plugin.QuickslotBarY.Value = Mathf.Round(v);
                ShowValues();
            });

            AddButton(gui, "RESET", Width / 2f - 80f, Height - 36f, 130f, () =>
            {
                Plugin.QuickslotBarX.Value = (float)Plugin.QuickslotBarX.DefaultValue;
                Plugin.QuickslotBarY.Value = (float)Plugin.QuickslotBarY.DefaultValue;
                s_x.SetValueWithoutNotify(Plugin.QuickslotBarX.Value);
                s_y.SetValueWithoutNotify(Plugin.QuickslotBarY.Value);
                ShowValues();
            });
            AddButton(gui, "DONE", Width / 2f + 80f, Height - 36f, 130f, Close);
            s_window.gameObject.SetActive(false);
        }

        private static Slider AddSlider(InventoryGui gui, string label, float y, float min, float max, out TMP_Text value, Action<float> changed)
        {
            TMP_Text name = UiKit.Text(s_window, gui.m_recipeDecription, label, 16f, UiKit.Parchment, TextAlignmentOptions.Left);
            UiKit.Place(name.rectTransform, 85f, y, 120f, 24f);

            GameObject go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = "Slider " + label;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(s_window, false);
            UiKit.Place(rect, 265f, y, 220f, 18f);
            // plain colours in the panel's palette: dark track, gold fill and handle
            foreach (Image image in go.GetComponentsInChildren<Image>(true))
            {
                switch (image.gameObject.name)
                {
                    case "Background": image.color = new Color(0f, 0f, 0f, 0.55f); break;
                    case "Fill": image.color = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.65f); break;
                    case "Handle": image.color = UiKit.Gold; break;
                }
            }
            Slider slider = go.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(v => changed(v));

            value = UiKit.Text(s_window, gui.m_recipeDecription, "", 16f, UiKit.Gold, TextAlignmentOptions.Right);
            UiKit.Place(value.rectTransform, Width - 50f, y, 60f, 24f);
            return slider;
        }

        private static void AddButton(InventoryGui gui, string label, float x, float y, float width, Action onClick)
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
                text.enableAutoSizing = true;
                text.fontSizeMin = 10f;
                text.fontSizeMax = 17f;
            }
        }
    }
}
