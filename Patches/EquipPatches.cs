using System.Collections.Generic;
using HarmonyLib;
using MordasGearhold.Core;
using UnityEngine;
using ItemType = ItemDrop.ItemData.ItemType;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Gear lives in its Gearhold slot (a hidden inventory cell) while it belongs to the set:
    ///  - equipping an item moves it into its slot; the previous occupant swaps to the item's old cell
    ///  - unequipping armour moves it out to a free grid cell
    ///  - weapon, shield, ranged and ammo slots keep their item when it is unequipped, so the shield stays
    ///    in the set's shield slot while a bow or two-hander is used
    /// </summary>
    internal static class Equipment
    {
        private static bool s_busy;

        internal static bool IsLocalReady(Humanoid humanoid)
        {
            Player player = humanoid as Player;
            return player != null && player == Player.m_localPlayer && GearholdState.IsReady(player)
                && !SetSwitcher.Switching && !GraveMarker.CreatingOwnGrave;
        }

        internal static ItemDrop.ItemData ItemIn(Player player, GearSlot slot)
        {
            Vector2i cell = Topology.Cell(player, slot);
            return player.GetInventory().GetItemAt(cell.x, cell.y);
        }

        /// <summary>The shield lying in the active set's shield slot.</summary>
        internal static ItemDrop.ItemData SetShield(Player player)
        {
            ItemDrop.ItemData item = ItemIn(player, GearSlot.Shield);
            return item != null && item.m_shared.m_itemType == ItemType.Shield ? item : null;
        }

        /// <summary>Puts an item into a slot cell. Whatever lay there takes the item's old cell (both stay the same objects).</summary>
        internal static void MoveToCell(Player player, ItemDrop.ItemData item, Vector2i cell)
        {
            if (item.m_gridPos == cell)
            {
                return;
            }
            Inventory inventory = player.GetInventory();
            ItemDrop.ItemData occupant = inventory.GetItemAt(cell.x, cell.y);
            if (occupant != null)
            {
                occupant.m_gridPos = item.m_gridPos;
            }
            item.m_gridPos = cell;
            Access.Changed(inventory);
        }

        /// <summary>Takes an item out of its slot into a free grid cell. False when the grid is full.</summary>
        internal static bool TakeOut(Player player, ItemDrop.ItemData item)
        {
            Vector2i free = Topology.FindFreeGridCell(player);
            if (free.x < 0)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_noroom");
                return false;
            }
            s_busy = true;
            try
            {
                if (player.IsItemEquiped(item))
                {
                    player.UnequipItem(item);
                }
            }
            finally
            {
                s_busy = false;
            }
            if (player.GetInventory().ContainsItem(item) && Topology.SlotAt(player, item.m_gridPos) != null)
            {
                if (Trinkets.IsExtraCell(player, item.m_gridPos) && !player.IsItemEquiped(item))
                {
                    item.m_equipped = false; // an extra trinket stops working as soon as it leaves its slot
                }
                item.m_gridPos = free;
                Access.Changed(player.GetInventory());
            }
            return true;
        }

        internal static bool Busy => s_busy;

        /// <summary>Puts a trinket or utility item into the first empty extra slot of its kind (Trinket 2/3, Utility 2/3).</summary>
        internal static bool TryFillExtraSlot(Player player, ItemDrop.ItemData item)
        {
            ItemType type = item.m_shared.m_itemType;
            if (type != ItemType.Trinket && type != ItemType.Utility)
            {
                return false;
            }
            foreach (GearSlot extra in Topology.ExtraSlots)
            {
                if (Topology.IsEnabled(extra) && Topology.ExtraType(extra) == type && ItemIn(player, extra) == null)
                {
                    MoveToCell(player, item, Topology.Cell(player, extra));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Moves an equipped item into its Gearhold slot. Ammo that already lies in one of the three ammo
        /// slots stays there; ammo from the grid takes the first empty ammo slot, or stays in the grid.
        /// </summary>
        internal static void FileIntoSlot(Player player, ItemDrop.ItemData item)
        {
            GearSlot? slot = Topology.SlotFor(item);
            if (!slot.HasValue)
            {
                return;
            }
            if (Topology.IsAmmoSlot(slot.Value))
            {
                GearSlot? current = Topology.SlotAt(player, item.m_gridPos);
                if (current.HasValue && Topology.IsAmmoSlot(current.Value))
                {
                    return;
                }
                for (int n = 0; n < Topology.AmmoSlotCount; n++)
                {
                    if (ItemIn(player, Topology.AmmoSlot(n)) == null)
                    {
                        MoveToCell(player, item, Topology.Cell(player, Topology.AmmoSlot(n)));
                        return;
                    }
                }
                return;
            }
            MoveToCell(player, item, Topology.Cell(player, slot.Value));
        }

        /// <summary>Alt+1/2/3: use the ammo of that ammo slot.</summary>
        internal static void SelectAmmo(Player player, int number)
        {
            ItemDrop.ItemData ammo = ItemIn(player, Topology.AmmoSlot(number));
            if (ammo == null)
            {
                player.Message(MessageHud.MessageType.TopLeft, $"Ammo slot {number + 1} is empty");
                return;
            }
            if (!ammo.m_equipped && player.EquipItem(ammo))
            {
                player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize(ammo.m_shared.m_name), ammo.m_stack, ammo.GetIcon());
            }
        }

        // ---- auto-unequip shield ----------------------------------------------------------------------
        // Decided one frame after a one-handed weapon was unequipped: when vanilla only swaps weapons
        // (sword -> axe, sword -> torch) the hand is filled again in the same frame and the shield stays.

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> s_hiddenRight =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem");

        private static int s_shieldCheckFrame = -1;

        internal static void ScheduleShieldCheck() => s_shieldCheckFrame = Time.frameCount;

        /// <summary>AutoShield applies to this weapon: one-handers, axes only with AutoShieldWithAxes.</summary>
        internal static bool WantsShield(ItemDrop.ItemData item) =>
            Plugin.AutoShield.Value && item != null && item.m_shared.m_itemType == ItemType.OneHandedWeapon
            && (Plugin.AutoShieldWithAxes.Value || item.m_shared.m_skillType != Skills.SkillType.Axes);

        internal static void LateShieldCheck(Player player)
        {
            if (s_shieldCheckFrame < 0 || Time.frameCount == s_shieldCheckFrame)
            {
                return;
            }
            s_shieldCheckFrame = -1;
            if (!Plugin.AutoShield.Value || !IsLocalReady(player))
            {
                return;
            }
            ItemDrop.ItemData left = Access.LeftItem(player);
            // eating hides the weapon (m_hiddenRightItem) and brings it back afterwards: keep the shield up
            if (Access.RightItem(player) == null && s_hiddenRight(player) == null
                && left != null && left.m_shared.m_itemType == ItemType.Shield)
            {
                player.UnequipItem(left);
            }
        }

        /// <summary>Equips new gear only into an EMPTY slot; never replaces what is worn.</summary>
        internal static void TryAutoEquip(Player player, ItemDrop.ItemData item)
        {
            if (!Plugin.AutoEquip.Value || item == null || s_busy || !player.GetInventory().ContainsItem(item) || player.IsItemEquiped(item))
            {
                return;
            }
            ItemDrop.ItemData right = Access.RightItem(player);
            ItemDrop.ItemData left = Access.LeftItem(player);
            bool slotFree;
            switch (item.m_shared.m_itemType)
            {
                case ItemType.Helmet: slotFree = Access.HelmetItem(player) == null; break;
                case ItemType.Chest: slotFree = Access.ChestItem(player) == null; break;
                case ItemType.Legs: slotFree = Access.LegItem(player) == null; break;
                case ItemType.Shoulder: slotFree = Access.ShoulderItem(player) == null; break;
                case ItemType.Utility: slotFree = Access.UtilityItem(player) == null; break;
                case ItemType.Trinket: slotFree = Access.TrinketItem(player) == null; break;
                case ItemType.Ammo: slotFree = Access.AmmoItem(player) == null; break;
                case ItemType.Shield:
                    slotFree = left == null && (right == null || right.m_shared.m_itemType == ItemType.OneHandedWeapon || right.m_shared.m_itemType == ItemType.Torch);
                    break;
                case ItemType.OneHandedWeapon:
                    slotFree = right == null && (left == null || left.m_shared.m_itemType == ItemType.Shield || left.m_shared.m_itemType == ItemType.Torch);
                    break;
                case ItemType.Bow:
                case ItemType.TwoHandedWeapon:
                case ItemType.TwoHandedWeaponLeft:
                    slotFree = right == null && left == null;
                    break;
                default:
                    slotFree = false;
                    break;
            }
            if (!slotFree && TryFillExtraSlot(player, item))
            {
                return; // main trinket/utility worn: an empty extra slot took it (active from there)
            }
            if (!slotFree)
            {
                return;
            }
            s_busy = true;
            try
            {
                player.EquipItem(item);
            }
            finally
            {
                s_busy = false;
            }
            // EquipItem's postfix skipped while busy: file the item into its slot now
            if (item.m_equipped)
            {
                FileIntoSlot(player, item);
            }
            if (item.m_equipped && WantsShield(item) && Access.LeftItem(player) == null)
            {
                ItemDrop.ItemData shield = SetShield(player);
                if (shield != null && !shield.m_equipped)
                {
                    player.EquipItem(shield);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class Humanoid_EquipItem_Patch
    {
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || item == null || Equipment.Busy || !Equipment.IsLocalReady(__instance))
            {
                return;
            }
            Player player = (Player)__instance;
            Equipment.FileIntoSlot(player, item);

            // one-handed weapon in hand and the shield hand empty: take the set's shield
            if (Equipment.WantsShield(item) && Access.LeftItem(player) == null)
            {
                ItemDrop.ItemData shield = Equipment.SetShield(player);
                if (shield != null && !shield.m_equipped)
                {
                    player.EquipItem(shield);
                }
            }
            // Bow / two-handed: vanilla unequips the shield; it stays in the shield slot.
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    internal static class Humanoid_UnequipItem_Patch
    {
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (item == null || item.m_equipped || Equipment.Busy || !Equipment.IsLocalReady(__instance))
            {
                return;
            }
            Player player = (Player)__instance;
            if (item.m_shared.m_itemType == ItemType.OneHandedWeapon && Plugin.AutoShield.Value)
            {
                Equipment.ScheduleShieldCheck();
            }
            if (!player.GetInventory().ContainsItem(item))
            {
                return;
            }
            GearSlot? slot = Topology.SlotAt(player, item.m_gridPos);
            if (slot.HasValue && Topology.IsArmorSlot(slot.Value))
            {
                Vector2i free = Topology.FindFreeGridCell(player);
                if (free.x >= 0)
                {
                    item.m_gridPos = free;
                    Access.Changed(player.GetInventory());
                }
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    internal static class Humanoid_Pickup_Patch
    {
        private static void Prefix(GameObject go, out ItemDrop.ItemData __state)
        {
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            __state = drop != null ? drop.m_itemData : null;
        }

        private static void Postfix(Humanoid __instance, bool autoequip, bool __result, ItemDrop.ItemData __state)
        {
            if (__result && autoequip && __state != null && Equipment.IsLocalReady(__instance))
            {
                Equipment.TryAutoEquip((Player)__instance, __state);
            }
        }
    }

    /// <summary>Crafting (and upgrading) creates a NEW item via Inventory.AddItem(name, ...) and never equips it.</summary>
    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class InventoryGui_DoCrafting_Patch
    {
        private static void Prefix(Player player, out HashSet<ItemDrop.ItemData> __state)
        {
            __state = player != null ? new HashSet<ItemDrop.ItemData>(player.GetInventory().GetAllItems()) : null;
        }

        private static void Postfix(Player player, HashSet<ItemDrop.ItemData> __state)
        {
            if (__state == null || !Equipment.IsLocalReady(player))
            {
                return;
            }
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(player.GetInventory().GetAllItems()))
            {
                if (!__state.Contains(item))
                {
                    Transmog.Unlock(player, item); // crafted: its look becomes available for transmog
                    Equipment.TryAutoEquip(player, item);
                }
            }
        }
    }
}
