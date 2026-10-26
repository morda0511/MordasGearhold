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
Charakterblatt 230,363,248,434
Kopf 58,178,64,64
Brust 58,266,64,64
Beine 58,354,64,64
Cape 58,442,64,64
Schild 402,178,64,64
Trinket 1 290,618,52,52
Trinket 2 350,618,52,52
Trinket 3 410,618,52,52
Utility-Titel 118,558,160,22
Custom 1 402,266,64,64
Custom 2 402,354,64,64
Custom 3 402,442,64,64
Utility 1 58,618,52,52
Utility 2 118,618,52,52
Utility 3 178,618,52,52
AMMO-Knopf 466,522,24,56
Ammo-Dropdown 502,682,100,246
Ammo 1 502,588,46,46
Ammo 2 502,666,46,46
Ammo 3 502,746,46,46
Ammo-Taste 1 502,628,64,24
Ammo-Taste 2 502,706,64,24
Ammo-Taste 3 502,786,64,24
Food-Titel 120,684,160,22
Food 1 68,716,46,46
Food 2 120,716,46,46
Food 3 172,716,46,46
Food-Taste 1 68,750,48,24
Food-Taste 2 120,750,48,24
Food-Taste 3 172,750,48,24
Weapon-2-Titel 230,684,66,22
Weapon 2 230,716,46,46
Weapon-2-Taste 230,750,48,24
Mead-Titel 340,684,160,22
Mead 1 288,716,46,46
Mead 2 340,716,46,46
Mead 3 392,716,46,46
Mead-Taste 1 288,750,48,24
Mead-Taste 2 340,750,48,24
Mead-Taste 3 392,750,48,24
Quickslot-Titel 230,780,410,22
Quickslot 1 48,810,46,46
Quickslot 2 100,810,46,46
Quickslot 3 152,810,46,46
Quickslot 4 204,810,46,46
Quickslot 5 256,810,46,46
Quickslot 6 308,810,46,46
Quickslot 7 360,810,46,46
Quickslot 8 412,810,46,46
Quickslot-Taste 1 48,842,48,24
Quickslot-Taste 2 100,842,48,24
Quickslot-Taste 3 152,842,48,24
Quickslot-Taste 4 204,842,48,24
Quickslot-Taste 5 256,842,48,24
Quickslot-Taste 6 308,842,48,24
Quickslot-Taste 7 360,842,48,24
Quickslot-Taste 8 412,842,48,24
Fußzeile 230,870,370,18
HUD-Zahnrad 434,868,24,24
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
