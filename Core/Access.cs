using System;
using System.Collections.Generic;
using HarmonyLib;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Reflection access to private vanilla members. Every member here was checked against the
    /// decompiled assembly_valheim of Valheim 1.0.20 (Stage-0 report, section C).
    /// </summary>
    internal static class Access
    {
        internal static readonly AccessTools.FieldRef<Inventory, List<ItemDrop.ItemData>> InventoryItems =
            AccessTools.FieldRefAccess<Inventory, List<ItemDrop.ItemData>>("m_inventory");

        // private bool Inventory.AddItem(ItemData item, int amount, int x, int y, bool skipValidPositionCheck)
        // Places a CLONE at exactly (x, y); merges only into a same-type stack lying on that very cell.
        private static readonly Func<Inventory, ItemDrop.ItemData, int, int, int, bool, bool> AddItemAtDelegate =
            AccessTools.MethodDelegate<Func<Inventory, ItemDrop.ItemData, int, int, int, bool, bool>>(
                AccessTools.Method(typeof(Inventory), "AddItem",
                    new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) }));

        private static readonly Action<Inventory, bool, bool> ChangedDelegate =
            AccessTools.MethodDelegate<Action<Inventory, bool, bool>>(
                AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) }));

        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItem = Slot("m_rightItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItem = Slot("m_leftItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HelmetItem = Slot("m_helmetItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> ChestItem = Slot("m_chestItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LegItem = Slot("m_legItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> ShoulderItem = Slot("m_shoulderItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> UtilityItem = Slot("m_utilityItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> TrinketItem = Slot("m_trinketItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> AmmoItem = Slot("m_ammoItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenRightItem = Slot("m_hiddenRightItem");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenLeftItem = Slot("m_hiddenLeftItem");

        internal static readonly AccessTools.FieldRef<Player, List<Player.MinorActionData>> ActionQueue =
            AccessTools.FieldRefAccess<Player, List<Player.MinorActionData>>("m_actionQueue");

        internal static readonly AccessTools.FieldRef<Player, bool> IsLoading =
            AccessTools.FieldRefAccess<Player, bool>("m_isLoading");

        internal static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem =
            AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");

        internal static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> GridElements =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        private static readonly Action<InventoryGui, ItemDrop.ItemData, Inventory, int> SetupDragItemDelegate =
            AccessTools.MethodDelegate<Action<InventoryGui, ItemDrop.ItemData, Inventory, int>>(
                AccessTools.Method(typeof(InventoryGui), "SetupDragItem",
                    new[] { typeof(ItemDrop.ItemData), typeof(Inventory), typeof(int) }));

        private static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Slot(string name)
        {
            return AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>(name);
        }

        internal static bool AddItemAt(Inventory inventory, ItemDrop.ItemData item, int amount, int x, int y)
        {
            return AddItemAtDelegate(inventory, item, amount, x, y, false);
        }

        internal static void Changed(Inventory inventory)
        {
            ChangedDelegate(inventory, false, false);
        }

        internal static void ClearDrag(InventoryGui gui)
        {
            SetupDragItemDelegate(gui, null, null, 1);
        }

        /// <summary>Picks an item up on the cursor exactly like a left-click in the vanilla inventory grid.</summary>
        internal static void StartDrag(InventoryGui gui, ItemDrop.ItemData item, Inventory inventory)
        {
            SetupDragItemDelegate(gui, item, inventory, item.m_stack);
        }
    }
}
