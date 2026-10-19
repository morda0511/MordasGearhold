using System;
using System.Collections.Generic;
using MordasGearhold.Core;
using MordasGearhold.Patches;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ItemType = ItemDrop.ItemData.ItemType;

namespace MordasGearhold.UI
{
    /// <summary>
    /// The Gearhold panel: centred between the vanilla player inventory and the crafting panel, built from
    /// vanilla parts. Every slot is a real storage cell of the active set (a hidden inventory row), not a mirror.
    ///   - pick an item in the inventory (left click), then left-click a slot: the item moves into the slot
    ///   - left-click a filled slot: the item goes back into the inventory
    ///   - right-click a slot: use the item (equip / unequip, eat, drink)
    /// </summary>
    internal static class GearholdPanel
    {
        private const float DesignWidth = 460f;
        private const float DesignHeight = 930f;
        private const float SlotTop = 178f;
        private const float SlotStep = 88f;
        private const float CompactHeight = 140f;
        private const float SlotSize = 64f;
        private const float QuickSize = 46f;

        private sealed class Slot
        {
            public GearSlot Kind;
            public InventoryElement Element;
            public TMP_Text Binding;
            public TMP_Text KeyButton;
            public TMP_Text Caption;
            public ItemDrop.ItemData Item;
            public string Label;
        }

        private static InventoryGui s_gui;
        private static RectTransform s_root;
        private static RectTransform s_body;
        private static readonly List<Slot> s_slots = new List<Slot>();
        private static readonly Button[] s_tabs = new Button[Topology.SetCount];
        private static TMP_Text s_stats;
        private static TMP_Text s_footer;
        private static bool s_failed;

        internal static void Tick()
        {
            SkinMenu.Tick();
            InventoryGui gui = InventoryGui.instance;
            Player player = Player.m_localPlayer;
            if (gui == null || s_failed)
            {
                return;
            }
            if (s_gui != gui || s_root == null)
            {
                try
                {
                    Build(gui);
                }
                catch (Exception ex)
                {
                    s_failed = true;
                    if (s_root != null)
                    {
                        // a half-built panel would sit over the inventory unlaid out, so it goes away completely
                        UnityEngine.Object.Destroy(s_root.gameObject);
                        s_root = null;
                    }
                    Plugin.Log.LogError("Gearhold panel could not be built, panel disabled: " + ex);
                    return;
                }
            }
            bool visible = Plugin.ShowPanel.Value && InventoryGui.IsVisible() && player != null && GearholdState.IsReady(player);
            if (s_root.gameObject.activeSelf != visible)
            {
                s_root.gameObject.SetActive(visible);
            }
            if (visible)
            {
                try
                {
                    Layout(gui);
                    Refresh(player);
                }
                catch (Exception ex)
                {
                    s_failed = true;
                    s_root.gameObject.SetActive(false);
                    Plugin.Log.LogError("Gearhold panel failed, panel disabled until restart: " + ex);
                }
            }
        }

        // ------------------------------------------------------------------ build

