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
            }, PieceTarget(player, piece), piece, player);
        }

        internal static void OpenForWeapon(Player player, ItemDrop.ItemData weapon)
        {
            s_openWeapon = weapon;
            if (!IsOpen)
            {
                string look0 = Transmog.GetItemLook(weapon);
                string dye0 = Dye.ReadItem(weapon);
                GlowValue glow0 = Glow.Get(weapon);
                GlowValue shine0 = Glow.GetShine(weapon);
                s_revert = () =>
                {
                    Transmog.SetItemLook(player, weapon, look0);
                    Glow.Set(player, weapon, glow0);
                    Glow.SetShine(player, weapon, shine0);
                    Dye.WriteItem(player, weapon, dye0);
                };
            }
            string current = Transmog.GetItemLook(weapon);
            List<GameObject> looks = Transmog.LooksFor(player, weapon.m_shared.m_itemType, weapon.m_shared.m_skillType);
            string name = Localization.instance.Localize(weapon.m_shared.m_name).ToUpperInvariant();
            Open(name + " LOOK", current, looks, looks.Count, look =>
            {
                Transmog.SetItemLook(player, weapon, look);
                OpenForWeapon(player, weapon);
            }, WeaponTarget(player, weapon), glowPlayer: player, glowWeapon: Glow.CanGlow(weapon) ? weapon : null, shineWeapon: Glow.CanShine(weapon) ? weapon : null);
        }

        /// <summary>Closes the menu. Without <paramref name="save"/>, every change made in it is undone.</summary>
        internal static void Close(bool save = false)
        {
            ZoneFlash.Stop();
            if (save) FlushPending(); else s_pending = null;
            Action revert = s_revert;
            s_revert = null;
            s_builtDye = null;
            s_presetsMode = false;
            TypingGuard.End();
            InventoryHider.Restore();
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
            TypingGuard.Tick();
            ZoneFlash.Tick();
            TickPending();
            if (IsOpen && !InventoryGui.IsVisible())
            {
                Close();
            }
            if (IsOpen)
            {
                CharacterPreview.Tick();
                RedrawIfZonesChanged();
            }
        }

        // what the COLOR section of the open menu was built for: redrawn when the zones of that change
        private static DyeTarget s_builtDye;
        private static Action s_builtReopen;
        private static int s_builtZoneId;
        private static float s_nextZoneCheck;

        /// <summary>What the COLOR section colours: a piece of the set, or a weapon. Zones, values and how to store them.</summary>
        private sealed class DyeTarget
        {
            public Func<ZoneSet> Zones;
            public Func<DyeValue[]> Get;
            public Action<DyeValue> SetAll;
            public Action<int, int, DyeValue> SetZone;
            public Func<Texture> BaseTexture;
            public Action<int, int> Flash; // blink a zone (-1 = all) on the character: zone, zone count
            public object Key; // a different key resets the chosen zone
        }

        private static DyeTarget PieceTarget(Player player, Transmog.Piece piece) => new DyeTarget
        {
            Zones = () => DyeRenderer.ZonesOf(player, piece),
            Get = () => Dye.GetZones(player, piece),
            SetAll = dye => Dye.Set(player, piece, dye),
            SetZone = (zone, count, dye) => Dye.SetZone(player, piece, zone, count, dye),
            BaseTexture = () => DyeRenderer.BaseTexture(player, piece),
            Flash = (zone, count) => ZoneFlash.Start(player, piece, null, zone, count),
            Key = piece,
        };

        private static DyeTarget WeaponTarget(Player player, ItemDrop.ItemData weapon) => new DyeTarget
        {
            Zones = () => DyeRenderer.WeaponZones(weapon),
            Get = () => Dye.GetItemZones(weapon),
            SetAll = dye => Dye.SetItem(player, weapon, dye),
            SetZone = (zone, count, dye) => Dye.SetItemZone(player, weapon, zone, count, dye),
            BaseTexture = () => DyeRenderer.WeaponTexture(weapon),
            Flash = (zone, count) => ZoneFlash.Start(player, null, weapon, zone, count),
            Key = weapon,
        };

        /// <summary>
        /// A new look or piece only shows on the character a moment later (vanilla builds the new model on its next
        /// update), so the menu may have been drawn with the colour zones of the old one: redraw as soon as the
        /// zones of what is shown now differ from what the menu was built with.
        /// </summary>
        private static void RedrawIfZonesChanged()
        {
            if (s_builtDye == null || s_builtReopen == null || Time.unscaledTime < s_nextZoneCheck)
            {
                return;
            }
            s_nextZoneCheck = Time.unscaledTime + 0.25f;
            int id = s_builtDye.Zones()?.Id ?? 0;
            if (id != s_builtZoneId)
            {
                s_builtReopen();
            }
        }

        private static void Open(string title, string current, List<GameObject> looks, int maxLooks, Action<string> choose,
            DyeTarget dye = null, Transmog.Piece? tabsFor = null, Player tabPlayer = null, Player glowPlayer = null, ItemDrop.ItemData glowWeapon = null, ItemDrop.ItemData shineWeapon = null)
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

            bool presets = s_presetsMode && tabPlayer != null && tabsFor.HasValue;
            if (!presets) s_presetsMode = false;
            if (presets)
            {
                dye = null;
                glowPlayer = null;
                glowWeapon = null;
                shineWeapon = null;
            }
            bool dyeable = dye != null;
            bool tabs = !presets && tabsFor.HasValue && tabPlayer != null;
            s_builtDye = null; // set again below when this menu has a COLOR section
            int cells = looks.Count + 1; // the "Hide" cell first, then every unlocked look
            int rows = presets ? Mathf.CeilToInt(LookPresets.Count * PresetRow / Cell) : Mathf.Max(1, Mathf.CeilToInt((maxLooks + 1) / (float)Columns));
            float ox = Pad + PreviewWidth;               // left edge of the right-hand column
            float columnWidth = Pad * 2f + Columns * Cell;
            float inner = columnWidth - Pad * 2f;
            float cx = ox + columnWidth / 2f;
            float width = ox + columnWidth;

            float tabsY = Pad + 50f;
            float ownLookY = tabs ? tabsY + 40f : Pad + 50f;
            float gridTop = ownLookY + 24f;
            float height = gridTop + rows * Cell + Pad;
            float colorBlock = 0f;
            if (dyeable)
            {
                colorBlock = 10f + ColorHeader + SwatchRow + 3 * ColorRow + 40f + ((dye.Zones()?.Count ?? 1) > 1 ? ZoneRow : 0f);
                height += colorBlock;
            }
            bool glowable = glowPlayer != null && glowWeapon != null;
            bool shinable = glowPlayer != null && shineWeapon != null;
            float effectBlock = 10f + ColorHeader + SwatchRow + 40f; // GLOW and SHINE are one block each
            if (glowable)
            {
                height += effectBlock;
            }
            if (shinable)
            {
                height += effectBlock;
            }
            height = Mathf.Max(height, MinHeight);
            s_menu.sizeDelta = new Vector2(width, height);

            // back arrow (discard) top left, title in the middle
            AddButton(gui, "< BACK", Pad + 50f, Pad + 14f, 100f, false, () => Close());
            TMP_Text header = UiKit.Text(s_content, gui.m_craftingStationName, presets ? "LOOK PRESETS" : title, 22f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(header.rectTransform, width / 2f, Pad + 14f, width - 300f, 30f);

            // left: live 3D view of your character, SAVE below it
            float previewTop = Pad + 40f;
            float previewBottom = height - Pad - 48f;
            float previewX = Pad + (PreviewWidth - Pad) / 2f;
            CharacterPreview.CreateView(s_content, previewX, (previewTop + previewBottom) / 2f,
                PreviewWidth - Pad, previewBottom - previewTop);
            AddButton(gui, "SAVE", previewX, height - Pad - 18f, 200f, false, () => Close(save: true));

            // right: one tab per piece, so all of them can be changed without leaving the menu
            if (tabs)
            {
                float tabWidth = inner / s_pieces.Length;
                for (int i = 0; i < s_pieces.Length; i++)
                {
                    Transmog.Piece piece = s_pieces[i];
                    AddButton(gui, PieceLabel(piece), ox + Pad + tabWidth / 2f + i * tabWidth, tabsY, tabWidth - 6f,
                        piece == tabsFor.Value, () => OpenForPiece(tabPlayer, piece));
                }
            }

            if (tabPlayer != null && tabsFor.HasValue)
            {
                Player presetPlayer = tabPlayer;
                Transmog.Piece presetPiece = tabsFor.Value;
                AddButton(gui, presets ? "LOOKS" : "PRESETS", width - Pad - 55f, Pad + 14f, 110f, false, () =>
                {
                    s_presetsMode = !presets;
                    OpenForPiece(presetPlayer, presetPiece);
                });
            }
            if (presets)
            {
                BuildPresets(gui, tabPlayer, tabsFor.Value, ox, gridTop - 10f, inner);
            }
            else
            {
                AddButton(gui, "OWN LOOK", cx, ownLookY, inner, current == null, () => choose(null));
            }
            for (int i = 0; i < (presets ? 0 : cells); i++)
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

            float sectionTop = gridTop + rows * Cell + 10f;
            if (dyeable)
            {
                AddColorSection(gui, dye, ox, columnWidth, sectionTop);
                s_builtDye = dye;
                s_builtZoneId = dye.Zones()?.Id ?? 0;
                // a weapon menu redraws itself like it was opened; a piece menu for the shown piece
                s_builtReopen = tabs ? (Action)(() => OpenForPiece(tabPlayer, tabsFor.Value)) : (() => OpenForWeapon(glowPlayer ?? Player.m_localPlayer, glowWeapon ?? s_openWeapon));
                sectionTop += colorBlock;
            }
            if (glowable)
            {
                AddGlowSection(gui, glowPlayer, glowWeapon, ox, columnWidth, sectionTop);
                sectionTop += effectBlock;
            }
            if (shinable)
            {
                AddGlowSection(gui, glowPlayer, shineWeapon, ox, columnWidth, sectionTop, shine: true);
            }

            // centred on the screen; scaled down when the screen is too small for it
            Rect area = s_blocker.rect;
            float scale = Mathf.Min(1f, (area.width - 40f) / width, (area.height - 40f) / height);
            s_menu.localScale = Vector3.one * Mathf.Max(0.4f, scale);
            s_menu.anchoredPosition = Vector2.zero;
            s_blocker.SetAsLastSibling();
            InventoryHider.Hide(s_blocker); // the other windows go while the menu is open
            s_blocker.gameObject.SetActive(true);
            CharacterPreview.Begin();
        }

        // ------------------------------------------------------------------ look presets

        private const float PresetRow = 56f;
        private static bool s_presetsMode;

        /// <summary>
        /// The preset list: per row the five looks as little icons, LOAD (puts the preset on the active set), SAVE (stores the
        /// current looks and colours there) and X. Weapon looks and colours are not part of a preset.
        /// </summary>
        private static void BuildPresets(InventoryGui gui, Player player, Transmog.Piece piece, float ox, float top, float inner)
        {
            float left = ox + Pad;
            for (int i = 0; i < LookPresets.Count; i++)
            {
                int index = i;
                float y = top + PresetRow / 2f + i * PresetRow;
                bool has = LookPresets.Has(player, index);
                AddNameField(gui, left + 10f + 105f, y - 18f, 210f, 20f, LookPresets.NameOf(player, index), has,
                    value => LookPresets.SetName(player, index, value));
                string[] looks = LookPresets.LooksOf(player, index, s_pieces);
                for (int k = 0; k < s_pieces.Length; k++)
                {
                    float x = left + 22f + k * 36f;
                    RectTransform cell = UiKit.Rect("PresetLook", s_content);
                    UiKit.Place(cell, x, y + 9f, 34f, 34f);
                    Image back = cell.gameObject.AddComponent<Image>();
                    back.color = new Color(0f, 0f, 0f, has ? 0.35f : 0.15f);
                    back.raycastTarget = false;
                    if (!has) continue;
                    if (looks[k] == Transmog.Hidden)
                    {
                        TMP_Text dash = UiKit.Text(s_content, gui.m_craftingStationName, "–", 18f, UiKit.Dim, TextAlignmentOptions.Center);
                        UiKit.Place(dash.rectTransform, x, y + 9f, 34f, 34f);
                    }
                    else if (looks[k] != null)
                    {
                        GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(looks[k]) : null;
                        ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                        if (drop != null)
                        {
                            RectTransform iconRect = UiKit.Rect("PresetIcon", s_content);
                            UiKit.Place(iconRect, x, y + 9f, 32f, 32f);
                            Image icon = iconRect.gameObject.AddComponent<Image>();
                            icon.sprite = drop.m_itemData.GetIcon();
                            icon.preserveAspect = true;
                            icon.raycastTarget = false;
                        }
                    }
                }
                if (has && LookPresets.HasColors(player, index))
                {
                    TMP_Text colored = UiKit.Text(s_content, gui.m_recipeDecription != null ? gui.m_recipeDecription : gui.m_craftingStationName, "+ colours", 11f, UiKit.Dim, TextAlignmentOptions.Left);
                    UiKit.Place(colored.rectTransform, left + 22f + 5 * 36f + 8f, y + 9f, 190f, 14f);
                }
                AddButton(gui, "LOAD", left + 270f, y, 70f, !has, () => { LookPresets.Apply(player, index); OpenForPiece(player, piece); });
                AddButton(gui, "SAVE", left + 346f, y, 70f, false, () => { LookPresets.Save(player, index); OpenForPiece(player, piece); });
                if (has)
                {
                    AddButton(gui, "X", left + 410f, y, 36f, false, () => { LookPresets.Clear(player, index); OpenForPiece(player, piece); });
                }
            }
        }

        /// <summary>A one-line text box (name of a preset): click, type, Enter or click elsewhere keeps it.</summary>
        private static void AddNameField(InventoryGui gui, float x, float y, float width, float height, string value, bool editable, Action<string> commit)
        {
            RectTransform rect = UiKit.Rect("PresetName", s_content);
            UiKit.Place(rect, x, y, width, height);
            Image back = rect.gameObject.AddComponent<Image>();
            back.color = new Color(0f, 0f, 0f, editable ? 0.35f : 0.15f);
            RectTransform viewport = UiKit.Rect("Viewport", rect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(6f, 0f);
            viewport.offsetMax = new Vector2(-6f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            TMP_Text text = UiKit.Text(viewport, gui.m_craftingStationName, value, 15f, editable ? UiKit.Gold : UiKit.Dim, TextAlignmentOptions.Left);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            if (!editable)
            {
                return; // an empty preset has no name to edit
            }
            // the input field reads its parts in Awake: switched off while they are put in
            rect.gameObject.SetActive(false);
            TMP_InputField input = rect.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = text;
            input.targetGraphic = back;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 18;
            input.richText = false;
            input.text = value;
            input.onSelect.AddListener(_ => TypingGuard.Begin());
            input.onDeselect.AddListener(_ => TypingGuard.End());
            input.onEndEdit.AddListener(v => { TypingGuard.End(); commit(v); });
            rect.gameObject.SetActive(true);
        }

        // the weapon of the open weapon menu (for redrawing it when the zones change)
        private static ItemDrop.ItemData s_openWeapon;

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
        private static object s_zoneKey;

        /// <summary>
        /// COLOR for a piece of the active set or a weapon. A target with several colour zones (metal, leather, cloth ...)
        /// first offers a chip per zone, ringed by its original colour, with its new colour in the middle;
        /// ALL colours every zone the same. Then swatches that turn the zone's colour into the chosen one,
        /// Black / White / Grey, the fine controls (Color, Intensity, Brightness) and a small RESET.
        /// Every click recolours the character at once; SAVE keeps it.
        /// </summary>
        private static void AddColorSection(InventoryGui gui, DyeTarget target, float ox, float columnWidth, float top)
        {
            float cx = ox + columnWidth / 2f;
            float inner = columnWidth - Pad * 2f;
            Image braid = UiKit.Named(gui.m_crafting, "BraidLineHorisontalMedium");
            UiKit.Divider(s_content, braid, cx, top, inner);
            TMP_Text title = UiKit.Text(s_content, gui.m_craftingStationName, "COLOR", 18f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, cx, top + 18f, inner, 24f);

            ZoneSet zones = target.Zones();
            int zoneCount = zones != null ? zones.Count : 1;
            if (!Equals(s_zoneKey, target.Key) || s_zone >= zoneCount)
            {
                s_zone = -1; // a new piece (or a look with fewer zones): back to the whole piece
                s_zoneKey = target.Key;
            }
            bool zoned = zoneCount > 1;
            float belowZones = top + ColorHeader + (zoned ? ZoneRow : 0f);

            Image[] chipCentres = new Image[zoneCount];
            Button[] chipButtons = new Button[zoneCount + 1]; // [0] = ALL
            DyeValue Current() { DyeValue[] all = target.Get(); return s_zone < 0 ? all[0] : all[Mathf.Min(s_zone, all.Length - 1)]; }

            // the colour the selected zone (or the whole piece) shows right now
            Color Displayed()
            {
                DyeValue d = Current();
                if (d.IsTarget) return d.TargetColor;
                if (zones != null && s_zone >= 0 && s_zone < zones.Count) return TextureDye.ShiftColor(zones.Chips[s_zone], d);
                if (zones != null && zones.Count == 1) return TextureDye.ShiftColor(zones.Chips[0], d);
                Texture texture = target.BaseTexture();
                Vector3 dom = texture != null ? TextureDye.Dominant(texture) : new Vector3(0f, 0.5f, 0.5f);
                return TextureDye.ShiftColor(Color.HSVToRGB(dom.x, dom.y, dom.z), d);
            }

            Slider[] sliders = new Slider[3];
            TMP_Text hexText = null;
            Image preview = null;
            TMP_InputField hexInput = null;
            void Refresh(bool moveSliders = true)
            {
                Color shown = Displayed();
                Color.RGBToHSV(shown, out float h, out float s, out float v);
                if (s < 0.02f && s_lastHue >= 0f) h = s_lastHue; // a grey keeps the hue the slider had
                if (moveSliders)
                {
                    sliders[0].SetValueWithoutNotify(h * 360f);
                    sliders[1].SetValueWithoutNotify(s * 100f);
                    sliders[2].SetValueWithoutNotify(v * 100f);
                }
                if (preview != null) preview.color = shown;
                if (hexInput != null && !hexInput.isFocused) hexInput.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(shown));
                if (zoned)
                {
                    DyeValue[] all = target.Get();
                    for (int k = 0; k < zoneCount; k++)
                    {
                        DyeValue shownDye = all.Length == 1 ? all[0] : (k < all.Length ? all[k] : default);
                        chipCentres[k].color = TextureDye.ShiftColor(zones.Chips[k], shownDye);
                    }
                    for (int k = 0; k <= zoneCount; k++)
                    {
                        chipButtons[k].interactable = k - 1 != s_zone; // the chosen one looks pressed
                    }
                }
            }
            // a chosen colour is applied at once; sliders send many values while dragged, so those are thinned out
            void SetDye(DyeValue d)
            {
                FlushPending();
                ApplyDye(target, d, zoned, zoneCount);
                Refresh();
            }
            void SetFromSliders()
            {
                float h = sliders[0].value / 360f, s = sliders[1].value / 100f, v = sliders[2].value / 100f;
                s_lastHue = h;
                DyeValue d = DyeValue.Target(Color.HSVToRGB(h, s, v));
                QueueDye(() => { ApplyDye(target, d, zoned, zoneCount); Refresh(false); });
                if (preview != null) preview.color = Color.HSVToRGB(h, s, v);
                if (hexInput != null) hexInput.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(h, s, v)));
            }

            // zone chips: ALL, then one per colour zone (ring = original colour, middle = colour now)
            if (zoned)
            {
                float chipSize = 34f, gap = 12f;
                float rowWidth = (zoneCount + 1) * chipSize + zoneCount * gap;
                float x0 = cx - rowWidth / 2f + chipSize / 2f;
                float chipY = top + ColorHeader + ZoneRow / 2f - 4f;
                chipButtons[0] = AddChip(gui, "ALL", x0, chipY, chipSize, null, out Image _, () => { FlushPending(); s_zone = -1; Refresh(); target.Flash(-1, zoneCount); });
                for (int k = 0; k < zoneCount; k++)
                {
                    int zone = k;
                    float x = x0 + (k + 1) * (chipSize + gap);
                    chipButtons[k + 1] = AddChip(gui, "Zone " + (k + 1), x, chipY, chipSize, zones.Chips[k], out chipCentres[k], () => { FlushPending(); s_zone = zone; Refresh(); target.Flash(zone, zoneCount); });
                }
            }

            // swatches: 8 colours + black, white, grey (each one a target colour, the model's shading stays)
            int count = s_hues.Length + 3;
            float step = inner / count;
            float size = Mathf.Min(step - 6f, 34f);
            float swatchY = belowZones + SwatchRow / 2f;
            for (int i = 0; i < count; i++)
            {
                float x = ox + Pad + step / 2f + i * step;
                Color color;
                string name;
                if (i < s_hues.Length)
                {
                    color = Color.HSVToRGB(s_hues[i].hue, 0.75f, 0.85f);
                    name = s_hues[i].name;
                }
                else if (i == s_hues.Length)
                {
                    color = new Color(0.08f, 0.08f, 0.08f);
                    name = "Black";
                }
                else if (i == s_hues.Length + 1)
                {
                    color = new Color(0.92f, 0.92f, 0.9f);
                    name = "White";
                }
                else
                {
                    color = new Color(0.5f, 0.5f, 0.5f);
                    name = "Grey";
                }
                Color chosen = color;
                AddSwatch(x, swatchY, size, color, name, () => { s_lastHue = -1f; SetDye(DyeValue.Target(chosen)); });
            }

            // the picker: hue, saturation and brightness of the colour the zone gets
            string[] names = { "Hue", "Saturation", "Brightness" };
            float[] maxima = { 360f, 100f, 100f };
            float rowsTop = belowZones + SwatchRow;
            for (int row = 0; row < 3; row++)
            {
                float y = rowsTop + ColorRow / 2f + row * ColorRow;
                TMP_Text label = UiKit.Text(s_content, gui.m_recipeDecription, names[row], 16f, UiKit.Parchment, TextAlignmentOptions.Left);
                UiKit.Place(label.rectTransform, ox + Pad + 62f, y, 120f, 24f);
                sliders[row] = AddPickerSlider(ox + Pad + 120f + (inner - 120f) / 2f, y, inner - 130f, maxima[row], row == 0, SetFromSliders);
            }
            float lastRow = rowsTop + 3 * ColorRow + 20f;
            RectTransform previewRect = UiKit.Rect("ColorPreview", s_content);
            UiKit.Place(previewRect, ox + Pad + 22f, lastRow, 34f, 26f);
            preview = previewRect.gameObject.AddComponent<Image>();
            preview.raycastTarget = false;
            Outline previewOutline = previewRect.gameObject.AddComponent<Outline>();
            previewOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            previewOutline.effectDistance = new Vector2(1.5f, -1.5f);
            hexInput = AddHexField(gui, ox + Pad + 118f, lastRow, 140f, 26f, value =>
            {
                if (TryParseHex(value, out Color parsed))
                {
                    s_lastHue = -1f;
                    SetDye(DyeValue.Target(parsed));
                }
                else
                {
                    Refresh();
                }
            });
            AddButton(gui, "RESET", ox + columnWidth - Pad - 60f, lastRow, 120f, false, () =>
            {
                FlushPending();
                if (zoned && s_zone < 0) target.SetAll(default); // ALL: every zone back to its own colours
                else ApplyDye(target, default, zoned, zoneCount);
                Refresh();
            });
            Refresh();
        }

        private static float s_lastHue = -1f;

        private static void ApplyDye(DyeTarget target, DyeValue d, bool zoned, int zoneCount)
        {
            ZoneFlash.Stop(); // the blinking shows the old colours: a new colour ends it first
            if (s_zone < 0 || !zoned) target.SetAll(d);
            else target.SetZone(s_zone, zoneCount, d);
        }

        // ---- thinning out slider changes: at most one recolouring every 0.2 s while dragging, the last value always lands
        private static Action s_pending;
        private static float s_pendingSince;
        private static float s_lastApplied;

        private static void QueueDye(Action apply)
        {
            if (s_pending == null) s_pendingSince = Time.unscaledTime;
            s_pending = apply;
        }

        private static void FlushPending()
        {
            Action pending = s_pending;
            s_pending = null;
            if (pending != null)
            {
                s_lastApplied = Time.unscaledTime;
                pending();
            }
        }

        private static void TickPending()
        {
            if (s_pending != null && Time.unscaledTime - s_lastApplied >= 0.2f)
            {
                FlushPending();
            }
        }

        private static bool TryParseHex(string text, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            if (!t.StartsWith("#")) t = "#" + t;
            return ColorUtility.TryParseHtmlString(t, out color);
        }

        private static Texture2D s_hueTexture;

        /// <summary>A slider for the colour picker: dark track (the hue one shows the rainbow), gold handle.</summary>
        private static Slider AddPickerSlider(float x, float y, float width, float max, bool rainbow, Action changed)
        {
            GameObject go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = "PickerSlider";
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(s_content, false);
            UiKit.Place(rect, x, y, width, 18f);
            foreach (Image image in go.GetComponentsInChildren<Image>(true))
            {
                switch (image.gameObject.name)
                {
                    case "Background":
                        image.color = new Color(0f, 0f, 0f, 0.55f);
                        if (rainbow)
                        {
                            if (s_hueTexture == null)
                            {
                                s_hueTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                                for (int i = 0; i < 256; i++) s_hueTexture.SetPixel(i, 0, Color.HSVToRGB(i / 255f, 0.85f, 0.9f));
                                s_hueTexture.Apply();
                            }
                            image.sprite = Sprite.Create(s_hueTexture, new Rect(0, 0, 256, 1), new Vector2(0.5f, 0.5f));
                            image.type = Image.Type.Simple;
                            image.color = Color.white;
                        }
                        break;
                    case "Fill": image.color = rainbow ? new Color(0f, 0f, 0f, 0f) : new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.55f); break;
                    case "Handle": image.color = UiKit.Gold; break;
                }
            }
            Slider slider = go.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(_ => changed());
            return slider;
        }

        /// <summary>The hex field "#RRGGBB" of the picker.</summary>
        private static TMP_InputField AddHexField(InventoryGui gui, float x, float y, float width, float height, Action<string> commit)
        {
            RectTransform rect = UiKit.Rect("HexField", s_content);
            UiKit.Place(rect, x, y, width, height);
            Image back = rect.gameObject.AddComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.4f);
            RectTransform viewport = UiKit.Rect("Viewport", rect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(8f, 0f);
            viewport.offsetMax = new Vector2(-8f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            TMP_Text text = UiKit.Text(viewport, gui.m_craftingStationName, "", 16f, UiKit.Gold, TextAlignmentOptions.Center);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            rect.gameObject.SetActive(false);
            TMP_InputField input = rect.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = text;
            input.targetGraphic = back;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 7;
            input.richText = false;
            input.onSelect.AddListener(_ => TypingGuard.Begin());
            input.onDeselect.AddListener(_ => TypingGuard.End());
            input.onEndEdit.AddListener(v => { TypingGuard.End(); commit(v); });
            rect.gameObject.SetActive(true);
            return input;
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
        private static void AddGlowSection(InventoryGui gui, Player player, ItemDrop.ItemData weapon, float ox, float columnWidth, float top, bool shine = false)
        {
            float cx = ox + columnWidth / 2f;
            float inner = columnWidth - Pad * 2f;
            Image braid = UiKit.Named(gui.m_crafting, "BraidLineHorisontalMedium");
            UiKit.Divider(s_content, braid, cx, top, inner);
            TMP_Text title = UiKit.Text(s_content, gui.m_craftingStationName, shine ? "SHINE" : "GLOW", 18f, UiKit.Gold, TextAlignmentOptions.Center);
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
                        () => SetEffect(player, weapon, shine, new GlowValue { IsSet = true, Hue = hue }));
                }
                else
                {
                    AddSwatch(x, swatchY, size, new Color(0.95f, 0.95f, 0.95f), "White",
                        () => SetEffect(player, weapon, shine, new GlowValue { IsSet = true, White = true }));
                }
            }
            AddButton(gui, "RESET", ox + columnWidth - Pad - 60f, top + ColorHeader + SwatchRow + 20f, 120f, false,
                () => SetEffect(player, weapon, shine, default));
        }

        private static void SetEffect(Player player, ItemDrop.ItemData weapon, bool shine, GlowValue value)
        {
            if (shine) Glow.SetShine(player, weapon, value);
            else Glow.Set(player, weapon, value);
        }

        private static void Nudge(ref DyeValue d, int channel, int delta)
        {
            if (channel == 0) d.Hue += delta;
            else if (channel == 1) d.Saturation += delta;
            else d.Brightness += delta;
        }

        /// <summary>The shift that turns the piece's dominant colour into the target hue (greyish pieces get some colour added).</summary>
        private static DyeValue TowardsHue(DyeTarget target, ZoneSet zones, int zone, float targetHue)
        {
            float hue, saturation;
            if (zones != null && zone >= 0 && zone < zones.Count)
            {
                // one zone: its own average colour
                Color.RGBToHSV(zones.Chips[zone], out hue, out saturation, out float _);
            }
            else
            {
                Texture texture = target.BaseTexture();
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
