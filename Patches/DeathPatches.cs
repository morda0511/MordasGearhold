using HarmonyLib;
using MordasGearhold.Core;

namespace MordasGearhold.Patches
{
    /// <summary>
    /// Death needs no transfer patch: Player.CreateTombStone moves the vanilla inventory (= inventory grid +
    /// active set's Gearhold slots) with positions into the grave, while stored sets sit in m_customData untouched.
    /// Gearhold only notes on the grave which set the gear came from.
    /// </summary>
    internal static class GraveMarker
    {
        internal const string ZdoKey = "MordasGearhold.Set";
        internal static bool CreatingOwnGrave;

        // Inventories of tombstones the player interacted with. Only for these do grid positions carry
        // meaning (Gearhold slot vs grid) when items are taken back; chest positions mean nothing.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Inventory, object> s_graves =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Inventory, object>();

        internal static void Register(Inventory inventory)
        {
            if (inventory != null && !s_graves.TryGetValue(inventory, out _))
            {
                s_graves.Add(inventory, null);
            }
        }

        internal static bool IsGrave(Inventory inventory) => inventory != null && s_graves.TryGetValue(inventory, out _);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
    internal static class Player_CreateTombStone_Patch
    {
        private static void Prefix(Player __instance)
        {
            GraveMarker.CreatingOwnGrave = __instance == Player.m_localPlayer;
            // extra trinkets carry the equipped flag; without it they go into the grave like the main trinket
            // (with "keep equipment" the vanilla trinket stays, so the extras stay too)
            if (GraveMarker.CreatingOwnGrave && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepEquip))
            {
                Trinkets.ClearFlags(__instance);
            }
        }

        private static void Finalizer() => GraveMarker.CreatingOwnGrave = false;
    }

    [HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
    internal static class TombStone_Setup_Patch
    {
        private static void Postfix(TombStone __instance)
        {
            Player player = Player.m_localPlayer;
            ZNetView view = __instance.GetComponent<ZNetView>();
            if (!GraveMarker.CreatingOwnGrave || player == null || view == null || !view.IsValid())
            {
                return;
            }
            view.GetZDO().Set(GraveMarker.ZdoKey, GearholdState.ActiveSet(player) + 1);
        }
    }

    /// <summary>
    /// Own grave holding gear of a set that is not active: switch to that set first, so the gear returns
    /// to its own rows. If the switch is not possible, open the grave window instead of auto-looting.
    /// </summary>
    [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
    internal static class TombStone_Interact_Patch
    {
        internal static bool ForceWindow;

        private static void Prefix(TombStone __instance, Humanoid character, bool hold)
        {
            ForceWindow = false;
            Player player = character as Player;
            ZNetView view = __instance.GetComponent<ZNetView>();
            GraveMarker.Register(__instance.GetComponent<Container>()?.GetInventory());
            if (hold || player == null || player != Player.m_localPlayer || !GearholdState.IsReady(player) || view == null || !view.IsValid())
            {
                return;
            }
            ZDO zdo = view.GetZDO();
            int origin = zdo.GetInt(GraveMarker.ZdoKey) - 1;
            if (origin < 0 || origin >= Topology.SetCount || zdo.GetLong(ZDOVars.s_owner) != Game.instance.GetPlayerProfile().GetPlayerID())
            {
                return;
            }
            if (origin == GearholdState.ActiveSet(player) || !HasSetGear(__instance, player))
            {
                return;
            }
            if (!SetSwitcher.TrySwitch(player, origin))
            {
                ForceWindow = true;
                player.Message(MessageHud.MessageType.Center, $"This gear belongs to set {origin + 1}");
            }
        }

        private static void Finalizer() => ForceWindow = false;

        private static bool HasSetGear(TombStone grave, Player player)
        {
            Container container = grave.GetComponent<Container>();
            foreach (ItemDrop.ItemData item in container.GetInventory().GetAllItems())
            {
                if (Topology.IsSetOwned(player, item.m_gridPos))
                {
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(TombStone), "EasyFitInInventory")]
    internal static class TombStone_EasyFit_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (TombStone_Interact_Patch.ForceWindow)
            {
                __result = false;
            }
        }
    }
}
