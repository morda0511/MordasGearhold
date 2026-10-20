using System;
using System.Collections.Generic;
using System.Linq;

namespace MordasGearhold.Core
{
    /// <summary>The fixed Gearhold slots. Each one is a real cell of the vanilla inventory in a hidden row.</summary>
    internal enum GearSlot
    {
        // Indices must never shift. Retired cell: 8 = ranged slot (up to 0.6.0); bows live in the hotbar now, items
        // found there are moved into the grid. Cell 6 was the weapon slot up to 0.3.0 and is the coin slot since
        // 0.18.0: it belongs to no set (all sets share it), and anything but coins found there goes into the grid.
        Helmet, Chest, Legs, Shoulder, Utility, Trinket, Coins, Shield,  // hidden row 0
        Ammo = 9, Ammo2, Ammo3, Trinket2, Trinket3, Utility2, Utility3,  // hidden row 1 (full)
        Quick1 = 16, Quick2, Quick3, Quick4, Quick5, Quick6, Quick7, Quick8 // hidden row 2
    }

    /// <summary>
    /// Layout of the vanilla player inventory under Gearhold:
    ///   rows 0 .. G-1        the normal inventory grid, visible, shared by all sets (row 0 = vanilla hotbar)
    ///   rows G .. G+2        HIDDEN: the Gearhold slots (equipment + quickslots) of the ACTIVE set
    /// A set is exactly the content of the Gearhold slots; a switch never touches the grid.
    /// Items stay in the vanilla inventory, so equipping, weight, saving and the tombstone keep working.
    /// </summary>
    internal static class Topology
    {
        internal const int Width = 8;
        internal const int SetCount = 3;
        internal const int VanillaRows = 4;
        internal const int DefaultExtraRows = 3;
        internal const int MaxExtraRows = 5;
        internal const int HiddenRows = 3;
        internal const int QuickslotCount = 8;

        /// <summary>Stored sets keep slot cells at y = 200 + row, independent of the grid height.</summary>
        internal const int LogicalHiddenBase = 200;

        internal static bool IsLocalInventory(Inventory inventory)
        {
            Player player = Player.m_localPlayer;
            return player != null && inventory != null && ReferenceEquals(player.GetInventory(), inventory);
        }

        internal static int GridRows(Player player)
        {
            int rows = GearholdState.ReadState(player).GridRows;
            return rows > 0 ? rows : VanillaRows + Plugin.ExtraRows.Value;
        }

        /// <summary>True for the hidden Gearhold slot cells a set consists of: the hidden rows except the shared coin slot.</summary>
        internal static bool IsSetOwned(Player player, Vector2i pos)
        {
            int grid = GridRows(player);
            return pos.y >= grid && !(pos.y == grid + (int)GearSlot.Coins / Width && pos.x == (int)GearSlot.Coins % Width);
        }

        internal const string CoinsName = "$item_coins";

        internal static bool IsCoins(ItemDrop.ItemData item) => item != null && item.m_shared.m_name == CoinsName;

        internal static Vector2i Cell(Player player, GearSlot slot)
        {
            int index = (int)slot;
            return new Vector2i(index % Width, GridRows(player) + index / Width);
        }

        internal static GearSlot QuickSlot(int number) => GearSlot.Quick1 + number;

        /// <summary>Equipment slot an item type is kept in while equipped, or null (weapons, bows, tools, torches stay in the grid).</summary>
        internal static GearSlot? SlotFor(ItemDrop.ItemData item)
        {
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Helmet: return GearSlot.Helmet;
                case ItemDrop.ItemData.ItemType.Chest: return GearSlot.Chest;
                case ItemDrop.ItemData.ItemType.Legs: return GearSlot.Legs;
                case ItemDrop.ItemData.ItemType.Shoulder: return GearSlot.Shoulder;
                case ItemDrop.ItemData.ItemType.Utility: return GearSlot.Utility;
                case ItemDrop.ItemData.ItemType.Trinket: return GearSlot.Trinket;
                case ItemDrop.ItemData.ItemType.Shield: return GearSlot.Shield;
                case ItemDrop.ItemData.ItemType.Ammo: return GearSlot.Ammo; // first of the three ammo slots
            }
            return null;
        }

        internal static bool IsArmorSlot(GearSlot slot) => slot <= GearSlot.Trinket;

        internal const int AmmoSlotCount = 3;

        internal static GearSlot AmmoSlot(int number) => GearSlot.Ammo + number;

        internal static bool IsAmmoSlot(GearSlot slot) => slot >= GearSlot.Ammo && slot <= GearSlot.Ammo3;

