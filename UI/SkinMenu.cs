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
    /// weapon in the inventory. It sits in the middle of the screen. Left: a live 3D view of your character with
    /// SAVE below it. Right: tabs for Head/Chest/Legs/Cape/Shield, OWN LOOK, the look grid and, for armour and
    /// shield, COLOR. Every choice shows on the character at once, but only SAVE keeps it: the back arrow (or
    /// closing the inventory) puts the looks and colours back to how they were when the menu opened.
    /// </summary>
    internal static class SkinMenu
    {
        private const int Columns = 7;
        private const float Cell = 64f;
        private const float Pad = 20f;
        private const float PreviewWidth = 360f;
        private const float ColorRow = 34f;
        private const float ColorHeader = 30f;
        private const float SwatchRow = 44f;
        private const float MinHeight = 640f;

        private static readonly Transmog.Piece[] s_pieces =
        {
            Transmog.Piece.Helmet, Transmog.Piece.Chest, Transmog.Piece.Legs, Transmog.Piece.Shoulder, Transmog.Piece.Shield,
        };

        private static InventoryGui s_gui;
        private static RectTransform s_blocker;
        private static RectTransform s_menu;
        private static RectTransform s_content;

        // what the menu changed since it opened is undone by this, unless SAVE was pressed
        private static Action s_revert;

        internal static bool IsOpen => s_blocker != null && s_blocker.gameObject.activeSelf;

        internal static void OpenForPiece(Player player, Transmog.Piece piece)
        {
            if (!IsOpen)
            {
                int set = GearholdState.ActiveSet(player);
                string looks0 = Transmog.ReadSetLooks(player, set);
                string dyes0 = Dye.ReadSet(player, set);
                s_revert = () =>
                {
                    Transmog.WriteSetLooks(player, set, looks0);
                    Dye.WriteSet(player, set, dyes0);
                };
            }
            string current = Transmog.GetLook(player, piece);
            List<GameObject> looks = Transmog.LooksFor(player, Transmog.TypeOf(piece), null);
            // the window keeps one size for all five pieces, so switching tabs does not make it jump
            int maxLooks = 0;
            foreach (Transmog.Piece p in s_pieces)
            {
                maxLooks = Mathf.Max(maxLooks, p == piece ? looks.Count : Transmog.LooksFor(player, Transmog.TypeOf(p), null).Count);
            }
            Open("LOOKS & COLORS", current, looks, maxLooks, look =>
            {
                Transmog.SetLook(player, piece, look);
                OpenForPiece(player, piece); // redraw: the chosen look gets its mark, the menu stays open
            }, player, piece);
        }

        internal static void OpenForWeapon(Player player, ItemDrop.ItemData weapon)
        {
            if (!IsOpen)
            {
                string look0 = Transmog.GetItemLook(weapon);
                GlowValue glow0 = Glow.Get(weapon);
                s_revert = () =>
                {
                    Transmog.SetItemLook(player, weapon, look0);
                    Glow.Set(player, weapon, glow0);
                };
            }
            string current = Transmog.GetItemLook(weapon);
            List<GameObject> looks = Transmog.LooksFor(player, weapon.m_shared.m_itemType, weapon.m_shared.m_skillType);
            string name = Localization.instance.Localize(weapon.m_shared.m_name).ToUpperInvariant();
            Open(name + " LOOK", current, looks, looks.Count, look =>
            {
                Transmog.SetItemLook(player, weapon, look);
                OpenForWeapon(player, weapon);
            }, glowPlayer: player, glowWeapon: Glow.CanGlow(weapon) ? weapon : null);
        }

        /// <summary>Closes the menu. Without <paramref name="save"/>, every change made in it is undone.</summary>
        internal static void Close(bool save = false)
        {
            Action revert = s_revert;
            s_revert = null;
            if (s_blocker != null)
            {
                s_blocker.gameObject.SetActive(false);
            }
            CharacterPreview.End();
            TextureDye.ReleaseZoneData(); // the pixel data kept for fast re-colouring is not needed outside the menu
            if (!save && revert != null && Player.m_localPlayer != null)
            {
                try
                {
                    revert();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Gearhold look menu: could not undo the unsaved changes: " + ex.Message);
                }
            }
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

        private static void Open(string title, string current, List<GameObject> looks, int maxLooks, Action<string> choose,
            Player dyePlayer = null, Transmog.Piece? dyePiece = null, Player glowPlayer = null, ItemDrop.ItemData glowWeapon = null)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
            {
                return;
            }
            if (s_gui != gui || s_blocker == null)
            {
                Build(gui);
            }
            for (int i = s_content.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(s_content.GetChild(i).gameObject);
            }

            bool dyeable = dyePlayer != null && dyePiece.HasValue;
            int cells = looks.Count + 1; // the "Hide" cell first, then every unlocked look
            int rows = Mathf.Max(1, Mathf.CeilToInt((maxLooks + 1) / (float)Columns));
            float ox = Pad + PreviewWidth;               // left edge of the right-hand column
            float columnWidth = Pad * 2f + Columns * Cell;
            float inner = columnWidth - Pad * 2f;
            float cx = ox + columnWidth / 2f;
            float width = ox + columnWidth;

            float tabsY = Pad + 50f;
            float ownLookY = dyeable ? tabsY + 40f : Pad + 50f;
            float gridTop = ownLookY + 24f;
            float height = gridTop + rows * Cell + Pad;
            if (dyeable)
            {
                height += 10f + ColorHeader + SwatchRow + 3 * ColorRow + 40f + (ZoneCount(dyePlayer, dyePiece.Value) > 1 ? ZoneRow : 0f);
            }
            bool glowable = glowPlayer != null && glowWeapon != null;
            if (glowable)
            {
                height += 10f + ColorHeader + SwatchRow + 40f;
            }
            height = Mathf.Max(height, MinHeight);
            s_menu.sizeDelta = new Vector2(width, height);

            // back arrow (discard) top left, title in the middle
            AddButton(gui, "< BACK", Pad + 50f, Pad + 14f, 100f, false, () => Close());
            TMP_Text header = UiKit.Text(s_content, gui.m_craftingStationName, title, 22f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(header.rectTransform, width / 2f, Pad + 14f, width - 300f, 30f);

            // left: live 3D view of your character, SAVE below it
            float previewTop = Pad + 40f;
            float previewBottom = height - Pad - 48f;
            float previewX = Pad + (PreviewWidth - Pad) / 2f;
            CharacterPreview.CreateView(s_content, previewX, (previewTop + previewBottom) / 2f,
                PreviewWidth - Pad, previewBottom - previewTop);
            AddButton(gui, "SAVE", previewX, height - Pad - 18f, 200f, false, () => Close(save: true));

            // right: one tab per piece, so all of them can be changed without leaving the menu
            if (dyeable)
            {
                float tabWidth = inner / s_pieces.Length;
                for (int i = 0; i < s_pieces.Length; i++)
                {
                    Transmog.Piece piece = s_pieces[i];
                    AddButton(gui, PieceLabel(piece), ox + Pad + tabWidth / 2f + i * tabWidth, tabsY, tabWidth - 6f,
                        piece == dyePiece.Value, () => OpenForPiece(dyePlayer, piece));
                }
            }

            AddButton(gui, "OWN LOOK", cx, ownLookY, inner, current == null, () => choose(null));
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
            if (glowable)
            {
                AddGlowSection(gui, glowPlayer, glowWeapon, ox, columnWidth, gridTop + rows * Cell + 10f);
            }

            // centred on the screen; scaled down when the screen is too small for it
            Rect area = s_blocker.rect;
            float scale = Mathf.Min(1f, (area.width - 40f) / width, (area.height - 40f) / height);
            s_menu.localScale = Vector3.one * Mathf.Max(0.4f, scale);
            s_menu.anchoredPosition = Vector2.zero;
            s_blocker.SetAsLastSibling();
            s_blocker.gameObject.SetActive(true);
            CharacterPreview.Begin();
        }

        private static string PieceLabel(Transmog.Piece piece)
        {
            switch (piece)
            {
                case Transmog.Piece.Helmet: return "HEAD";
                case Transmog.Piece.Shoulder: return "CAPE";
                default: return piece.ToString().ToUpperInvariant();
            }
        }

        private static readonly (string name, float hue)[] s_hues =
        {
            ("Red", 0f), ("Orange", 0.08f), ("Yellow", 0.15f), ("Green", 0.33f),
            ("Teal", 0.48f), ("Blue", 0.6f), ("Purple", 0.76f), ("Pink", 0.89f),
        };

        private const float ZoneRow = 46f;

        // which colour zone the COLOR controls act on: -1 = the whole piece, 0.. = one zone (reset on a new piece)
        private static int s_zone = -1;
        private static Transmog.Piece? s_zonePiece;

        /// <summary>How many colour zones the piece shows in the menu (1 = no zone choice).</summary>
        private static int ZoneCount(Player player, Transmog.Piece piece)
        {
            ZoneSet zones = DyeRenderer.ZonesOf(player, piece);
            return zones != null ? zones.Count : 1;
        }

        /// <summary>
        /// COLOR for this piece in the active set. A piece with several colour zones (metal, leather, cloth ...)
        /// first offers a chip per zone, ringed by its original colour, with its new colour in the middle;
        /// ALL colours every zone the same. Then swatches that turn the zone's colour into the chosen one,
        /// Black / White / Grey, the fine controls (Color, Intensity, Brightness) and a small RESET.
        /// Every click recolours the character at once; SAVE keeps it.
        /// </summary>
        private static void AddColorSection(InventoryGui gui, Player player, Transmog.Piece piece, float ox, float columnWidth, float top)
        {
            float cx = ox + columnWidth / 2f;
            float inner = columnWidth - Pad * 2f;
            Image braid = UiKit.Named(gui.m_crafting, "BraidLineHorisontalMedium");
            UiKit.Divider(s_content, braid, cx, top, inner);
            TMP_Text title = UiKit.Text(s_content, gui.m_craftingStationName, "COLOR", 18f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, cx, top + 18f, inner, 24f);

            ZoneSet zones = DyeRenderer.ZonesOf(player, piece);
            int zoneCount = zones != null ? zones.Count : 1;
            if (s_zonePiece != piece || s_zone >= zoneCount)
            {
                s_zone = -1; // a new piece (or a look with fewer zones): back to the whole piece
                s_zonePiece = piece;
            }
            bool zoned = zoneCount > 1;
            float belowZones = top + ColorHeader + (zoned ? ZoneRow : 0f);

            Image[] chipCentres = new Image[zoneCount];
            Button[] chipButtons = new Button[zoneCount + 1]; // [0] = ALL
            DyeValue Current() => s_zone < 0 ? Dye.Get(player, piece) : Dye.GetZones(player, piece)[Mathf.Min(s_zone, Dye.GetZones(player, piece).Length - 1)];

            TMP_Text[] values = new TMP_Text[3];
            void Refresh()
            {
                DyeValue d = Current();
                values[0].text = (d.Hue > 0 ? "+" : "") + d.Hue + "°";
                values[1].text = (d.Saturation > 0 ? "+" : "") + d.Saturation + "%";
                values[2].text = (d.Brightness > 0 ? "+" : "") + d.Brightness + "%";
                if (zoned)
                {
                    DyeValue[] all = Dye.GetZones(player, piece);
                    for (int k = 0; k < zoneCount; k++)
                    {
                        DyeValue dye = all.Length == 1 ? all[0] : (k < all.Length ? all[k] : default);
                        chipCentres[k].color = TextureDye.ShiftColor(zones.Chips[k], dye);
                    }
                    for (int k = 0; k <= zoneCount; k++)
                    {
                        chipButtons[k].interactable = k - 1 != s_zone; // the chosen one looks pressed
                    }
                }
            }
            void SetDye(DyeValue d)
            {
                if (s_zone < 0 || !zoned) Dye.Set(player, piece, d);
                else Dye.SetZone(player, piece, s_zone, zoneCount, d);
                Refresh();
            }

            // zone chips: ALL, then one per colour zone (ring = original colour, middle = colour now)
            if (zoned)
            {
                float chipSize = 34f, gap = 12f;
                float rowWidth = (zoneCount + 1) * chipSize + zoneCount * gap;
                float x0 = cx - rowWidth / 2f + chipSize / 2f;
                float chipY = top + ColorHeader + ZoneRow / 2f - 4f;
                chipButtons[0] = AddChip(gui, "ALL", x0, chipY, chipSize, null, out Image _, () => { s_zone = -1; Refresh(); });
                for (int k = 0; k < zoneCount; k++)
                {
                    int zone = k;
                    float x = x0 + (k + 1) * (chipSize + gap);
                    chipButtons[k + 1] = AddChip(gui, "Zone " + (k + 1), x, chipY, chipSize, zones.Chips[k], out chipCentres[k], () => { s_zone = zone; Refresh(); });
                }
            }

            // swatches: 8 colours + black, white, grey
            int count = s_hues.Length + 3;
            float step = inner / count;
            float size = Mathf.Min(step - 6f, 34f);
            float swatchY = belowZones + SwatchRow / 2f;
            for (int i = 0; i < count; i++)
            {
                float x = ox + Pad + step / 2f + i * step;
                if (i < s_hues.Length)
                {
                    float hue = s_hues[i].hue;
                    AddSwatch(x, swatchY, size, Color.HSVToRGB(hue, 0.75f, 0.85f), s_hues[i].name, () => SetDye(TowardsHue(player, piece, zones, s_zone, hue)));
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
            float rowsTop = belowZones + SwatchRow;
            for (int row = 0; row < 3; row++)
            {
                int channel = row;
                float y = rowsTop + ColorRow / 2f + row * ColorRow;
                TMP_Text label = UiKit.Text(s_content, gui.m_recipeDecription, names[row], 16f, UiKit.Parchment, TextAlignmentOptions.Left);
                UiKit.Place(label.rectTransform, ox + Pad + 60f, y, 120f, 24f);
                AddButton(gui, "-", ox + columnWidth - Pad - 150f, y, 40f, false, () =>
                {
                    DyeValue d = Current();
                    Nudge(ref d, channel, -steps[channel]);
                    SetDye(d);
                });
                values[row] = UiKit.Text(s_content, gui.m_recipeDecription, "", 16f, UiKit.Gold, TextAlignmentOptions.Center);
                UiKit.Place(values[row].rectTransform, ox + columnWidth - Pad - 90f, y, 70f, 24f);
                AddButton(gui, "+", ox + columnWidth - Pad - 30f, y, 40f, false, () =>
                {
                    DyeValue d = Current();
                    Nudge(ref d, channel, steps[channel]);
                    SetDye(d);
                });
            }
            float resetY = rowsTop + 3 * ColorRow + 20f;
            AddButton(gui, "RESET", ox + columnWidth - Pad - 60f, resetY, 120f, false, () =>
            {
                if (zoned && s_zone < 0) Dye.Set(player, piece, default); // ALL: every zone back to its own colours
                else SetDye(default);
                Refresh();
            });
            Refresh();
        }

        /// <summary>
        /// A square chip made of vanilla parts: a dark frame (a tab button), the zone's original colour as a ring and
        /// its colour now in the middle. Without colours it is the ALL chip with a label.
        /// </summary>
        private static Button AddChip(InventoryGui gui, string name, float x, float y, float size, Color? original, out Image centre, Action onClick)
        {
            Button button = UnityEngine.Object.Instantiate(gui.m_tabCraft.gameObject, s_content).GetComponent<Button>();
            button.gameObject.name = "Chip " + name;
            button.gameObject.SetActive(true);
            button.interactable = true;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
            UiKit.Place((RectTransform)button.transform, x, y, size + 6f, size + 6f);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            centre = null;
            if (original.HasValue)
            {
                if (text != null) text.gameObject.SetActive(false);
                RectTransform ring = UiKit.Rect("Ring", button.transform);
                ring.anchorMin = Vector2.zero;
                ring.anchorMax = Vector2.one;
                ring.offsetMin = new Vector2(4f, 4f);
                ring.offsetMax = new Vector2(-4f, -4f);
                Image ringImage = ring.gameObject.AddComponent<Image>();
                ringImage.color = original.Value;
                ringImage.raycastTarget = false;
                RectTransform middle = UiKit.Rect("Now", ring);
                middle.anchorMin = Vector2.zero;
                middle.anchorMax = Vector2.one;
                middle.offsetMin = new Vector2(8f, 8f);
                middle.offsetMax = new Vector2(-8f, -8f);
                centre = middle.gameObject.AddComponent<Image>();
                centre.color = original.Value;
                centre.raycastTarget = false;
            }
            else if (text != null)
            {
                text.text = name;
                text.enableAutoSizing = true;
                text.fontSizeMin = 8f;
                text.fontSizeMax = 13f;
            }
            return button;
        }

        /// <summary>
        /// GLOW for a weapon with glow effects (fire, frost, lightning, glowing cores): a swatch replaces the colour
        /// of every coloured glow part, White makes them white, RESET gives the weapon its own colours back.
        /// </summary>
        private static void AddGlowSection(InventoryGui gui, Player player, ItemDrop.ItemData weapon, float ox, float columnWidth, float top)
        {
            float cx = ox + columnWidth / 2f;
            float inner = columnWidth - Pad * 2f;
            Image braid = UiKit.Named(gui.m_crafting, "BraidLineHorisontalMedium");
            UiKit.Divider(s_content, braid, cx, top, inner);
            TMP_Text title = UiKit.Text(s_content, gui.m_craftingStationName, "GLOW", 18f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, cx, top + 18f, inner, 24f);

            int count = s_hues.Length + 1;
            float step = inner / count;
            float size = Mathf.Min(step - 6f, 34f);
            float swatchY = top + ColorHeader + SwatchRow / 2f;
            for (int i = 0; i < count; i++)
            {
                float x = ox + Pad + step / 2f + i * step;
                if (i < s_hues.Length)
                {
                    int hue = Mathf.RoundToInt(s_hues[i].hue * 360f);
                    AddSwatch(x, swatchY, size, Color.HSVToRGB(s_hues[i].hue, 0.85f, 1f), s_hues[i].name,
                        () => Glow.Set(player, weapon, new GlowValue { IsSet = true, Hue = hue }));
                }
                else
                {
                    AddSwatch(x, swatchY, size, new Color(0.95f, 0.95f, 0.95f), "White",
                        () => Glow.Set(player, weapon, new GlowValue { IsSet = true, White = true }));
                }
            }
            AddButton(gui, "RESET", ox + columnWidth - Pad - 60f, top + ColorHeader + SwatchRow + 20f, 120f, false,
                () => Glow.Set(player, weapon, default));
        }

        private static void Nudge(ref DyeValue d, int channel, int delta)
        {
            if (channel == 0) d.Hue += delta;
            else if (channel == 1) d.Saturation += delta;
            else d.Brightness += delta;
        }

        /// <summary>The shift that turns the piece's dominant colour into the target hue (greyish pieces get some colour added).</summary>
        private static DyeValue TowardsHue(Player player, Transmog.Piece piece, ZoneSet zones, int zone, float targetHue)
        {
            float hue, saturation;
            if (zones != null && zone >= 0 && zone < zones.Count)
            {
                // one zone: its own average colour
                Color.RGBToHSV(zones.Chips[zone], out hue, out saturation, out float _);
            }
            else
            {
                Texture texture = DyeRenderer.BaseTexture(player, piece);
                Vector3 dominant = texture != null ? TextureDye.Dominant(texture) : new Vector3(0f, 0.5f, 0.5f);
                hue = dominant.x;
                saturation = dominant.y;
            }
            float delta = targetHue - hue;
            delta -= Mathf.Round(delta); // shortest way around the colour wheel, -0.5..0.5
            return new DyeValue
            {
                Hue = Mathf.RoundToInt(delta * 360f),
                Saturation = saturation < 0.3f ? 50 : 10,
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

            // full-screen layer behind the menu: keeps clicks away from the inventory while the menu is open
            // (it does not close the menu; that is the back arrow)
            s_blocker = UiKit.Rect("MordasGearholdSkinMenu", parent);
            s_blocker.anchorMin = Vector2.zero;
            s_blocker.anchorMax = Vector2.one;
            s_blocker.offsetMin = s_blocker.offsetMax = Vector2.zero;
            Image catcher = s_blocker.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0.35f);

            s_menu = UiKit.Rect("Menu", s_blocker);
            s_menu.anchorMin = s_menu.anchorMax = new Vector2(0.5f, 0.5f);
            s_menu.pivot = new Vector2(0.5f, 0.5f);
            // clicks bubble up to the nearest click handler; this one keeps them inside the menu
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
            board.raycastTarget = true;

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
            UiKit.Place((RectTransform)button.transform, x, y, width, 30f);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.richText = false; // "< BACK" is text, not a tag
                text.enableAutoSizing = true;
                text.fontSizeMin = 10f;
                text.fontSizeMax = 17f;
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
    }
}
