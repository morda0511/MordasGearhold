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
            if (ColorSync.TryGet(player, out ColorSync.Record record))
            {
                Put(s_right(vis), record.Weapon(ColorSync.Right));
                Put(s_left(vis), record.Weapon(ColorSync.Left));
                Put(s_rightBack(vis), record.Weapon(ColorSync.RightBack));
                Put(s_leftBack(vis), record.Weapon(ColorSync.LeftBack));
                return;
            }
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
            Put(instance, Glow.Get(item), Glow.GetShine(item), Localization.instance.Localize(item.m_shared.m_name));
        }

        private static void Put(GameObject instance, ColorSync.WeaponColors colors)
        {
            if (instance != null)
            {
                Put(instance, colors.Glow, colors.Shine, instance.name);
            }
        }

        private static void Put(GameObject instance, GlowValue glow, GlowValue shine, string label)
        {
            GlowKeeper keeper = instance.GetComponent<GlowKeeper>();
            if (keeper == null)
            {
                if (!glow.IsSet && !shine.IsSet) return; // nothing to change, nothing to remember
                keeper = instance.AddComponent<GlowKeeper>();
            }
            keeper.Label = label;
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
        private static readonly Dictionary<Shader, string[]> s_colorProperties = new Dictionary<Shader, string[]>();
        private static readonly string[] s_effectShaders = { "Particle", "Additive", "Unlit", "Glow", "Alpha Blended", "Gradient Mapped", "Multiply" };

        /// <summary>Every colour property of a shader (cached): effect materials keep their colour under many names.</summary>
        private static string[] ColorProperties(Shader shader)
        {
            if (shader == null) return new string[0];
            if (!s_colorProperties.TryGetValue(shader, out string[] names))
            {
                List<string> list = new List<string>();
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color) list.Add(shader.GetPropertyName(i));
                }
                names = list.ToArray();
                s_colorProperties[shader] = names;
            }
            return names;
        }

        /// <summary>A material of a light effect (flare, flame, glow quad), not of the weapon itself: all its colours are recoloured.</summary>
        private static bool IsEffect(Renderer renderer, Material material)
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) return true;
            string shader = material.shader != null ? material.shader.name : "";
            foreach (string marker in s_effectShaders)
            {
                if (shader.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
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
            public bool HasTrails;
            public ParticleSystem.MinMaxGradient TrailLife, TrailOver;
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
                if (renderer is ParticleSystemRenderer || renderer is MeshRenderer || renderer is SkinnedMeshRenderer
                    || renderer is TrailRenderer || renderer is LineRenderer)
                {
                    m_renderers.Add((renderer, InShine(renderer.transform)));
                }
                if (renderer is TrailRenderer tr) m_lines.Add((renderer, tr.colorGradient, InShine(renderer.transform)));
                else if (renderer is LineRenderer lr) m_lines.Add((renderer, lr.colorGradient, InShine(renderer.transform)));
            }
            foreach (Particles p in m_particles)
            {
                ParticleSystem.TrailModule trails = p.System.trails;
                if (trails.enabled)
                {
                    p.HasTrails = true;
                    p.TrailLife = trails.colorOverLifetime;
                    p.TrailOver = trails.colorOverTrail;
                }
            }
            CaptureLightSources();
            Report();
        }

        // trail and line renderers: their colour is a gradient
        private readonly List<(Renderer renderer, Gradient gradient, bool shine)> m_lines = new List<(Renderer, Gradient, bool)>();

        /// <summary>The name shown in the log for this weapon (its item name; the model itself is always called "attach").</summary>
        internal string Label;

        private static string PathOf(Transform t, Transform root)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != root && p != root.parent; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

        private static string Describe(ParticleSystem.MinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return Hex(g.color);
                case ParticleSystemGradientMode.TwoColors: return Hex(g.colorMin) + ".." + Hex(g.colorMax);
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    return g.gradient != null && g.gradient.colorKeys.Length > 0 ? "gradient " + Hex(g.gradient.colorKeys[0].color) + ".." + Hex(g.gradient.colorKeys[g.gradient.colorKeys.Length - 1].color) : "gradient";
                case ParticleSystemGradientMode.TwoGradients: return "two gradients";
                default: return g.mode.ToString();
            }
        }

        /// <summary>
        /// Once per weapon, in the log: every effect part with its colour before the recolouring. A colour that
        /// stays after GLOW or SHINE is on a part that is not in this list (or comes from outside the weapon).
        /// </summary>
        private void Report()
        {
            string name = !string.IsNullOrEmpty(Label) ? Label : gameObject.name.Replace("(Clone)", "");
            if (!s_reported.Add(name)) return;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("Gearhold effects of ").Append(name).Append(':');
            foreach (Particles p in m_particles)
            {
                ParticleSystemRenderer renderer = p.System.GetComponent<ParticleSystemRenderer>();
                Material material = renderer != null ? renderer.sharedMaterial : null;
                ParticleSystem.LightsModule lights = p.System.lights;
                sb.Append("\n  particles ").Append(PathOf(p.System.transform, transform)).Append(p.Shine ? " [shine]" : "")
                  .Append(" start=").Append(Describe(p.Start))
                  .Append(" shader=").Append(material != null && material.shader != null ? material.shader.name : "-")
                  .Append(lights.enabled ? " particle-lights(" + (lights.light != null ? Hex(lights.light.color) : "none") + (lights.useParticleColor ? ", x particle colour" : "") + ")" : "");
            }
            foreach ((Light light, Color color, bool shine) in m_lights)
            {
                sb.Append("\n  light ").Append(PathOf(light.transform, transform)).Append(shine ? " [shine]" : "").Append(' ').Append(light.type)
                  .Append(' ').Append(Hex(color)).Append(" intensity ").Append(light.intensity.ToString("0.##")).Append(" range ").Append(light.range.ToString("0.#"));
            }
            foreach ((Renderer renderer, Gradient gradient, bool shine) in m_lines)
            {
                sb.Append("\n  ").Append(renderer.GetType().Name).Append(' ').Append(PathOf(renderer.transform, transform));
            }
            foreach ((Renderer renderer, bool shine) in m_renderers)
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && IsEffect(renderer, material))
                    {
                        sb.Append("\n  effect mesh ").Append(PathOf(renderer.transform, transform)).Append(" shader=").Append(material.shader != null ? material.shader.name : "-");
                    }
                }
            }
            sb.Append("\n  swing trails: ").Append(m_trails.Count);
            Plugin.Log.LogInfo(sb.ToString());
        }

        // Lights that particle systems create themselves copy a Light that lives outside this weapon (a shared
        // prefab we must not change): such a system gets its own copy of that Light with the new colour.
        private readonly List<(ParticleSystem system, Light original, Light copy, bool shine)> m_lightSources = new List<(ParticleSystem, Light, Light, bool)>();

        private void CaptureLightSources()
        {
            foreach (Particles p in m_particles)
            {
                ParticleSystem.LightsModule lights = p.System.lights;
                if (lights.enabled && lights.light != null && !lights.light.transform.IsChildOf(transform))
                {
                    m_lightSources.Add((p.System, lights.light, null, p.Shine));
                }
            }
        }

        private void RecolorLightSources()
        {
            for (int i = 0; i < m_lightSources.Count; i++)
            {
                (ParticleSystem system, Light original, Light copy, bool shine) = m_lightSources[i];
                if (system == null || original == null) continue;
                m_forShine = shine;
                bool wanted = (shine ? m_shine : m_glow).IsSet;
                ParticleSystem.LightsModule lights = system.lights;
                if (wanted && copy == null)
                {
                    GameObject clone = Object.Instantiate(original.gameObject, transform);
                    clone.SetActive(false); // only a template for the lights the particles create
                    copy = clone.GetComponent<Light>();
                    m_lightSources[i] = (system, original, copy, shine);
                }
                if (copy != null)
                {
                    copy.color = Tint(original.color);
                    lights.light = wanted ? copy : original;
                }
            }
            m_forShine = false;
        }


        private static readonly HashSet<string> s_reported = new HashSet<string>();

        private void Recolor()
        {
            RecolorLightSources();
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
                if (p.HasTrails)
                {
                    ParticleSystem.TrailModule trails = p.System.trails;
                    trails.colorOverLifetime = Recolor(p.TrailLife);
                    trails.colorOverTrail = Recolor(p.TrailOver);
                }
            }
            foreach ((Renderer renderer, Gradient gradient, bool shine) in m_lines)
            {
                if (renderer == null) continue;
                m_forShine = shine;
                if (renderer is TrailRenderer trail) trail.colorGradient = Recolor(gradient);
                else if (renderer is LineRenderer line) line.colorGradient = Recolor(gradient);
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
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null) continue;
                    // light effects (particles, glow quads, trails) change every colour they have; the weapon's own
                    // material only its emission (glowing runes, cores), so the metal and leather keep their colours
                    bool effect = IsEffect(renderer, material);
                    renderer.GetPropertyBlock(s_block, i);
                    foreach (string property in ColorProperties(material.shader))
                    {
                        if (effect || property == "_EmissionColor")
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
