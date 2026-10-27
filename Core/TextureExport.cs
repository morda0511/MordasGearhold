using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// For the dye lab: Ctrl + Shift + F10 in the world writes the textures of every armour piece, cape and shield
    /// (model texture and the painted body textures) as PNG into BepInEx/config/MordasGearhold-textures.
    /// </summary>
    internal static class TextureExport
    {
        private static readonly int s_chestTex = Shader.PropertyToID("_ChestTex");
        private static readonly int s_legsTex = Shader.PropertyToID("_LegsTex");

        internal static void Tick()
        {
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                || !Input.GetKeyDown(KeyCode.F10))
            {
                return;
            }
            try
            {
                int count = Export(Path.Combine(Paths.ConfigPath, "MordasGearhold-textures"));
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Gearhold: " + count + " textures exported (config/MordasGearhold-textures)");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("Gearhold texture export failed: " + ex);
            }
        }

        private static int Export(string folder)
        {
            Directory.CreateDirectory(folder);
            HashSet<Texture> done = new HashSet<Texture>();
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            int count = 0;
            if (ObjectDB.instance == null) return 0;
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                switch (drop.m_itemData.m_shared.m_itemType)
                {
                    case ItemDrop.ItemData.ItemType.Helmet:
                    case ItemDrop.ItemData.ItemType.Chest:
                    case ItemDrop.ItemData.ItemType.Legs:
                    case ItemDrop.ItemData.ItemType.Shoulder:
                    case ItemDrop.ItemData.ItemType.Shield:
                        break;
                    default:
                        continue;
                }
                Material armor = drop.m_itemData.m_shared.m_armorMaterial;
                if (armor != null)
                {
                    count += Describe(folder, prefab.name, "armorMaterial", armor, report, done);
                    count += Save(folder, prefab.name, "chest", armor.HasProperty(s_chestTex) ? armor.GetTexture(s_chestTex) : null, done);
                    count += Save(folder, prefab.name, "legs", armor.HasProperty(s_legsTex) ? armor.GetTexture(s_legsTex) : null, done);
                }
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material != null && material.HasProperty("_MainTex"))
                        {
                            count += Save(folder, prefab.name, "model", material.GetTexture("_MainTex"), done);
                        }
                        count += Describe(folder, prefab.name, renderer.name, material, report, done);
                    }
                }
            }
            File.WriteAllText(Path.Combine(folder, "materials.txt"), report.ToString());
            return count;
        }

        /// <summary>Lists the shader and every texture of a material in the report, and exports the metal / gloss maps (they tell metal from cloth).</summary>
        private static int Describe(string folder, string item, string where, Material material, System.Text.StringBuilder report, HashSet<Texture> done)
        {
            if (material == null || material.shader == null) return 0;
            int count = 0;
            report.Append(item).Append(" | ").Append(where).Append(" | ").Append(material.name).Append(" | ").Append(material.shader.name).AppendLine();
            for (int i = 0; i < material.shader.GetPropertyCount(); i++)
            {
                if (material.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                string property = material.shader.GetPropertyName(i);
                Texture texture = material.GetTexture(property);
                report.Append("    ").Append(property).Append(" = ").Append(texture != null ? texture.name + " " + texture.width + "x" + texture.height : "-").AppendLine();
                if (texture != null && (property.IndexOf("Metal", System.StringComparison.OrdinalIgnoreCase) >= 0 || property.IndexOf("Gloss", System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    count += Save(folder, item, property.TrimStart('_'), texture, done);
                }
            }
            return count;
        }

        private static int Save(string folder, string item, string kind, Texture texture, HashSet<Texture> done)
        {
            if (texture == null || !done.Add(texture)) return 0;
            Texture2D copy = TextureDye.ReadBack(texture, 1024, false);
            try
            {
                string name = item + "__" + kind + "__" + texture.name;
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), copy.EncodeToPNG());
                return 1;
            }
            finally
            {
                Object.Destroy(copy);
            }
        }
    }
}
