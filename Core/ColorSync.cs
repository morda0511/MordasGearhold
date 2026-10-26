using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Multiplayer colours. Your game writes the dyes of the worn pieces and the dye / glow / shine of the weapons in
    /// your hands and on your body into your player's network data (one short text, only when it changed); the games
    /// of the other players read it and colour your character with the same code that colours their own.
    /// Players without Gearhold simply see the normal colours; the server needs nothing.
    /// Text format: "1;Helmet=h,s,b|h,s,b;Chest=...;R=dye~glow~shine;L=...;RB=...;LB=..." (R/L hands, RB/LB body mounts).
    /// </summary>
    internal static class ColorSync
    {
        internal const string ZdoKey = "MordasGearhold.Colors";
        private const string Version = "1";
        private const int MaxLength = 2048;
        private const int MaxZones = 8;

        internal const int Right = 0, Left = 1, RightBack = 2, LeftBack = 3;
        private static readonly string[] s_weaponKeys = { "R", "L", "RB", "LB" };

        internal struct WeaponColors
        {
            public DyeValue[] Dyes;
            public GlowValue Glow, Shine;
        }

        internal sealed class Record
        {
            internal string Raw = "";
            internal int Id;
            internal readonly Dictionary<string, DyeValue[]> Pieces = new Dictionary<string, DyeValue[]>();
            internal readonly WeaponColors[] Weapons = new WeaponColors[4];

            internal Record()
            {
                for (int i = 0; i < Weapons.Length; i++)
                {
                    Weapons[i].Dyes = new DyeValue[1];
                }
            }

            internal DyeValue[] Piece(Transmog.Piece piece) =>
                Pieces.TryGetValue(piece.ToString(), out DyeValue[] zones) && zones.Length > 0 ? zones : new DyeValue[1];

            internal WeaponColors Weapon(int index) => Weapons[index];
        }

        // ------------------------------------------------------------------ sending

        private static float s_nextSend;
        private static string s_sent;

        /// <summary>Text of your current colours (empty = nothing is coloured).</summary>
        internal static string Build(Player player)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Transmog.Piece piece in Enum.GetValues(typeof(Transmog.Piece)))
            {
                if (!Dye.IsDyed(player, piece)) continue;
                sb.Append(';').Append(piece).Append('=').Append(string.Join("|", Dye.GetZones(player, piece).Select(z => z.ToString())));
            }
            ItemDrop.ItemData holster = MordasGearhold.Patches.Equipment.ItemIn(player, GearSlot.Holster);
            AppendWeapon(sb, 0, Access.RightItem(player));
            AppendWeapon(sb, 1, Access.LeftItem(player));
            AppendWeapon(sb, 2, Access.HiddenRightItem(player) ?? holster);
            AppendWeapon(sb, 3, Access.HiddenLeftItem(player) ?? holster);
            return sb.Length == 0 ? "" : Version + sb;
        }

        private static void AppendWeapon(StringBuilder sb, int index, ItemDrop.ItemData item)
        {
            if (item == null || !Transmog.IsWeapon(item)) return;
            string dye = Dye.ReadItem(item) ?? "";
            string glow = Glow.Get(item).ToString();
            string shine = Glow.GetShine(item).ToString();
            if (dye.Length == 0 && glow.Length == 0 && shine.Length == 0) return;
            sb.Append(';').Append(s_weaponKeys[index]).Append('=').Append(dye).Append('~').Append(glow).Append('~').Append(shine);
        }

        private static void Send(Player player)
        {
            ZNetView view = player.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() && view.IsOwner() ? view.GetZDO() : null;
            if (zdo == null) return;
            string text = Build(player);
            if (text.Length > MaxLength) text = "";
            if (text == s_sent && zdo.GetString(ZdoKey, "") == text) return;
            zdo.Set(ZdoKey, text);
            s_sent = text;
        }

        // ------------------------------------------------------------------ receiving

        private static readonly Dictionary<Player, Record> s_records = new Dictionary<Player, Record>();
        private static readonly List<Player> s_gone = new List<Player>();

        /// <summary>The colours another player sent (null for you, for players without Gearhold, and when switched off).</summary>
        internal static bool TryGet(Player player, out Record record)
        {
            record = null;
            return player != null && player != Player.m_localPlayer && s_records.TryGetValue(player, out record) && record != null;
        }

        private static void Receive(Player me)
        {
            bool show = Plugin.ShowOthersColors.Value;
            foreach (Player other in Player.GetAllPlayers())
            {
                if (other == null || other == me) continue;
                string raw = "";
                if (show)
                {
                    ZNetView view = other.GetComponent<ZNetView>();
                    ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                    raw = zdo != null ? zdo.GetString(ZdoKey, "") : "";
                }
                s_records.TryGetValue(other, out Record known);
                if (known == null ? raw.Length == 0 : known.Raw == raw)
                {
                    continue; // nothing new
                }
                Record parsed = Parse(raw);
                parsed.Id = other.GetInstanceID();
                // an empty record stays until the next real one so the old colours get cleared once
                s_records[other] = parsed;
                VisEquipment vis = other.GetComponent<VisEquipment>();
                if (vis != null)
                {
                    DyeRenderer.ApplyTo(other, vis);
                    try
                    {
                        GlowRenderer.ApplyTo(other, vis);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError("Gearhold weapon glow failed: " + ex);
                    }
                }
            }
            s_gone.Clear();
            foreach (Player p in s_records.Keys)
            {
                if (p == null) s_gone.Add(p);
            }
            foreach (Player p in s_gone)
            {
                if (s_records.TryGetValue(p, out Record gone)) TextureDye.Forget(gone.Id);
                s_records.Remove(p);
            }
        }

        /// <summary>Reads the text defensively: it comes from another computer, so every value is clamped and bad parts are dropped.</summary>
        internal static Record Parse(string raw)
        {
            Record record = new Record { Raw = raw ?? "" };
            if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength)
            {
                return record;
            }
            string[] parts = raw.Split(';');
            if (parts.Length == 0 || parts[0] != Version)
            {
                return record;
            }
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                string key = parts[i].Substring(0, eq);
                string value = parts[i].Substring(eq + 1);
                int weapon = Array.IndexOf(s_weaponKeys, key);
                if (weapon >= 0)
                {
                    string[] f = value.Split('~');
                    if (f.Length != 3) continue;
                    record.Weapons[weapon] = new WeaponColors
                    {
                        Dyes = ParseDyes(f[0]),
                        Glow = GlowValue.Parse(f[1]),
                        Shine = GlowValue.Parse(f[2]),
                    };
                }
                else if (Enum.IsDefined(typeof(Transmog.Piece), key))
                {
                    record.Pieces[key] = ParseDyes(value);
                }
            }
            return record;
        }

        private static DyeValue[] ParseDyes(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new DyeValue[1];
            DyeValue[] zones = raw.Split('|').Take(MaxZones).Select(DyeValue.Parse).ToArray();
            return zones.Length > 0 ? zones : new DyeValue[1];
        }

        /// <summary>The colouring numbers changed (dye file): colour your character and the others again.</summary>
        private static void ReapplyAll(Player me)
        {
            DyeRenderer.Apply(me);
            foreach (KeyValuePair<Player, Record> pair in s_records)
            {
                VisEquipment vis = pair.Key != null ? pair.Key.GetComponent<VisEquipment>() : null;
                if (vis != null)
                {
                    DyeRenderer.ApplyTo(pair.Key, vis);
                }
            }
        }

        // ------------------------------------------------------------------ once per half second

        internal static void Tick(Player me)
        {
            if (Time.unscaledTime < s_nextSend)
            {
                return;
            }
            s_nextSend = Time.unscaledTime + 0.5f;
            try
            {
                if (DyeTuning.Tick()) ReapplyAll(me);
                Send(me);
                Receive(me);
            }
            catch (Exception ex)
            {
                s_nextSend = Time.unscaledTime + 5f;
                Plugin.Log.LogError("Gearhold colour sync failed: " + ex);
            }
        }
    }
}