        private static void Build(InventoryGui gui)
        {
            s_gui = gui;
            s_slots.Clear();
            RectTransform parent = (RectTransform)gui.m_player.parent;
            s_root = UiKit.Rect("MordasGearholdPanel", parent);
            s_root.anchorMin = s_root.anchorMax = new Vector2(0.5f, 0.5f);
            s_root.pivot = new Vector2(0.5f, 1f);
            s_root.sizeDelta = new Vector2(DesignWidth, DesignHeight);

            // The crafting panel's own parts, found by name (see the image list logged by 0.8.2):
            //   Crafting/Darken  darken_blob             soft shadow behind the board
            //   Crafting/Bkg     woodpanel_crafting_240  the wooden board
            Image darken = UiKit.Named(gui.m_crafting, "Darken");
            if (darken != null)
            {
                UiKit.Fill(s_root, "Darken", darken, 40f);
            }
            Image board = UiKit.Named(gui.m_crafting, "Bkg") ?? UiKit.FindBackground(gui.m_crafting);
            Image boardImage = UiKit.Fill(s_root, "Board", board, 0f);
            boardImage.raycastTarget = true; // clicks on the panel must not reach the world behind it
            if (board == null)
            {
                boardImage.color = UiKit.PanelFallback;
            }
            Plugin.Log.LogInfo("Gearhold panel background: " + (board != null ? board.name + " / " + board.sprite.name : "none found, plain fallback"));
            Image braid = UiKit.Named(gui.m_crafting, "BraidLineHorisontalMedium");
            Image descriptionBox = UiKit.Named(gui.m_crafting, "Decription");

            TMP_Text header = gui.m_craftingStationName;
            TMP_Text body = gui.m_recipeDecription != null ? gui.m_recipeDecription : header;

            TMP_Text title = UiKit.Text(s_root, header, "MORDA'S GEARHOLD", 34f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, DesignWidth / 2f, 34f, DesignWidth - 40f, 44f);
            TMP_Text subtitle = UiKit.Text(s_root, body, "INVENTORY · LOADOUTS · QUICKSLOTS", 14f, UiKit.Dim, TextAlignmentOptions.Center);
            UiKit.Place(subtitle.rectTransform, DesignWidth / 2f, 64f, DesignWidth - 40f, 20f);

            for (int i = 0; i < Topology.SetCount; i++)
            {
                int index = i;
                Button tab = UnityEngine.Object.Instantiate(gui.m_tabCraft.gameObject, s_root).GetComponent<Button>();
                tab.gameObject.name = "SetTab" + (i + 1);
                tab.gameObject.SetActive(true);
                tab.onClick = new Button.ButtonClickedEvent();
                tab.onClick.AddListener(() => SetSwitcher.TrySwitch(Player.m_localPlayer, index));
                TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = "SET " + (i + 1);
                }
                UiKit.Place((RectTransform)tab.transform, 90f + i * 140f, 102f, 128f, 34f);
                s_tabs[i] = tab;
            }

            s_body = UiKit.Rect("Body", s_root);
            s_body.anchorMin = new Vector2(0f, 0f);
            s_body.anchorMax = new Vector2(1f, 1f);
            s_body.offsetMin = s_body.offsetMax = Vector2.zero;

            UiKit.Divider(s_body, braid, DesignWidth / 2f, 132f, DesignWidth - 40f);

            // left: armour and the three utility slots; right: shield, ammo and the three trinkets;
            // the character sheet in between (slot 1 of utility/trinket = vanilla slot, 2 and 3 always active)
            const float leftX = 58f, rightX = DesignWidth - 58f;
            float Row(int i) => SlotTop + i * SlotStep;
            AddSlot(gui, GearSlot.Helmet, "Head", leftX, Row(0));
            AddSlot(gui, GearSlot.Chest, "Chest", leftX, Row(1));
            AddSlot(gui, GearSlot.Legs, "Legs", leftX, Row(2));
            AddSlot(gui, GearSlot.Shoulder, "Cape", leftX, Row(3));
            AddSlot(gui, GearSlot.Utility, "Utility 1", leftX, Row(4));
            AddSlot(gui, GearSlot.Utility2, "Utility 2", leftX, Row(5));
            AddSlot(gui, GearSlot.Utility3, "Utility 3", leftX, Row(6));
            AddSlot(gui, GearSlot.Shield, "Shield", rightX, Row(0));
            AddSlot(gui, GearSlot.Trinket, "Trinket 1", rightX, Row(4));
            AddSlot(gui, GearSlot.Trinket2, "Trinket 2", rightX, Row(5));
            AddSlot(gui, GearSlot.Trinket3, "Trinket 3", rightX, Row(6));
            for (int n = 0; n < Topology.AmmoSlotCount; n++)
            {
                Slot ammo = AddSlot(gui, Topology.AmmoSlot(n), "Ammo " + (n + 1), rightX, Row(1 + n));
                // the caption shows the ammo hotkey; clicking it lets you press a new one (like the quickslot buttons)
                // (a GameObject holds only one Graphic, so the click area is a child of the caption text, not on it)
                int number = n;
                RectTransform area = UiKit.Rect("KeyClickArea", ammo.Caption.transform);
                area.anchorMin = Vector2.zero;
                area.anchorMax = Vector2.one;
                area.offsetMin = area.offsetMax = Vector2.zero;
                Image hit = area.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0.001f);
                Button rebind = area.gameObject.AddComponent<Button>();
                rebind.transition = Selectable.Transition.None;
                rebind.onClick.AddListener(() => KeyRebinder.Begin(Plugin.KeyAmmo[number]));
            }
            // the dark box vanilla puts behind a recipe description, behind the character sheet
            float sheetTop = 146f, sheetBottom = Row(6) + 42f;
            if (descriptionBox != null)
            {
                RectTransform boxRect = UiKit.Rect("SheetBox", s_body);
                UiKit.Place(boxRect, DesignWidth / 2f, (sheetTop + sheetBottom) / 2f, 248f, sheetBottom - sheetTop);
                Image box = boxRect.gameObject.AddComponent<Image>();
                UiKit.CopyLook(box, descriptionBox, Color.clear);
                box.raycastTarget = false;
            }
            TMP_Text viking = UiKit.Text(s_body, header, "YOUR VIKING", 22f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Place(viking.rectTransform, DesignWidth / 2f, 160f, 230f, 28f);
            s_stats = BuildScrollingSheet(gui, body, DesignWidth / 2f, 180f, 232f, sheetBottom - 8f);

            float quickTop = sheetBottom + 30f;
            UiKit.Divider(s_body, braid, DesignWidth / 2f, quickTop, DesignWidth - 40f);
            TMP_Text quickTitle = UiKit.Text(s_body, header, "QUICKSLOTS · ACTIVE SET", 16f, UiKit.Gold, TextAlignmentOptions.Left);
            UiKit.Place(quickTitle.rectTransform, DesignWidth / 2f, quickTop + 18f, DesignWidth - 50f, 22f);
            for (int x = 0; x < Topology.QuickslotCount; x++)
            {
                float slotX = DesignWidth / 2f + (x - 3.5f) * (QuickSize + 6f);
                Slot quick = AddSlot(gui, Topology.QuickSlot(x), null, slotX, quickTop + 58f, QuickSize);
                quick.Label = "Quickslot " + (x + 1);
                quick.KeyButton = AddKeyButton(gui, x, slotX, quickTop + 96f);
            }
            s_footer = UiKit.Text(s_body, body, "", 12f, UiKit.Dim, TextAlignmentOptions.Center);
            UiKit.Place(s_footer.rectTransform, DesignWidth / 2f, quickTop + 124f, DesignWidth - 30f, 18f);

            s_root.gameObject.SetActive(false);
        }

