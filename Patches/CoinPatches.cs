using HarmonyLib;
using MordasGearhold.Core;
using UnityEngine;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// The coin slot (top right of the panel): one hidden cell shared by all sets. Coins you carry are moved into
    /// it on their own, and what lies there weighs nothing. The cell itself holds one vanilla stack (the game cuts
    /// bigger stacks on load); everything beyond goes into the purse, a plain number kept with the character
    /// (Player.m_customData "MordasGearhold.Purse"), so there is no limit. The slot shows slot + purse, traders see
    /// and take the purse too, and the slot is topped up from the purse whenever it has room. Without Gearhold the
    /// purse simply waits in the save and comes back with it.
    /// </summary>
    internal static class Coins
    {
        private const string PurseKey = GearholdState.KeyPrefix + "Purse";
        private static float s_next;

        internal static ItemDrop.ItemData InSlot(Player player) => Equipment.ItemIn(player, GearSlot.Coins);

        internal static int Purse(Player player) =>
            player != null && player.m_customData.TryGetValue(PurseKey, out string raw) && int.TryParse(raw, out int n) && n > 0 ? n : 0;

        private static void SetPurse(Player player, int amount)
        {
            if (amount > 0) player.m_customData[PurseKey] = amount.ToString();
            else player.m_customData.Remove(PurseKey);
        }

        /// <summary>Everything in the coin slot and the purse.</summary>
        internal static int Total(Player player)
        {
            ItemDrop.ItemData slot = InSlot(player);
            return (Topology.IsCoins(slot) ? slot.m_stack : 0) + Purse(player);
        }

        /// <summary>Takes coins from the purse (for buying); returns how many it took.</summary>
        internal static int TakeFromPurse(Player player, int amount)
        {
            int purse = Purse(player);
            int take = Mathf.Min(purse, Mathf.Max(0, amount));
            SetPurse(player, purse - take);
            return take;
        }

        /// <summary>
        /// A few times per second: anything that is no coin leaves the slot, loose coins fill the slot and then the
        /// purse, and the slot is topped up from the purse.
        /// </summary>
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
                // e.g. a weapon left in this cell by the old weapon slot (0.1.0-0.3.0)
                if (Topology.FindFreeGridCell(player).x >= 0)
                {
                    Equipment.TakeOut(player, slot);
                }
                return;
            }
            ItemDrop.ItemData drag = InventoryGui.instance != null ? Access.DragItem(InventoryGui.instance) : null;
            bool changed = false;
            if (Plugin.AutoStoreCoins.Value && slot != drag)
            {
                int grid = Topology.GridRows(player);
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
                    int moved = Mathf.Min(Mathf.Max(0, slot.m_shared.m_maxStackSize - slot.m_stack), item.m_stack);
                    slot.m_stack += moved;
                    // the slot is full: the rest goes into the purse
                    SetPurse(player, Purse(player) + item.m_stack - moved);
                    inventory.RemoveItem(item);
                    changed = true;
                }
            }
            changed |= TopUp(player, inventory, slot, drag);
            if (changed)
            {
                Access.Changed(inventory);
            }
        }

        /// <summary>Fills the coin slot from the purse up to one stack.</summary>
        private static bool TopUp(Player player, Inventory inventory, ItemDrop.ItemData slot, ItemDrop.ItemData drag)
        {
            int purse = Purse(player);
            if (purse <= 0 || (slot != null && slot == drag))
            {
                return false;
            }
            if (slot != null)
            {
                int room = slot.m_shared.m_maxStackSize - slot.m_stack;
                if (room <= 0) return false;
                int add = Mathf.Min(room, purse);
                slot.m_stack += add;
                SetPurse(player, purse - add);
                return true;
            }
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Coins") : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                return false;
            }
            ItemDrop.ItemData coins = drop.m_itemData.Clone();
            coins.m_dropPrefab = prefab; // without it the item could not be saved
            int amount = Mathf.Min(purse, coins.m_shared.m_maxStackSize);
            coins.m_stack = amount;
            Vector2i cell = Topology.Cell(player, GearSlot.Coins);
            if (!Access.AddItemAt(inventory, coins, amount, cell.x, cell.y))
            {
                return false;
            }
            SetPurse(player, purse - amount);
            return true;
        }
    }

    /// <summary>Counting coins (traders, costs) includes the purse.</summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems))]
    internal static class Inventory_CountItems_Purse_Patch
    {
        private static void Postfix(Inventory __instance, string name, ref int __result)
        {
            if (name == Topology.CoinsName && Topology.IsLocalInventory(__instance) && GearholdState.IsReady(Player.m_localPlayer))
            {
                __result += Coins.Purse(Player.m_localPlayer);
            }
        }
    }

    /// <summary>Paying coins (buying) takes what the carried coins lack from the purse.</summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class Inventory_RemoveItem_Purse_Patch
    {
        private static void Prefix(Inventory __instance, string name, ref int amount)
        {
            Player player = Player.m_localPlayer;
            if (name != Topology.CoinsName || !Topology.IsLocalInventory(__instance) || !GearholdState.IsReady(player))
            {
                return;
            }
            int carried = 0;
            foreach (ItemDrop.ItemData item in __instance.GetAllItems())
            {
                if (Topology.IsCoins(item)) carried += item.m_stack;
            }
            if (amount > carried)
            {
                amount -= Coins.TakeFromPurse(player, amount - carried);
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
