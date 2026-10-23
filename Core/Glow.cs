using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Glow or shine colour of a weapon: a hue for every coloured part, or white. Default = the weapon's own colours.
    /// GLOW is the fire, frost, lightning and glowing cores; SHINE is the white shimmer an upgraded weapon gets.
    /// </summary>
    internal struct GlowValue
    {
        public bool IsSet;
        public bool White;
        public int Hue; // 0..359

        internal static GlowValue Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return default;
            if (raw == "white") return new GlowValue { IsSet = true, White = true };
            return int.TryParse(raw, out int hue) ? new GlowValue { IsSet = true, Hue = ((hue % 360) + 360) % 360 } : default;
        }

        public override string ToString() => !IsSet ? "" : White ? "white" : Hue.ToString();

        /// <summary>
        /// The colour with its hue replaced: brightness, strength and alpha stay, so fire still looks like fire.
        /// Greyish and white parts (smoke, the upgrade shimmer) are left alone.
        /// </summary>
        internal Color Apply(Color c)
        {
            if (!IsSet) return c;
            Color.RGBToHSV(c, out _, out float s, out float v);
            if (s < 0.12f || v <= 0f) return c;
            Color result = Color.HSVToRGB(White ? 0f : Hue / 360f, White ? 0f : s, v, true);
            result.a = c.a;
            return result;
        }

        /// <summary>
        /// For the shimmer: it is white to begin with, so there is no hue to replace; the colour is set outright
        /// (a clearly tinted version of its brightness), alpha stays.
        /// </summary>
        internal Color ApplyShine(Color c)
        {
            if (!IsSet || White) return c;
            Color.RGBToHSV(c, out _, out float s, out float v);
            Color result = Color.HSVToRGB(Hue / 360f, Mathf.Max(s, 0.8f), Mathf.Max(v, 0.9f), true);
            result.a = c.a;
            return result;
        }
    }

    /// <summary>Glow and shine colours are stored on the weapon itself (like its look), so they follow it into every set.</summary>
    internal static class Glow
    {
        internal const string ItemKey = "MordasGearhold.Glow";
        internal const string ShineKey = "MordasGearhold.Shine";
        internal const string ShineObject = "UpgraderGlow"; // the shimmer of upgraded weapons

        internal static GlowValue Get(ItemDrop.ItemData item) =>
            item != null && item.m_customData.TryGetValue(ItemKey, out string raw) ? GlowValue.Parse(raw) : default;

        internal static GlowValue GetShine(ItemDrop.ItemData item) =>
            item != null && item.m_customData.TryGetValue(ShineKey, out string raw) ? GlowValue.Parse(raw) : default;

        internal static void Set(Player player, ItemDrop.ItemData item, GlowValue glow)
        {
            if (!glow.IsSet) item.m_customData.Remove(ItemKey);
            else item.m_customData[ItemKey] = glow.ToString();
            GlowRenderer.Apply(player);
        }

        internal static void SetShine(Player player, ItemDrop.ItemData item, GlowValue shine)
        {
            if (!shine.IsSet) item.m_customData.Remove(ShineKey);
            else item.m_customData[ShineKey] = shine.ToString();
            GlowRenderer.Apply(player);
        }

        /// <summary>True when the weapon has glow parts at all (particles or lights); only those get the GLOW controls.</summary>
        internal static bool CanGlow(ItemDrop.ItemData item)
        {
            GameObject prefab = item?.m_dropPrefab;
            return prefab != null && (prefab.GetComponentInChildren<ParticleSystem>(true) != null || prefab.GetComponentInChildren<Light>(true) != null);
        }

        /// <summary>True when the weapon has the upgrade shimmer.</summary>
        internal static bool CanShine(ItemDrop.ItemData item)
        {
            GameObject prefab = item?.m_dropPrefab;
            if (prefab == null) return false;
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.IndexOf(ShineObject, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Puts the glow and shine colours on the weapon objects your character holds or carries on the back. Only these
    /// copies change; the item and other players' copies keep their colours. Called after vanilla rebuilt the
    /// visible gear and when a colour was chosen.
    /// </summary>
    internal static class GlowRenderer
    {
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_right = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_left = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightBack = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightBackItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_leftBack = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftBackItemInstance");

        internal static void Apply(Player player)
        {
            VisEquipment vis = player != null ? player.GetComponent<VisEquipment>() : null;
            if (vis != null)
            {
                ApplyTo(player, vis);
            }
        }

        internal static void ApplyTo(Player player, VisEquipment vis)
        {
            // the holstered weapon (Weapon 2) hangs on a back mount without being a hidden hand item
            ItemDrop.ItemData holster = MordasGearhold.Patches.Equipment.ItemIn(player, GearSlot.Holster);
            Put(s_right(vis), Access.RightItem(player));
            Put(s_left(vis), Access.LeftItem(player));
            Put(s_rightBack(vis), Access.HiddenRightItem(player) ?? holster);
            Put(s_leftBack(vis), Access.HiddenLeftItem(player) ?? holster);
        }

        private static void Put(GameObject instance, ItemDrop.ItemData item)
        {
            if (instance == null || item == null || !Transmog.IsWeapon(item))
            {
                return;
            }
            GlowValue glow = Glow.Get(item);
            GlowValue shine = Glow.GetShine(item);
            GlowKeeper keeper = instance.GetComponent<GlowKeeper>();
            if (keeper == null)
            {
                if (!glow.IsSet && !shine.IsSet) return; // nothing to change, nothing to remember
                keeper = instance.AddComponent<GlowKeeper>();
            }
            keeper.Set(glow, shine);
        }
    }

    /// <summary>
    /// Lives on one weapon object: remembers its original glow colours and recolours them. The originals are taken
    /// a few frames late, because vanilla's ParticleIntensityScaler sets the colours again in its Start. Parts below
    /// the upgrade shimmer object take the SHINE colour, everything else the GLOW colour.
    /// </summary>
    internal sealed class GlowKeeper : MonoBehaviour
    {
        private static readonly string[] s_materialColors = { "_Color", "_TintColor", "_EmissionColor" };
        private static readonly FieldInfo s_trailColors = AccessTools.Field(typeof(MeleeWeaponTrail), "_colors");
        private static readonly MaterialPropertyBlock s_block = new MaterialPropertyBlock();

        private sealed class Particles
        {
            public ParticleSystem System;
            public bool Shine;
            public ParticleSystem.MinMaxGradient Start;
            public ParticleSystem.MinMaxGradient Life;
            public ParticleSystem.MinMaxGradient Custom1;
            public ParticleSystem.MinMaxGradient Custom2;
            public bool HasCustom1, HasCustom2;
        }

        private readonly List<Particles> m_particles = new List<Particles>();
        private readonly List<(Light light, Color color, bool shine)> m_lights = new List<(Light, Color, bool)>();
        private readonly List<(MeleeWeaponTrail trail, Color[] colors)> m_trails = new List<(MeleeWeaponTrail, Color[])>();
        private readonly List<(Renderer renderer, bool shine)> m_renderers = new List<(Renderer, bool)>();
        private GlowValue m_glow;
        private GlowValue m_shine;
        private bool m_forShine; // which of the two the colour helpers below use right now
        private bool m_captured;

        internal void Set(GlowValue glow, GlowValue shine)
        {
            m_glow = glow;
            m_shine = shine;
            if (m_captured) Recolor();
            else enabled = true;
        }

        private Color Tint(Color c) => m_forShine ? m_shine.ApplyShine(c) : m_glow.Apply(c);

        private bool InShine(Transform t)
        {
            for (; t != null && t != transform; t = t.parent)
            {
                if (t.name.IndexOf(Glow.ShineObject, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private int m_frames;

        private void LateUpdate()
        {
            if (!m_captured)
            {
                if (++m_frames < 3) return; // let the effects' own Start (and the intensity scaler) run first
                Capture();
                m_captured = true;
                Recolor();
            }
            enabled = false;
        }

        private void Capture()
        {
            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.CustomDataModule custom = ps.customData;
                Particles p = new Particles { System = ps, Shine = InShine(ps.transform), Start = ps.main.startColor, Life = ps.colorOverLifetime.color };
                if (custom.enabled && custom.GetMode(ParticleSystemCustomData.Custom1) == ParticleSystemCustomDataMode.Color)
                {
                    p.HasCustom1 = true;
                    p.Custom1 = custom.GetColor(ParticleSystemCustomData.Custom1);
                }
                if (custom.enabled && custom.GetMode(ParticleSystemCustomData.Custom2) == ParticleSystemCustomDataMode.Color)
                {
                    p.HasCustom2 = true;
                    p.Custom2 = custom.GetColor(ParticleSystemCustomData.Custom2);
                }
                m_particles.Add(p);
            }
            foreach (Light light in GetComponentsInChildren<Light>(true))
            {
                m_lights.Add((light, light.color, InShine(light.transform)));
            }
            if (s_trailColors != null)
            {
                foreach (MeleeWeaponTrail trail in GetComponentsInChildren<MeleeWeaponTrail>(true))
                {
                    if (s_trailColors.GetValue(trail) is Color[] colors)
                    {
                        m_trails.Add((trail, (Color[])colors.Clone()));
                    }
                }
            }
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                {
                    m_renderers.Add((renderer, InShine(renderer.transform)));
                }
            }
        }

        private void Recolor()
        {
            foreach (Particles p in m_particles)
            {
                if (p.System == null) continue;
                m_forShine = p.Shine;
                ParticleSystem.MainModule main = p.System.main;
                main.startColor = Recolor(p.Start);
                ParticleSystem.ColorOverLifetimeModule life = p.System.colorOverLifetime;
                if (life.enabled) life.color = Recolor(p.Life);
                ParticleSystem.CustomDataModule custom = p.System.customData;
                if (p.HasCustom1) custom.SetColor(ParticleSystemCustomData.Custom1, Recolor(p.Custom1));
                if (p.HasCustom2) custom.SetColor(ParticleSystemCustomData.Custom2, Recolor(p.Custom2));
            }
            foreach ((Light light, Color color, bool shine) in m_lights)
            {
                m_forShine = shine;
                if (light != null) light.color = Tint(color);
            }
            m_forShine = false;
            foreach ((MeleeWeaponTrail trail, Color[] colors) in m_trails)
            {
                if (trail == null) continue;
                Color[] recolored = new Color[colors.Length];
                for (int i = 0; i < colors.Length; i++) recolored[i] = Tint(colors[i]);
                s_trailColors.SetValue(trail, recolored);
            }
            // material colours: particles get every colour, the weapon itself only its emission (glowing runes, cores)
            foreach ((Renderer renderer, bool shine) in m_renderers)
            {
                if (renderer == null) continue;
                m_forShine = shine;
                bool particles = renderer is ParticleSystemRenderer;
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null) continue;
                    renderer.GetPropertyBlock(s_block, i);
                    foreach (string property in s_materialColors)
                    {
                        if ((particles || property == "_EmissionColor") && material.HasProperty(property))
                        {
                            s_block.SetColor(property, Tint(material.GetColor(property)));
                        }
                    }
                    renderer.SetPropertyBlock(s_block, i);
                }
            }
            m_forShine = false;
        }

        private ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Tint(g.color));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Tint(g.colorMin), Tint(g.colorMax));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Recolor(g.gradient));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Recolor(g.gradientMin), Recolor(g.gradientMax));
                case ParticleSystemGradientMode.RandomColor:
                    ParticleSystem.MinMaxGradient random = new ParticleSystem.MinMaxGradient(Recolor(g.gradient));
                    random.mode = ParticleSystemGradientMode.RandomColor;
                    return random;
                default:
                    return g;
            }
        }

        private Gradient Recolor(Gradient gradient)
        {
            if (gradient == null) return null;
            GradientColorKey[] keys = gradient.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Tint(keys[i].color);
            Gradient result = new Gradient { mode = gradient.mode };
            result.SetKeys(keys, gradient.alphaKeys);
            return result;
        }
    }
}