        /// <summary>Trinket 2 and 3: always active while filled (vanilla itself only knows one trinket slot).</summary>
        internal static bool IsExtraTrinketSlot(GearSlot slot) => slot == GearSlot.Trinket2 || slot == GearSlot.Trinket3;

        /// <summary>Utility 2 and 3: the same for utility items (belts, wishbone, ...).</summary>
        internal static bool IsExtraUtilitySlot(GearSlot slot) => slot == GearSlot.Utility2 || slot == GearSlot.Utility3;

        internal static bool IsExtraSlot(GearSlot slot) => IsExtraTrinketSlot(slot) || IsExtraUtilitySlot(slot);

        internal static bool IsUtilitySlot(GearSlot slot) => slot == GearSlot.Utility || IsExtraUtilitySlot(slot);

        /// <summary>The item type an extra slot takes.</summary>
        internal static ItemDrop.ItemData.ItemType ExtraType(GearSlot slot) =>
            IsExtraUtilitySlot(slot) ? ItemDrop.ItemData.ItemType.Utility : ItemDrop.ItemData.ItemType.Trinket;

        /// <summary>False for Trinket/Utility 2 and 3 when the config (TrinketSlots, UtilitySlots) switches them off.</summary>
        internal static bool IsEnabled(GearSlot slot)
        {
            switch (slot)
            {
                case GearSlot.Trinket2: return Plugin.TrinketSlots.Value >= 2;
                case GearSlot.Trinket3: return Plugin.TrinketSlots.Value >= 3;
                case GearSlot.Utility2: return Plugin.UtilitySlots.Value >= 2;
                case GearSlot.Utility3: return Plugin.UtilitySlots.Value >= 3;
                default: return true;
            }
        }

        internal static readonly GearSlot[] ExtraSlots ={ GearSlot.Trinket2, GearSlot.Trinket3, GearSlot.Utility2, GearSlot.Utility3 };

        internal static bool IsTrinketSlot(GearSlot slot) => slot == GearSlot.Trinket || IsExtraTrinketSlot(slot);

        /// <summary>Whether an item may lie in a Gearhold slot (quickslots take anything).</summary>
        internal static bool Accepts(GearSlot slot, ItemDrop.ItemData item)
        {
            if (slot >= GearSlot.Quick1) return true;
            if (slot == GearSlot.Coins) return IsCoins(item);
            if (IsTrinketSlot(slot)) return item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket;
            if (IsUtilitySlot(slot)) return item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility;
            if (IsAmmoSlot(slot)) return item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo;
            return SlotFor(item) == slot;
        }

        /// <summary>The Gearhold slot a live cell belongs to, or null for grid cells.</summary>
        internal static GearSlot? SlotAt(Player player, Vector2i pos)
        {
            int grid = GridRows(player);
            if (pos.y < grid || pos.y >= grid + HiddenRows)
            {
                return null;
            }
            int index = (pos.y - grid) * Width + pos.x;
            return Enum.IsDefined(typeof(GearSlot), index) ? (GearSlot?)index : null;
        }

        internal static Vector2i ToLogical(Player player, Vector2i pos)
        {
            int grid = GridRows(player);
            return pos.y >= grid ? new Vector2i(pos.x, LogicalHiddenBase + pos.y - grid) : pos;
        }

        internal static Vector2i FromLogical(Player player, Vector2i pos)
        {
            return pos.y >= LogicalHiddenBase ? new Vector2i(pos.x, GridRows(player) + pos.y - LogicalHiddenBase) : pos;
        }

        /// <summary>
        /// Whether a stored (logical) position is a slot cell of a set. The coin slot is none: an old record there
        /// (the weapon slot of 0.1.0–0.3.0) goes into the grid like any other leftover.
        /// </summary>
        internal static bool IsSlotRecord(Vector2i logical)
        {
            int index = (logical.y - LogicalHiddenBase) * Width + logical.x;
            return logical.x >= 0 && logical.x < Width && logical.y >= LogicalHiddenBase && logical.y < LogicalHiddenBase + HiddenRows
                && index != (int)GearSlot.Coins && Enum.IsDefined(typeof(GearSlot), index);
        }

        /// <summary>Rows bought from the trader (vanilla unique key "invrows", base 4).</summary>
        private static int PurchasedRows(Player player)
        {
            if (player.TryGetUniqueKeyValue(Player.InventoryRowsKey, out string value) && int.TryParse(value, out int rows))
            {
                return Math.Max(0, Math.Min(9, rows) - VanillaRows);
            }
            return 0;
        }

