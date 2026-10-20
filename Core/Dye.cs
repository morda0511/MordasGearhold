using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Dye = a colour shift per visible piece and equipment set: hue in degrees (-180..180), saturation and
    /// brightness in percent (-100..100). Stored in m_customData like the looks; applied only when the gear
    /// is rebuilt or the dye changes (DyeRenderer), never by polling.
    /// </summary>
    internal struct DyeValue
    {
        public int Hue;        // degrees, -180..180
        public int Saturation; // percent, -100..100
        public int Brightness; // percent, -100..100

        public bool IsNeutral => Hue == 0 && Saturation == 0 && Brightness == 0;

        public override string ToString() => Hue + "," + Saturation + "," + Brightness;

        internal static DyeValue Parse(string raw)
        {
            DyeValue dye = new DyeValue();
            string[] parts = raw.Split(',');
            if (parts.Length == 3)
            {
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out dye.Hue);
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out dye.Saturation);
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out dye.Brightness);
            }
            return dye.Clamped();
        }

        internal DyeValue Clamped()
        {
            int hue = ((Hue + 180) % 360 + 360) % 360 - 180; // wrap around the colour wheel
            return new DyeValue
            {
                Hue = hue,
                Saturation = UnityEngine.Mathf.Clamp(Saturation, -100, 100),
                Brightness = UnityEngine.Mathf.Clamp(Brightness, -100, 100),
            };
        }
    }

    internal static class Dye
    {
        private static string Key(int set) => GearholdState.KeyPrefix + "Dye." + set;

        internal static DyeValue Get(Player player, Transmog.Piece piece)
        {
            return Read(player).TryGetValue(piece.ToString(), out DyeValue dye) ? dye : default;
        }

        internal static void Set(Player player, Transmog.Piece piece, DyeValue dye)
        {
            Dictionary<string, DyeValue> all = Read(player);
            dye = dye.Clamped();
            if (dye.IsNeutral) all.Remove(piece.ToString());
            else all[piece.ToString()] = dye;
            int set = GearholdState.ActiveSet(player);
            if (all.Count == 0) player.m_customData.Remove(Key(set));
            else player.m_customData[Key(set)] = string.Join(";", all.Select(kv => kv.Key + "=" + kv.Value));
            DyeRenderer.Apply(player);
        }

        /// <summary>All colours of one set as stored (null = none); the look menu restores this when closed without saving.</summary>
        internal static string ReadSet(Player player, int set) =>
            player.m_customData.TryGetValue(Key(set), out string raw) ? raw : null;

        internal static void WriteSet(Player player, int set, string raw)
        {
            if (string.IsNullOrEmpty(raw)) player.m_customData.Remove(Key(set));
            else player.m_customData[Key(set)] = raw;
            DyeRenderer.Apply(player);
        }

        private static Dictionary<string, DyeValue> Read(Player player)
        {
            Dictionary<string, DyeValue> result = new Dictionary<string, DyeValue>();
            if (player.m_customData.TryGetValue(Key(GearholdState.ActiveSet(player)), out string raw))
            {
                foreach (string part in raw.Split(';'))
                {
                    string[] kv = part.Split('=');
                    if (kv.Length == 2)
                    {
                        result[kv[0]] = DyeValue.Parse(kv[1]);
                    }
                }
            }
            return result;
        }
    }
}
