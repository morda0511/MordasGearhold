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
Charakterblatt 230,483,248,674
Kopf 58,178,64,64
Brust 58,266,64,64
Beine 58,354,64,64
Cape 58,442,64,64
Schild 402,178,64,64
Trinket 1 308,856,46,46
Trinket 2 360,856,46,46
Trinket 3 412,856,46,46
Utility-Titel 118,778,160,22
Custom 1 402,262,52,52
Custom-Taste 1 402,320,56,22
Custom 2 402,358,52,52
Custom-Taste 2 402,416,56,22
Custom 3 402,454,52,52
Custom-Taste 3 402,512,56,22
Utility 1 48,856,46,46
Utility 2 100,856,46,46
Utility 3 152,856,46,46
AMMO-Knopf 461,522,14,56
Ammo-Dropdown 508,618,96,246
Ammo 1 508,526,46,46
Ammo 2 508,605,46,46
Ammo 3 508,684,46,46
Ammo-Taste 1 508,566,64,24
Ammo-Taste 2 508,645,64,24
Ammo-Taste 3 508,724,64,24
Food-Titel 100,910,156,22
Food 1 48,945,46,46
Food 2 100,945,46,46
Food 3 152,945,46,46
Food-Taste 1 48,979,48,24
Food-Taste 2 100,979,48,24
Food-Taste 3 152,979,48,24
Weapon-2-Titel 230,910,66,22
Weapon 2 230,945,46,46
Weapon-2-Taste 230,979,48,24
Mead-Titel 360,910,156,22
Mead 1 308,945,46,46
Mead 2 360,945,46,46
Mead 3 412,945,46,46
Mead-Taste 1 308,979,48,24
Mead-Taste 2 360,979,48,24
Mead-Taste 3 412,979,48,24
Quickslot-Titel 230,1000,410,22
Quickslot 1 48,1020,46,46
Quickslot 2 100,1020,46,46
Quickslot 3 152,1020,46,46
Quickslot 4 204,1020,46,46
Quickslot 5 256,1020,46,46
Quickslot 6 308,1020,46,46
Quickslot 7 360,1020,46,46
Quickslot 8 412,1020,46,46
Quickslot-Taste 1 48,1052,48,24
Quickslot-Taste 2 100,1052,48,24
Quickslot-Taste 3 152,1052,48,24
Quickslot-Taste 4 204,1052,48,24
Quickslot-Taste 5 256,1052,48,24
Quickslot-Taste 6 308,1052,48,24
Quickslot-Taste 7 360,1052,48,24
Quickslot-Taste 8 412,1052,48,24
Fußzeile 230,1078,370,18
HUD-Zahnrad 368,46,26,26
Custom 4 402,550,52,52
Custom-Taste 4 402,608,56,22
Custom 5 402,646,52,52
Custom-Taste 5 402,704,56,22
Custom 6 58,550,52,52
Custom-Taste 6 58,608,56,22
Custom 7 58,646,52,52
Custom-Taste 7 58,704,56,22
Custom 8 58,742,52,52
Custom-Taste 8 58,800,56,22
Custom 9 402,742,52,52
Custom-Taste 9 402,800,56,22
";

        internal const string FileName = "MordasGearhold.layout.json";
        private static readonly Dictionary<string, Part> s_parts = new Dictionary<string, Part>();
        internal static float Height { get; private set; } = 1090f;

        /// <summary>Reads the built-in layout, then the optional file on top of it.</summary>
        internal static void Load()
        {
            s_parts.Clear();
            Height = 1090f;
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
