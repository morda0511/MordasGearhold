using HarmonyLib;
using MordasGearhold.Core;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Spawn order (Game.SpawnPlayer): Player.Load -> Player.OnSpawned -> SetInventorySize(invrows).
    /// Vanilla's SetInventorySize clamps to 9 rows and calls DropInvalidItems, which would throw every
    /// item outside that height onto the ground. Gearhold owns the inventory height instead.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.Load))]
    internal static class Player_Load_Patch
    {
        private static void Prefix(Player __instance)
        {
            GearholdState.SetReady(__instance, false);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Player_OnSpawned_Patch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
            {
                return;
            }
            Topology.Apply(__instance);
            SetSwitcher.OnSpawned(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize))]
    internal static class Player_SetInventorySize_Patch
    {
        private static bool Prefix(Player __instance, int rows)
        {
            if (__instance != Player.m_localPlayer)
            {
                return true;
            }
            // keep the vanilla bookkeeping of purchased rows, but let Gearhold size the grid
            rows = UnityEngine.Mathf.Clamp(rows, 0, 9);
            __instance.AddUniqueKeyValue(Player.InventoryRowsKey, rows.ToString());
            Topology.Apply(__instance);
            return false;
        }
    }
}