        /// <summary>
        /// Brings the live inventory to the configured layout. Never drops items: a layout change that would
        /// leave items outside the grid is refused.
        /// </summary>
        internal static void Apply(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
            {
                return;
            }
            Inventory inventory = player.GetInventory();
            GearholdState.State state = GearholdState.ReadState(player);
            int wantGrid = VanillaRows + Plugin.ExtraRows.Value + PurchasedRows(player);

            if (state.GridRows <= 0)
            {
                int lowest = inventory.GetAllItems().Select(i => i.m_gridPos.y + 1).DefaultIfEmpty(0).Max();
                state.GridRows = Math.Max(wantGrid, lowest);
                Plugin.Log.LogInfo($"Gearhold layout initialised: {state.GridRows} inventory rows.");
            }
            state.HiddenRows = HiddenRows;
            if (state.GridRows != wantGrid)
            {
                if (TryResize(player, ref state, wantGrid, out string reason))
                {
                    Plugin.Log.LogInfo($"Gearhold layout changed: {state.GridRows} inventory rows.");
                }
                else
                {
                    Plugin.Log.LogWarning("Gearhold layout change refused: " + reason);
                    player.Message(MessageHud.MessageType.Center, "Gearhold: rows not changed – " + reason);
                }
            }
            GearholdState.WriteState(player, state);

            int total = state.GridRows + HiddenRows;
            if (inventory.GetHeight() != total)
            {
                inventory.SetHeight(total);
                Access.Changed(inventory);
            }
            if (InventoryGui.instance != null)
            {
                InventoryGui.instance.SetInventorySize(state.GridRows);
            }
        }

        private static float s_nextHeightCheck;

        /// <summary>
        /// Once a second: another mod may have changed the inventory height (Path of Valheiman does so once when it
        /// hands its old equipment rows back). The grid plus the hidden slot rows are put back; nothing moves.
        /// </summary>
        internal static void KeepHeight(Player player)
        {
            if (UnityEngine.Time.unscaledTime < s_nextHeightCheck)
            {
                return;
            }
            s_nextHeightCheck = UnityEngine.Time.unscaledTime + 1f;
            GearholdState.State state = GearholdState.ReadState(player);
            Inventory inventory = player.GetInventory();
            int total = state.GridRows + HiddenRows;
            if (state.GridRows <= 0 || inventory.GetHeight() == total)
            {
                return;
            }
            if (inventory.GetAllItems().Any(i => i.m_gridPos.y >= total))
            {
                return; // never shrink over items: they would become unreachable
            }
            Plugin.Log.LogInfo($"Inventory height was changed to {inventory.GetHeight()} by another mod; Gearhold sets it back to {total}.");
            inventory.SetHeight(total);
            Access.Changed(inventory);
            if (InventoryGui.instance != null)
            {
                InventoryGui.instance.SetInventorySize(state.GridRows);
            }
        }

        private static bool TryResize(Player player, ref GearholdState.State state, int newGrid, out string reason)
        {
            reason = null;
            int oldGrid = state.GridRows;
            List<ItemDrop.ItemData> live = player.GetInventory().GetAllItems();
            if (newGrid < oldGrid && live.Any(i => i.m_gridPos.y >= newGrid && i.m_gridPos.y < oldGrid))
            {
                reason = $"row {newGrid + 1}–{oldGrid} still holds items";
                return false;
            }
            foreach (ItemDrop.ItemData item in live)
            {
                if (item.m_gridPos.y >= oldGrid)
                {
                    item.m_gridPos = new Vector2i(item.m_gridPos.x, item.m_gridPos.y - oldGrid + newGrid);
                }
            }
            state.GridRows = newGrid;
            return true;
        }

        /// <summary>First free cell between two rows.</summary>
        internal static Vector2i FindFree(Inventory inventory, int fromRow, int toRowExclusive, bool topDown)
        {
            int width = inventory.GetWidth();
            int count = toRowExclusive - fromRow;
            for (int n = 0; n < count; n++)
            {
                int y = topDown ? fromRow + n : toRowExclusive - 1 - n;
                for (int x = 0; x < width; x++)
                {
                    if (inventory.GetItemAt(x, y) == null)
                    {
                        return new Vector2i(x, y);
                    }
                }
            }
            return new Vector2i(-1, -1);
        }

        /// <summary>Free grid cell for an item taken out of a slot: below the hotbar first, the hotbar last.</summary>
        internal static Vector2i FindFreeGridCell(Player player)
        {
            Inventory inventory = player.GetInventory();
            Vector2i cell = FindFree(inventory, 1, GridRows(player), true);
            return cell.x >= 0 ? cell : FindFree(inventory, 0, 1, true);
        }

        internal static int FreeVisibleCells(Player player)
        {
            Inventory inventory = player.GetInventory();
            int grid = GridRows(player);
            int used = inventory.GetAllItems().Count(i => i.m_gridPos.y < grid);
            return Math.Max(0, grid * inventory.GetWidth() - used);
        }
    }
}
