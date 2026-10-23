using System.Reflection;
using HarmonyLib;
using MordasGearhold.Core;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// The weapon in the Weapon 2 slot is worn on the body: on the same back / hip mounts vanilla uses for a
    /// weapon put away with R (one-handers at the hip, two-handers, atgeirs and bows on the back). Vanilla
    /// syncs these mounts, so other players see it too. A weapon vanilla itself put away keeps its mount.
    /// </summary>
    internal static class HolsterVisual
    {
        private static readonly MethodInfo SetupVis = AccessTools.Method(typeof(Humanoid), "SetupVisEquipment");
        private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> VisField =
            AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");

        private static ItemDrop.ItemData s_shown;
        private static bool s_shownEquipped;
        private static bool s_wasEnabled;

        /// <summary>Once a frame: redraw the mounts when the holster content changed (slot filled, swapped, set switched).</summary>
        internal static void Tick(Player player)
        {
            bool enabled = Plugin.EnableWeapon2.Value && Plugin.ShowWeapon2OnBody.Value;
            ItemDrop.ItemData item = Equipment.ItemIn(player, GearSlot.Holster);
            bool equipped = item != null && item.m_equipped;
            if (item == s_shown && equipped == s_shownEquipped && enabled == s_wasEnabled)
            {
                return;
            }
            s_shown = item;
            s_shownEquipped = equipped;
            s_wasEnabled = enabled;
            VisEquipment vis = VisField(player);
            if (vis != null && SetupVis != null)
            {
                SetupVis.Invoke(player, new object[] { vis, false });
            }
        }

        [HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
        private static class Humanoid_SetupVisEquipment
        {
            private static void Postfix(Humanoid __instance, VisEquipment visEq, bool isRagdoll)
            {
                if (isRagdoll || visEq == null || !Plugin.EnableWeapon2.Value || !Plugin.ShowWeapon2OnBody.Value)
                {
                    return;
                }
                Player player = __instance as Player;
                if (player == null || player != Player.m_localPlayer || !GearholdState.IsReady(player))
                {
                    return;
                }
                ItemDrop.ItemData item = Equipment.ItemIn(player, GearSlot.Holster);
                if (item == null || item.m_equipped || item.m_dropPrefab == null || !Topology.IsHolsterWeapon(item))
                {
                    return;
                }
                // the weapon's chosen look (transmog) shows on the body too; "hidden" shows nothing
                int hash = Transmog.WeaponHash(item);
                if (hash == 0)
                {
                    return;
                }
                int variant = Transmog.GetItemLook(item) == null ? item.m_variant : 0;
                bool leftFree = Access.HiddenLeftItem(player) == null;
                bool rightFree = Access.HiddenRightItem(player) == null;
                // bows are left-hand items (vanilla hides them on the left mount), everything else on the right one
                bool preferLeft = item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow
                    || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
                if (preferLeft ? leftFree : !rightFree && leftFree)
                {
                    visEq.SetLeftBackItem(hash, variant, item.m_quality);
                }
                else if (rightFree)
                {
                    visEq.SetRightBackItem(hash, item.m_quality);
                }
            }
        }
    }
}
