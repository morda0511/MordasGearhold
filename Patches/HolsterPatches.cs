using HarmonyLib;
using MordasGearhold.Core;
using UnityEngine;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Holster slot (between food and mead): a second weapon. The vanilla hide key (R) swaps the weapon in hand
    /// with it instead of hiding the hands. Empty holster, or nothing to swap: R works like vanilla.
    /// Only the key press is caught: other calls of HideHandItems (eating, swimming) stay vanilla.
    /// </summary>
    internal static class Holster
    {
        private static int s_hideFrame = -1;

        /// <summary>A Weapon2Swap key is set: it swaps, and the hide key (R) stays vanilla.</summary>
        internal static bool HasOwnKey => Plugin.Weapon2SwapKey.Value.MainKey != KeyCode.None;

        /// <summary>The own swap key with all its modifiers is held right now.</summary>
        private static bool OwnKeyHeld()
        {
            BepInEx.Configuration.KeyboardShortcut key = Plugin.Weapon2SwapKey.Value;
            if (key.MainKey == KeyCode.None || !ZInput.GetKey(key.MainKey, false))
            {
                return false;
            }
            foreach (KeyCode modifier in key.Modifiers)
            {
                if (!ZInput.GetKey(modifier, false))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The own swap key: same rules as vanilla's hide key (not while attacking or dodging).</summary>
        internal static void SwapByKey(Player player)
        {
            if (player.InAttack() || player.InDodge() || player.IsDead() || player.InCutscene() || player.IsTeleporting())
            {
                return;
            }
            TrySwap(player);
        }
        private static float s_nextCheck;

        /// <summary>Slot switched off in the config: a weapon still lying in it goes back into the inventory.</summary>
        internal static void Tick(Player player)
        {
            if (Plugin.EnableWeapon2.Value || UnityEngine.Time.unscaledTime < s_nextCheck)
            {
                return;
            }
            s_nextCheck = UnityEngine.Time.unscaledTime + 1f;
            ItemDrop.ItemData item = Equipment.ItemIn(player, GearSlot.Holster);
            if (item != null && !item.m_equipped && Equipment.IsLocalReady(player))
            {
                Equipment.TakeOut(player, item);
            }
        }

        /// <summary>Weapon in hand that can go into the holster: right hand first, bows sit in the left hand.</summary>
        private static ItemDrop.ItemData HeldWeapon(Player player)
        {
            ItemDrop.ItemData right = Access.RightItem(player);
            if (right != null)
            {
                return Topology.IsHolsterWeapon(right) ? right : null;
            }
            ItemDrop.ItemData left = Access.LeftItem(player);
            return left != null && Topology.IsHolsterWeapon(left) ? left : null;
        }

        /// <summary>Weapon in hand into the holster, holstered weapon into the hand (they trade cells).</summary>
        internal static bool TrySwap(Player player)
        {
            if (!Plugin.EnableWeapon2.Value || !Equipment.IsLocalReady(player))
            {
                return false;
            }
            ItemDrop.ItemData stored = Equipment.ItemIn(player, GearSlot.Holster);
            if (stored == null || stored.m_equipped || !Topology.IsHolsterWeapon(stored))
            {
                return false;
            }
            ItemDrop.ItemData held = HeldWeapon(player);
            if (held == null)
            {
                return false;
            }
            player.UnequipItem(held);
            Equipment.MoveToCell(player, held, Topology.Cell(player, GearSlot.Holster)); // stored takes held's old cell
            player.EquipItem(stored);
            return true;
        }

        [HarmonyPatch(typeof(Player), "Update")]
        private static class Player_Update
        {
            private static void Prefix(Player __instance)
            {
                if (__instance == Player.m_localPlayer && (ZInput.GetButtonDown("Hide") || ZInput.GetButtonUp("JoyHide")))
                {
                    s_hideFrame = Time.frameCount;
                }
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.HideHandItems))]
        private static class Humanoid_HideHandItems
        {
            private static bool Prefix(Humanoid __instance, bool onlyRightHand, bool animation, ref bool __result)
            {
                if (HasOwnKey && s_hideFrame == Time.frameCount && OwnKeyHeld() && __instance == Player.m_localPlayer)
                {
                    // the own swap key shares the hide key (e.g. Alt+R with R = hide): that press is the swap,
                    // the hands are not put away as well
                    s_hideFrame = -1;
                    __result = false;
                    return false;
                }
                if (HasOwnKey || s_hideFrame != Time.frameCount || onlyRightHand || !animation)
                {
                    return true;
                }
                Player player = __instance as Player;
                if (player == null || player != Player.m_localPlayer || !TrySwap(player))
                {
                    return true;
                }
                s_hideFrame = -1;
                __result = true;
                return false;
            }
        }
    }
}
