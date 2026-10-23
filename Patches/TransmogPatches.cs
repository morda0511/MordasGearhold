using HarmonyLib;
using MordasGearhold.Core;
using MordasGearhold.UI;

namespace MordasGearhold.Patches
{
    /// <summary>Vanilla sets the visible gear from the real items; afterwards the chosen looks replace them.</summary>
    [HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
    internal static class Humanoid_SetupVisEquipment_Patch
    {
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> s_hiddenLeft =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenLeftItem");

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> s_hiddenRight =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem");

        private static void Postfix(Humanoid __instance, VisEquipment visEq, bool isRagdoll)
        {
            Player player = __instance as Player;
            if (player == null || visEq == null || !ShowsLooks(player))
            {
                return;
            }
            Transmog.Apply(player, visEq, isRagdoll, s_hiddenLeft(player), s_hiddenRight(player));
        }

        /// <summary>Your own player in the world, or the character shown on the character selection screen.</summary>
        internal static bool ShowsLooks(Player player)
        {
            return (player == Player.m_localPlayer && GearholdState.IsReady(player)) || CharacterSelectPreview.Is(player);
        }
    }

    /// <summary>
    /// Character selection: FejdStartup.SetupCharacterPreview spawns a player and loads the save into it
    /// (PlayerProfile.LoadPlayerData), so the stored looks and dyes of the active set are there; show them.
    /// </summary>
    [HarmonyPatch(typeof(FejdStartup), "SetupCharacterPreview")]
    internal static class CharacterSelectPreview
    {
        private static Player s_preview;

        internal static bool Is(Player player) => player != null && s_preview != null && player == s_preview;

        private static void Postfix(FejdStartup __instance)
        {
            s_preview = __instance.GetPreviewPlayer();
            if (s_preview != null)
            {
                Transmog.Refresh(s_preview);
            }
        }
    }

    /// <summary>
    /// Dyes are applied right after vanilla rebuilt the visible gear: UpdateEquipmentVisuals calls UpdateLodgroup
    /// only when a piece changed, so this runs on changes, not every frame.
    /// </summary>
    [HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
    internal static class VisEquipment_UpdateLodgroup_Dye_Patch
    {
        private static void Postfix(VisEquipment __instance)
        {
            Player player = __instance.GetComponent<Player>();
            if (player != null && Humanoid_SetupVisEquipment_Patch.ShowsLooks(player))
            {
                DyeRenderer.ApplyTo(player, __instance); // logs its own failures
                try
                {
                    GlowRenderer.ApplyTo(player, __instance);
                }
                catch (System.Exception ex)
                {
                    // never let a colour effect stop vanilla from building the rest of the visuals
                    Plugin.Log.LogError("Gearhold weapon glow failed: " + ex);
                }
            }
        }
    }

    /// <summary>Weapons with a look in the vanilla inventory grid get the look badge in their corner.</summary>
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class InventoryGrid_LookBadges_Patch
    {
        private static void Postfix(InventoryGrid __instance, Player player)
        {
            if (player == null || player != Player.m_localPlayer || !GearholdState.IsReady(player))
            {
                return;
            }
            Inventory inventory = __instance.GetInventory();
            if (inventory != player.GetInventory())
            {
                return;
            }
            int width = inventory.GetWidth();
            var elements = Access.GridElements(__instance);
            for (int i = 0; i < elements.Count; i++)
            {
                ItemDrop.ItemData item = inventory.GetItemAt(i % width, i / width);
                string look = item != null && Transmog.IsWeapon(item) ? Transmog.ItemMarker(item) : null;
                LookBadge.Show(elements[i].transform, look);
            }
        }
    }

    /// <summary>
    /// Right-click on an item in your inventory:
    ///  - Alt + right-click on a weapon opens its look menu
    ///  - a trinket or utility item goes into a free extra slot (2/3) when slot 1 is already worn; vanilla would
    ///    always swap it with slot 1, because the game knows only one trinket and one utility slot
    /// Everything else stays vanilla.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
    internal static class InventoryGui_OnRightClickItem_Patch
    {
        private static bool Prefix(InventoryGrid grid, ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            if (item == null || player == null || !GearholdState.IsReady(player) || grid.GetInventory() != player.GetInventory())
            {
                return true;
            }
            bool alt = ZInput.GetKey(UnityEngine.KeyCode.LeftAlt, false) || ZInput.GetKey(UnityEngine.KeyCode.RightAlt, false);
            if (alt && Transmog.IsWeapon(item))
            {
                SkinMenu.OpenForWeapon(player, item);
                return false;
            }
            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            ItemDrop.ItemData main = type == ItemDrop.ItemData.ItemType.Trinket ? Access.TrinketItem(player)
                : type == ItemDrop.ItemData.ItemType.Utility ? Access.UtilityItem(player) : null;
            if (main != null && main != item && !player.IsItemEquiped(item) && Equipment.TryFillExtraSlot(player, item))
            {
                return false;
            }
            return true;
        }
    }
}
