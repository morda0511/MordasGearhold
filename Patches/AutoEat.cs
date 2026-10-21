using System.Collections.Generic;
using MordasGearhold.Core;
using UnityEngine;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Auto-eat from the active set: the food slots (config AutoEatFoodSlots, default on) first, then the
    /// quickslots (config AutoEat, default off).
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
            if ((!Plugin.AutoEat.Value && !Plugin.AutoEatFoodSlots.Value) || Time.time < s_next)
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
            foreach (GearSlot slot in Sources())
            {
                ItemDrop.ItemData item = Equipment.ItemIn(player, slot);
                if (item != null && Topology.IsFood(item) && player.CanEat(item, false) && player.CanConsumeItem(item, true))
                {
                    player.UseItem(null, item, true);
                    return; // one bite per tick, like a player pressing the key
                }
            }
        }

        private static IEnumerable<GearSlot> Sources()
        {
            if (Plugin.AutoEatFoodSlots.Value)
            {
                for (int i = 0; i < Topology.FoodSlotCount; i++) yield return GearSlot.Food1 + i;
            }
            if (Plugin.AutoEat.Value)
            {
                for (int i = 0; i < Topology.QuickslotCount; i++) yield return Topology.QuickSlot(i);
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
    }
}
