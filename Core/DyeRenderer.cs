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
        private static readonly HashSet<string> s_loggedMeshes = new HashSet<string>();

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

        internal static void ApplyTo(Player player, VisEquipment vis)
        {
            DyeObject(s_helmet(vis), Dye.Get(player, Transmog.Piece.Helmet));
            DyeObjects(s_chest(vis), Dye.Get(player, Transmog.Piece.Chest));
            DyeObjects(s_legs(vis), Dye.Get(player, Transmog.Piece.Legs));
            DyeObjects(s_shoulder(vis), Dye.Get(player, Transmog.Piece.Shoulder));
            DyeValue shield = Dye.Get(player, Transmog.Piece.Shield);
            if (IsShield(s_leftHash(vis))) DyeObject(s_left(vis), shield);
            if (IsShield(s_leftBackHash(vis))) DyeObject(s_leftBack(vis), shield);

            if (vis.m_bodyModel != null)
            {
                Material body = vis.m_bodyModel.material; // vanilla already works on this per-character copy
                DyeBody(body, s_chestTex, s_chestHash(vis), Dye.Get(player, Transmog.Piece.Chest));
                DyeBody(body, s_legsTex, s_legHash(vis), Dye.Get(player, Transmog.Piece.Legs));
            }
        }

        private static bool IsShield(int hash)
        {
            GameObject prefab = hash != 0 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(hash) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
        }

        // ------------------------------------------------------------------ attached pieces

        private static void DyeObjects(List<GameObject> objects, DyeValue dye)
        {
            if (objects == null) return;
            foreach (GameObject go in objects) DyeObject(go, dye);
        }

        private static void DyeObject(GameObject go, DyeValue dye)
        {
            if (go == null) return;
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue; // no particles, flares, ...
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null || !material.HasProperty(s_hue)) continue; // e.g. Standard shader: no HSV
                    if (dye.IsNeutral)
                    {
                        renderer.SetPropertyBlock(null, i);
                        continue;
                    }
                    if (s_loggedMeshes.Add(go.name + "/" + renderer.name + "/" + material.name))
                    {
                        Texture main = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                        Plugin.Log.LogInfo($"[Dye] mesh {go.name}/{renderer.name}: material {material.name}, texture {(main != null ? main.name : "-")}");
                    }
                    renderer.GetPropertyBlock(s_block, i);
                    s_block.SetFloat(s_hue, Scale(material.shader, "_Hue", dye.Hue / 180f, hue: true));
                    s_block.SetFloat(s_saturation, Scale(material.shader, "_Saturation", dye.Saturation / 100f, hue: false));
                    s_block.SetFloat(s_value, Scale(material.shader, "_Value", dye.Brightness / 100f, hue: false));
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

        private static void DyeBody(Material body, int textureId, int itemHash, DyeValue dye)
        {
            Texture original = OriginalBody(textureId, itemHash);
            if (original == null) return;
            Texture wanted = dye.IsNeutral ? original : TextureDye.Get(original, dye);
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
        private const int CacheSize = 8;
        private static readonly List<(Texture source, string dye, Texture2D copy)> s_cache = new List<(Texture, string, Texture2D)>();
        private static readonly Dictionary<Texture, Vector3> s_dominant = new Dictionary<Texture, Vector3>();
        private static readonly HashSet<string> s_logged = new HashSet<string>();

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
        private static Texture2D ReadBack(Texture source, int maxSize, bool mipmaps)
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
            LogAlpha(source, pixels);
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

        /// <summary>Diagnostics for the skin question: how much of the texture is armour (alpha) vs. skin.</summary>
        private static void LogAlpha(Texture source, Color32[] pixels)
        {
            if (!s_logged.Add(source.name))
            {
                return;
            }
            int clear = 0, partial = 0, solid = 0;
            foreach (Color32 p in pixels)
            {
                if (p.a < 13) clear++;
                else if (p.a > 242) solid++;
                else partial++;
            }
            float n = Mathf.Max(1, pixels.Length) / 100f;
            string format = source is Texture2D t2 ? t2.format.ToString() : source.GetType().Name;
            Plugin.Log.LogInfo($"[Dye] body texture {source.name} ({format} {source.width}x{source.height}): " +
                $"skin/clear {clear / n:0}%, partial {partial / n:0}%, armour {solid / n:0}%");
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
