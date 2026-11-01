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
        private const int Columns = 9;
        private const int LookColumns = 6;         // the look grid: big cells with the name under each
        private const float LookCell = 96f;
        private const float LookRow = LookCell;
        private const float Cell = 64f;
        private const float Pad = 20f;
        private const float ListWidth = 0f;        // no piece list on the left any more: the pieces sit in a row under the search bar
        private const float RightWidth = 488f;     // preview and COLOR on the right
        private const float PreviewHeight = 440f;
        private const float EffectsBlock = 126f;     // GLOW / SHINE under COLOR
        private const float ColorRow = 34f;
        private const float ColorHeader = 30f;
        private const float SwatchRow = 44f;
        private const float MinHeight = 880f; // one size for every tab, also with the GLOW and SHINE tabs

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

        // what the open menu already can undo: the looks and colours of the set, and every weapon looked at since it opened
        private static bool s_setCaptured;
        private static readonly HashSet<ItemDrop.ItemData> s_weaponsCaptured = new HashSet<ItemDrop.ItemData>();

        private static void BeginSession()
        {
            if (IsOpen) return;
            s_revert = null;
            s_setCaptured = false;
            s_weaponsCaptured.Clear();
        }

        private static int s_colourTab;
        private const float TabsExtra = 16f;   // the colour box is a little taller when it carries the COLOR / GLOW / SHINE tabs

        // search text and sort of the look grid (they stay while you switch pieces)
        private static string s_search = "";
        private static bool s_sortByPower;

        internal static void OpenForPiece(Player player, Transmog.Piece piece)
        {
            BeginSession();
            if (!s_setCaptured)
            {
                s_setCaptured = true;
                int set = GearholdState.ActiveSet(player);
                string looks0 = Transmog.ReadSetLooks(player, set);
                string dyes0 = Dye.ReadSet(player, set);
                s_revert += () =>
                {
                    Transmog.WriteSetLooks(player, set, looks0);
                    Dye.WriteSet(player, set, dyes0);
                };
            }
            string current = Transmog.GetLook(player, piece);
            List<GameObject> all = Transmog.LooksFor(player, Transmog.TypeOf(piece), null);
            // the window keeps one size for all pieces, so switching tabs does not make it jump
            int maxLooks = 0;
            foreach (Transmog.Piece p in s_pieces)
            {
                maxLooks = Mathf.Max(maxLooks, p == piece ? all.Count : Transmog.LooksFor(player, Transmog.TypeOf(p), null).Count);
            }
            Open("LOOKS & COLORS", current, Arrange(all), maxLooks, look =>
            {
                Transmog.SetLook(player, piece, look);
                OpenForPiece(player, piece); // redraw: the chosen look gets its mark, the menu stays open
            }, PieceTarget(player, piece), piece, player, original: MordasGearhold.Patches.Equipment.ItemIn(player, SlotOf(piece)));
        }

        internal static void OpenForWeapon(Player player, ItemDrop.ItemData weapon)
        {
            BeginSession();
            s_openWeapon = weapon;
            if (s_weaponsCaptured.Add(weapon))
            {
                string look0 = Transmog.GetItemLook(weapon);
                string dye0 = Dye.ReadItem(weapon);
                GlowValue glow0 = Glow.Get(weapon);
                GlowValue shine0 = Glow.GetShine(weapon);
                s_revert += () =>
                {
                    Transmog.SetItemLook(player, weapon, look0);
                    Glow.Set(player, weapon, glow0);
                    Glow.SetShine(player, weapon, shine0);
                    Dye.WriteItem(player, weapon, dye0);
                };
            }
            string current = Transmog.GetItemLook(weapon);
            List<GameObject> all = Transmog.LooksFor(player, weapon.m_shared.m_itemType, weapon.m_shared.m_skillType);
            string name = Localization.instance.Localize(weapon.m_shared.m_name).ToUpperInvariant();
            Open(name + " LOOK", current, Arrange(all), all.Count, look =>
            {
                Transmog.SetItemLook(player, weapon, look);
                OpenForWeapon(player, weapon);
            }, WeaponTarget(player, weapon), null, player, glowPlayer: player, glowWeapon: Glow.CanGlow(weapon) ? weapon : null, shineWeapon: Glow.CanShine(weapon) ? weapon : null, original: weapon);
        }

        /// <summary>The WEAPONS tab: the weapon chosen last (or the first one you carry), with a list of all of them.</summary>
        internal static void OpenWeaponsTab(Player player)
        {
            List<ItemDrop.ItemData> owned = OwnedWeapons(player);
            ItemDrop.ItemData pick = s_openWeapon != null && owned.Contains(s_openWeapon) ? s_openWeapon : (owned.Count > 0 ? owned[0] : null);
            if (pick != null)
            {
                EquipForPreview(player, pick);
                OpenForWeapon(player, pick);
                return;
            }
            BeginSession();
            s_openWeapon = null;
            Open("WEAPONS", null, new List<GameObject>(), 0, _ => { }, null, null, player);
        }

        // the weapon in your hand before the menu put another one there to show it (restored when the menu closes)
        private static bool s_handsTaken;
        private static ItemDrop.ItemData s_handBefore;
        private static ItemDrop.ItemData s_handShown;

        /// <summary>Takes the picked weapon in hand, so the character on the right shows it.</summary>
        private static void EquipForPreview(Player player, ItemDrop.ItemData weapon)
        {
            if (player == null || weapon == null || !player.GetInventory().ContainsItem(weapon))
            {
                return;
            }
            if (!s_handsTaken)
            {
                s_handsTaken = true;
                s_handBefore = Access.RightItem(player) ?? Access.LeftItem(player);
            }
            s_handShown = weapon;
            if (!weapon.m_equipped)
            {
                player.EquipItem(weapon, true);
            }
        }

        private static void RestoreHands()
        {
            if (!s_handsTaken)
            {
                return;
            }
            Player player = Player.m_localPlayer;
            ItemDrop.ItemData before = s_handBefore, shown = s_handShown;
            s_handsTaken = false;
            s_handBefore = s_handShown = null;
            if (player == null || shown == null || !shown.m_equipped)
            {
                return; // the player put something else in hand meanwhile
            }
            if (before != null && player.GetInventory().ContainsItem(before))
            {
                player.EquipItem(before, true);
            }
            else
            {
                player.UnequipItem(shown, true);
            }
        }

        /// <summary>Every weapon in your inventory, quickslots and Weapon 2 slot (what you carry), sorted by name.</summary>
        private static List<ItemDrop.ItemData> OwnedWeapons(Player player)
        {
            List<ItemDrop.ItemData> result = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (item != null && Transmog.IsWeapon(item)) result.Add(item);
            }
            result.Sort((a, b) => string.Compare(Localization.instance.Localize(a.m_shared.m_name), Localization.instance.Localize(b.m_shared.m_name), StringComparison.CurrentCultureIgnoreCase));
            return result;
        }

        private static GearSlot SlotOf(Transmog.Piece piece)
        {
            switch (piece)
            {
                case Transmog.Piece.Helmet: return GearSlot.Helmet;
                case Transmog.Piece.Chest: return GearSlot.Chest;
                case Transmog.Piece.Legs: return GearSlot.Legs;
                case Transmog.Piece.Shoulder: return GearSlot.Shoulder;
                default: return GearSlot.Shield;
            }
        }

        private static float PowerOf(GameObject prefab)
        {
            ItemDrop.ItemData.SharedData shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            return shared.m_armor + shared.m_blockPower + shared.m_damages.GetTotalDamage();
        }

        /// <summary>Search text and sort order applied to the looks that fit (the list arrives sorted by name).</summary>
        private static List<GameObject> Arrange(List<GameObject> looks)
        {
            string query = s_search.Trim();
            List<GameObject> result = new List<GameObject>(looks.Count);
            foreach (GameObject prefab in looks)
            {
                string name = Localization.instance.Localize(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
                if (query.Length == 0 || name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) result.Add(prefab);
            }
            if (s_sortByPower)
            {
                result = new List<GameObject>(System.Linq.Enumerable.ThenBy(
                    System.Linq.Enumerable.OrderBy(result, p => PowerOf(p)),
                    p => Localization.instance.Localize(p.GetComponent<ItemDrop>().m_itemData.m_shared.m_name)));
            }
            return result;
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
            RestoreHands();
            if (s_blocker != null)
            {
                s_blocker.gameObject.SetActive(false);
            }
            CharacterPreview.End();
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                InventoryGui.instance.Hide(); // SAVE and BACK end the visit: cursor and camera come back, you can play on
            }
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
            DyeTarget dye = null, Transmog.Piece? tabsFor = null, Player tabPlayer = null, Player glowPlayer = null, ItemDrop.ItemData glowWeapon = null, ItemDrop.ItemData shineWeapon = null, ItemDrop.ItemData original = null)
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
            bool tabs = tabPlayer != null && !presets;          // the piece list on the left (not on the presets page)
            bool weapons = tabs && !tabsFor.HasValue;            // the WEAPONS tab
            bool noWeapon = weapons && dye == null;              // WEAPONS tab and nothing to look at
            List<ItemDrop.ItemData> owned = weapons ? OwnedWeapons(tabPlayer) : null;
            s_builtDye = null; // set again below when this menu has a COLOR section
            int cells = looks.Count + 2; // the original, the "Hide" cell, then every unlocked look
            int rows = presets ? Mathf.CeilToInt(LookPresets.Count * PresetRow / Cell) : Mathf.Max(1, Mathf.CeilToInt((maxLooks + 2) / (float)LookColumns));

            // three columns: the piece list, the looks (toolbar, weapons, grid) and the preview with COLOR
            float centreLeft = Pad + ListWidth + Pad;            // left edge of the look grid
            float ox = centreLeft - Pad;
            float centreWidth = Pad * 2f + Columns * Cell;
            float inner = centreWidth - Pad * 2f;
            float cx = ox + centreWidth / 2f;
            float cardW = inner;
            float cardCx = cx;
            float rightOx = ox + centreWidth;
            float width = rightOx + RightWidth;

            // the centre, from the top: toolbar, the weapons you carry (WEAPONS tab), OWN LOOK, the grid
            float toolbarY = Pad + 56f;
            float flow = presets ? Pad + 50f : toolbarY + 28f;
            float catTop = flow;                                 // the row of piece buttons
            if (tabs) flow += IconButton + 16f + 8f;
            float weaponsTop = flow;
            int weaponRows = 0;
            if (weapons && owned.Count > 0)
            {
                weaponRows = Mathf.CeilToInt(owned.Count / (float)Columns);
                flow += 22f + weaponRows * Cell + 10f;
            }
            float ownLookY = flow + 16f;
            float gridTop = ownLookY + 24f;
            // GLOW and SHINE of a weapon share one box under COLOR (a tab switches between them), so the window keeps its size
            bool glowable = glowPlayer != null && glowWeapon != null;
            bool shinable = glowPlayer != null && shineWeapon != null;
            float rowPitch = presets ? Cell : LookRow;
            float looksBottomY = gridTop + rows * rowPitch + 8f;     // the card "shown now" sits right under the look grid
            float cardTop = looksBottomY + 10f;
            float height = (noWeapon ? gridTop + 60f : cardTop + 72f) + Pad;

            // the right column: preview, then COLOR and the weapon effects
            float previewTop = Pad + 40f;
            float rightBottom = previewTop + PreviewHeight + 10f;
            float colorBlock = 0f;
            if (dyeable)
            {
                colorBlock = 10f + ColorHeader + SwatchRow + 3 * ColorRow + 40f + ((dye.Zones()?.Count ?? 1) > 1 || glowable || shinable ? ZoneRow : 0f) + (glowable || shinable ? TabsExtra : 0f);
                rightBottom += colorBlock;
            }
            // the card "shown now" ends at the height of the colour box
            float colourBottom = dyeable ? previewTop + PreviewHeight + 10f + colorBlock - 1f : 0f;
            float cardBottom = Mathf.Max(cardTop + 72f, colourBottom);
            float cardCy = (cardTop + cardBottom) / 2f;
            float cardH = cardBottom - cardTop;
            rightBottom += 56f; // SAVE
            height = Mathf.Max(height, rightBottom, MinHeight, noWeapon ? 0f : cardBottom + Pad);
            s_menu.sizeDelta = new Vector2(width, height);

            // back arrow (discard) top left, title in the middle
            AddButton(gui, "< BACK", Pad + 50f, Pad + 14f, 100f, false, () => Close());
            TMP_Text header = UiKit.Text(s_content, gui.m_craftingStationName, presets ? "LOOK PRESETS" : title, 22f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(header.rectTransform, width / 2f, Pad + 14f, width - 300f, 30f);

            // the sections sit in the game's own dark boxes (the recipe list look), cut apart by the golden rules of the crafting window
            float rightCx = rightOx + RightWidth / 2f;
            if (!presets)
            {
                AddRule(gui, width / 2f, Pad + 38f, width - Pad * 2f);
                if (tabs) AddBox(gui, cx, catTop + (IconButton + 16f) / 2f, inner + 12f, IconButton + 16f);
                AddBox(gui, rightCx, previewTop + PreviewHeight / 2f, RightWidth - Pad + 8f, PreviewHeight + 6f);
                if (dyeable) AddBox(gui, rightCx, previewTop + PreviewHeight + 10f + colorBlock / 2f, RightWidth - Pad + 8f, colorBlock - 2f);
                if (!noWeapon)
                {
                    if (weapons && owned.Count > 0)
                    {
                        float boxTop = weaponsTop - 6f, boxBottom = weaponsTop + 22f + weaponRows * Cell + 4f;
                        AddBox(gui, cx, (boxTop + boxBottom) / 2f, inner + 12f, boxBottom - boxTop);
                    }
                    float looksTop = ownLookY - 18f;
                    AddBox(gui, cx, (looksTop + looksBottomY) / 2f, inner + 12f, looksBottomY - looksTop);
                    AddBox(gui, cx, cardCy, inner + 12f, cardH);
                }
            }

            // right: live 3D view of your character, SAVE at the bottom
            CharacterPreview.CreateView(s_content, rightCx, previewTop + PreviewHeight / 2f, RightWidth - Pad * 2f, PreviewHeight);
            AddButton(gui, "SAVE", rightOx + RightWidth - Pad - 100f, height - Pad - 18f, 200f, false, () => Close(save: true));

            // left: the pieces, so all of them can be changed without leaving the menu
            Action redraw = null;
            if (tabs)
            {
                // one icon button per piece, side by side under the search bar
                string[] icons = { "helm", "brustpanzer", "legs", "cape", "schild", "waffe" };
                for (int i = 0; i < icons.Length; i++)
                {
                    int index = i;
                    bool selected = i < s_pieces.Length ? (!weapons && s_pieces[i] == tabsFor.Value) : weapons;
                    float bx = centreLeft + (i + 0.5f) * (inner / icons.Length);
                    float by = catTop + 8f + IconButton / 2f;
                    AddIconButton(gui, UiKit.PieceIcon(icons[i]), bx, by, IconButton, selected, () =>
                    {
                        s_presetsMode = false;
                        if (index < s_pieces.Length) OpenForPiece(tabPlayer, s_pieces[index]);
                        else OpenWeaponsTab(tabPlayer);
                    });
                }
                redraw = tabsFor.HasValue ? (Action)(() => OpenForPiece(tabPlayer, tabsFor.Value)) : (() => OpenWeaponsTab(tabPlayer));
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

            // toolbar: search and sort for the look grid
            if (!presets && !noWeapon && redraw != null)
            {
                float searchW = inner - 198f;
                AddNameField(gui, centreLeft + searchW / 2f, toolbarY, searchW, 28f, s_search, true, value =>
                {
                    s_search = (value ?? "").Trim();
                    redraw();
                }, "Search");
                AddButton(gui, s_sortByPower ? "SORT: POWER" : "SORT: NAME", centreLeft + Columns * Cell - 95f, toolbarY, 190f, false, () =>
                {
                    s_sortByPower = !s_sortByPower;
                    redraw();
                });
            }

            // WEAPONS tab: everything you carry, pick one to change its look and colour
            if (weapons)
            {
                TMP_Text weaponsLabel = UiKit.Text(s_content, gui.m_recipeDecription != null ? gui.m_recipeDecription : gui.m_craftingStationName,
                    owned.Count > 0 ? "YOUR WEAPONS" : "No weapons in your inventory.", 16f, UiKit.Gold, TextAlignmentOptions.Center);
                UiKit.Place(weaponsLabel.rectTransform, cx, weaponsTop + 8f, inner, 22f);
                for (int i = 0; i < owned.Count; i++)
                {
                    ItemDrop.ItemData weapon = owned[i];
                    float wx = centreLeft + Cell / 2f + (i % Columns) * Cell;
                    float wy = weaponsTop + 22f + Cell / 2f + (i / Columns) * Cell;
                    AddCell(gui, weapon.GetIcon(), weapon.m_shared.m_name, wx, wy, ReferenceEquals(weapon, s_openWeapon), () => { EquipForPreview(tabPlayer, weapon); OpenForWeapon(tabPlayer, weapon); });
                }
            }

            if (presets)
            {
                BuildPresets(gui, tabPlayer, tabsFor.Value, Pad, gridTop - 10f, rightOx - Pad * 2f);
            }
            else if (!noWeapon)
            {
                // a dark inset behind the look grid, like the inventory grid, with its heading and the number of looks
                TMP_Text heading = UiKit.Text(s_content, gui.m_craftingStationName, "LOOKS  (" + looks.Count + ")", 17f, UiKit.Gold, TextAlignmentOptions.Center);
                UiKit.Place(heading.rectTransform, cx, ownLookY - 4f, inner, 24f);
            }
            for (int i = 0; i < (presets || noWeapon ? 0 : cells); i++)
            {
                float x = cx - LookColumns * LookCell / 2f + LookCell / 2f + (i % LookColumns) * LookCell;
                float y = gridTop + 4f + LookCell / 2f + (i / LookColumns) * LookRow;
                if (i == 0)
                {
                    // the original: the gear itself; picking it takes the chosen look off again
                    AddCell(gui, original?.GetIcon(), original != null ? original.m_shared.m_name : "Original", x, y, current == null, () => { ResetColours(dye, tabPlayer, tabsFor.HasValue ? null : original); choose(null); }, noSlash: true, size: LookCell);
                    AddCaption(gui, current == null ? "Original" : "Reset", x, y);
                    continue;
                }
                if (i == 1)
                {
                    AddCell(gui, null, "Hide", x, y, current == Transmog.Hidden, () => choose(Transmog.Hidden), size: LookCell);
                    AddCaption(gui, "Hide", x, y);
                    continue;
                }
                GameObject prefab = looks[i - 2];
                ItemDrop.ItemData data = prefab.GetComponent<ItemDrop>().m_itemData;
                AddCell(gui, data.GetIcon(), data.m_shared.m_name, x, y, prefab.name == current, () => choose(prefab.name), size: LookCell);
            }

            // the card at the bottom: what the piece looks like now
            if (!presets && !noWeapon)
            {
                ItemDrop.ItemData shown = original;
                string shownName = original != null ? Localization.instance.Localize(original.m_shared.m_name) : "Nothing worn";
                if (current == Transmog.Hidden)
                {
                    shown = null;
                    shownName = "Hidden";
                }
                else if (current != null && ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(current) != null)
                {
                    shown = ObjectDB.instance.GetItemPrefab(current).GetComponent<ItemDrop>().m_itemData;
                    shownName = Localization.instance.Localize(shown.m_shared.m_name);
                }
                float cardY = cardCy;
                float iconSize = Mathf.Min(cardH - 36f, 96f);
                TMP_Text cardTitle = UiKit.Text(s_content, gui.m_recipeDecription != null ? gui.m_recipeDecription : gui.m_craftingStationName, "SHOWN NOW", 16f, UiKit.Gold, TextAlignmentOptions.Center);
                UiKit.Place(cardTitle.rectTransform, cardCx, cardTop + 18f, cardW, 20f);
                if (shown != null && shown.GetIcon() != null)
                {
                    RectTransform iconRect = UiKit.Rect("CardIcon", s_content);
                    UiKit.Place(iconRect, centreLeft + 24f + iconSize / 2f, cardY + 12f, iconSize, iconSize);
                    Image icon = iconRect.gameObject.AddComponent<Image>();
                    icon.sprite = shown.GetIcon();
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                }
                TMP_Text cardName = UiKit.Text(s_content, gui.m_craftingStationName, shownName, 26f, UiKit.Gold, TextAlignmentOptions.Center);
                UiKit.Place(cardName.rectTransform, cardCx, cardY + 12f, cardW - 2f * (iconSize + 40f), 34f);
            }

            if (noWeapon)
            {
                TMP_Text none = UiKit.Text(s_content, gui.m_recipeDecription != null ? gui.m_recipeDecription : gui.m_craftingStationName,
                    "Carry a weapon to change its look and colour.", 16f, UiKit.Dim, TextAlignmentOptions.Center);
                UiKit.Place(none.rectTransform, cx, gridTop + 20f, inner, 28f);
            }

            float sectionTop = previewTop + PreviewHeight + 10f;
            if (dyeable)
            {
                // COLOR, and for a weapon with effects GLOW and SHINE too: the tabs switch what the one box below them colours
                bool tabbed = (glowable || shinable) && redraw != null;
                int tab = s_colourTab == 1 && !glowable ? 0 : s_colourTab == 2 && !shinable ? 0 : s_colourTab;
                DyeTarget colourTarget = !tabbed || tab == 0 ? dye : tab == 1 ? EffectTarget(glowPlayer, glowWeapon, false) : EffectTarget(glowPlayer, shineWeapon, true);
                if (tabbed)
                {
                    List<(string label, int index)> names = new List<(string, int)> { ("COLOR", 0) };
                    if (glowable) names.Add(("GLOW", 1));
                    if (shinable) names.Add(("SHINE", 2));
                    float tabWidth = 112f, gap = 8f;
                    float firstX = rightOx + RightWidth / 2f - (names.Count * tabWidth + (names.Count - 1) * gap) / 2f + tabWidth / 2f;
                    for (int t = 0; t < names.Count; t++)
                    {
                        int index = names[t].index;
                        AddButton(gui, names[t].label, firstX + t * (tabWidth + gap), sectionTop + 19f, tabWidth, tab == index, () => { s_colourTab = index; redraw(); });
                    }
                }
                AddColorSection(gui, colourTarget, rightOx, RightWidth, sectionTop, title: !tabbed);
                s_builtDye = colourTarget;
                s_builtZoneId = colourTarget.Zones()?.Id ?? 0;
                // a weapon menu redraws itself like it was opened; a piece menu for the shown piece
                s_builtReopen = redraw ?? (() => OpenForWeapon(glowPlayer ?? Player.m_localPlayer, glowWeapon ?? s_openWeapon));
                sectionTop += colorBlock;
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

        private const float PresetRow = 84f;
        private static bool s_presetsMode;

        /// <summary>
        /// The preset list, one band per preset: its name (a text box: click it to rename), the five looks as icons, and
        /// LOAD (puts the preset on the active set), SAVE (stores the current looks and colours there) and X.
        /// Weapon looks and colours are not part of a preset.
        /// </summary>
        private static void BuildPresets(InventoryGui gui, Player player, Transmog.Piece piece, float left, float top, float width)
        {
            TMP_Text hint = UiKit.Text(s_content, gui.m_recipeDecription != null ? gui.m_recipeDecription : gui.m_craftingStationName,
                "Click a name to rename it. LOAD puts a preset on, SAVE stores what you wear now.", 15f, UiKit.Dim, TextAlignmentOptions.Center);
            UiKit.Place(hint.rectTransform, left + width / 2f, top - 18f, width, 22f);
            float right = left + width - 14f;
            for (int i = 0; i < LookPresets.Count; i++)
            {
                int index = i;
                float y = top + PresetRow / 2f + i * PresetRow;
                bool has = LookPresets.Has(player, index);
                RectTransform band = UiKit.Rect("PresetBand", s_content);
                UiKit.Place(band, left + width / 2f, y, width, PresetRow - 8f);
                Image bandImage = band.gameObject.AddComponent<Image>();
                bandImage.color = new Color(0f, 0f, 0f, has ? 0.3f : 0.16f);
                bandImage.raycastTarget = false;

                AddNameField(gui, left + 16f + 130f, y - 22f, 260f, 30f, LookPresets.NameOf(player, index), has,
                    value => LookPresets.SetName(player, index, value));
                string[] looks = LookPresets.LooksOf(player, index, s_pieces);
                for (int k = 0; k < s_pieces.Length; k++)
                {
                    float x = left + 16f + 22f + k * 50f;
                    float iy = y + 16f;
                    RectTransform cell = UiKit.Rect("PresetLook", s_content);
                    UiKit.Place(cell, x, iy, 46f, 46f);
                    Image back = cell.gameObject.AddComponent<Image>();
                    back.color = new Color(0f, 0f, 0f, has ? 0.4f : 0.18f);
                    back.raycastTarget = false;
                    if (!has) continue;
                    if (looks[k] == Transmog.Hidden)
                    {
                        TMP_Text dash = UiKit.Text(s_content, gui.m_craftingStationName, "–", 22f, UiKit.Dim, TextAlignmentOptions.Center);
                        UiKit.Place(dash.rectTransform, x, iy, 46f, 46f);
                    }
                    else if (looks[k] != null)
                    {
                        GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(looks[k]) : null;
                        ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                        if (drop != null)
                        {
                            RectTransform iconRect = UiKit.Rect("PresetIcon", s_content);
                            UiKit.Place(iconRect, x, iy, 44f, 44f);
                            Image icon = iconRect.gameObject.AddComponent<Image>();
                            icon.sprite = drop.m_itemData.GetIcon();
                            icon.preserveAspect = true;
                            icon.raycastTarget = false;
                        }
                    }
                }
                if (has && LookPresets.HasColors(player, index))
                {
                    TMP_Text colored = UiKit.Text(s_content, gui.m_recipeDecription != null ? gui.m_recipeDecription : gui.m_craftingStationName, "+ colours", 13f, UiKit.Dim, TextAlignmentOptions.Left);
                    UiKit.Place(colored.rectTransform, left + 16f + 22f + 5 * 50f + 40f, y + 16f, 120f, 18f);
                }
                AddButton(gui, "LOAD", right - 44f - 8f - 100f - 8f - 50f, y, 100f, !has, () => { LookPresets.Apply(player, index); OpenForPiece(player, piece); });
                AddButton(gui, "SAVE", right - 44f - 8f - 50f, y, 100f, false, () => { LookPresets.Save(player, index); OpenForPiece(player, piece); });
                if (has)
                {
                    AddButton(gui, "X", right - 22f, y, 44f, false, () => { LookPresets.Clear(player, index); OpenForPiece(player, piece); });
                }
            }
        }

        /// <summary>A one-line text box (name of a preset): click, type, Enter or click elsewhere keeps it.</summary>
        private static void AddNameField(InventoryGui gui, float x, float y, float width, float height, string value, bool editable, Action<string> commit, string placeholder = null)
        {
            RectTransform rect = UiKit.Rect("PresetName", s_content);
            UiKit.Place(rect, x, y, width, height);
            Image back = rect.gameObject.AddComponent<Image>();
            UiKit.StyleField(back, editable);
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
            TMP_Text hint = null;
            if (placeholder != null)
            {
                // shown inside the box while nothing is typed
                hint = UiKit.Text(viewport, gui.m_craftingStationName, placeholder, 15f, UiKit.Dim, TextAlignmentOptions.Left);
                hint.rectTransform.anchorMin = Vector2.zero;
                hint.rectTransform.anchorMax = Vector2.one;
                hint.rectTransform.offsetMin = hint.rectTransform.offsetMax = Vector2.zero;
                hint.raycastTarget = false;
                hint.gameObject.SetActive(string.IsNullOrEmpty(value));
            }
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
            if (hint != null)
            {
                input.onValueChanged.AddListener(v => hint.gameObject.SetActive(string.IsNullOrEmpty(v)));
            }
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
        private static void AddColorSection(InventoryGui gui, DyeTarget target, float ox, float columnWidth, float top, bool title = true)
        {
            float cx = ox + columnWidth / 2f;
            float inner = columnWidth - Pad * 2f;
            if (title)
            {
                TMP_Text titleText = UiKit.Text(s_content, gui.m_craftingStationName, "COLOR", 18f, UiKit.Gold, TextAlignmentOptions.Center);
                UiKit.Place(titleText.rectTransform, cx, top + 18f, inner, 24f);
            }

            float head = title ? ColorHeader : ColorHeader + TabsExtra;
            ZoneSet zones = target.Zones();
            int zoneCount = zones != null ? zones.Count : 1;
            if (!Equals(s_zoneKey, target.Key) || s_zone >= zoneCount)
            {
                s_zone = -1; // a new piece (or a look with fewer zones): back to the whole piece
                s_zoneKey = target.Key;
            }
            bool zoned = zoneCount > 1;
            float belowZones = top + head + (zoned ? ZoneRow : 0f);

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
                float chipY = top + head + ZoneRow / 2f - 4f;
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
            UiKit.StyleField(back);
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

        /// <summary>GLOW or SHINE of a weapon as a colour target: it takes the same swatches, sliders and hex field as COLOR.</summary>
        private static DyeTarget EffectTarget(Player player, ItemDrop.ItemData weapon, bool shine)
        {
            Action<DyeValue> apply = d =>
            {
                GlowValue glow = GlowFromDye(d);
                if (shine) Glow.SetShine(player, weapon, glow);
                else Glow.Set(player, weapon, glow);
            };
            return new DyeTarget
            {
                Zones = () => null,
                Get = () => new[] { DyeFromGlow(shine ? Glow.GetShine(weapon) : Glow.Get(weapon)) },
                SetAll = apply,
                SetZone = (zone, count, d) => apply(d),
                BaseTexture = () => null,
                Flash = (zone, count) => { },
                Key = System.Tuple.Create(weapon, shine),
            };
        }

        private static DyeValue DyeFromGlow(GlowValue glow)
        {
            if (!glow.IsSet) return DyeValue.Target(Color.HSVToRGB(0.08f, 0.85f, 1f)); // nothing chosen: shown as a warm default, RESET leaves it unset
            if (glow.White) return DyeValue.Target(new Color(0.95f, 0.95f, 0.95f));
            return DyeValue.Target(Color.HSVToRGB(glow.Hue / 360f, glow.Toned ? glow.Sat / 100f : 0.85f, glow.Toned ? glow.Val / 100f : 1f));
        }

        private static GlowValue GlowFromDye(DyeValue d)
        {
            if (!d.IsTarget)
            {
                return default; // RESET: the weapon's own colours
            }
            Color.RGBToHSV(d.TargetColor, out float h, out float s, out float v);
            if (s < 0.1f && v > 0.6f)
            {
                return new GlowValue { IsSet = true, White = true };
            }
            return new GlowValue { IsSet = true, Toned = true, Hue = Mathf.RoundToInt(h * 360f) % 360, Sat = Mathf.RoundToInt(s * 100f), Val = Mathf.RoundToInt(v * 100f) };
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

        private const float ListTop = 62f;      // the piece list starts at the height of the search bar
        private const float IconButton = 76f;

        /// <summary>The Original cell: the piece (or weapon) gets its own colours back, and with it its own glow and shine.</summary>
        private static void ResetColours(DyeTarget dye, Player player, ItemDrop.ItemData weapon)
        {
            dye?.SetAll(default);
            if (weapon != null && player != null)
            {
                Glow.Set(player, weapon, default);
                Glow.SetShine(player, weapon, default);
            }
        }

        /// <summary>A square tab button with an icon instead of text; the chosen one is lit gold, the others dim.</summary>
        private static void AddIconButton(InventoryGui gui, Sprite icon, float x, float y, float size, bool selected, Action onClick)
        {
            Button button = UnityEngine.Object.Instantiate(gui.m_tabCraft.gameObject, s_content).GetComponent<Button>();
            button.gameObject.SetActive(true);
            button.interactable = !selected;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
            UiKit.Place((RectTransform)button.transform, x, y, size, size);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = "";
            }
            RectTransform iconRect = UiKit.Rect("Icon", button.transform);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(size * 0.72f, size * 0.72f);
            iconRect.anchoredPosition = Vector2.zero;
            Image image = iconRect.gameObject.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = selected ? UiKit.Gold : new Color(UiKit.Parchment.r, UiKit.Parchment.g, UiKit.Parchment.b, 0.7f);
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
        /// <summary>A dark box framed by the golden rule of the crafting window (the line under its tabs) on all four sides.</summary>
        private static void AddBox(InventoryGui gui, float x, float y, float width, float height)
        {
            RectTransform rect = UiKit.Rect("Box", s_content);
            UiKit.Place(rect, x, y, width, height);
            Image fill = rect.gameObject.AddComponent<Image>();
            fill.color = new Color(0f, 0f, 0f, 0.4f);
            fill.raycastTarget = false;
            Image template = UiKit.Named(gui.m_crafting, "TabsButtons/TabBorder");
            UiKit.Divider(s_content, template, x, y - height / 2f, width);
            UiKit.Divider(s_content, template, x, y + height / 2f, width);
            foreach (float side in new[] { x - width / 2f, x + width / 2f })
            {
                Image line = UiKit.Divider(s_content, template, side, y, height);
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        /// <summary>The golden rule under the crafting window's tabs, as a divider between two sections.</summary>
        private static void AddRule(InventoryGui gui, float x, float y, float width)
        {
            UiKit.Divider(s_content, UiKit.Named(gui.m_crafting, "TabsButtons/TabBorder"), x, y, width);
        }

        private static void AddCaption(InventoryGui gui, string text, float x, float y)
        {
            // the label at the bottom edge of the Original and Hide cells
            TMP_Text caption = UiKit.Text(s_content, gui.m_recipeDecription != null ? gui.m_recipeDecription : gui.m_craftingStationName, text, 12f, UiKit.Parchment, TextAlignmentOptions.Center);
            caption.overflowMode = TextOverflowModes.Truncate;
            UiKit.Place(caption.rectTransform, x, y + LookCell / 2f - 12f, LookCell, 18f);
            caption.raycastTarget = false;
        }

        private static void AddCell(InventoryGui gui, Sprite icon, string name, float x, float y, bool selected, Action onClick, bool noSlash = false, float size = 0f)
        {
            GameObject go = UnityEngine.Object.Instantiate(gui.m_playerGrid.m_elementPrefab, s_content);
            RectTransform rect = (RectTransform)go.transform;
            Vector2 native = rect.sizeDelta.x > 1f ? rect.sizeDelta : new Vector2(Cell, Cell);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.localScale = Vector3.one * (((size > 0f ? size : Cell) - 6f) / native.x);

            InventoryElement element = go.GetComponent<InventoryElement>();
            element.m_icon.enabled = icon != null;
            element.m_icon.sprite = icon;
            if (icon == null && !noSlash)
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