        /// <summary>
        /// The character sheet inside a scroll view (top..bottom in design coordinates): a masked viewport, the text
        /// as content that grows with its lines, and a copy of the crafting list's vanilla scrollbar that only
        /// shows when the text is longer than the view. Mouse wheel scrolls.
        /// </summary>
        private static TMP_Text BuildScrollingSheet(InventoryGui gui, TMP_Text font, float x, float top, float width, float bottom)
        {
            RectTransform view = UiKit.Rect("SheetView", s_body);
            UiKit.Place(view, x, (top + bottom) / 2f, width, bottom - top);
            view.gameObject.AddComponent<RectMask2D>();
            Image hit = view.gameObject.AddComponent<Image>(); // something to catch the mouse wheel
            hit.color = new Color(0f, 0f, 0f, 0.001f);

            TMP_Text text = UiKit.Text(view, font, "", 13.5f, UiKit.Parchment, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.lineSpacing = -8f;
            RectTransform content = text.rectTransform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(-14f, 0f); // leave room for the scrollbar
            ContentSizeFitter fitter = text.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 25f;

            if (gui.m_recipeListScroll != null)
            {
                Scrollbar bar = UnityEngine.Object.Instantiate(gui.m_recipeListScroll.gameObject, s_body).GetComponent<Scrollbar>();
                bar.onValueChanged = new Scrollbar.ScrollEvent(); // drop the link to the crafting list
                UiKit.Place((RectTransform)bar.transform, x + width / 2f - 5f, (top + bottom) / 2f, 10f, bottom - top);
                bar.direction = Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbar = bar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            return text;
        }

        /// <summary>Small vanilla-style button under a quickslot: shows the key, a click lets you press a new one.</summary>
        private static TMP_Text AddKeyButton(InventoryGui gui, int quickslot, float x, float y)
        {
            Button button = UnityEngine.Object.Instantiate(gui.m_tabCraft.gameObject, s_body).GetComponent<Button>();
            button.gameObject.name = "QuickslotKey" + (quickslot + 1);
            button.gameObject.SetActive(true);
            button.interactable = true;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => KeyRebinder.Begin(Plugin.KeyQuickslots[quickslot]));
            UiKit.Place((RectTransform)button.transform, x, y, QuickSize + 2f, 24f);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.enableAutoSizing = false;
                label.fontSize = 13f;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            UITooltip tooltip = button.GetComponent<UITooltip>();
            if (tooltip == null)
            {
                // a fresh UITooltip has no window prefab and throws on hover: borrow the one of an inventory slot
                GameObject prefab = gui.m_playerGrid.m_elementPrefab.GetComponent<InventoryElement>()?.m_tooltip?.m_tooltipPrefab;
                if (prefab == null)
                {
                    return label;
                }
                tooltip = button.gameObject.AddComponent<UITooltip>();
                tooltip.m_tooltipPrefab = prefab;
            }
            tooltip.m_topic = "Quickslot " + (quickslot + 1);
            tooltip.m_text = "Click, then press the new key (Shift / Ctrl / Alt allowed).\nEsc cancels · Delete clears.";
            return label;
        }

        private static Slot AddSlot(InventoryGui gui, GearSlot kind, string label, float x, float y, float size = SlotSize)
        {
            GameObject go = UnityEngine.Object.Instantiate(gui.m_playerGrid.m_elementPrefab, s_body);
            go.name = "Slot" + kind;
            RectTransform rect = (RectTransform)go.transform;
            Vector2 native = rect.sizeDelta.x > 1f ? rect.sizeDelta : new Vector2(size, size);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.localScale = Vector3.one * (size / native.x);

            Slot slot = new Slot { Kind = kind, Element = go.GetComponent<InventoryElement>(), Label = label };
            UIInputHandler input = go.GetComponentInChildren<UIInputHandler>();
            if (input != null)
            {
                input.m_onLeftClick = _ => OnLeftClick(slot);
                input.m_onRightDown = _ => OnRightClick(slot);
                input.m_onPointerEnter = _ => OnHover(slot);
            }
            slot.Binding = go.transform.Find("binding")?.GetComponent<TMP_Text>();
            if (slot.Binding != null)
            {
                slot.Binding.enabled = false;
            }
            if (label != null)
            {
                TMP_Text caption = UiKit.Text(s_body, gui.m_recipeDecription, label, 13f, UiKit.Dim, TextAlignmentOptions.Center);
                UiKit.Place(caption.rectTransform, x, y + size / 2f + 11f, 110f, 18f);
                slot.Caption = caption;
            }
            s_slots.Add(slot);
            return slot;
        }

        // ------------------------------------------------------------------ layout

        private static readonly Vector3[] s_corners = new Vector3[4];

        private static void Layout(InventoryGui gui)
        {
            RectTransform parent = (RectTransform)s_root.parent;
            gui.m_player.GetWorldCorners(s_corners);
            float left = parent.InverseTransformPoint(s_corners[2]).x;
            float top = parent.InverseTransformPoint(s_corners[1]).y;
            float right = parent.rect.xMax - 20f;
            if (gui.m_crafting != null && gui.m_crafting.gameObject.activeInHierarchy)
            {
                gui.m_crafting.GetWorldCorners(s_corners);
                right = parent.InverseTransformPoint(s_corners[0]).x;
            }
            float gap = right - left - 24f;
            float available = top - (parent.rect.yMin + 70f);

            // Shrink to the title bar instead of covering an open container / tombstone.
            bool compact = gui.IsContainerOpen() && Overlaps(gui.m_container, parent, left, right, top, DesignHeight);
            float height = compact ? CompactHeight : DesignHeight;
            float scale = Mathf.Clamp(Mathf.Min(gap / DesignWidth, available / height), 0.45f, 1f);

            s_body.gameObject.SetActive(!compact);
            s_root.sizeDelta = new Vector2(DesignWidth, height);
            s_root.localScale = Vector3.one * scale;
            s_root.localPosition = new Vector3((left + right) / 2f, top, 0f);
        }

        private static bool Overlaps(RectTransform other, RectTransform parent, float left, float right, float top, float height)
        {
            if (other == null || !other.gameObject.activeInHierarchy)
            {
                return false;
            }
            other.GetWorldCorners(s_corners);
            Vector3 min = parent.InverseTransformPoint(s_corners[0]);
            Vector3 max = parent.InverseTransformPoint(s_corners[2]);
            return max.x > left && min.x < right && max.y > top - height && min.y < top;
        }

        // ------------------------------------------------------------------ refresh

        private static void Refresh(Player player)
        {
            int active = GearholdState.ActiveSet(player);
            for (int i = 0; i < s_tabs.Length; i++)
            {
                s_tabs[i].interactable = i != active;
            }
            ItemDrop.ItemData drag = Access.DragItem(s_gui);
            foreach (Slot slot in s_slots)
            {
                // Trinket/Utility 2 and 3 can be switched off in the config (TrinketSlots, UtilitySlots)
                bool enabled = Topology.IsEnabled(slot.Kind);
                if (slot.Element.gameObject.activeSelf != enabled)
                {
                    slot.Element.gameObject.SetActive(enabled);
                    if (slot.Caption != null)
                    {
                        slot.Caption.gameObject.SetActive(enabled);
                    }
                }
                if (!enabled)
                {
                    continue;
                }
                slot.Item = Equipment.ItemIn(player, slot.Kind);
                Draw(player, slot, drag);
                if (slot.KeyButton != null)
                {
                    int quickslot = slot.Kind - GearSlot.Quick1;
                    string key = Plugin.KeyLabel(quickslot);
                    slot.KeyButton.text = KeyRebinder.IsWaitingFor(Plugin.KeyQuickslots[quickslot]) ? "<color=#ffc96e>...</color>" : (key.Length > 0 ? key : "–");
                }
                if (slot.Caption != null && Topology.IsAmmoSlot(slot.Kind))
                {
                    int number = slot.Kind - GearSlot.Ammo;
                    string key = Plugin.AmmoKeyLabel(number);
                    if (KeyRebinder.IsWaitingFor(Plugin.KeyAmmo[number]))
                        slot.Caption.text = $"Ammo {number + 1} · <color=#ffc96e>press key</color>";
                    else
                        slot.Caption.text = key.Length > 0 ? $"Ammo {number + 1} · {key}" : $"Ammo {number + 1} · <color=#e0a040>set key</color>";
                }
            }

            // the sheet is text-heavy: rebuild it a few times per second, not every frame
            if (Time.unscaledTime >= s_nextSheet)
            {
                s_nextSheet = Time.unscaledTime + 0.25f;
                s_stats.text = CharacterSheet.Build(player);
                int dormant = GearholdState.TryReadSet(player, active, out SetPackage own, out _) ? own.Records.Count : 0;
                s_footer.text = dormant > 0
                    ? $"<color=#e0a040>{dormant} items of this set wait for their mod to return</color>"
                    : "Only the Gearhold slots change with the set · the inventory is shared";
            }
        }

        private static float s_nextSheet;

        private static void Draw(Player player, Slot slot, ItemDrop.ItemData drag)
        {
            InventoryElement e = slot.Element;
            ItemDrop.ItemData item = slot.Item;
            if (e.m_selected != null) e.m_selected.SetActive(false);
            if (e.m_queued != null) e.m_queued.enabled = item != null && player.IsEquipActionQueued(item);
            if (e.m_noteleport != null) e.m_noteleport.enabled = false;
            if (e.m_food != null) e.m_food.enabled = false;
            if (e.m_dropFocus != null) e.m_dropFocus.enabled = drag != null && Accepts(slot.Kind, drag);
            Transmog.Piece? piece = Transmog.PieceFor(slot.Kind);
            // sparkle when a look or a colour is chosen for this piece
            string marker = null;
            if (piece.HasValue)
            {
                marker = Transmog.GetLook(player, piece.Value) ?? (Dye.Get(player, piece.Value).IsNeutral ? null : "dye");
            }
            LookBadge.Show(e.transform, marker);
            if (item == null)
            {
                e.m_icon.enabled = false;
                e.m_equiped.enabled = false;
                e.m_amount.enabled = false;
                if (e.m_quality != null) e.m_quality.enabled = false;
                e.m_durability.gameObject.SetActive(false);
                return;
            }
            e.m_icon.enabled = true;
            e.m_icon.sprite = item.GetIcon();
            bool dimmed = item == drag || (Topology.IsArmorSlot(slot.Kind) && !item.m_equipped);
            e.m_icon.color = dimmed ? Color.grey : Color.white;
            e.m_equiped.enabled = item.m_equipped;
            bool stack = item.m_shared.m_maxStackSize > 1;
            e.m_amount.enabled = stack;
            if (stack) e.m_amount.text = item.m_stack + "/" + item.m_shared.m_maxStackSize;
            if (e.m_quality != null)
            {
                e.m_quality.enabled = item.m_shared.m_maxQuality > 1;
                e.m_quality.text = item.m_quality.ToString();
            }
            bool worn = item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability();
            e.m_durability.gameObject.SetActive(worn);
            if (worn)
            {
                e.m_durability.SetValue(item.m_durability <= 0f ? 1f : item.GetDurabilityPercentage());
            }
        }

        // ------------------------------------------------------------------ input

        private static bool Accepts(GearSlot slot, ItemDrop.ItemData item)
        {
            if (slot >= GearSlot.Quick1)
            {
                return true;
            }
            if (Topology.IsTrinketSlot(slot))
            {
                return item.m_shared.m_itemType == ItemType.Trinket;
            }
            if (Topology.IsUtilitySlot(slot))
            {
                return item.m_shared.m_itemType == ItemType.Utility;
            }
            if (Topology.IsAmmoSlot(slot))
            {
                return item.m_shared.m_itemType == ItemType.Ammo;
            }
            return Topology.SlotFor(item) == slot;
        }

        private static void OnLeftClick(Slot slot)
        {
            Player player = Player.m_localPlayer;
            if (player == null || s_gui == null)
            {
                return;
            }
            ItemDrop.ItemData drag = Access.DragItem(s_gui);
            if (drag == null)
            {
                if (slot.Item == null)
                {
                    return;
                }
                if (slot.Kind >= GearSlot.Quick1)
                {
                    // quickslots: pick the item up like in the vanilla grid; click another quickslot to move or swap,
                    // or a grid cell to put it back into the inventory
                    Access.StartDrag(s_gui, slot.Item, player.GetInventory());
                }
                else
                {
                    Equipment.TakeOut(player, slot.Item);
                }
                return;
            }
            if (!player.GetInventory().ContainsItem(drag))
            {
                player.Message(MessageHud.MessageType.Center, "Take the item into your inventory first");
                return;
            }
            if (!Accepts(slot.Kind, drag))
            {
                player.Message(MessageHud.MessageType.Center, "This item does not fit into " + (slot.Label ?? "this slot"));
                return;
            }
            Access.ClearDrag(s_gui);
            PutIntoSlot(player, slot.Kind, drag);
        }

        private static void PutIntoSlot(Player player, GearSlot kind, ItemDrop.ItemData item)
        {
            ItemDrop.ItemData occupant = Equipment.ItemIn(player, kind);
            bool occupantWorn = occupant != null && occupant.m_equipped;
            Equipment.MoveToCell(player, item, Topology.Cell(player, kind));
            if (kind >= GearSlot.Quick1 || Topology.IsExtraSlot(kind) || item.m_equipped)
            {
                return; // extra trinkets need no equip call: Trinkets.Tick activates them in their slot
            }
            ItemDrop.ItemData right = Access.RightItem(player);
            bool equip;
            switch (kind)
            {
                case GearSlot.Shield:
                    equip = occupantWorn || (Access.LeftItem(player) == null && right != null
                        && (right.m_shared.m_itemType == ItemType.OneHandedWeapon || right.m_shared.m_itemType == ItemType.Torch));
                    break;
                case GearSlot.Ammo:
                case GearSlot.Ammo2:
                case GearSlot.Ammo3:
                    // fill a second/third ammo slot without switching away from the ammo in use
                    equip = occupantWorn || Access.AmmoItem(player) == null;
                    break;
                default:
                    equip = true; // armour: putting it in the slot means wearing it
                    break;
            }
            if (equip)
            {
                player.EquipItem(item);
            }
        }

        private static void OnRightClick(Slot slot)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }
            ItemDrop.ItemData item = slot.Item != null && player.GetInventory().ContainsItem(slot.Item) ? slot.Item : null;
            Transmog.Piece? piece = Transmog.PieceFor(slot.Kind);
            if (piece.HasValue)
            {
                // armour and shield: the look menu (taking gear off is a left-click on the slot)
                SkinMenu.OpenForPiece(player, piece.Value);
                return;
            }
            if (item != null)
            {
                player.UseItem(null, item, true);
            }
        }

        private static void OnHover(Slot slot)
        {
            UITooltip tooltip = slot.Element.m_tooltip;
            if (tooltip == null)
            {
                return;
            }
            if (slot.Item != null)
            {
                tooltip.Set(slot.Item.m_shared.m_name, slot.Item.GetTooltip(), s_gui.m_playerGrid.m_tooltipAnchor);
            }
            else
            {
                tooltip.Set("", "");
            }
        }
    }
}
