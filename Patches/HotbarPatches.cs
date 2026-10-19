using HarmonyLib;
using UnityEngine;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// The vanilla hotbar reacts to 1..8 regardless of modifiers, so Alt+1 (ammo slot 1) would also use
    /// hotbar item 1. While a Gearhold hotkey with modifiers on that digit is held, the hotbar stays quiet.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]
    internal static class Player_UseHotbarItem_Patch
    {
        private static bool Prefix(Player __instance, int index)
        {
            if (__instance != Player.m_localPlayer || index < 0 || index > 9)
            {
                return true;
            }
            return !Plugin.ModifiedHotkeyHeld(KeyCode.Alpha0 + index);
        }
    }
}
