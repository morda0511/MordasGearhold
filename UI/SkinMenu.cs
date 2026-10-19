using System;
using System.Collections.Generic;
using MordasGearhold.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MordasGearhold.UI
{
    /// <summary>
    /// Look and colour menu, opened by a right-click on a Gearhold armour/shield slot or by Alt + right-click on a
    /// weapon in the inventory. Left: a live 3D view of your character. Right: OWN LOOK, the look grid and, for
    /// armour and shield, COLOR. Built from the same vanilla parts as the panel. A click outside closes it.
    /// </summary>
    internal static class SkinMenu
    {
        private const int Columns = 6;
        private const float Cell = 56f;
        private const float Pad = 16f;
        private const float PreviewWidth = 240f;
        private const float ColorRow = 32f;
        private const float ColorHeader = 30f;
        private const float SwatchRow = 40f;

        private static InventoryGui s_gui;
        private static RectTransform s_blocker;
        private static RectTransform s_menu;
        private static RectTransform s_content;

        internal static bool IsOpen => s_blocker != null && s_blocker.gameObject.activeSelf;

        internal static void OpenForPiece(Player player, Transmog.Piece piece)
        {
            string current = Transmog.GetLook(player, piece);
            List<GameObject> looks = Transmog.LooksFor(player, Transmog.TypeOf(piece), null);
            Open(piece.ToString().ToUpperInvariant() + " LOOK", current, looks, look =>
            {
                Transmog.SetLook(player, piece, look);
                OpenForPiece(player, piece); // redraw: the chosen look gets its mark, the menu stays open
            }, player, piece);
        }

        internal static void OpenForWeapon(Player player, ItemDrop.ItemData weapon)
        {
            string current = Transmog.GetItemLook(weapon);
            List<GameObject> looks = Transmog.LooksFor(player, weapon.m_shared.m_itemType, weapon.m_shared.m_skillType);
            string name = Localization.instance.Localize(weapon.m_shared.m_name).ToUpperInvariant();
            Open(name + " LOOK", current, looks, look =>
            {
                Transmog.SetItemLook(player, weapon, look);
                OpenForWeapon(player, weapon);
            });
        }

        internal static void Close()
        {
            if (s_blocker != null)
            {
                s_blocker.gameObject.SetActive(false);
            }
            CharacterPreview.End();
        }

        /// <summary>Called every frame by the panel: the menu lives only while the inventory is open.</summary>
        internal static void Tick()
        {
            if (IsOpen && !InventoryGui.IsVisible())
            {
                Close();
            }
            if (IsOpen)
            {
                CharacterPreview.Tick();
            }
        }


        private static void Open(string title, string current, List<GameObject> looks, Action<string> choose,
            Player dyePlayer = null, Transmog.Piece? dyePiece = null)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
            {
                return;
            }
            bool reopen = IsOpen; // redraw after a choice: keep the menu where it is
            if (s_gui != gui || s_blocker == null)
            {
                Build(gui);
                reopen = false;
            }
            Vector2 oldPosition = s_menu.anchoredPosition;
            for (int i = s_content.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(s_content.GetChild(i).gameObject);
            }

            // grid: the "Hide" cell first, then every unlocked look
            int cells = looks.Count + 1;
            int rows = Mathf.CeilToInt(cells / (float)Columns);
            float ox = PreviewWidth;                      // left edge of the right-hand column
            float columnWidth = Pad * 2f + Columns * Cell;
            float width = ox + columnWidth;
            float height = Pad * 2f + 34f + 40f + rows * Cell;
            bool dyeable = dyePlayer != null && dyePiece.HasValue;
            if (dyeable)
            {
                height += 10f + ColorHeader + SwatchRow + 3 * ColorRow + 38f;
            }
            height = Mathf.Max(height, 380f);
            s_menu.sizeDelta = new Vector2(width, height);

            TMP_Text header = UiKit.Text(s_content, gui.m_craftingStationName, title, 20f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(header.rectTransform, width / 2f, Pad + 12f, width - Pad * 2f, 26f);

            // live 3D view of your character
            float previewTop = Pad + 34f;
            CharacterPreview.CreateView(s_content, Pad + (PreviewWidth - Pad) / 2f, previewTop + (height - previewTop - Pad) / 2f,
                PreviewWidth - Pad, height - previewTop - Pad);

            AddButton(gui, "OWN LOOK", ox + columnWidth / 2f, Pad + 34f + 14f, columnWidth - Pad * 2f, current == null, () => choose(null));

            float gridTop = Pad + 34f + 40f;
            for (int i = 0; i < cells; i++)
            {
                float x = ox + Pad + Cell / 2f + (i % Columns) * Cell;
                float y = gridTop + Cell / 2f + (i / Columns) * Cell;
                if (i == 0)
                {
                    AddCell(gui, null, "Hide", x, y, current == Transmog.Hidden, () => choose(Transmog.Hidden));
                    continue;
                }
                GameObject prefab = looks[i - 1];
                ItemDrop.ItemData data = prefab.GetComponent<ItemDrop>().m_itemData;
                AddCell(gui, data.GetIcon(), data.m_shared.m_name, x, y, prefab.name == current, () => choose(prefab.name));
            }

            if (dyeable)
            {
                AddColorSection(gui, dyePlayer, dyePiece.Value, ox, columnWidth, gridTop + rows * Cell + 10f);
            }

            if (reopen)
            {
                s_menu.anchoredPosition = oldPosition;
            }
            else
            {
                PlaceAtMouse(width, height);
            }
            s_blocker.SetAsLastSibling();
            s_blocker.gameObject.SetActive(true);
            CharacterPreview.Begin();
        }

        private static readonly (string name, float hue)[] s_hues =
        {
            ("Red", 0f), ("Orange", 0.08f), ("Yellow", 0.15f), ("Green", 0.33f),
            ("Teal", 0.48f), ("Blue", 0.6f), ("Purple", 0.76f), ("Pink", 0.89f),
        };

        /// <summary>
        /// COLOR for this piece in the active set: swatches that turn the piece's dominant colour into the chosen
        /// one, Black / White / Grey, then fine controls (Color, Intensity, Brightness) and RESET COLOR.
        /// Every click recolours the character at once; the menu stays open.
        /// </summary>
        private static void AddColorSection(InventoryGui gui, Player player, Transmog.Piece piece, float ox, float columnWidth, float top)
        {
            float cx = ox + columnWidth / 2f;
            float inner = columnWidth - Pad * 2f;
            Image braid = UiKit.Named(gui.m_crafting, "BraidLineHorisontalMedium");
            UiKit.Divider(s_content, braid, cx, top, inner);
            TMP_Text title = UiKit.Text(s_content, gui.m_craftingStationName, "COLOR", 18f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, cx, top + 18f, inner, 24f);

            TMP_Text[] values = new TMP_Text[3];
            void Refresh()
            {
                DyeValue d = Dye.Get(player, piece);
                values[0].text = (d.Hue > 0 ? "+" : "") + d.Hue + "°";
                values[1].text = (d.Saturation > 0 ? "+" : "") + d.Saturation + "%";
                values[2].text = (d.Brightness > 0 ? "+" : "") + d.Brightness + "%";
            }
            void SetDye(DyeValue d)
            {
                Dye.Set(player, piece, d);
                Refresh();
            }

            // swatches: 8 colours + black, white, grey
            int count = s_hues.Length + 3;
            float step = inner / count;
            float size = Mathf.Min(step - 4f, 28f);
            float swatchY = top + ColorHeader + SwatchRow / 2f;
            for (int i = 0; i < count; i++)
            {
                float x = ox + Pad + step / 2f + i * step;
                if (i < s_hues.Length)
                {
                    float hue = s_hues[i].hue;
                    AddSwatch(x, swatchY, size, Color.HSVToRGB(hue, 0.75f, 0.85f), s_hues[i].name, () => SetDye(TowardsHue(player, piece, hue)));
                }
                else if (i == s_hues.Length)
                {
                    AddSwatch(x, swatchY, size, new Color(0.08f, 0.08f, 0.08f), "Black", () => SetDye(new DyeValue { Saturation = -60, Brightness = -70 }));
                }
                else if (i == s_hues.Length + 1)
                {
                    AddSwatch(x, swatchY, size, new Color(0.92f, 0.92f, 0.9f), "White", () => SetDye(new DyeValue { Saturation = -95, Brightness = 60 }));
                }
                else
                {
                    AddSwatch(x, swatchY, size, new Color(0.5f, 0.5f, 0.5f), "Grey", () => SetDye(new DyeValue { Saturation = -100 }));
                }
            }

            // fine controls
            string[] names = { "Color", "Intensity", "Brightness" };
            int[] steps = { 15, 10, 10 };
            float rowsTop = top + ColorHeader + SwatchRow;
            for (int row = 0; row < 3; row++)
            {
                int channel = row;
                float y = rowsTop + ColorRow / 2f + row * ColorRow;
                TMP_Text label = UiKit.Text(s_content, gui.m_recipeDecription, names[row], 15f, UiKit.Parchment, TextAlignmentOptions.Left);
                UiKit.Place(label.rectTransform, ox + Pad + 60f, y, 120f, 24f);
                AddButton(gui, "-", ox + columnWidth - Pad - 150f, y, 40f, false, () =>
                {
                    DyeValue d = Dye.Get(player, piece);
                    Nudge(ref d, channel, -steps[channel]);
                    SetDye(d);
                });
                values[row] = UiKit.Text(s_content, gui.m_recipeDecription, "", 15f, UiKit.Gold, TextAlignmentOptions.Center);
                UiKit.Place(values[row].rectTransform, ox + columnWidth - Pad - 90f, y, 70f, 24f);
                AddButton(gui, "+", ox + columnWidth - Pad - 30f, y, 40f, false, () =>
                {
                    DyeValue d = Dye.Get(player, piece);
                    Nudge(ref d, channel, steps[channel]);
                    SetDye(d);
                });
            }
            float resetY = rowsTop + 3 * ColorRow + 20f;
            AddButton(gui, "RESET COLOR", cx, resetY, inner, false, () => SetDye(default));
            Refresh();
        }

        private static void Nudge(ref DyeValue d, int channel, int delta)
        {
            if (channel == 0) d.Hue += delta;
            else if (channel == 1) d.Saturation += delta;
            else d.Brightness += delta;
        }

        /// <summary>The shift that turns the piece's dominant colour into the target hue (greyish pieces get some colour added).</summary>
        private static DyeValue TowardsHue(Player player, Transmog.Piece piece, float targetHue)
        {
            Texture texture = DyeRenderer.BaseTexture(player, piece);
            Vector3 dominant = texture != null ? TextureDye.Dominant(texture) : new Vector3(0f, 0.5f, 0.5f);
            float delta = targetHue - dominant.x;
            delta -= Mathf.Round(delta); // shortest way around the colour wheel, -0.5..0.5
            return new DyeValue
            {
                Hue = Mathf.RoundToInt(delta * 360f),
                Saturation = dominant.y < 0.3f ? 50 : 10,
            };
        }

        private static void AddSwatch(float x, float y, float size, Color color, string name, Action onClick)
        {
            RectTransform rect = UiKit.Rect("Swatch" + name, s_content);
            UiKit.Place(rect, x, y, size, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
        }

        private static void Build(InventoryGui gui)
        {
            s_gui = gui;
            RectTransform parent = (RectTransform)gui.m_player.parent;

            // full-screen catcher: a click outside the menu closes it
            s_blocker = UiKit.Rect("MordasGearholdSkinMenu", parent);
            s_blocker.anchorMin = Vector2.zero;
            s_blocker.anchorMax = Vector2.one;
            s_blocker.offsetMin = s_blocker.offsetMax = Vector2.zero;
            Image catcher = s_blocker.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0.001f);
            Button closeButton = s_blocker.gameObject.AddComponent<Button>();
            closeButton.transition = Selectable.Transition.None;
            closeButton.onClick.AddListener(Close);

            s_menu = UiKit.Rect("Menu", s_blocker);
            s_menu.anchorMin = s_menu.anchorMax = new Vector2(0.5f, 0.5f);
            s_menu.pivot = new Vector2(0f, 1f);
            // clicks bubble up to the nearest click handler; without this one, a click on the board or the
            // preview would reach the close-catcher behind the menu
            s_menu.gameObject.AddComponent<ClickSink>();
            Image darken = UiKit.Named(gui.m_crafting, "Darken");
            if (darken != null)
            {
                UiKit.Fill(s_menu, "Darken", darken, 24f);
            }
            Image board = UiKit.Fill(s_menu, "Board", UiKit.Named(gui.m_crafting, "Bkg"), 0f);
            if (board.sprite == null)
            {
                board.color = UiKit.PanelFallback;
            }
            board.raycastTarget = true; // clicks on the menu itself do not close it

            s_content = UiKit.Rect("Content", s_menu);
            s_content.anchorMin = Vector2.zero;
            s_content.anchorMax = Vector2.one;
            s_content.offsetMin = s_content.offsetMax = Vector2.zero;
            s_blocker.gameObject.SetActive(false);
        }

        private static void AddButton(InventoryGui gui, string label, float x, float y, float width, bool selected, Action onClick)
        {
            Button button = UnityEngine.Object.Instantiate(gui.m_tabCraft.gameObject, s_content).GetComponent<Button>();
            button.gameObject.SetActive(true);
            button.interactable = !selected; // like the vanilla tabs: the active choice looks pressed
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
            UiKit.Place((RectTransform)button.transform, x, y, width, 28f);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.enableAutoSizing = true;
                text.fontSizeMin = 10f;
                text.fontSizeMax = 16f;
            }
        }

        /// <summary>One cell of the look grid (vanilla inventory slot). Without an icon it is the "Hide" cell: a red slash.</summary>
        private static void AddCell(InventoryGui gui, Sprite icon, string name, float x, float y, bool selected, Action onClick)
        {
            GameObject go = UnityEngine.Object.Instantiate(gui.m_playerGrid.m_elementPrefab, s_content);
            RectTransform rect = (RectTransform)go.transform;
            Vector2 native = rect.sizeDelta.x > 1f ? rect.sizeDelta : new Vector2(Cell, Cell);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.localScale = Vector3.one * ((Cell - 6f) / native.x);

            InventoryElement element = go.GetComponent<InventoryElement>();
            element.m_icon.enabled = icon != null;
            element.m_icon.sprite = icon;
            if (icon == null)
            {
                RectTransform slash = UiKit.Rect("HideSlash", go.transform);
                slash.anchorMin = slash.anchorMax = new Vector2(0.5f, 0.5f);
                slash.pivot = new Vector2(0.5f, 0.5f);
                slash.sizeDelta = new Vector2(native.x * 1.05f, Mathf.Max(3f, native.x * 0.07f));
                slash.localRotation = Quaternion.Euler(0f, 0f, 45f);
                Image line = slash.gameObject.AddComponent<Image>();
                line.color = new Color(0.85f, 0.15f, 0.1f, 0.95f);
                line.raycastTarget = false;
            }
            element.m_equiped.enabled = selected;
            element.m_amount.enabled = false;
            if (element.m_quality != null) element.m_quality.enabled = false;
            if (element.m_queued != null) element.m_queued.enabled = false;
            if (element.m_noteleport != null) element.m_noteleport.enabled = false;
            if (element.m_food != null) element.m_food.enabled = false;
            if (element.m_selected != null) element.m_selected.SetActive(false);
            element.m_durability.gameObject.SetActive(false);
            TMP_Text binding = go.transform.Find("binding")?.GetComponent<TMP_Text>();
            if (binding != null) binding.enabled = false;
            if (element.m_tooltip != null)
            {
                element.m_tooltip.Set(name, "", null);
            }
            UIInputHandler input = go.GetComponentInChildren<UIInputHandler>();
            if (input != null)
            {
                input.m_onLeftClick = _ => onClick();
            }
        }

        private static void PlaceAtMouse(float width, float height)
        {
            RectTransform parent = (RectTransform)s_blocker.parent;
            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(s_blocker, Input.mousePosition, camera, out Vector2 local);
            Rect area = s_blocker.rect;
            float x = Mathf.Clamp(local.x + 12f, area.xMin + 10f, area.xMax - width - 10f);
            float y = Mathf.Clamp(local.y - 12f, area.yMin + height + 10f, area.yMax - 10f);
            s_menu.anchoredPosition = new Vector2(x, y);
        }
    }
}
