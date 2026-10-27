using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Makes a colour zone blink on the character (about 1.7 s) when it is chosen in the look menu, so it is clear what
    /// the colour controls will change. Only the display is changed: the saved colours are never touched. While
    /// "on", the zone is drawn in a bright yellow through the normal colouring (DyeRenderer asks Modify).
    /// </summary>
    internal static class ZoneFlash
    {
        private const float Period = 0.22f;
        private const float Duration = 1.7f;

        private static Player s_player;
        private static int s_piece = -1;
        private static ItemDrop.ItemData s_weapon;
        private static int s_zone;
        private static int s_count;
        private static float s_start;
        private static bool s_active;
        private static bool s_on;

        private static readonly DyeValue Highlight = DyeValue.Target(new Color(1f, 0.85f, 0.2f));

        internal static void Start(Player player, Transmog.Piece? piece, ItemDrop.ItemData weapon, int zone, int zoneCount)
        {
            s_player = player;
            s_piece = piece.HasValue ? (int)piece.Value : -1;
            s_weapon = weapon;
            s_zone = zone;
            s_count = Mathf.Max(1, zoneCount);
            s_start = Time.unscaledTime;
            s_active = true;
            s_on = true;
            DyeRenderer.Apply(player);
        }

        internal static void Stop()
        {
            if (!s_active)
            {
                return;
            }
            s_active = false;
            s_on = false;
            if (s_player != null) DyeRenderer.Apply(s_player);
        }

        /// <summary>Every frame while the look menu is open.</summary>
        internal static void Tick()
        {
            if (!s_active)
            {
                return;
            }
            float t = Time.unscaledTime - s_start;
            if (t >= Duration || s_player == null)
            {
                Stop();
                return;
            }
            bool on = (int)(t / Period) % 2 == 0;
            if (on != s_on)
            {
                s_on = on;
                DyeRenderer.Apply(s_player);
            }
        }

        internal static DyeValue[] Modify(Player player, Transmog.Piece piece, DyeValue[] dyes) =>
            s_active && s_on && s_weapon == null && player == s_player && s_piece == (int)piece ? Flashed(dyes) : dyes;

        internal static DyeValue[] ModifyItem(ItemDrop.ItemData item, DyeValue[] dyes) =>
            s_active && s_on && s_weapon != null && ReferenceEquals(item, s_weapon) ? Flashed(dyes) : dyes;

        private static DyeValue[] Flashed(DyeValue[] dyes)
        {
            int n = Mathf.Max(s_count, dyes.Length);
            DyeValue[] result = new DyeValue[n];
            for (int i = 0; i < n; i++)
            {
                // one stored value colours every zone; a list keeps its own values
                result[i] = dyes.Length == 1 ? dyes[0] : (i < dyes.Length ? dyes[i] : default);
                if (s_zone < 0 || i == s_zone) result[i] = Highlight;
            }
            return result;
        }
    }
}
