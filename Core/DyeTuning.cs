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
    internal static class DyeTuning
    {
        internal const string FileName = "MordasGearhold.dye.json";

        internal static int StartClusters = 6;
        internal static int MaxZones = 4;
        internal static float MinShare = 0.025f;
        internal static float MergeDistance = 0.20f;
        internal static float BrightnessWeight = 0.6f;
        internal static int Sample = 96;
        internal static float Softness = 0.03f;
        internal static float SatMax = 1.5f;
        internal static float ValMax = 2f;
        internal static float SatFollow = 1f;
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
                DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (stamp == s_stamp) return false;
                s_stamp = stamp;
                Load(stamp == DateTime.MinValue ? null : File.ReadAllText(path));
                Plugin.Log.LogInfo(stamp == DateTime.MinValue ? "Gearhold dye numbers: file gone, defaults." : "Gearhold dye numbers loaded from " + FileName);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Gearhold dye numbers: could not read the file: " + ex.Message);
                return false;
            }
        }

        private static void Load(string json)
        {
            StartClusters = Mathf.RoundToInt(Number(json, "startClusters", 6f, 2f, 12f));
            MaxZones = Mathf.RoundToInt(Number(json, "maxZones", 4f, 1f, 8f));
            MinShare = Number(json, "minShare", 0.025f, 0f, 0.2f);
            MergeDistance = Number(json, "merge", 0.20f, 0.01f, 1f);
            BrightnessWeight = Number(json, "bw", 0.6f, 0f, 3f);
            Sample = Mathf.RoundToInt(Number(json, "sample", 96f, 24f, 256f));
            Softness = Number(json, "soft", 0.03f, 0.001f, 0.5f);
            SatMax = Number(json, "satMax", 1.5f, 1f, 5f);
            ValMax = Number(json, "valMax", 2f, 1f, 6f);
            SatFollow = Number(json, "satFollow", 1f, 0f, 1f);
            ValFollow = Number(json, "valFollow", 1f, 0f, 1f);
            Version++;
            Zones.Forget();
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
