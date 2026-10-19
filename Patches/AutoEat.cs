using MordasGearhold.Core;
using UnityEngine;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Optional auto-eat (config AutoEat, default off): eats food from the active set's quickslots.
    ///  - always when one of the three food slots is empty
    ///  - with AutoEatEarly also as soon as vanilla allows eating a food again (Food.CanEatAgain, half its time)
    /// Eligibility is asked with Player.CanEat(item, showMessages: false), so nothing is spammed, and eating
    /// goes through the normal UseItem path (animation, effects, stack - 1).
    /// </summary>
    internal static class AutoEat
    {
        private const float Interval = 1f;
        private static float s_next;

        internal static void Tick(Player player)
        {
            if (!Plugin.AutoEat.Value || Time.time < s_next)
            {
                return;
            }
            s_next = Time.time + Interval;
            if (!Equipment.IsLocalReady(player) || player.IsDead() || player.IsTeleporting() || player.InCutscene()
                || player.InAttack() || player.InDodge() || InventoryGui.IsVisible())
            {
                return;
            }
            if (!NeedsFood(player))
            {
                return;
            }
            for (int i = 0; i < Topology.QuickslotCount; i++)
            {
                ItemDrop.ItemData item = Equipment.ItemIn(player, Topology.QuickSlot(i));
                if (IsFood(item) && player.CanEat(item, false) && player.CanConsumeItem(item, true))
                {
                    player.UseItem(null, item, true);
                    return; // one bite per tick, like a player pressing the key
                }
            }
        }

        private static bool NeedsFood(Player player)
        {
            var foods = player.GetFoods();
            if (foods.Count < 3)
            {
                return true;
            }
            if (!Plugin.AutoEatEarly.Value)
            {
                return false;
            }
            foreach (Player.Food food in foods)
            {
                if (food.CanEatAgain())
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsFood(ItemDrop.ItemData item)
        {
            return item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable
                && (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f || item.m_shared.m_foodEitr > 0f);
        }
    }
}
