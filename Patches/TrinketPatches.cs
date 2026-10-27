using System.Collections.Generic;
using HarmonyLib;
using MordasGearhold.Core;
using UnityEngine;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Three trinkets at once. Trinket 1 is vanilla's own trinket slot (Humanoid.m_trinketItem). Trinket 2 and 3
    /// are "extra" trinkets: active while they lie in their Gearhold slot. Vanilla only counts m_trinketItem, so
    /// for the extras Gearhold adds what a worn trinket does (verified in Valheim 1.0.20):
    ///  - full-adrenaline effect: Player.AddAdrenaline fires it for EVERY inventory item flagged m_equipped,
    ///    so the extras only need the flag
    ///  - equipment modifiers (max adrenaline, ...): Player.UpdateModifiers sums the slot fields -> postfix below
    ///  - equip status effect: Humanoid.UpdateEquipmentStatusEffects only knows the slot fields -> kept here
    ///  - durability drain: Humanoid.UpdateEquipment drains the slot fields -> drained here
    /// </summary>
    internal static class Trinkets
    {
        private static readonly AccessTools.FieldRef<Player, float[]> s_modifierValues =
            AccessTools.FieldRefAccess<Player, float[]>("m_equipmentModifierValues");

        private static readonly System.Reflection.FieldInfo s_modifierSourcesField =
            AccessTools.Field(typeof(Player), "s_equipmentModifierSourceFields");

        private static readonly HashSet<ItemDrop.ItemData> s_flagged = new HashSet<ItemDrop.ItemData>();
        private static readonly HashSet<StatusEffect> s_effects = new HashSet<StatusEffect>();
        private static readonly List<ItemDrop.ItemData> s_extras = new List<ItemDrop.ItemData>(4);

        /// <summary>
        /// The extra trinkets and utilities that are active right now (in Trinket 2/3 or Utility 2/3, the right
        /// type, not broken, allowed in this world). Utilities work the same way since 0.14.0.
        /// </summary>
        internal static List<ItemDrop.ItemData> ActiveExtras(Player player)
        {
            s_extras.Clear();
            foreach (GearSlot slot in Topology.ExtraSlots)
            {
                ItemDrop.ItemData item = Equipment.ItemIn(player, slot);
                if (item != null && Topology.IsEnabled(slot) && CanBeActive(item, Topology.ExtraType(slot)) && !player.IsItemEquiped(item))
                {
                    s_extras.Add(item);
                }
            }
            return s_extras;
        }

        private static bool CanBeActive(ItemDrop.ItemData item, ItemDrop.ItemData.ItemType type)
        {
            if (item.m_shared.m_itemType != type)
            {
                return false;
            }
            if (item.m_shared.m_useDurability && item.m_durability <= 0f)
            {
                return false;
            }
            // same rule as Humanoid.EquipItem for trinkets in a new-game-plus world
            return !(Game.m_worldLevel > 0 && item.m_worldLevel < Game.m_worldLevel);
        }

        /// <summary>Every frame: flags, status effects and durability of the extra trinkets.</summary>
        internal static void Tick(Player player)
        {
            if (!Equipment.IsLocalReady(player) || player.IsDead())
            {
                return;
            }
            EmptyDisabledSlots(player);
            List<ItemDrop.ItemData> extras = ActiveExtras(player);

            // flags: on for active extras, off again for anything Gearhold flagged that is no longer one
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(s_flagged))
            {
                if (!extras.Contains(item))
                {
                    if (!player.IsItemEquiped(item))
                    {
                        item.m_equipped = false;
                    }
                    s_flagged.Remove(item);
                }
            }
            foreach (ItemDrop.ItemData item in extras)
            {
                item.m_equipped = true;
                s_flagged.Add(item);
            }

            // equip status effects of the extras
            HashSet<StatusEffect> wanted = new HashSet<StatusEffect>();
            foreach (ItemDrop.ItemData item in extras)
            {
                if (item.m_shared.m_equipStatusEffect != null)
                {
                    wanted.Add(item.m_shared.m_equipStatusEffect);
                }
            }
            SEMan seman = player.GetSEMan();
            ItemDrop.ItemData mainTrinket = Access.TrinketItem(player);
            ItemDrop.ItemData mainUtility = Access.UtilityItem(player);
            foreach (StatusEffect effect in s_effects)
            {
                bool fromMain = (mainTrinket != null && mainTrinket.m_shared.m_equipStatusEffect == effect)
                    || (mainUtility != null && mainUtility.m_shared.m_equipStatusEffect == effect);
                if (!wanted.Contains(effect) && !fromMain)
                {
                    seman.RemoveStatusEffect(effect.NameHash());
                }
            }
            foreach (StatusEffect effect in wanted)
            {
                if (!seman.HaveStatusEffect(effect.NameHash()))
                {
                    seman.AddStatusEffect(effect, resetTime: false, 0, 0f, -1);
                }
            }
            s_effects.Clear();
            s_effects.UnionWith(wanted);

            // durability drain like Humanoid.DrainEquipedItemDurability; a broken extra stops working but is
            // never destroyed by Gearhold
            foreach (ItemDrop.ItemData item in extras)
            {
                if (!item.m_shared.m_useDurability)
                {
                    continue;
                }
                item.m_durability -= item.m_shared.m_durabilityDrain * Time.deltaTime * Game.m_durabilityRate;
                if (item.m_durability <= 0f)
                {
                    item.m_durability = 0f;
                    player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_broke", item.m_shared.m_name), 0, item.GetIcon());
                }
            }
        }

        /// <summary>
        /// Items in a Trinket/Utility 2 or 3 slot that the config switched off go back into the grid. With a full
        /// grid they simply stay (inactive, stored in the hidden cell) until there is room; nothing is dropped.
        /// </summary>
        private static void EmptyDisabledSlots(Player player)
        {
            foreach (GearSlot slot in Topology.CountedSlots)
            {
                if (Topology.IsEnabled(slot))
                {
                    continue;
                }
                ItemDrop.ItemData item = Equipment.ItemIn(player, slot);
                if (item != null && Topology.FindFreeGridCell(player).x >= 0)
                {
                    Equipment.TakeOut(player, item);
                }
            }
        }

        /// <summary>Called by the modifier postfix: adds the extras' modifiers (max adrenaline, stamina, ...).</summary>
        internal static void AddModifiers(Player player)
        {
            if (!Equipment.IsLocalReady(player))
            {
                return;
            }
            System.Reflection.FieldInfo[] sources = s_modifierSourcesField.GetValue(null) as System.Reflection.FieldInfo[];
            float[] values = s_modifierValues(player);
            if (sources == null || values == null)
            {
                return;
            }
            foreach (ItemDrop.ItemData item in ActiveExtras(player))
            {
                for (int i = 0; i < values.Length && i < sources.Length; i++)
                {
                    if (sources[i].GetValue(item.m_shared) is float value)
                    {
                        values[i] += value;
                    }
                }
            }
        }

        /// <summary>Before saving/serialising or dying: take the flag off, so vanilla treats them as normal items.</summary>
        internal static void ClearFlags(Player player)
        {
            foreach (ItemDrop.ItemData item in s_flagged)
            {
                if (!player.IsItemEquiped(item))
                {
                    item.m_equipped = false;
                }
            }
            s_flagged.Clear();
        }

        internal static bool IsExtraCell(Player player, Vector2i pos)
        {
            GearSlot? slot = Topology.SlotAt(player, pos);
            return slot.HasValue && Topology.IsExtraSlot(slot.Value);
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateModifiers")]
    internal static class Player_UpdateModifiers_Patch
    {
        private static void Postfix(Player __instance) => Trinkets.AddModifiers(__instance);
    }

    /// <summary>
    /// Player.Load re-equips everything flagged m_equipped (EquipInventoryItems). Extra trinkets would be pushed
    /// into the single vanilla trinket slot one after another, so they lose the flag first; Trinkets.Tick sets it
    /// again once the player is ready.
    /// </summary>
    [HarmonyPatch(typeof(Player), "EquipInventoryItems")]
    internal static class Player_EquipInventoryItems_Patch
    {
        private static void Prefix(Player __instance)
        {
            if (GearholdState.ReadState(__instance).GridRows <= 0)
            {
                return; // no Gearhold layout on this character yet
            }
            foreach (ItemDrop.ItemData item in __instance.GetInventory().GetAllItems())
            {
                if (item.m_equipped && Trinkets.IsExtraCell(__instance, item.m_gridPos))
                {
                    item.m_equipped = false;
                }
            }
        }
    }
}
