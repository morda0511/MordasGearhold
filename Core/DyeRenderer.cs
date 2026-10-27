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
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_right = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightBack = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightBackItemInstance");
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
                case Transmog.Piece.Shield:
                    if (IsShield(s_leftHash(vis))) objects.Add(s_left(vis));
                    if (IsShield(s_leftBackHash(vis))) objects.Add(s_leftBack(vis));
                    break;
            }
            foreach (GameObject go in objects)
            {
                Texture found = MainTextureOf(go);
                if (found != null) return found;
            }
            // nothing of the piece is on the character right now (a shield that is put away, a look not built yet):
            // the texture of the item's own model shows the same colours
            return ItemTexture(player, piece);
        }

        /// <summary>The first model texture of an object (meshes only, any shader).</summary>
        private static Texture MainTextureOf(GameObject go)
        {
            if (go == null) return null;
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
                    {
                        return material.GetTexture("_MainTex");
                    }
                }
            }
            return null;
        }

        /// <summary>The model texture of the item worn in a piece's slot, or of the look chosen for it.</summary>
        private static Texture ItemTexture(Player player, Transmog.Piece piece)
        {
            ItemDrop.ItemData item;
            switch (piece)
            {
                case Transmog.Piece.Helmet: item = Access.HelmetItem(player); break;
                case Transmog.Piece.Chest: item = Access.ChestItem(player); break;
                case Transmog.Piece.Legs: item = Access.LegItem(player); break;
                case Transmog.Piece.Shoulder: item = Access.ShoulderItem(player); break;
                default: item = MordasGearhold.Patches.Equipment.ItemIn(player, GearSlot.Shield); break;
            }
            return PrefabOf(item, Transmog.GetLook(player, piece));
        }

        /// <summary>The texture of a weapon's model (or of its chosen look), also while it is not drawn.</summary>
        internal static Texture WeaponTexture(ItemDrop.ItemData weapon) => PrefabOf(weapon, Transmog.GetItemLook(weapon));

        internal static ZoneSet WeaponZones(ItemDrop.ItemData weapon)
        {
            GameObject prefab = weapon != null ? (!string.IsNullOrEmpty(Transmog.GetItemLook(weapon)) && Transmog.GetItemLook(weapon) != Transmog.Hidden && ObjectDB.instance != null
                ? ObjectDB.instance.GetItemPrefab(Transmog.GetItemLook(weapon)) : weapon.m_dropPrefab) : null;
            List<Texture> textures = TexturesOf(prefab != null ? prefab : weapon?.m_dropPrefab);
            return Zones.OfSet(textures);
        }

        /// <summary>Every model texture (the main texture of every material) of an object, once each.</summary>
        internal static List<Texture> TexturesOf(GameObject go)
        {
            List<Texture> list = new List<Texture>();
            if (go == null) return list;
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.HasProperty("_MainTex"))
                    {
                        Texture t = material.GetTexture("_MainTex");
                        if (t != null && !list.Contains(t)) list.Add(t);
                    }
                }
            }
            return list;
        }

        private static Texture PrefabOf(ItemDrop.ItemData item, string look)
        {
            if (item == null || look == Transmog.Hidden) return null;
            GameObject prefab = !string.IsNullOrEmpty(look) && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(look) : item.m_dropPrefab;
            return MainTextureOf(prefab != null ? prefab : item.m_dropPrefab);
        }

        private static Texture OriginalBody(int textureId, int itemHash)
        {
            if (itemHash == 0) return null;
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemHash) : null;
            Material armor = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_armorMaterial : null;
            return armor != null && armor.HasProperty(textureId) ? armor.GetTexture(textureId) : null; // e.g. the Lox armour has no painted body texture
        }


        internal static void Apply(Player player)
        {
            VisEquipment vis = player != null ? s_vis(player) : null;
            if (vis != null)
            {
                ApplyTo(player, vis);
                try
                {
                    GlowRenderer.ApplyTo(player, vis); // the dyes may have cleared a weapon's property block: glow goes on again
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError("Gearhold weapon glow failed: " + ex);
                }
            }
        }

        /// <summary>The colour zones of a piece, from the texture that shows its colours (null without one).</summary>
        internal static ZoneSet ZonesOf(Player player, Transmog.Piece piece) => Zones.OfSet(PieceTextures(player, piece));

        /// <summary>All textures the visible piece uses: the painted body texture (chest, legs) and the textures of its models.</summary>
        private static List<Texture> PieceTextures(Player player, Transmog.Piece piece)
        {
            List<Texture> list = new List<Texture>();
            VisEquipment vis = player != null ? s_vis(player) : null;
            if (vis == null) return list;
            Texture painted = piece == Transmog.Piece.Chest ? OriginalBody(s_chestTex, s_chestHash(vis))
                : piece == Transmog.Piece.Legs ? OriginalBody(s_legsTex, s_legHash(vis)) : null;
            if (painted != null) list.Add(painted);
            List<GameObject> objects = new List<GameObject>();
            switch (piece)
            {
                case Transmog.Piece.Helmet: objects.Add(s_helmet(vis)); break;
                case Transmog.Piece.Chest: if (s_chest(vis) != null) objects.AddRange(s_chest(vis)); break;
                case Transmog.Piece.Legs: if (s_legs(vis) != null) objects.AddRange(s_legs(vis)); break;
                case Transmog.Piece.Shoulder: if (s_shoulder(vis) != null) objects.AddRange(s_shoulder(vis)); break;
                case Transmog.Piece.Shield:
                    if (IsShield(s_leftHash(vis))) objects.Add(s_left(vis));
                    if (IsShield(s_leftBackHash(vis))) objects.Add(s_leftBack(vis));
                    break;
            }
            foreach (GameObject go in objects)
            {
                foreach (Texture t in TexturesOf(go))
                {
                    if (!list.Contains(t)) list.Add(t);
                }
            }
            if (list.Count == 0)
            {
                Texture own = ItemTexture(player, piece);
                if (own != null) list.Add(own);
            }
            return list;
        }

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
            DyeValue[] dyes = ZoneFlash.Modify(player, piece, Dye.GetZones(player, piece));
            bool same = true;
            for (int i = 1; i < dyes.Length; i++)
            {
                if (!dyes[i].SameAs(dyes[0]))
                {
                    same = false;
                }
            }
            bool anyTarget = dyes.Length > 0 && dyes[0].IsTarget;
            ZoneSet zones = same && !anyTarget ? null : ZonesOf(player, piece);
            if (zones == null || (zones.Count < 2 && !anyTarget))
            {
                return new Plan { Dyes = dyes }; // one colour for the whole piece
            }
            DyeValue[] fitted = new DyeValue[zones.Count]; // missing zones stay as they are, extra ones are dropped
            for (int i = 0; i < fitted.Length; i++)
            {
                fitted[i] = dyes.Length == 1 ? dyes[0] : (i < dyes.Length ? dyes[i] : default); // one value = every zone
            }
            return new Plan { Dyes = fitted, Zones = zones };
        }

        private static float s_nextError;

        /// <summary>
        /// Dyes all visible pieces. Runs inside a postfix of vanilla's gear update, so a failure here is logged and
        /// never passed on (an exception would stop vanilla from building the rest of the visuals).
        /// </summary>
        internal static void ApplyTo(Player player, VisEquipment vis)
        {
            TextureDye.BeginPass(player.GetInstanceID());
            try
            {
                ApplyAll(player, vis);
            }
            catch (System.Exception ex)
            {
                if (Time.unscaledTime >= s_nextError)
                {
                    s_nextError = Time.unscaledTime + 5f;
                    Plugin.Log.LogError("Gearhold dye failed: " + ex);
                }
            }
            finally
            {
                TextureDye.EndPass();
            }
        }

        private static void ApplyAll(Player player, VisEquipment vis)
        {
            Dyed(Transmog.Piece.Helmet, DyeObject(s_helmet(vis), PlanFor(player, Transmog.Piece.Helmet)), s_helmet(vis), player);
            Plan chest = PlanFor(player, Transmog.Piece.Chest);
            Dyed(Transmog.Piece.Chest, DyeObjects(s_chest(vis), chest), s_chest(vis) != null && s_chest(vis).Count > 0 ? s_chest(vis)[0] : null, player);
            Plan legs = PlanFor(player, Transmog.Piece.Legs);
            Dyed(Transmog.Piece.Legs, DyeObjects(s_legs(vis), legs), s_legs(vis) != null && s_legs(vis).Count > 0 ? s_legs(vis)[0] : null, player);
            DyeObjects(s_shoulder(vis), PlanFor(player, Transmog.Piece.Shoulder));
            Plan shield = PlanFor(player, Transmog.Piece.Shield);
            if (IsShield(s_leftHash(vis))) Dyed(Transmog.Piece.Shield, DyeObject(s_left(vis), shield), s_left(vis), player);
            if (IsShield(s_leftBackHash(vis))) Dyed(Transmog.Piece.Shield, DyeObject(s_leftBack(vis), shield), s_leftBack(vis), player);

            if (vis.m_bodyModel != null)
            {
                Material body = vis.m_bodyModel.material; // vanilla already works on this per-character copy
                DyeBody(body, s_chestTex, s_chestHash(vis), chest);
                DyeBody(body, s_legsTex, s_legHash(vis), legs);
            }

            if (ColorSync.TryGet(player, out ColorSync.Record record))
            {
                // another player: the weapon colours come from what their game sent
                DyeWeaponCore(s_right(vis), record.Weapon(ColorSync.Right).Dyes, null);
                DyeWeaponCore(s_left(vis), record.Weapon(ColorSync.Left).Dyes, null);
                DyeWeaponCore(s_rightBack(vis), record.Weapon(ColorSync.RightBack).Dyes, null);
                DyeWeaponCore(s_leftBack(vis), record.Weapon(ColorSync.LeftBack).Dyes, null);
                return;
            }

            // weapons carry their own dye: in the hands, and the holstered ones on the body
            ItemDrop.ItemData left = Access.LeftItem(player);
            ItemDrop.ItemData holster = MordasGearhold.Patches.Equipment.ItemIn(player, GearSlot.Holster);
            DyeWeapon(player, s_right(vis), Access.RightItem(player));
            DyeWeapon(player, s_left(vis), left);
            DyeWeapon(player, s_rightBack(vis), Access.HiddenRightItem(player) ?? holster);
            DyeWeapon(player, s_leftBack(vis), Access.HiddenLeftItem(player) ?? holster);
        }

        private static readonly HashSet<string> s_reported = new HashSet<string>();

        /// <summary>A dyed piece whose dye touched nothing is logged once: it shows which shader or model refuses colours.</summary>
        private static void Dyed(Transmog.Piece piece, int touched, GameObject sample, Player player)
        {
            if (touched > 0 || sample == null || !Dye.IsDyed(player, piece) || !s_reported.Add(piece + "/" + sample.name))
            {
                return;
            }
            System.Text.StringBuilder shaders = new System.Text.StringBuilder();
            foreach (Renderer renderer in sample.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null) shaders.Append(renderer.GetType().Name).Append(':').Append(material.shader != null ? material.shader.name : "-").Append(' ');
                }
            }
            Plugin.Log.LogWarning($"Gearhold: the dye of {piece} ({sample.name}) changed nothing. Renderers: {shaders}");
        }

        private static bool IsShield(int hash)
        {
            GameObject prefab = hash != 0 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(hash) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
        }

        // ------------------------------------------------------------------ attached pieces

        // renderers that carry a recoloured main texture in their property block (cleared again when the dye changes back)
        private static readonly HashSet<Renderer> s_textured = new HashSet<Renderer>();

        private static int DyeObjects(List<GameObject> objects, Plan plan)
        {
            int touched = 0;
            if (objects == null) return 0;
            foreach (GameObject go in objects) touched += DyeObject(go, plan);
            return touched;
        }

        /// <summary>
        /// Dyes one model. Materials with the game's colour controls (_Hue, _Saturation, _Value) get them for one
        /// colour on the whole piece; with colour zones, and for shaders without such controls (Standard and others),
        /// the model's own texture is replaced by a recoloured copy through the renderer's property block.
        /// Returns how many materials were touched.
        /// </summary>
        private static int DyeObject(GameObject go, Plan plan)
        {
            int touched = 0;
            if (go == null) return 0;
            s_textured.RemoveWhere(r => r == null);
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue; // no particles, flares, ...
                Material[] materials = renderer.sharedMaterials;
                if (s_textured.Remove(renderer))
                {
                    for (int j = 0; j < materials.Length; j++)
                    {
                        renderer.SetPropertyBlock(null, j); // the recoloured texture of the last time goes; set again below if wanted
                    }
                }
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null) continue;
                    bool hsv = material.HasProperty(s_hue);
                    Texture main = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    DyeValue dye = plan.First;
                    if (plan.Uniform && dye.IsNeutral)
                    {
                        if (hsv) renderer.SetPropertyBlock(null, i);
                        continue;
                    }
                    if (plan.Uniform && hsv)
                    {
                        renderer.GetPropertyBlock(s_block, i);
                        s_block.SetFloat(s_hue, Scale(material.shader, "_Hue", dye.Hue / 180f, hue: true));
                        s_block.SetFloat(s_saturation, Scale(material.shader, "_Saturation", dye.Saturation / 100f, hue: false));
                        s_block.SetFloat(s_value, Scale(material.shader, "_Value", dye.Brightness / 100f, hue: false));
                        renderer.SetPropertyBlock(s_block, i);
                        touched++;
                    }
                    else if (main != null)
                    {
                        // zones, or a shader without colour controls: this model's own texture, recoloured
                        Texture recoloured = plan.Uniform ? TextureDye.Get(main, dye, coverage: false) : TextureDye.GetZoned(main, plan.Zones, plan.Dyes, false);
                        renderer.GetPropertyBlock(s_block, i);
                        s_block.SetTexture("_MainTex", recoloured);
                        if (hsv)
                        {
                            s_block.SetFloat(s_hue, 0f);
                            s_block.SetFloat(s_saturation, 0f);
                            s_block.SetFloat(s_value, 0f);
                        }
                        renderer.SetPropertyBlock(s_block, i);
                        s_textured.Add(renderer);
                        touched++;
                    }
                    else if (hsv)
                    {
                        renderer.GetPropertyBlock(s_block, i);
                        s_block.SetFloat(s_hue, Scale(material.shader, "_Hue", dye.Hue / 180f, hue: true));
                        s_block.SetFloat(s_saturation, Scale(material.shader, "_Saturation", dye.Saturation / 100f, hue: false));
                        s_block.SetFloat(s_value, Scale(material.shader, "_Value", dye.Brightness / 100f, hue: false));
                        renderer.SetPropertyBlock(s_block, i);
                        touched++;
                    }
                }
            }
            return touched;
        }

        /// <summary>
        /// Dyes the model of a weapon with the weapon's own dye (kept on the item, so it follows the weapon through
        /// every set). Always the texture way: weapon shaders rarely have colour controls. The zones come from the
        /// weapon's model texture, so they are the same whether it is drawn or not.
        /// </summary>
        private static void DyeWeapon(Player player, GameObject go, ItemDrop.ItemData item)
        {
            if (go == null || item == null || !Transmog.IsWeapon(item)) return;
            DyeValue[] dyes = ZoneFlash.ModifyItem(item, Dye.GetItemZones(item));
            DyeWeaponCore(go, dyes, Dye.ItemDyed(item) ? WeaponZones(item) : null);
        }

        /// <summary>The weapon texture way of DyeWeapon; <paramref name="baseZones"/> null = zones found from the model's own texture.</summary>
        private static void DyeWeaponCore(GameObject go, DyeValue[] dyes, ZoneSet baseZones)
        {
            if (go == null) return;
            bool dyed = false;
            foreach (DyeValue d in dyes)
            {
                if (!d.IsNeutral) dyed = true;
            }
            s_textured.RemoveWhere(r => r == null);
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                Material[] materials = renderer.sharedMaterials;
                if (s_textured.Remove(renderer))
                {
                    for (int j = 0; j < materials.Length; j++)
                    {
                        renderer.SetPropertyBlock(null, j); // the glow colours are put on again right after the dyes
                    }
                }
                if (!dyed) continue;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    Texture main = material != null && material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    if (main == null) continue;
                    ZoneSet zones = baseZones ?? Zones.OfSet(TexturesOf(go));
                    DyeValue[] fitted = new DyeValue[zones.Count];
                    for (int k = 0; k < fitted.Length; k++)
                    {
                        fitted[k] = dyes.Length == 1 ? dyes[0] : (k < dyes.Length ? dyes[k] : default);
                    }
                    renderer.GetPropertyBlock(s_block, i);
                    s_block.SetTexture("_MainTex", TextureDye.GetZoned(main, zones, fitted, false));
                    renderer.SetPropertyBlock(s_block, i);
                    s_textured.Add(renderer);
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
        private const int CacheSize = 32;
        private static readonly List<(Texture source, string dye, Texture2D copy)> s_cache = new List<(Texture, string, Texture2D)>();
        private static readonly Dictionary<Texture, Vector3> s_dominant = new Dictionary<Texture, Vector3>();

        // ------------------------------------------------------------------ colour zones

        private sealed class ZonedPixels
        {
            public Color32[] Source;
            public byte[] Weights; // per pixel one byte per zone, summing to about 255
            public float[] Lab;    // OKLab of every pixel (3 floats), for moving colours to a target
            public int Width, Height, Zones;
        }

        private const int ZonedCacheSize = 4;
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

        /// <summary>
        /// Moves the pixels of a zone to a chosen target colour, in OKLab: the lightness follows the target scaled by how
        /// light the pixel is compared with the zone's average, and the colour axes become the target's plus the
        /// pixel's own deviation from the zone's average colour (turned towards the new hue). So the texture keeps its
        /// light, shadow and fine colour variation around the new colour instead of turning into a flat, plastic one.
        /// </summary>
        internal struct TargetMap
        {
            private float _tL, _ta, _tb, _cL, _ca, _cb, _cos, _sin;

            internal static TargetMap Make(DyeValue dye, Color chip)
            {
                TargetMap m = new TargetMap();
                Oklab.FromColor(dye.TargetColor, out m._tL, out m._ta, out m._tb);
                Oklab.FromColor(chip, out m._cL, out m._ca, out m._cb);
                m._cos = 1f;
                float targetChroma = Mathf.Sqrt(m._ta * m._ta + m._tb * m._tb), chipChroma = Mathf.Sqrt(m._ca * m._ca + m._cb * m._cb);
                if (targetChroma > 0.03f && chipChroma > 0.03f)
                {
                    float angle = Mathf.Atan2(m._tb, m._ta) - Mathf.Atan2(m._cb, m._ca);
                    m._cos = Mathf.Cos(angle);
                    m._sin = Mathf.Sin(angle);
                }
                return m;
            }

            internal void Apply(float L, float a, float b, out byte r, out byte g, out byte bl)
            {
                float rel = Mathf.Clamp(L / Mathf.Max(_cL, 0.05f), 0f, DyeTuning.ValMax);
                float newL = Mathf.Clamp01(_tL * (DyeTuning.ValFollow >= 0.999f ? rel : Mathf.Pow(rel, DyeTuning.ValFollow)));
                float da = (a - _ca) * DyeTuning.Detail, db = (b - _cb) * DyeTuning.Detail;
                Oklab.ToRgb(newL, _ta + da * _cos - db * _sin, _tb + da * _sin + db * _cos, out r, out g, out bl);
            }
        }


        /// <summary>The result of a dye on one colour of a zone (for the preview chips in the menu).</summary>
        internal static Color ShiftColor(Color colour, DyeValue dye)
        {
            Color.RGBToHSV(colour, out float h, out float s, out float v);
            if (dye.IsTarget) return dye.TargetColor;
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
            data.Lab = new float[n * 3];
            float[] e = new float[z];
            for (int i = 0; i < n; i++)
            {
                Color32 px = data.Source[i];
                Oklab.FromRgb(px.r, px.g, px.b, out float pl, out float pa, out float pb);
                data.Lab[i * 3] = pl;
                data.Lab[i * 3 + 1] = pa;
                data.Lab[i * 3 + 2] = pb;
                Vector3 f = Zones.Feature(pl, pa, pb, zones.Params);
                float best = float.MaxValue;
                for (int k = 0; k < z; k++)
                {
                    e[k] = (f - zones.Centers[k]).sqrMagnitude;
                    if (e[k] < best) best = e[k];
                }
                float sum = 0f;
                for (int k = 0; k < z; k++)
                {
                    e[k] = Mathf.Exp(-(e[k] - best) / zones.Params.Softness);
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
            string key = "z" + zones.Id + "v" + DyeTuning.Version + (coverage ? "b" : "m") + string.Join("|", System.Array.ConvertAll(dyes, d => d.ToString()));
            foreach (var entry in s_cache)
            {
                if (entry.source == source && entry.dye == key && entry.copy != null)
                {
                    s_pinned.Add(entry.copy);
                    return entry.copy;
                }
            }
            Texture2D copy = MakeZoned(source, zones, dyes, coverage);
            s_cache.Add((source, key, copy));
            s_pinned.Add(copy);
            return copy;
        }

        // Textures that the current pass put on the character: never destroyed by the cache (a destroyed texture
        // that is still on a renderer makes that piece vanish, e.g. the helmet while the legs are recoloured).
        // one set per character (yours and the other players'), so a pass for one never frees another one's textures
        private static readonly Dictionary<int, HashSet<Texture>> s_pins = new Dictionary<int, HashSet<Texture>>();
        private static HashSet<Texture> s_pinned = new HashSet<Texture>();

        /// <summary>Called before all pieces of one character are dyed again: from here on only what is requested counts as in use.</summary>
        internal static void BeginPass(int owner = 0)
        {
            if (!s_pins.TryGetValue(owner, out s_pinned))
            {
                s_pinned = s_pins[owner] = new HashSet<Texture>();
            }
            s_pinned.Clear();
        }

        /// <summary>A character is gone: its textures may be freed.</summary>
        internal static void Forget(int owner) => s_pins.Remove(owner);

        private static bool Pinned(Texture texture)
        {
            foreach (HashSet<Texture> set in s_pins.Values)
            {
                if (set.Contains(texture)) return true;
            }
            return false;
        }

        /// <summary>Called after the pass: the oldest copies that are not in use any more are destroyed.</summary>
        internal static void EndPass()
        {
            int i = 0;
            while (s_cache.Count > CacheSize && i < s_cache.Count)
            {
                if (Pinned(s_cache[i].copy))
                {
                    i++;
                    continue;
                }
                Object.Destroy(s_cache[i].copy);
                s_cache.RemoveAt(i);
            }
        }

        private static Texture2D MakeZoned(Texture source, ZoneSet zones, DyeValue[] dyes, bool coverage)
        {
            ZonedPixels data = PixelsOf(source, zones);
            int n = data.Source.Length, z = zones.Count;
            float[] zoneHue = new float[z];
            TargetMap[] targets = new TargetMap[z];
            bool[] active = new bool[z];
            for (int k = 0; k < z; k++)
            {
                Color.RGBToHSV(zones.Chips[k], out zoneHue[k], out float _, out float _);
                active[k] = k < dyes.Length && !dyes[k].IsNeutral;
                if (active[k] && dyes[k].IsTarget) targets[k] = TargetMap.Make(dyes[k], zones.Chips[k]);
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
                    float cr, cg, cb;
                    if (!active[k])
                    {
                        cr = p.r / 255f;
                        cg = p.g / 255f;
                        cb = p.b / 255f;
                    }
                    else if (dyes[k].IsTarget)
                    {
                        targets[k].Apply(data.Lab[i * 3], data.Lab[i * 3 + 1], data.Lab[i * 3 + 2], out byte tr, out byte tg, out byte tb);
                        cr = tr / 255f;
                        cg = tg / 255f;
                        cb = tb / 255f;
                    }
                    else
                    {
                        Color c = ShiftHsv(h, s, v, dyes[k], zoneHue[k]);
                        cr = c.r;
                        cg = c.g;
                        cb = c.b;
                    }
                    r += cr * w;
                    g += cg * w;
                    b += cb * w;
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

        internal static Texture Get(Texture source, DyeValue dye, bool coverage = true)
        {
            string key = dye.ToString() + (coverage ? "" : "m") + (dye.IsTarget ? "v" + DyeTuning.Version : "");
            foreach (var entry in s_cache)
            {
                if (entry.source == source && entry.dye == key && entry.copy != null)
                {
                    s_pinned.Add(entry.copy);
                    return entry.copy;
                }
            }
            Texture2D copy = Make(source, dye, coverage);
            s_cache.Add((source, key, copy));
            s_pinned.Add(copy);
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

        private static Texture2D Make(Texture source, DyeValue dye, bool coverage)
        {
            Texture2D copy = ReadBack(source, MaxSize, true);
            copy.name = source.name + "_gearhold_dye";
            Color32[] pixels = copy.GetPixels32();
            Vector3 dominant = dye.IsTarget ? Dominant(source) : default;
            TargetMap target = dye.IsTarget ? TargetMap.Make(dye, Color.HSVToRGB(dominant.x, dominant.y, dominant.z)) : default;
            float hueShift = dye.Hue / 360f;
            float sat = dye.Saturation / 100f;
            float val = dye.Brightness / 100f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (coverage && p.a == 0)
                {
                    continue; // skin shows through here
                }
                Color.RGBToHSV(new Color32(p.r, p.g, p.b, 255), out float hh, out float ss, out float vv);
                float s0 = ss, v0 = vv;
                hh = hh + hueShift;
                hh -= Mathf.Floor(hh);
                ss = sat >= 0f ? ss + (1f - ss) * sat : ss * (1f + sat);
                vv = val >= 0f ? vv + (1f - vv) * val : vv * (1f + val);
                Color32 shifted;
                if (dye.IsTarget)
                {
                    Oklab.FromRgb(p.r, p.g, p.b, out float tl, out float ta, out float tb);
                    target.Apply(tl, ta, tb, out byte tr, out byte tg, out byte tbl);
                    shifted = new Color32(tr, tg, tbl, 255);
                }
                else
                {
                    shifted = Color.HSVToRGB(hh, ss, vv);
                }
                Color32 mixed = coverage ? Color32.Lerp(p, shifted, p.a / 255f) : shifted;
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
