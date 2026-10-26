using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// The numbers behind the colouring (zone detection and the way a zone takes its colour). They can be changed while
    /// the game runs: the file "MordasGearhold.dye.json" in the BepInEx config folder (exported by tools/dye-lab.html) is
    /// read again whenever it changes, the colour zones are found anew and the characters are recoloured.
    /// </summary>
    /// <summary>The numbers of the zone detection: global, or the own set of one texture (MordasGearhold.zones.json).</summary>
    internal sealed class ZoneParams
    {
        public int StartClusters = 12, MaxZones = 8, Sample = 96;
        public float MinShare = 0.012f, MergeDistance = 0.12f, BrightnessWeight = 0.7f, ChromaWeight = 6f, Softness = 0.12f;
    }

    internal static class DyeTuning
    {
        internal const string ZonesFileName = "MordasGearhold.zones.json";

        /// <summary>The global zone numbers (replaced on every reload).</summary>
        internal static ZoneParams Default = new ZoneParams();

        private static readonly System.Collections.Generic.Dictionary<string, ZoneParams> s_perTexture = new System.Collections.Generic.Dictionary<string, ZoneParams>();

        /// <summary>The zone numbers for a texture by its name: its own set when the zones file has one, else the global ones.</summary>
        internal static ZoneParams ParamsFor(string textureName) =>
            textureName != null && s_perTexture.TryGetValue(textureName, out ZoneParams own) ? own : Default;

        internal const string FileName = "MordasGearhold.dye.json";

        internal static float Detail = 1f;
        internal static float ValMax = 3f;
        internal static float ValFollow = 1f;

        /// <summary>Changes with every reload: part of the cache keys of recoloured textures.</summary>
        internal static int Version { get; private set; }

        private static DateTime s_stamp = DateTime.MinValue;
        private static float s_next;

        /// <summary>Checks the file about once a second (called by the colour sync tick); true when new numbers were loaded.</summary>
        internal static bool Tick()
        {
            if (Time.unscaledTime < s_next) return false;
            s_next = Time.unscaledTime + 1f;
            try
            {
                string path = Path.Combine(Paths.ConfigPath, FileName);
                string zonesPath = Path.Combine(Paths.ConfigPath, ZonesFileName);
                DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                DateTime zonesStamp = File.Exists(zonesPath) ? File.GetLastWriteTimeUtc(zonesPath) : DateTime.MinValue;
                DateTime both = stamp > zonesStamp ? stamp : zonesStamp;
                if (both == s_stamp) return false;
                s_stamp = both;
                Load(stamp == DateTime.MinValue ? null : File.ReadAllText(path), zonesStamp == DateTime.MinValue ? null : File.ReadAllText(zonesPath));
                Plugin.Log.LogInfo(stamp == DateTime.MinValue ? "Gearhold dye numbers: file gone, defaults." : "Gearhold dye numbers loaded from " + FileName);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Gearhold dye numbers: could not read the file: " + ex.Message);
                return false;
            }
        }

        private static void Load(string json, string zonesJson)
        {
            Detail = Number(json, "detail", 1f, 0f, 2f);
            ValMax = Number(json, "valMax", 3f, 1f, 6f);
            ValFollow = Number(json, "valFollow", 1f, 0f, 1f);
            Default = ReadZoneParams(json, null);
            s_perTexture.Clear();
            if (zonesJson != null)
            {
                foreach (Match m in Regex.Matches(zonesJson, @"""([^""]+)""\s*:\s*\{([^{}]*)\}"))
                {
                    s_perTexture[m.Groups[1].Value] = ReadZoneParams(m.Groups[2].Value, Default);
                }
            }
            Version++;
            Zones.Forget();
        }

        /// <summary>Zone numbers from a json text; keys that are missing keep the value of <paramref name="baseline"/> (or the built-in default).</summary>
        private static ZoneParams ReadZoneParams(string json, ZoneParams baseline)
        {
            ZoneParams b = baseline ?? new ZoneParams();
            return new ZoneParams
            {
                StartClusters = Mathf.RoundToInt(Number(json, "startClusters", b.StartClusters, 2f, 16f)),
                MaxZones = Mathf.RoundToInt(Number(json, "maxZones", b.MaxZones, 1f, 8f)),
                MinShare = Number(json, "minShare", b.MinShare, 0f, 0.2f),
                MergeDistance = Number(json, "merge", b.MergeDistance, 0.01f, 2f),
                BrightnessWeight = Number(json, "bw", b.BrightnessWeight, 0f, 3f),
                ChromaWeight = Number(json, "wc", b.ChromaWeight, 0f, 20f),
                Sample = Mathf.RoundToInt(Number(json, "sample", b.Sample, 24f, 256f)),
                Softness = Number(json, "soft", b.Softness, 0.001f, 1f),
            };
        }

        private static float Number(string json, string key, float fallback, float min, float max)
        {
            if (json == null) return fallback;
            Match m = Regex.Match(json, "\"" + key + @"""\s*:\s*(-?[0-9.]+(?:[eE]-?[0-9]+)?)");
            return m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? Mathf.Clamp(value, min, max) : fallback;
        }
    }
}
