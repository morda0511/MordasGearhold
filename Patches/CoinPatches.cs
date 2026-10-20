using HarmonyLib;
using MordasGearhold.Core;
using UnityEngine;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// The coin slot (top right of the panel): one hidden cell shared by all sets. Coins you carry are moved into
    /// it on their own, and what lies there weighs nothing. It holds one vanilla stack (the game cuts bigger stacks
    /// on load, so it never goes above that); coins beyond a full slot stay in the grid and weigh as usual.
    /// </summary>
    internal static class Coins
    {
        private static float s_next;

        internal static ItemDrop.ItemData InSlot(Player player) => Equipment.ItemIn(player, GearSlot.Coins);

        /// <summary>A few times per second: anything that is no coin leaves the slot, loose coins go in.</summary>
        internal static void Tick(Player player)
        {
            if (Time.unscaledTime < s_next || !Equipment.IsLocalReady(player) || player.IsDead())
            {
                return;
            }
            s_next = Time.unscaledTime + 0.25f;
            Inventory inventory = player.GetInventory();
            ItemDrop.ItemData slot = InSlot(player);
            if (slot != null && !Topology.IsCoins(slot))
            {
                // e.g. a weapon left in this cell by the old weapon slot (0.1.0–0.3.0)
                if (Topology.FindFreeGridCell(player).x >= 0)
                {
                    Equipment.TakeOut(player, slot);
                }
                return;
            }
            if (!Plugin.AutoStoreCoins.Value)
            {
                return;
            }
            ItemDrop.ItemData drag = InventoryGui.instance != null ? Access.DragItem(InventoryGui.instance) : null;
            int grid = Topology.GridRows(player);
            bool changed = false;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToArray())
            {
                if (item == slot || item == drag || item.m_gridPos.y >= grid || !Topology.IsCoins(item))
                {
                    continue;
                }
                if (slot == null)
                {
                    item.m_gridPos = Topology.Cell(player, GearSlot.Coins);
                    slot = item;
                    changed = true;
                    continue;
                }
                int room = slot.m_shared.m_maxStackSize - slot.m_stack;
                if (room <= 0 || slot.m_worldLevel != item.m_worldLevel)
                {
                    break; // the slot is full: the rest stays in the grid
                }
                int moved = Mathf.Min(room, item.m_stack);
                slot.m_stack += moved;
                item.m_stack -= moved;
                if (item.m_stack <= 0)
                {
                    inventory.RemoveItem(item);
                }
                changed = true;
            }
            if (changed)
            {
                Access.Changed(inventory);
            }
        }
    }

    /// <summary>Coins in the coin slot weigh nothing.</summary>
    [HarmonyPatch(typeof(Inventory), "UpdateTotalWeight")]
    internal static class Inventory_UpdateTotalWeight_Coins_Patch
    {
        private static readonly AccessTools.FieldRef<Inventory, float> s_totalWeight =
            AccessTools.FieldRefAccess<Inventory, float>("m_totalWeight");

        private static void Postfix(Inventory __instance)
        {
            Player player = Player.m_localPlayer;
            if (!Topology.IsLocalInventory(__instance) || !GearholdState.IsReady(player))
            {
                return;
            }
            ItemDrop.ItemData coins = Coins.InSlot(player);
            if (Topology.IsCoins(coins))
            {
                s_totalWeight(__instance) = Mathf.Max(0f, s_totalWeight(__instance) - coins.GetWeight());
            }
        }
    }
}
