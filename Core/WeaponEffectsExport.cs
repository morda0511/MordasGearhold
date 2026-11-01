using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// For sorting out which weapon has which effect: Ctrl + Shift + F9 in the world writes two files into BepInEx/config:
    ///  - MordasGearhold-weapons.csv: one line per weapon (damage types, number of particle systems, lights, trails,
    ///    glowing materials, shimmer, colours, attack effects, status effects), to sort and filter in a spreadsheet;
    ///  - MordasGearhold-weapons.txt: the same in detail, every effect part with its path and colour.
    /// </summary>
    internal static class WeaponEffectsExport
    {
        internal static void Tick()
        {
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                || !Input.GetKeyDown(KeyCode.F9))
            {
                return;
            }
            try
            {
                int count = Export();
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Gearhold: " + count + " weapons written (config/MordasGearhold-weapons.csv and .txt)");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("Gearhold weapon effects export failed: " + ex);
            }
        }

        private static int Export()
        {
            if (ObjectDB.instance == null) return 0;
            StringBuilder csv = new StringBuilder();
            StringBuilder txt = new StringBuilder();
            csv.AppendLine("prefab;name;type;skill;damage;particles;lights;trails;lines;glowing_materials;shimmer;colours;attack_effects;status_effects");
            int count = 0;
            foreach (GameObject prefab in ObjectDB.instance.m_items.OrderBy(p => p != null ? p.name : ""))
            {
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || !Transmog.IsWeapon(drop.m_itemData)) continue;
                ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                count++;

                List<string> detail = new List<string>();
                HashSet<string> colours = new HashSet<string>();
                int particles = 0, lights = 0, trails = 0, lines = 0, glowing = 0;
                bool shimmer = false;
                foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.IndexOf(Glow.ShineObject, System.StringComparison.OrdinalIgnoreCase) >= 0) shimmer = true;
                }
                foreach (ParticleSystem ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles++;
                    Color c = ps.main.startColor.Evaluate(0.5f);
                    string name = ColorName(c);
                    if (name != "none") colours.Add(name);
                    detail.Add("  particles  " + PathOf(ps.transform, prefab.transform) + "  start " + Hex(c) + " (" + name + ")  max " + ps.main.maxParticles
                        + (ps.colorOverLifetime.enabled ? "  colourOverLifetime" : "") + (ps.trails.enabled ? "  trails" : ""));
                }
                foreach (Light light in prefab.GetComponentsInChildren<Light>(true))
                {
                    lights++;
                    string name = ColorName(light.color);
                    if (name != "none") colours.Add(name);
                    detail.Add("  light      " + PathOf(light.transform, prefab.transform) + "  " + Hex(light.color) + " (" + name + ")  range " + light.range.ToString("0.0") + "  intensity " + light.intensity.ToString("0.0"));
                }
                foreach (MeleeWeaponTrail trail in prefab.GetComponentsInChildren<MeleeWeaponTrail>(true))
                {
                    trails++;
                    detail.Add("  swing trail " + PathOf(trail.transform, prefab.transform));
                }
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is TrailRenderer) { trails++; detail.Add("  trail renderer " + PathOf(renderer.transform, prefab.transform)); continue; }
                    if (renderer is LineRenderer) { lines++; detail.Add("  line renderer  " + PathOf(renderer.transform, prefab.transform)); continue; }
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        if (material.HasProperty("_EmissionColor"))
                        {
                            Color e = material.GetColor("_EmissionColor");
                            if (e.maxColorComponent > 0.05f)
                            {
                                glowing++;
                                string name = ColorName(e);
                                if (name != "none") colours.Add(name);
                                detail.Add("  glowing material " + PathOf(renderer.transform, prefab.transform) + "  " + material.name + "  emission " + Hex(e) + " (" + name + ")  shader " + (material.shader != null ? material.shader.name : "?"));
                            }
                        }
                    }
                }

                List<string> damage = new List<string>();
                HitData.DamageTypes d = shared.m_damages;
                if (d.m_fire > 0f) damage.Add("fire");
                if (d.m_frost > 0f) damage.Add("frost");
                if (d.m_lightning > 0f) damage.Add("lightning");
                if (d.m_poison > 0f) damage.Add("poison");
                if (d.m_spirit > 0f) damage.Add("spirit");

                List<string> attackEffects = new List<string>();
                CollectEffects(attackEffects, "primary", shared.m_attack);
                CollectEffects(attackEffects, "secondary", shared.m_secondaryAttack);
                List<string> status = new List<string>();
                if (shared.m_attackStatusEffect != null) status.Add("attack:" + shared.m_attackStatusEffect.name);
                if (shared.m_equipStatusEffect != null) status.Add("equip:" + shared.m_equipStatusEffect.name);

                string display = Localization.instance.Localize(shared.m_name);
                csv.Append(prefab.name).Append(';').Append(display).Append(';').Append(shared.m_itemType).Append(';').Append(shared.m_skillType).Append(';')
                    .Append(string.Join(",", damage)).Append(';').Append(particles).Append(';').Append(lights).Append(';').Append(trails).Append(';').Append(lines).Append(';')
                    .Append(glowing).Append(';').Append(shimmer ? "yes" : "").Append(';').Append(string.Join(",", colours.OrderBy(c => c))).Append(';')
                    .Append(string.Join(" | ", attackEffects)).Append(';').Append(string.Join(",", status)).AppendLine();

                txt.Append(prefab.name).Append("  (").Append(display).Append(", ").Append(shared.m_itemType).Append(", ").Append(shared.m_skillType).AppendLine(")");
                txt.Append("  damage: ").AppendLine(damage.Count > 0 ? string.Join(", ", damage) : "-");
                txt.Append("  glow parts: ").Append(particles).Append(" particle systems, ").Append(lights).Append(" lights, ").Append(trails).Append(" trails, ")
                    .Append(lines).Append(" lines, ").Append(glowing).Append(" glowing materials").AppendLine(shimmer ? ", upgrade shimmer" : "");
                foreach (string line in detail) txt.AppendLine(line);
                foreach (string line in attackEffects) txt.AppendLine("  attack effect " + line);
                foreach (string line in status) txt.AppendLine("  status effect " + line);
                txt.AppendLine();
            }
            File.WriteAllText(Path.Combine(Paths.ConfigPath, "MordasGearhold-weapons.csv"), csv.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(Paths.ConfigPath, "MordasGearhold-weapons.txt"), txt.ToString(), new UTF8Encoding(true));
            return count;
        }

        /// <summary>The prefab names of the effects an attack plays: they tell fire, frost or lightning even when the weapon model has no glow.</summary>
        private static void CollectEffects(List<string> into, string label, Attack attack)
        {
            if (attack == null) return;
            AddList(into, label + " start", attack.m_startEffect);
            AddList(into, label + " trail", attack.m_trailStartEffect);
            AddList(into, label + " trigger", attack.m_triggerEffect);
            AddList(into, label + " burst", attack.m_burstEffect);
            AddList(into, label + " hit", attack.m_hitEffect);
            AddList(into, label + " hit ground", attack.m_hitTerrainEffect);
            if (attack.m_attackProjectile != null) into.Add(label + " projectile " + attack.m_attackProjectile.name);
        }

        private static void AddList(List<string> into, string label, EffectList list)
        {
            if (list?.m_effectPrefabs == null) return;
            foreach (EffectList.EffectData effect in list.m_effectPrefabs)
            {
                if (effect?.m_prefab != null) into.Add(label + " " + effect.m_prefab.name);
            }
        }

        private static string PathOf(Transform t, Transform root)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b)));

        /// <summary>A rough colour name from the hue: for sorting, not for show. "none" for black or invisible.</summary>
        private static string ColorName(Color c)
        {
            Color.RGBToHSV(new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b)), out float h, out float s, out float v);
            if (v < 0.08f || c.a < 0.02f) return "none";
            if (s < 0.2f) return v > 0.7f ? "white" : "grey";
            float deg = h * 360f;
            if (deg < 15f || deg >= 340f) return "red";
            if (deg < 40f) return "orange";
            if (deg < 70f) return "yellow";
            if (deg < 160f) return "green";
            if (deg < 200f) return "cyan";
            if (deg < 260f) return "blue";
            if (deg < 300f) return "purple";
            return "pink";
        }
    }
}
