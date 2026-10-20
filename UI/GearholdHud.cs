namespace MordasGearhold.UI
{
    /// <summary>HUD parts of Gearhold. Only the quickslot bar; the "SET n" label under the hotbar was removed in 0.13.1.</summary>
    internal static class GearholdHud
    {
        internal static void Tick()
        {
            Hud hud = Hud.instance;
            if (hud != null)
            {
                QuickslotBar.TickAll(hud, Player.m_localPlayer);
            }
        }
    }
}
