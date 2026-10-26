using System.Collections.Generic;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Look presets: the looks (transmog) and colours of the five visible pieces of the active set, saved under a
    /// number and applied to whichever set is active later. Weapon looks and weapon colours belong to the weapons
    /// themselves and are not part of a preset. Stored in the player's custom data like everything else.
    /// </summary>
    internal static class LookPresets
    {
        internal const int Count = 8;
        private const char Separator = '\u001f';

        private static string Key(int index) => GearholdState.KeyPrefix + "Preset." + index;

        internal static bool Has(Player player, int index) => player.m_customData.ContainsKey(Key(index));

        /// <summary>Stores the looks and colours of the active set in a preset slot.</summary>
        internal static void Save(Player player, int index)
        {
            int set = GearholdState.ActiveSet(player);
            string looks = Transmog.ReadSetLooks(player, set) ?? "";
            string dyes = Dye.ReadSet(player, set) ?? "";
            player.m_customData[Key(index)] = looks + Separator + dyes;
        }

        internal static void Clear(Player player, int index)
        {
            player.m_customData.Remove(Key(index));
            player.m_customData.Remove(NameKey(index));
        }

        private static string NameKey(int index) => GearholdState.KeyPrefix + "PresetName." + index;

        internal static string NameOf(Player player, int index) =>
            player.m_customData.TryGetValue(NameKey(index), out string name) && name.Length > 0 ? name : "Preset " + (index + 1);

        internal static void SetName(Player player, int index, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 18) name = name.Substring(0, 18);
            if (name.Length == 0 || name == "Preset " + (index + 1)) player.m_customData.Remove(NameKey(index));
            else player.m_customData[NameKey(index)] = name;
        }

        /// <summary>Puts a preset on the active set (the character changes at once).</summary>
        internal static void Apply(Player player, int index)
        {
            if (!TryRead(player, index, out string looks, out string dyes))
            {
                return;
            }
            int set = GearholdState.ActiveSet(player);
            Transmog.WriteSetLooks(player, set, looks);
            Dye.WriteSet(player, set, dyes);
        }

        private static bool TryRead(Player player, int index, out string looks, out string dyes)
        {
            looks = dyes = null;
            if (!player.m_customData.TryGetValue(Key(index), out string raw))
            {
                return false;
            }
            string[] parts = raw.Split(Separator);
            looks = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : null;
            dyes = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
            return true;
        }

        /// <summary>The look of every piece in a preset (prefab name, "-" = hidden, null = the piece's own look), in piece order.</summary>
        internal static string[] LooksOf(Player player, int index, Transmog.Piece[] pieces)
        {
            string[] result = new string[pieces.Length];
            if (!TryRead(player, index, out string looks, out _) || looks == null)
            {
                return result;
            }
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (string part in looks.Split(';'))
            {
                string[] kv = part.Split('=');
                if (kv.Length == 2 && kv[1].Length > 0) map[kv[0]] = kv[1];
            }
            for (int i = 0; i < pieces.Length; i++)
            {
                map.TryGetValue(pieces[i].ToString(), out result[i]);
            }
            return result;
        }

        /// <summary>True when a preset colours at least one piece.</summary>
        internal static bool HasColors(Player player, int index) =>
            TryRead(player, index, out _, out string dyes) && dyes != null;
    }
}
