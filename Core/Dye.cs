using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

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

    /// <summary>
    /// The dyes of every piece of the active set. A piece has one DyeValue per colour zone (see Zones): stored as
    /// "Piece=h,s,b|h,s,b|..." in m_customData. A single value (also what 0.12–1.3 stored) colours the whole piece.
    /// </summary>
    internal static class Dye
    {
        private static string Key(int set) => GearholdState.KeyPrefix + "Dye." + set;

        /// <summary>The whole-piece dye, or the one of the first zone when the zones differ.</summary>
        internal static DyeValue Get(Player player, Transmog.Piece piece) => GetZones(player, piece)[0];

        /// <summary>One value per zone (at least one). All values equal = the whole piece has one colour.</summary>
        internal static DyeValue[] GetZones(Player player, Transmog.Piece piece)
        {
            return Read(player).TryGetValue(piece.ToString(), out DyeValue[] zones) && zones.Length > 0 ? zones : new DyeValue[1];
        }

        internal static bool IsDyed(Player player, Transmog.Piece piece)
        {
            foreach (DyeValue zone in GetZones(player, piece))
            {
                if (!zone.IsNeutral) return true;
            }
            return false;
        }

        /// <summary>Colours the whole piece (every zone) the same.</summary>
        internal static void Set(Player player, Transmog.Piece piece, DyeValue dye) => Write(player, piece, new[] { dye });

        /// <summary>Colours one zone of <paramref name="zoneCount"/>; the other zones keep what they have.</summary>
        internal static void SetZone(Player player, Transmog.Piece piece, int zone, int zoneCount, DyeValue dye)
        {
            DyeValue[] current = GetZones(player, piece);
            DyeValue[] next = new DyeValue[Mathf.Max(zoneCount, zone + 1)];
            for (int i = 0; i < next.Length; i++)
            {
                // a whole-piece value fills every zone; a zone list keeps its own values
                next[i] = current.Length == 1 ? current[0] : (i < current.Length ? current[i] : default);
            }
            next[zone] = dye;
            Write(player, piece, next);
        }

        private static void Write(Player player, Transmog.Piece piece, DyeValue[] zones)
        {
            Dictionary<string, DyeValue[]> all = Read(player);
            DyeValue[] clamped = zones.Select(z => z.Clamped()).ToArray();
            if (clamped.All(z => z.IsNeutral)) all.Remove(piece.ToString());
            else all[piece.ToString()] = clamped;
            int set = GearholdState.ActiveSet(player);
            if (all.Count == 0) player.m_customData.Remove(Key(set));
            else player.m_customData[Key(set)] = string.Join(";", all.Select(kv => kv.Key + "=" + string.Join("|", kv.Value.Select(z => z.ToString()))));
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

        private static Dictionary<string, DyeValue[]> Read(Player player)
        {
            Dictionary<string, DyeValue[]> result = new Dictionary<string, DyeValue[]>();
            if (player.m_customData.TryGetValue(Key(GearholdState.ActiveSet(player)), out string raw))
            {
                foreach (string part in raw.Split(';'))
                {
                    string[] kv = part.Split('=');
                    if (kv.Length == 2)
                    {
                        result[kv[0]] = kv[1].Split('|').Select(DyeValue.Parse).ToArray();
                    }
                }
            }
            return result;
        }
    }
}
