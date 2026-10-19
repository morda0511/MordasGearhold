using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Everything Gearhold persists lives in <c>Player.m_customData</c>, which vanilla saves and loads
    /// verbatim (Player.Save / Player.Load) and never prunes, so stored sets survive an uninstall.
    ///
    /// Ownership rule: the active set lives in the hidden Gearhold slot rows of the vanilla inventory; every inactive
    /// set lives in its package. The package of the ACTIVE set only ever holds records that could not
    /// become live items (dormant: missing prefab, unreadable, oversized stack).
    /// </summary>
    internal static class GearholdState
    {
        internal const string KeyPrefix = "MordasGearhold.";
        private const string StateKey = KeyPrefix + "State";
        private const string JournalKey = KeyPrefix + "Journal";
        private const string QuarantinePrefix = KeyPrefix + "Quarantine.";

        private sealed class Runtime
        {
            public bool Ready;
        }

        private static readonly ConditionalWeakTable<Player, Runtime> s_runtime = new ConditionalWeakTable<Player, Runtime>();

        internal struct State
        {
            public int Active;
            public int GridRows;   // visible inventory rows; 0 = never applied on this character
            public int HiddenRows; // 0 = character from 0.1.0 (no slot rows yet)
        }

        internal static string SetKey(int index) => KeyPrefix + "Set." + index;

        internal static bool IsReady(Player player) => player != null && s_runtime.GetOrCreateValue(player).Ready;

        internal static void SetReady(Player player, bool ready) => s_runtime.GetOrCreateValue(player).Ready = ready;

        internal static int ActiveSet(Player player) => ReadState(player).Active;

        internal static State ReadState(Player player)
        {
            State state = new State();
            if (player.m_customData.TryGetValue(StateKey, out string raw))
            {
                int legacySetRows = 0, legacySharedRows = 0;
                foreach (string part in raw.Split(';'))
                {
                    string[] kv = part.Split('=');
                    if (kv.Length != 2 || !int.TryParse(kv[1], out int value))
                    {
                        continue;
                    }
                    switch (kv[0])
                    {
                        case "a": state.Active = Math.Max(0, Math.Min(Topology.SetCount - 1, value)); break;
                        case "r": state.GridRows = value; break;
                        case "g": state.HiddenRows = value; break;
                        // 0.1.0 / 0.2.0 split the grid into set rows (s) and shared rows (h): together they are the grid now
                        case "s": legacySetRows = value; break;
                        case "h": legacySharedRows = value; break;
                    }
                }
                if (state.GridRows <= 0 && legacySetRows + legacySharedRows > 0)
                {
                    state.GridRows = legacySetRows + legacySharedRows;
                }
            }
            return state;
        }

        internal static void WriteState(Player player, State state)
        {
            player.m_customData[StateKey] = "v=3;a=" + state.Active + ";r=" + state.GridRows + ";g=" + state.HiddenRows;
        }

        /// <summary>Reads a stored set. A missing key is an empty set; a damaged one is reported, never altered.</summary>
        internal static bool TryReadSet(Player player, int index, out SetPackage package, out string error)
        {
            error = null;
            if (!player.m_customData.TryGetValue(SetKey(index), out string raw) || string.IsNullOrEmpty(raw))
            {
                package = new SetPackage { SetIndex = index };
                return true;
            }
            if (!SetPackage.TryDecode(raw, out package, out error))
            {
                package = null;
                return false;
            }
            package.SetIndex = index;
            return true;
        }

        internal static void WriteSet(Player player, SetPackage package)
        {
            if (package.IsEmpty)
            {
                player.m_customData.Remove(SetKey(package.SetIndex));
            }
            else
            {
                player.m_customData[SetKey(package.SetIndex)] = package.Encode();
            }
        }

        /// <summary>Moves a value out of the way, unchanged, so nothing can overwrite or discard it.</summary>
        internal static void Quarantine(Player player, string key, string reason)
        {
            if (!player.m_customData.TryGetValue(key, out string value))
            {
                return;
            }
            string target = QuarantinePrefix + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + key.Substring(KeyPrefix.Length);
            player.m_customData[target] = reason.Replace('|', '/') + "|" + value;
            player.m_customData.Remove(key);
            Plugin.Log.LogError("Quarantined " + key + " -> " + target + ": " + reason);
        }

        internal static int QuarantineCount(Player player) => player.m_customData.Keys.Count(k => k.StartsWith(QuarantinePrefix, StringComparison.Ordinal));

        // ---------------------------------------------------------------- journal

        internal sealed class Journal
        {
            public int From;
            public int To;
            public string OutFingerprint;
            public string InFingerprint;
            public string OutPackage;
            public string InPackage;
        }

        internal static void WriteJournal(Player player, Journal journal)
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(1);
            pkg.Write(journal.From);
            pkg.Write(journal.To);
            pkg.Write(journal.OutFingerprint);
            pkg.Write(journal.InFingerprint);
            pkg.Write(journal.OutPackage ?? "");
            pkg.Write(journal.InPackage ?? "");
            player.m_customData[JournalKey] = pkg.GetBase64();
        }

        internal static void ClearJournal(Player player) => player.m_customData.Remove(JournalKey);

        internal static bool TryReadJournal(Player player, out Journal journal, out bool present)
        {
            journal = null;
            present = player.m_customData.TryGetValue(JournalKey, out string raw);
            if (!present)
            {
                return false;
            }
            try
            {
                ZPackage pkg = new ZPackage(raw);
                pkg.ReadInt();
                journal = new Journal
                {
                    From = pkg.ReadInt(),
                    To = pkg.ReadInt(),
                    OutFingerprint = pkg.ReadString(),
                    InFingerprint = pkg.ReadString(),
                    OutPackage = pkg.ReadString(),
                    InPackage = pkg.ReadString(),
                };
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Unreadable switch journal: " + ex.Message);
                return false;
            }
        }

        internal static string JournalKeyName => JournalKey;

        // ---------------------------------------------------------------- fingerprints

        /// <summary>Fingerprint of live set items in logical positions, comparable with record fingerprints.</summary>
        internal static string Fingerprint(Player player, IEnumerable<ItemDrop.ItemData> items)
        {
            return string.Join(",", items
                .Select(i =>
                {
                    Vector2i pos = Topology.ToLogical(player, i.m_gridPos);
                    return (i.m_dropPrefab != null ? i.m_dropPrefab.name.GetStableHashCode() : 0) + ":" + i.m_stack + ":" + i.m_quality + ":" + pos.x + ":" + pos.y;
                })
                .OrderBy(s => s, StringComparer.Ordinal));
        }

        internal static string Fingerprint(IEnumerable<ItemRecord> records)
        {
            return string.Join(",", records.Where(r => r.Parsed)
                .Select(r => r.PrefabHash + ":" + r.Header.m_stack + ":" + r.Header.m_quality + ":" + r.Header.m_gridPos.x + ":" + r.Header.m_gridPos.y)
                .OrderBy(s => s, StringComparer.Ordinal));
        }
    }
}
