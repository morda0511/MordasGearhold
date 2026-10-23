using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Applies dyes to ONE character's visible gear, never to shared assets (unlike WackysDatabase, which edits
    /// the item prefabs' shared materials so every copy of an item changes):
    ///  - attached pieces (helmet, cape, shield, chest/leg models) use the Custom/Creature shader, which has
    ///    _Hue/_Saturation/_Value: set per renderer with a MaterialPropertyBlock
    ///  - the painted part of chest and legs lives in the body material (Custom/Player, _ChestTex/_LegsTex) with no
    ///    colour control of its own: replaced by a recoloured copy of the texture (cached, made once per change)
    /// Called only when the gear was rebuilt (VisEquipment.UpdateLodgroup), the dye changed or the set switched.
    /// Findings from the 0.11.x VisualProbe log.
    /// </summary>
    internal static class DyeRenderer
    {
        private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> s_vis = AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_helmet = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_helmetItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_left = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_leftBack = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftBackItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, List<GameObject>> s_chest = AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_chestItemInstances");
        private static readonly AccessTools.FieldRef<VisEquipment, List<GameObject>> s_legs = AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_legItemInstances");
        private static readonly AccessTools.FieldRef<VisEquipment, List<GameObject>> s_shoulder = AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_shoulderItemInstances");
        private static readonly AccessTools.FieldRef<VisEquipment, int> s_chestHash = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentChestItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> s_legHash = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLegItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> s_leftHash = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLeftItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> s_leftBackHash = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLeftBackItemHash");

        private static readonly int s_hue = Shader.PropertyToID("_Hue");
        private static readonly int s_saturation = Shader.PropertyToID("_Saturation");
        private static readonly int s_value = Shader.PropertyToID("_Value");
        private static readonly int s_chestTex = Shader.PropertyToID("_ChestTex");
        private static readonly int s_legsTex = Shader.PropertyToID("_LegsTex");

        private static readonly MaterialPropertyBlock s_block = new MaterialPropertyBlock();

        /// <summary>The texture that shows a piece's colours (for the colour swatches): painted body texture or model texture.</summary>
        internal static Texture BaseTexture(Player player, Transmog.Piece piece)
        {
            VisEquipment vis = player != null ? s_vis(player) : null;
            if (vis == null) return null;
            if (piece == Transmog.Piece.Chest)
            {
                Texture painted = OriginalBody(s_chestTex, s_chestHash(vis));
                if (painted != null) return painted;
            }
            if (piece == Transmog.Piece.Legs)
            {
                Texture painted = OriginalBody(s_legsTex, s_legHash(vis));
                if (painted != null) return painted;
            }
            List<GameObject> objects = new List<GameObject>();
            switch (piece)
            {
                case Transmog.Piece.Helmet: objects.Add(s_helmet(vis)); break;
                case Transmog.Piece.Chest: if (s_chest(vis) != null) objects.AddRange(s_chest(vis)); break;
                case Transmog.Piece.Legs: if (s_legs(vis) != null) objects.AddRange(s_legs(vis)); break;
                case Transmog.Piece.Shoulder: if (s_shoulder(vis) != null) objects.AddRange(s_shoulder(vis)); break;
                case Transmog.Piece.Shield: objects.Add(s_left(vis)); objects.Add(s_leftBack(vis)); break;
            }
            foreach (GameObject go in objects)
            {
                if (go == null) continue;
                foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material != null && material.HasProperty(s_hue) && material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
                        {
                            return material.GetTexture("_MainTex");
                        }
                    }
                }
            }
            return null;
        }

        private static Texture OriginalBody(int textureId, int itemHash)
        {
            if (itemHash == 0) return null;
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemHash) : null;
            Material armor = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_armorMaterial : null;
            return armor != null ? armor.GetTexture(textureId) : null;
        }


        internal static void Apply(Player player)
        {
            VisEquipment vis = player != null ? s_vis(player) : null;
            if (vis != null)
            {
                ApplyTo(player, vis);
            }
        }

        /// <summary>The colour zones of a piece, from the texture that shows its colours (null without one).</summary>
        internal static ZoneSet ZonesOf(Player player, Transmog.Piece piece) => Zones.Of(BaseTexture(player, piece));

        /// <summary>What to do for one piece: one colour for all of it, or a dye per colour zone.</summary>
        private sealed class Plan
        {
            public DyeValue[] Dyes;
            public ZoneSet Zones; // only when the zones are dyed differently
            public bool Uniform => Zones == null;
            public DyeValue First => Dyes[0];
        }

        private static Plan PlanFor(Player player, Transmog.Piece piece)
        {
            DyeValue[] dyes = Dye.GetZones(player, piece);
            bool same = true;
            for (int i = 1; i < dyes.Length; i++)
            {
                if (dyes[i].Hue != dyes[0].Hue || dyes[i].Saturation != dyes[0].Saturation || dyes[i].Brightness != dyes[0].Brightness)
                {
                    same = false;
                }
            }
            ZoneSet zones = same ? null : ZonesOf(player, piece);
            if (zones == null || zones.Count < 2)
            {
                return new Plan { Dyes = dyes }; // one colour for the whole piece
            }
            DyeValue[] fitted = new DyeValue[zones.Count]; // missing zones stay as they are, extra ones are dropped
            for (int i = 0; i < fitted.Length; i++)
            {
                fitted[i] = i < dyes.Length ? dyes[i] : default;
            }
            return new Plan { Dyes = fitted, Zones = zones };
        }

        internal static void ApplyTo(Player player, VisEquipment vis)
        {
            DyeObject(s_helmet(vis), PlanFor(player, Transmog.Piece.Helmet));
            Plan chest = PlanFor(player, Transmog.Piece.Chest);
            DyeObjects(s_chest(vis), chest);
            Plan legs = PlanFor(player, Transmog.Piece.Legs);
            DyeObjects(s_legs(vis), legs);
            DyeObjects(s_shoulder(vis), PlanFor(player, Transmog.Piece.Shoulder));
            Plan shield = PlanFor(player, Transmog.Piece.Shield);
            if (IsShield(s_leftHash(vis))) DyeObject(s_left(vis), shield);
            if (IsShield(s_leftBackHash(vis))) DyeObject(s_leftBack(vis), shield);

            if (vis.m_bodyModel != null)
            {
                Material body = vis.m_bodyModel.material; // vanilla already works on this per-character copy
                DyeBody(body, s_chestTex, s_chestHash(vis), chest);
                DyeBody(body, s_legsTex, s_legHash(vis), legs);
            }
        }

        private static bool IsShield(int hash)
        {
            GameObject prefab = hash != 0 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(hash) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
        }

        // ------------------------------------------------------------------ attached pieces

        // renderers that carry a recoloured main texture in their property block (cleared again when the dye is uniform)
        private static readonly HashSet<Renderer> s_textured = new HashSet<Renderer>();

        private static void DyeObjects(List<GameObject> objects, Plan plan)
        {
            if (objects == null) return;
            foreach (GameObject go in objects) DyeObject(go, plan);
        }

        private static void DyeObject(GameObject go, Plan plan)
        {
            if (go == null) return;
            s_textured.RemoveWhere(r => r == null);
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue; // no particles, flares, ...
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null || !material.HasProperty(s_hue)) continue; // e.g. Standard shader: no HSV
                    if (plan.Uniform && s_textured.Remove(renderer))
                    {
                        renderer.SetPropertyBlock(null, i); // back from zones: drop the recoloured texture
                    }
                    DyeValue dye = plan.First;
                    if (plan.Uniform && dye.IsNeutral)
                    {
                        renderer.SetPropertyBlock(null, i);
                        continue;
                    }
                    renderer.GetPropertyBlock(s_block, i);
                    Texture main = !plan.Uniform && material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    if (main != null)
                    {
                        // colour zones: this model's own texture with every zone dyed; the shader HSV stays neutral
                        s_block.SetTexture("_MainTex", TextureDye.GetZoned(main, plan.Zones, plan.Dyes, false));
                        s_block.SetFloat(s_hue, 0f);
                        s_block.SetFloat(s_saturation, 0f);
                        s_block.SetFloat(s_value, 0f);
                        s_textured.Add(renderer);
                    }
                    else
                    {
                        s_block.SetFloat(s_hue, Scale(material.shader, "_Hue", dye.Hue / 180f, hue: true));
                        s_block.SetFloat(s_saturation, Scale(material.shader, "_Saturation", dye.Saturation / 100f, hue: false));
                        s_block.SetFloat(s_value, Scale(material.shader, "_Value", dye.Brightness / 100f, hue: false));
                    }
                    renderer.SetPropertyBlock(s_block, i);
                }
            }
        }

        /// <summary>
        /// Maps -1..1 onto the shader's own range (read with GetPropertyRangeLimits instead of guessing):
        /// hue covers half the range per direction (a full turn), saturation/brightness reach the range ends.
        /// </summary>
        private static float Scale(Shader shader, string property, float amount, bool hue)
        {
            int index = shader.FindPropertyIndex(property);
            Vector2 range = index >= 0 ? shader.GetPropertyRangeLimits(index) : new Vector2(-1f, 1f);
            if (hue)
            {
                return amount * (range.y - range.x) / 2f;
            }
            return amount >= 0f ? amount * range.y : -amount * range.x;
        }

        // ------------------------------------------------------------------ painted chest / legs

        private static void DyeBody(Material body, int textureId, int itemHash, Plan plan)
        {
            Texture original = OriginalBody(textureId, itemHash);
            if (original == null) return;
            Texture wanted;
            if (!plan.Uniform) wanted = TextureDye.GetZoned(original, plan.Zones, plan.Dyes, true);
            else wanted = plan.First.IsNeutral ? original : TextureDye.Get(original, plan.First);
            if (body.GetTexture(textureId) != wanted)
            {
                body.SetTexture(textureId, wanted);
            }
        }
    }


    /// <summary>
    /// Recoloured copies of textures, made on the GPU-to-CPU path (works for non-readable textures):
    /// Blit -> ReadPixels -> HSV shift per pixel -> upload. At most 1024 px per side, a few entries cached.
    /// The alpha channel is the armour's coverage on the body (alpha 0 = skin shows through): the shift is
    /// weighted by it, so skin and soft edges keep their colour (0.12.0 shifted every pixel fully).
    /// </summary>
    internal static class TextureDye
    {
        private const int MaxSize = 1024;
        private const int CacheSize = 16;
        private static readonly List<(Texture source, string dye, Texture2D copy)> s_cache = new List<(Texture, string, Texture2D)>();
        private static readonly Dictionary<Texture, Vector3> s_dominant = new Dictionary<Texture, Vector3>();

        // ------------------------------------------------------------------ colour zones

        private sealed class ZonedPixels
        {
            public Color32[] Source;
            public byte[] Weights; // per pixel one byte per zone, summing to about 255
            public int Width, Height, Zones;
        }

        private const int ZonedCacheSize = 4;
        private const float Softness = 0.03f; // how softly one zone fades into the next (squared colour distance)
        private static readonly List<(Texture source, int zones, ZonedPixels data)> s_zoned = new List<(Texture, int, ZonedPixels)>();

        /// <summary>Frees the pixel data kept for fast re-colouring (the look menu closed); the coloured copies stay.</summary>
        internal static void ReleaseZoneData() => s_zoned.Clear();

        /// <summary>
        /// A colour of a zone moved by the zone's dye. The zone is one material, so every pixel of it gets the same
        /// hue: the zone's own hue turned by the dye (a zone dyed Red is red all over, also the odd accent colour
        /// that fell into it, and grey pixels, whose own hue is only noise, follow too). Saturation and brightness
        /// keep each pixel's own shading and are pushed like for any pixel.
        /// </summary>
        internal static Color ShiftHsv(float hue, float saturation, float value, DyeValue dye, float zoneHue)
        {
            float h = zoneHue + dye.Hue / 360f;
            h -= Mathf.Floor(h);
            float sat = dye.Saturation / 100f, val = dye.Brightness / 100f;
            float s = sat >= 0f ? saturation + (1f - saturation) * sat : saturation * (1f + sat);
            float v = val >= 0f ? value + (1f - value) * val : value * (1f + val);
            return Color.HSVToRGB(h, s, v);
        }

        /// <summary>The result of a dye on one colour of a zone (for the preview chips in the menu).</summary>
        internal static Color ShiftColor(Color colour, DyeValue dye)
        {
            Color.RGBToHSV(colour, out float h, out float s, out float v);
            return dye.IsNeutral ? colour : ShiftHsv(h, s, v, dye, h);
        }

        private static ZonedPixels PixelsOf(Texture source, ZoneSet zones)
        {
            foreach (var entry in s_zoned)
            {
                if (entry.source == source && entry.zones == zones.Id)
                {
                    return entry.data;
                }
            }
            Texture2D read = ReadBack(source, MaxSize, false);
            ZonedPixels data = new ZonedPixels { Source = read.GetPixels32(), Width = read.width, Height = read.height, Zones = zones.Count };
            Object.Destroy(read);
            int n = data.Source.Length, z = zones.Count;
            data.Weights = new byte[n * z];
            float[] e = new float[z];
            for (int i = 0; i < n; i++)
            {
                Color.RGBToHSV(data.Source[i], out float h, out float s, out float v);
                Vector3 f = Zones.Feature(h, s, v);
                float best = float.MaxValue;
                for (int k = 0; k < z; k++)
                {
                    e[k] = (f - zones.Centers[k]).sqrMagnitude;
                    if (e[k] < best) best = e[k];
                }
                float sum = 0f;
                for (int k = 0; k < z; k++)
                {
                    e[k] = Mathf.Exp(-(e[k] - best) / Softness);
                    sum += e[k];
                }
                for (int k = 0; k < z; k++)
                {
                    data.Weights[i * z + k] = (byte)Mathf.RoundToInt(e[k] / sum * 255f);
                }
            }
            s_zoned.Add((source, zones.Id, data));
            if (s_zoned.Count > ZonedCacheSize)
            {
                s_zoned.RemoveAt(0);
            }
            return data;
        }

        /// <summary>
        /// A copy of the texture with every colour zone dyed on its own: each pixel belongs softly to the zones
        /// nearest in colour and takes the weighted mix of their results. <paramref name="coverage"/>: the alpha
        /// channel is the armour's coverage on the body (skin shows through where it is 0), like in Make.
        /// </summary>
        internal static Texture GetZoned(Texture source, ZoneSet zones, DyeValue[] dyes, bool coverage)
        {
            string key = "z" + zones.Id + (coverage ? "b" : "m") + string.Join("|", System.Array.ConvertAll(dyes, d => d.ToString()));
            foreach (var entry in s_cache)
            {
                if (entry.source == source && entry.dye == key && entry.copy != null)
                {
                    return entry.copy;
                }
            }
            Texture2D copy = MakeZoned(source, zones, dyes, coverage);
            s_cache.Add((source, key, copy));
            if (s_cache.Count > CacheSize)
            {
                Object.Destroy(s_cache[0].copy);
                s_cache.RemoveAt(0);
            }
            return copy;
        }

        private static Texture2D MakeZoned(Texture source, ZoneSet zones, DyeValue[] dyes, bool coverage)
        {
            ZonedPixels data = PixelsOf(source, zones);
            int n = data.Source.Length, z = zones.Count;
            float[] zoneHue = new float[z];
            bool[] active = new bool[z];
            for (int k = 0; k < z; k++)
            {
                Color.RGBToHSV(zones.Chips[k], out zoneHue[k], out float _, out float _);
                active[k] = k < dyes.Length && !dyes[k].IsNeutral;
            }
            Color32[] result = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                Color32 p = data.Source[i];
                if (coverage && p.a == 0)
                {
                    result[i] = p; // skin shows through here
                    continue;
                }
                int touched = 0, sum = 0;
                for (int k = 0; k < z; k++)
                {
                    int w = data.Weights[i * z + k];
                    sum += w;
                    if (active[k]) touched += w;
                }
                if (touched < 2 || sum == 0)
                {
                    result[i] = p; // this pixel belongs to zones that stay as they are
                    continue;
                }
                Color.RGBToHSV(p, out float h, out float s, out float v);
                float r = 0f, g = 0f, b = 0f;
                for (int k = 0; k < z; k++)
                {
                    float w = data.Weights[i * z + k] / (float)sum;
                    if (w <= 0f) continue;
                    Color c = active[k] ? ShiftHsv(h, s, v, dyes[k], zoneHue[k]) : new Color(p.r / 255f, p.g / 255f, p.b / 255f);
                    r += c.r * w;
                    g += c.g * w;
                    b += c.b * w;
                }
                Color32 mixed = new Color(r, g, b);
                if (coverage)
                {
                    mixed = Color32.Lerp(p, mixed, p.a / 255f);
                }
                result[i] = new Color32(mixed.r, mixed.g, mixed.b, p.a);
            }
            Texture2D copy = new Texture2D(data.Width, data.Height, TextureFormat.RGBA32, true) { name = source.name + "_gearhold_zones" };
            copy.SetPixels32(result);
            copy.Apply(true, true); // mipmaps, then free the CPU copy
            return copy;
        }

        internal static Texture Get(Texture source, DyeValue dye)
        {
            string key = dye.ToString();
            foreach (var entry in s_cache)
            {
                if (entry.source == source && entry.dye == key && entry.copy != null)
                {
                    return entry.copy;
                }
            }
            Texture2D copy = Make(source, dye);
            s_cache.Add((source, key, copy));
            if (s_cache.Count > CacheSize)
            {
                Object.Destroy(s_cache[0].copy);
                s_cache.RemoveAt(0);
            }
            return copy;
        }

        /// <summary>A CPU-readable copy of any texture, scaled to fit maxSize.</summary>
        internal static Texture2D ReadBack(Texture source, int maxSize, bool mipmaps)
        {
            float scale = Mathf.Min(1f, maxSize / (float)Mathf.Max(source.width, source.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(w, h, TextureFormat.RGBA32, mipmaps);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static Texture2D Make(Texture source, DyeValue dye)
        {
            Texture2D copy = ReadBack(source, MaxSize, true);
            copy.name = source.name + "_gearhold_dye";
            Color32[] pixels = copy.GetPixels32();
            float hueShift = dye.Hue / 360f;
            float sat = dye.Saturation / 100f;
            float val = dye.Brightness / 100f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.a == 0)
                {
                    continue; // skin shows through here
                }
                Color.RGBToHSV(new Color32(p.r, p.g, p.b, 255), out float hh, out float ss, out float vv);
                hh = hh + hueShift;
                hh -= Mathf.Floor(hh);
                ss = sat >= 0f ? ss + (1f - ss) * sat : ss * (1f + sat);
                vv = val >= 0f ? vv + (1f - vv) * val : vv * (1f + val);
                Color32 shifted = Color.HSVToRGB(hh, ss, vv);
                Color32 mixed = Color32.Lerp(p, shifted, p.a / 255f);
                pixels[i] = new Color32(mixed.r, mixed.g, mixed.b, p.a);
            }
            copy.SetPixels32(pixels);
            copy.Apply(true, true); // mipmaps, then free the CPU copy
            return copy;
        }

        /// <summary>
        /// Dominant colour of a texture as (hue 0..1, saturation, brightness): the alpha- and saturation-weighted
        /// circular mean of its hues. Used to turn a colour swatch ("make it red") into a hue shift.
        /// </summary>
        internal static Vector3 Dominant(Texture source)
        {
            if (s_dominant.TryGetValue(source, out Vector3 cached))
            {
                return cached;
            }
            Texture2D small = ReadBack(source, 64, false);
            Color32[] pixels = small.GetPixels32();
            Object.Destroy(small);
            float x = 0f, y = 0f, sSum = 0f, vSum = 0f, weight = 0f;
            foreach (Color32 p in pixels)
            {
                if (p.a < 128) continue;
                Color.RGBToHSV(p, out float h, out float s, out float v);
                float w = Mathf.Max(0.05f, s) * (v > 0.08f ? 1f : 0.1f);
                x += Mathf.Cos(h * Mathf.PI * 2f) * w;
                y += Mathf.Sin(h * Mathf.PI * 2f) * w;
                sSum += s * w;
                vSum += v * w;
                weight += w;
            }
            float hue = Mathf.Atan2(y, x) / (Mathf.PI * 2f);
            if (hue < 0f) hue += 1f;
            Vector3 result = weight > 0f ? new Vector3(hue, sSum / weight, vSum / weight) : new Vector3(0f, 0f, 0.5f);
            s_dominant[source] = result;
            return result;
        }
    }
}
