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

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Path of Valheiman (treextr) has its own equipment system ("5. Controls / Equipment system": slots beside the
    /// crafting window, purse, tool bar, transmog). It keeps those slots in rows directly under the inventory grid,
    /// the same rows Gearhold uses for its sets, so the two would overwrite each other's cells. Path of Valheiman
    /// offers an API for exactly this case, Valheiman.Api.Equipment.TurnOff(owner): its equipment system stays off
    /// (like the config switch), never drops items, and moves anything still lying in its old cells back into the
    /// backpack once. Everything else of Path of Valheiman (passive tree, loot, crafting ...) keeps working.
    /// Called from Plugin.Awake, before any character loads; Path of Valheiman is loaded first (soft dependency).
    /// </summary>
    internal static class PathOfValheiman
    {
        internal const string Guid = "treextr.pathofvalheiman";

        internal static bool Present => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(Guid);

        private static bool s_implicitsResolved;
        private static MethodInfo s_implicits;
        private static PropertyInfo s_implicitText;

        /// <summary>
        /// The implicit lines of an item (Path of Valheiman belts, rings, ...: what the item itself always does, e.g.
        /// "+50 carry weight"), read through Valheiman.Api.Items.Implicits. Empty without Path of Valheiman.
        /// </summary>
        internal static List<string> Implicits(ItemDrop.ItemData item)
        {
            List<string> result = new List<string>();
            if (item == null || !Present)
            {
                return result;
            }
            if (!s_implicitsResolved)
            {
                s_implicitsResolved = true;
                Type items = AccessTools.TypeByName("Valheiman.Api.Items");
                Type info = AccessTools.TypeByName("Valheiman.Api.ImplicitInfo");
                s_implicits = items != null ? AccessTools.Method(items, "Implicits", new[] { typeof(ItemDrop.ItemData) }) : null;
                s_implicitText = info != null ? AccessTools.Property(info, "Text") : null;
            }
            if (s_implicits == null || s_implicitText == null)
            {
                return result;
            }
            try
            {
                if (s_implicits.Invoke(null, new object[] { item }) is System.Collections.IEnumerable list)
                {
                    foreach (object line in list)
                    {
                        if (s_implicitText.GetValue(line) is string text && text.Length > 0)
                        {
                            result.Add(text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug("Path of Valheiman implicits unavailable: " + ex.Message);
            }
            return result;
        }

        private static bool s_affixesResolved;
        private static MethodInfo s_affixes;
        private static PropertyInfo s_affixText;
        private static PropertyInfo s_affixLocal;

        /// <summary>
        /// The enchantments (affixes) of an item as Path of Valheiman shows them: its text and whether it works on
        /// the item itself (local) or on the character. Empty without Path of Valheiman or for unidentified items.
        /// Read through Valheiman.Api.Items.Affixes, so nothing of Path of Valheiman is changed.
        /// </summary>
        internal static List<(string text, bool local)> Affixes(ItemDrop.ItemData item)
        {
            List<(string, bool)> result = new List<(string, bool)>();
            if (item == null || !Present)
            {
                return result;
            }
            if (!s_affixesResolved)
            {
                s_affixesResolved = true;
                Type items = AccessTools.TypeByName("Valheiman.Api.Items");
                Type info = AccessTools.TypeByName("Valheiman.Api.AffixInfo");
                s_affixes = items != null ? AccessTools.Method(items, "Affixes", new[] { typeof(ItemDrop.ItemData) }) : null;
                s_affixText = info != null ? AccessTools.Property(info, "Text") : null;
                s_affixLocal = info != null ? AccessTools.Property(info, "Local") : null;
            }
            if (s_affixes == null || s_affixText == null)
            {
                return result;
            }
            try
            {
                if (s_affixes.Invoke(null, new object[] { item }) is System.Collections.IEnumerable list)
                {
                    foreach (object affix in list)
                    {
                        string text = s_affixText.GetValue(affix) as string;
                        bool local = s_affixLocal != null && s_affixLocal.GetValue(affix) is bool l && l;
                        if (!string.IsNullOrEmpty(text))
                        {
                            result.Add((text, local));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug("Path of Valheiman affixes unavailable: " + ex.Message);
            }
            return result;
        }

        internal static void TakeOverEquipment()
        {
            if (!Present)
            {
                return;
            }
            try
            {
                Type api = AccessTools.TypeByName("Valheiman.Api.Equipment");
                MethodInfo turnOff = api != null ? AccessTools.Method(api, "TurnOff", new[] { typeof(string) }) : null;
                if (turnOff == null)
                {
                    Plugin.Log.LogWarning("Path of Valheiman found, but without its equipment API: turn its \"Equipment system\" " +
                        "off in its config (5. Controls), or both mods use the same inventory rows.");
                    return;
                }
                turnOff.Invoke(null, new object[] { Plugin.ModName });
                Plugin.Log.LogInfo("Path of Valheiman found: its equipment system is turned off, Gearhold keeps the equipment slots.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not turn off Path of Valheiman's equipment system: " + ex.Message);
            }
        }
    }
}

namespace MordasGearhold.Patches
{
    /// <summary>
    /// With its equipment system off, Path of Valheiman still attaches its old rows once on load when the character
    /// was saved with them ("the save still holds equipment cells"), and rearranges the rows under the grid until it
    /// has handed them back. Under Gearhold those rows are the set slots, so that never happens: Path of Valheiman
    /// is told the save holds none of its rows, and Gearhold's own load check (SetSwitcher.MoveOrphansToGrid) moves
    /// anything that does not belong into a slot into the inventory instead.
    /// </summary>
    [HarmonyPatch]
    internal static class PathOfValheiman_HoldsOurRows_Patch
    {
        private static MethodBase Target() =>
            AccessTools.Method(AccessTools.TypeByName("PassiveTree.Gear.EquipRows"), "HoldsOurRows");

        private static bool Prepare() => PathOfValheiman.Present && Target() != null;

        private static MethodBase TargetMethod() => Target();

        private static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }
}

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Path of Valheiman's own prefix on Player.SetInventorySize (EquipRows.GameRows) rebuilds and shifts the rows
    /// under the grid on load, even with its equipment system off when the save still remembers its rows. Under
    /// Gearhold it is skipped and SetInventorySize runs as without Path of Valheiman (Gearhold's own patch keeps the
    /// layout). The HoldsOurRows patch above covers the same case; this one also holds if the JIT inlined it.
    /// </summary>
    [HarmonyPatch]
    internal static class PathOfValheiman_GameRows_Patch
    {
        private static MethodBase Target()
        {
            Type rows = AccessTools.TypeByName("PassiveTree.Gear.EquipRows");
            Type gameRows = rows != null ? AccessTools.Inner(rows, "GameRows") : null;
            return gameRows != null ? AccessTools.Method(gameRows, "Prefix") : null;
        }

        private static bool Prepare() => PathOfValheiman.Present && Target() != null;

        private static MethodBase TargetMethod() => Target();

        private static bool Prefix(ref bool __result)
        {
            __result = true; // "run the original SetInventorySize"
            return false;
        }
    }
}
