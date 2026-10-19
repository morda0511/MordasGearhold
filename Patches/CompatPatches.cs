using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MordasGearhold.Core;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// StoreAndCraft's "Sort" packs the whole player inventory over every row. Under Gearhold that would push
    /// items into the hidden slot rows, so the player sort is replaced by one that stays inside the grid.
    /// Chest sorting is untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class StoreAndCraft_SortPlayerInventory_Patch
    {
        private static MethodBase Target()
        {
            Type type = AccessTools.TypeByName("StoreAndCraft.InventorySort");
            return type != null ? AccessTools.Method(type, "SortPlayerInventory", new[] { typeof(Player) }) : null;
        }

        private static bool Prepare() => Target() != null;

        private static MethodBase TargetMethod() => Target();

        private static bool Prefix(Player player)
        {
            if (player == null || player != Player.m_localPlayer || !GearholdState.IsReady(player))
            {
                return true;
            }
            GearholdSort.Sort(player);
            return false;
        }
    }

    internal static class GearholdSort
    {
        /// <summary>
        /// Reorders unequipped items of the inventory grid below the hotbar; the Gearhold slot rows are never
        /// touched. Only positions change; no item is merged, split or removed.
        /// </summary>
        internal static void Sort(Player player)
        {
            Inventory inventory = player.GetInventory();
            SortArea(inventory, 1, Topology.GridRows(player));
            Access.Changed(inventory);
        }

        private static void SortArea(Inventory inventory, int fromRow, int toRow)
        {
            int width = inventory.GetWidth();
            List<ItemDrop.ItemData> inArea = inventory.GetAllItems().Where(i => i.m_gridPos.y >= fromRow && i.m_gridPos.y < toRow).ToList();
            List<ItemDrop.ItemData> movable = inArea.Where(i => !i.m_equipped).ToList();
            HashSet<int> fixedCells = new HashSet<int>(inArea.Where(i => i.m_equipped).Select(i => i.m_gridPos.y * width + i.m_gridPos.x));
            movable.Sort((a, b) =>
            {
                int byType = a.m_shared.m_itemType.CompareTo(b.m_shared.m_itemType);
                if (byType != 0) return byType;
                int byName = string.CompareOrdinal(a.m_shared.m_name, b.m_shared.m_name);
                return byName != 0 ? byName : b.m_quality.CompareTo(a.m_quality);
            });
            int next = 0;
            foreach (ItemDrop.ItemData item in movable)
            {
                while (fixedCells.Contains(fromRow * width + next))
                {
                    next++;
                }
                item.m_gridPos = new Vector2i(next % width, fromRow + next / width);
                next++;
            }
        }
    }
}
