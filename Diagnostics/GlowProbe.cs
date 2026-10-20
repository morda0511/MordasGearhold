using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MordasGearhold.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MordasGearhold.Diagnostics
{
    /// <summary>
    /// TEMPORARY diagnostic probe for the planned weapon glow colours (config LogGlowDiagnostics, on in this test
    /// build). Runs only when the local player's visible gear was rebuilt (VisEquipment.UpdateLodgroup, so nothing
    /// is polled) and logs once per weapon what its glow is made of: particle systems with their colours, lights,
    /// trails, materials with emission, other effect components, and the effect prefabs of the item and its attacks
    /// (hit, trigger, trail ...). Changes nothing.
    /// </summary>
    [HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
    internal static class GlowProbe
    {
        private static readonly HashSet<string> s_logged = new HashSet<string>();

        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_right =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_left =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");

        private static void Postfix(VisEquipment __instance)
        {
            Player player = Player.m_localPlayer;
            if (!Plugin.LogGlowDiagnostics.Value || player == null || __instance.gameObject != player.gameObject)
            {
                return;
            }
            Probe(s_right(__instance), Access.RightItem(player));
            Probe(s_left(__instance), Access.LeftItem(player));
        }

        private static void Probe(GameObject instance, ItemDrop.ItemData item)
        {
            // keyed by the item, not the instance: nearly every weapon instance is called "attach(Clone)"
            if (instance == null || item == null || !s_logged.Add(item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name))
            {
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("\n  weapon ").Append(item.m_shared.m_name).Append(" (").Append(instance.name).Append(")");
            DescribeVisual(sb, instance, "    ", withRenderers: true);

            // effect prefabs the item spawns (hit, trigger, trail, ...)
            DescribeEffectLists(sb, item.m_shared, "shared");
            if (item.m_shared.m_attack != null) DescribeEffectLists(sb, item.m_shared.m_attack, "attack");
            if (item.m_shared.m_secondaryAttack != null) DescribeEffectLists(sb, item.m_shared.m_secondaryAttack, "secondary");
            if (item.m_shared.m_attackStatusEffect != null)
            {
                sb.Append("\n    status effect on hit: ").Append(item.m_shared.m_attackStatusEffect.name);
            }
            Plugin.Log.LogInfo("[GlowProbe]" + sb);
        }

        private static void DescribeEffectLists(StringBuilder sb, object owner, string label)
        {
            foreach (FieldInfo field in owner.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(EffectList) || !(field.GetValue(owner) is EffectList list) || list.m_effectPrefabs == null)
                {
                    continue;
                }
                foreach (EffectList.EffectData data in list.m_effectPrefabs)
                {
                    if (data?.m_prefab == null) continue;
                    sb.Append("\n    effect ").Append(label).Append('.').Append(field.Name).Append(" -> ").Append(data.m_prefab.name);
                    DescribeVisual(sb, data.m_prefab, "      ", withRenderers: false);
                }
            }
        }

        private static void DescribeVisual(StringBuilder sb, GameObject root, string indent, bool withRenderers)
        {
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                sb.Append('\n').Append(indent).Append("particles ").Append(Path(root, ps.transform))
                    .Append(" | start ").Append(Gradient(main.startColor));
                ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
                if (col.enabled) sb.Append(" | over life ").Append(Gradient(col.color));
                // gradient-mapped flames are white particles: their colours may come from the custom data streams
                ParticleSystem.CustomDataModule custom = ps.customData;
                if (custom.enabled)
                {
                    foreach (ParticleSystemCustomData stream in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 })
                    {
                        ParticleSystemCustomDataMode mode = custom.GetMode(stream);
                        sb.Append(" | ").Append(stream).Append(' ').Append(mode);
                        if (mode == ParticleSystemCustomDataMode.Color) sb.Append(' ').Append(Gradient(custom.GetColor(stream)));
                        else if (mode == ParticleSystemCustomDataMode.Vector)
                        {
                            sb.Append(" (");
                            for (int c = 0; c < custom.GetVectorComponentCount(stream); c++)
                            {
                                ParticleSystem.MinMaxCurve curve = custom.GetVector(stream, c);
                                sb.Append(c > 0 ? " " : "").Append(curve.mode == ParticleSystemCurveMode.Constant ? curve.constant.ToString("0.###") : curve.mode.ToString());
                            }
                            sb.Append(')');
                        }
                    }
                }
                List<ParticleSystemVertexStream> streams = new List<ParticleSystemVertexStream>();
                ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    psr.GetActiveVertexStreams(streams);
                    if (streams.Count > 0) sb.Append(" | streams ").Append(string.Join(",", streams));
                }
                ParticleSystemRenderer pr = ps.GetComponent<ParticleSystemRenderer>();
                if (pr != null && pr.sharedMaterial != null)
                {
                    sb.Append(" | material ").Append(Material(pr.sharedMaterial));
                }
            }
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                sb.Append('\n').Append(indent).Append("light ").Append(Path(root, light.transform))
                    .Append(" | ").Append(light.type).Append(" color ").Append(Hex(light.color))
                    .Append(" intensity ").Append(light.intensity.ToString("0.##")).Append(" range ").Append(light.range.ToString("0.#"));
            }
            foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true))
            {
                sb.Append('\n').Append(indent).Append("trail ").Append(Path(root, trail.transform))
                    .Append(" | ").Append(Hex(trail.startColor)).Append(" -> ").Append(Hex(trail.endColor))
                    .Append(" | material ").Append(trail.sharedMaterial != null ? Material(trail.sharedMaterial) : "-");
            }
            foreach (LineRenderer line in root.GetComponentsInChildren<LineRenderer>(true))
            {
                sb.Append('\n').Append(indent).Append("line ").Append(Path(root, line.transform))
                    .Append(" | ").Append(Hex(line.startColor)).Append(" -> ").Append(Hex(line.endColor));
            }
            if (withRenderers)
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        sb.Append('\n').Append(indent).Append("mesh ").Append(Path(root, renderer.transform))
                            .Append(" | ").Append(Material(material));
                    }
                }
            }
            // other components that may drive a glow (flicker, trails, ...); their colour and material fields too
            HashSet<string> others = new HashSet<string>();
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                others.Add(behaviour.GetType().Name);
                foreach (FieldInfo field in behaviour.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    object value = field.GetValue(behaviour);
                    if (value is Color c)
                        sb.Append('\n').Append(indent).Append(behaviour.GetType().Name).Append('.').Append(field.Name).Append(" = ").Append(Hex(c));
                    else if (value is Color[] cs && cs.Length > 0)
                        sb.Append('\n').Append(indent).Append(behaviour.GetType().Name).Append('.').Append(field.Name).Append(" = ").Append(string.Join(" ", System.Array.ConvertAll(cs, Hex)));
                    else if (value is Gradient g)
                        sb.Append('\n').Append(indent).Append(behaviour.GetType().Name).Append('.').Append(field.Name).Append(" = ").Append(Keys(g));
                    else if (value is Material m && m != null)
                        sb.Append('\n').Append(indent).Append(behaviour.GetType().Name).Append('.').Append(field.Name).Append(" = ").Append(Material(m));
                }
            }
            if (others.Count > 0)
            {
                sb.Append('\n').Append(indent).Append("components: ").Append(string.Join(", ", others));
            }
        }

        /// <summary>Material name, shader, and every colour property plus the emission state.</summary>
        private static string Material(Material material)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(material.name).Append(" [").Append(material.shader != null ? material.shader.name : "-").Append(']');
            Shader shader = material.shader;
            if (shader != null)
            {
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    string name = shader.GetPropertyName(i);
                    ShaderPropertyType type = shader.GetPropertyType(i);
                    if (type == ShaderPropertyType.Color)
                    {
                        sb.Append(' ').Append(name).Append('=').Append(Hex(material.GetColor(name)));
                    }
                    else if (type == ShaderPropertyType.Texture)
                    {
                        Texture t = material.GetTexture(name);
                        sb.Append(' ').Append(name).Append('=').Append(t != null ? t.name : "-");
                    }
                    else if (shader.name.IndexOf("Particle") >= 0)
                    {
                        // particle shaders: every value, to find where e.g. the gradient-mapped flames keep their colours
                        if (type == ShaderPropertyType.Vector) sb.Append(' ').Append(name).Append('=').Append(material.GetVector(name));
                        else sb.Append(' ').Append(name).Append('=').Append(material.GetFloat(name).ToString("0.###"));
                    }
                }
            }
            if (material.IsKeywordEnabled("_EMISSION")) sb.Append(" _EMISSION on");
            return sb.ToString();
        }

        private static string Gradient(ParticleSystem.MinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return Hex(g.color);
                case ParticleSystemGradientMode.TwoColors: return Hex(g.colorMin) + ".." + Hex(g.colorMax);
                case ParticleSystemGradientMode.Gradient: return "gradient " + Keys(g.gradient);
                case ParticleSystemGradientMode.TwoGradients: return "gradients " + Keys(g.gradientMin) + " / " + Keys(g.gradientMax);
                default: return g.mode.ToString();
            }
        }

        private static string Keys(Gradient gradient)
        {
            if (gradient == null) return "-";
            List<string> keys = new List<string>();
            foreach (GradientColorKey key in gradient.colorKeys) keys.Add(Hex(key.color));
            return "(" + string.Join(" ", keys) + ")";
        }

        private static string Hex(Color c) =>
            "#" + ColorUtility.ToHtmlStringRGBA(c) + (c.maxColorComponent > 1f ? "(HDR " + c.maxColorComponent.ToString("0.#") + ")" : "");

        private static string Path(GameObject root, Transform t)
        {
            if (t == root.transform) return root.name;
            List<string> parts = new List<string>();
            for (Transform p = t; p != null && p != root.transform; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
