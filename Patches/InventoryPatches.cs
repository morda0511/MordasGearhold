using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MordasGearhold.Core;
using UnityEngine;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Automatic placement in the player inventory only uses the visible grid; the hidden slot rows are
    /// filled on purpose only (Gearhold panel, equipping). Items keep vanilla's own top-first/bottom-first preference.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
    internal static class Inventory_FindEmptySlot_Patch
    {
        private static bool Prefix(Inventory __instance, bool topFirst, ref Vector2i __result)
        {
            Player player = Player.m_localPlayer;
            if (!Topology.IsLocalInventory(__instance) || !GearholdState.IsReady(player))
            {
                return true;
            }
            __result = Topology.FindFree(__instance, 0, Topology.GridRows(player), topFirst);
            return false;
        }
    }

    /// <summary>Free-space checks (pickup, tombstone "fits in inventory") must not count the hidden slot cells.</summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetEmptySlots))]
    internal static class Inventory_GetEmptySlots_Patch
    {
        private static void Postfix(Inventory __instance, ref int __result)
        {
            Player player = Player.m_localPlayer;
            if (Topology.IsLocalInventory(__instance) && GearholdState.IsReady(player))
            {
                __result = Topology.FreeVisibleCells(player);
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
    internal static class Inventory_HaveEmptySlot_Patch
    {
        private static void Postfix(Inventory __instance, ref bool __result)
        {
            Player player = Player.m_localPlayer;
            if (Topology.IsLocalInventory(__instance) && GearholdState.IsReady(player))
            {
                __result = Topology.FreeVisibleCells(player) > 0;
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
    internal static class Inventory_CanAddItem_Patch
    {
        private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
        {
            Player player = Player.m_localPlayer;
            if (!Topology.IsLocalInventory(__instance) || !GearholdState.IsReady(player))
            {
                return;
            }
            if (stack <= 0)
            {
                stack = item.m_stack;
            }
            __result = __instance.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel)
                + Topology.FreeVisibleCells(player) * item.m_shared.m_maxStackSize >= stack;
        }
    }

    /// <summary>
    /// Inventory.MoveAll from a tombstone into the player. Vanilla first tries the old cell, then falls back
    /// to ANY free cell, including the hidden Gearhold slot rows. Here slot items return to their slot, all
    /// other items stay in the grid; what does not fit stays in the grave. Chests keep vanilla behaviour.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll))]
    internal static class Inventory_MoveAll_Patch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory)
        {
            Player player = Player.m_localPlayer;
            if (!Topology.IsLocalInventory(__instance) || !GearholdState.IsReady(player) || !GraveMarker.IsGrave(fromInventory))
            {
                return true;
            }
            int grid = Topology.GridRows(player);
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(fromInventory.GetAllItems()))
            {
                // slot items go back into their Gearhold slot, everything else into the grid
                const int from = 0;
                int to = grid;

                bool done = false;
                Vector2i pos = item.m_gridPos;
                bool ownCell = pos.x >= 0 && pos.x < __instance.GetWidth()
                    && ((pos.y >= from && pos.y < to) || Topology.SlotAt(player, pos) != null);
                if (ownCell)
                {
                    done = Access.AddItemAt(__instance, item, item.m_stack, pos.x, pos.y);
                }
                // top up existing stacks inside the same area
                if (!done && item.m_shared.m_maxStackSize > 1)
                {
                    foreach (ItemDrop.ItemData stack in __instance.GetAllItems().ToList())
                    {
                        if (item.m_stack <= 0) break;
                        if (stack.m_gridPos.y < from || stack.m_gridPos.y >= to || !stack.IsSameType(item) || stack.m_stack >= stack.m_shared.m_maxStackSize)
                        {
                            continue;
                        }
                        done = Access.AddItemAt(__instance, item, item.m_stack, stack.m_gridPos.x, stack.m_gridPos.y);
                    }
                }
                if (!done && item.m_stack > 0)
                {
                    Vector2i free = Topology.FindFree(__instance, from, to, true);
                    if (free.x >= 0)
                    {
                        done = Access.AddItemAt(__instance, item, item.m_stack, free.x, free.y);
                    }
                }
                if (done || item.m_stack <= 0)
                {
                    fromInventory.RemoveItem(item);
                }
            }
            Access.Changed(__instance);
            Access.Changed(fromInventory);

            // Death unequipped everything; gear that is back in its slot is worn again (empty slots only).
            foreach (GearSlot slot in new[] { GearSlot.Helmet, GearSlot.Chest, GearSlot.Legs, GearSlot.Shoulder, GearSlot.Utility,
                         GearSlot.Trinket, GearSlot.Shield, GearSlot.Ammo })
            {
                Equipment.TryAutoEquip(player, Equipment.ItemIn(player, slot));
            }
            return false;
        }
    }

    /// <summary>"Stack all" into a chest never takes items out of the Gearhold slots (e.g. food in a quickslot).</summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
    internal static class Inventory_StackAll_Patch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, bool message, ref int __result)
        {
            Player player = Player.m_localPlayer;
            if (!Topology.IsLocalInventory(fromInventory) || !GearholdState.IsReady(player))
            {
                return true;
            }
            int visible = Topology.GridRows(player);
            int before = message ? __instance.CountItems(null) : 0;
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(fromInventory.GetAllItems()))
            {
                if (item.m_gridPos.y < visible && __instance.ContainsItemByName(item.m_shared.m_name)
                    && !player.IsItemEquiped(item) && __instance.AddItem(item))
                {
                    fromInventory.RemoveItem(item);
                }
            }
            int moved = __instance.CountItems(null) - before;
            if (message)
            {
                player.Message(MessageHud.MessageType.Center, moved > 0 ? "$msg_stackall " + moved : "$msg_stackall_none");
            }
            Access.Changed(__instance);
            Access.Changed(fromInventory);
            Game.instance.IncrementPlayerStat(PlayerStatType.PlaceStacks);
            __result = moved;
            return false;
        }
    }

    /// <summary>Crafting and building pay from the inventory grid first, the Gearhold slots last.</summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class Inventory_RemoveItemByName_Patch
    {
        private static bool Prefix(Inventory __instance, string name, int amount, int itemQuality, bool worldLevelBased)
        {
            Player player = Player.m_localPlayer;
            if (!Plugin.SharedFirstForCrafting.Value || !Topology.IsLocalInventory(__instance) || !GearholdState.IsReady(player))
            {
                return true;
            }
            int grid = Topology.GridRows(player);
            List<ItemDrop.ItemData> list = Access.InventoryItems(__instance);
            List<ItemDrop.ItemData> ordered = list.Where(i => i.m_gridPos.y < grid)
                .Concat(list.Where(i => i.m_gridPos.y >= grid)).ToList();
            foreach (ItemDrop.ItemData item in ordered)
            {
                if (item.m_shared.m_name == name && (itemQuality < 0 || item.m_quality == itemQuality)
                    && (!worldLevelBased || item.m_worldLevel >= Game.m_worldLevel))
                {
                    int take = Mathf.Min(item.m_stack, amount);
                    item.m_stack -= take;
                    amount -= take;
                    if (amount <= 0)
                    {
                        break;
                    }
                }
            }
            list.RemoveAll(x => x.m_stack <= 0);
            Access.Changed(__instance);
            return false;
        }
    }

    /// <summary>Items in stored sets travel with the player, so portal rules must see them too.</summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.IsTeleportable))]
    internal static class Inventory_IsTeleportable_Patch
    {
        private static void Postfix(Inventory __instance, bool allowAllItems, ref bool __result)
        {
            Player player = Player.m_localPlayer;
            if (!__result || !Topology.IsLocalInventory(__instance) || !GearholdState.IsReady(player))
            {
                return;
            }
            bool teleportAll = allowAllItems || ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll);
            for (int index = 0; index < Topology.SetCount; index++)
            {
                if (!GearholdState.TryReadSet(player, index, out SetPackage package, out _))
                {
                    continue;
                }
                foreach (byte[] raw in package.Records)
                {
                    ItemRecord record = ItemRecords.Parse(raw);
                    GameObject prefab = record.Parsed ? ObjectDB.instance.GetItemPrefab(record.PrefabHash) : null;
                    ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                    if (drop == null)
                    {
                        continue;
                    }
                    ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                    if (shared.m_toolTier >= 1000 || (!teleportAll && !shared.m_teleportable))
                    {
                        __result = false;
                        return;
                    }
                }
            }
        }
    }

    /// <summary>The vanilla player grid shows only the visible rows; the slot rows are drawn by the Gearhold panel.</summary>
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class InventoryGrid_HideSlotRows_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(InventoryGrid __instance, out int __state)
        {
            __state = -1;
            Player player = Player.m_localPlayer;
            Inventory inventory = __instance.GetInventory();
            if (!Topology.IsLocalInventory(inventory) || !GearholdState.IsReady(player))
            {
                return;
            }
            __state = inventory.GetHeight();
            inventory.SetHeight(Topology.GridRows(player));
        }

        private static void Finalizer(InventoryGrid __instance, int __state)
        {
            if (__state >= 0)
            {
                __instance.GetInventory()?.SetHeight(__state);
            }
        }
    }
}
