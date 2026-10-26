using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;

namespace MordasGearhold.UI
{
    /// <summary>
    /// Positions and sizes of the parts of the Gearhold panel (centre x/y, width, height in design pixels of the 460 px
    /// wide panel). The defaults are built in; a file "MordasGearhold.layout.json" in the BepInEx config folder
    /// (exported by tools/layout-editor.html) overrides any part of it.
    /// </summary>
    internal static class PanelLayout
    {
        internal struct Part
        {
            public float X, Y, W, H;
        }

        private const string Defaults = @"
Titel 230,34,420,44
Untertitel 230,64,420,20
Münzen 416,46,52,52
Set 1 90,102,128,34
Set 2 230,102,128,34
Set 3 370,102,128,34
Charakterblatt 230,343,248,394
Kopf 58,178,64,64
Brust 58,266,64,64
Beine 58,354,64,64
Cape 58,442,64,64
Schild 402,178,64,64
Trinket 1 290,570,52,52
Trinket 2 350,570,52,52
Trinket 3 410,570,52,52
Utility-Titel 118,532,160,22
Utility 1 58,570,52,52
Utility 2 118,570,52,52
Utility 3 178,570,52,52
AMMO-Knopf 461,522,14,56
Ammo-Dropdown 494,680,68,246
Ammo 1 496,588,46,46
Ammo 2 496,666,46,46
Ammo 3 496,746,46,46
Ammo-Taste 1 496,628,48,24
Ammo-Taste 2 496,706,48,24
Ammo-Taste 3 496,786,48,24
Food-Titel 120,644,160,22
Food 1 68,682,46,46
Food 2 120,682,46,46
Food 3 172,682,46,46
Food-Taste 1 68,720,48,24
Food-Taste 2 120,720,48,24
Food-Taste 3 172,720,48,24
Weapon-2-Titel 230,644,66,22
Weapon 2 230,682,46,46
Weapon-2-Taste 230,720,48,24
Mead-Titel 340,644,160,22
Mead 1 288,682,46,46
Mead 2 340,682,46,46
Mead 3 392,682,46,46
Mead-Taste 1 288,720,48,24
Mead-Taste 2 340,720,48,24
Mead-Taste 3 392,720,48,24
Quickslot-Titel 230,760,410,22
Quickslot 1 48,800,46,46
Quickslot 2 100,800,46,46
Quickslot 3 152,800,46,46
Quickslot 4 204,800,46,46
Quickslot 5 256,800,46,46
Quickslot 6 308,800,46,46
Quickslot 7 360,800,46,46
Quickslot 8 412,800,46,46
Quickslot-Taste 1 48,838,48,24
Quickslot-Taste 2 100,838,48,24
Quickslot-Taste 3 152,838,48,24
Quickslot-Taste 4 204,838,48,24
Quickslot-Taste 5 256,838,48,24
Quickslot-Taste 6 308,838,48,24
Quickslot-Taste 7 360,838,48,24
Quickslot-Taste 8 412,838,48,24
Fußzeile 230,866,370,18
HUD-Zahnrad 434,864,24,24
";

        internal const string FileName = "MordasGearhold.layout.json";
        private static readonly Dictionary<string, Part> s_parts = new Dictionary<string, Part>();
        internal static float Height { get; private set; } = 894f;

        /// <summary>Reads the built-in layout, then the optional file on top of it.</summary>
        internal static void Load()
        {
            s_parts.Clear();
            Height = 894f;
            foreach (string line in Defaults.Split('\n'))
            {
                string text = line.Trim();
                int space = text.LastIndexOf(' ');
                if (space <= 0) continue;
                string[] n = text.Substring(space + 1).Split(',');
                if (n.Length == 4)
                {
                    s_parts[text.Substring(0, space)] = new Part { X = F(n[0]), Y = F(n[1]), W = F(n[2]), H = F(n[3]) };
                }
            }
            try
            {
                string path = Path.Combine(Paths.ConfigPath, FileName);
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path);
                Match h = Regex.Match(json, "^\\s*\\{\\s*\"height\"\\s*:\\s*(-?[0-9.]+)");
                if (h.Success) Height = Math.Max(200f, Math.Min(2000f, F(h.Groups[1].Value)));
                foreach (Match m in Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\\{([^{}]*)\\}"))
                {
                    if (!s_parts.TryGetValue(m.Groups[1].Value, out Part part)) continue;
                    part.X = Field(m.Groups[2].Value, "x", part.X);
                    part.Y = Field(m.Groups[2].Value, "y", part.Y);
                    part.W = Math.Max(4f, Field(m.Groups[2].Value, "width", part.W));
                    part.H = Math.Max(4f, Field(m.Groups[2].Value, "height", part.H));
                    s_parts[m.Groups[1].Value] = part;
                }
                Plugin.Log.LogInfo("Gearhold panel layout loaded from " + path);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Gearhold: layout file ignored: " + ex.Message);
            }
        }

        internal static Part Get(string name) => s_parts.TryGetValue(name, out Part part) ? part : default;

        private static float Field(string body, string key, float fallback)
        {
            Match m = Regex.Match(body, "\"" + key + "\"\\s*:\\s*(-?[0-9.]+)");
            return m.Success ? F(m.Groups[1].Value) : fallback;
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
