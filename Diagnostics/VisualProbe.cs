using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace MordasGearhold.Diagnostics
{
    /// <summary>
    /// TEMPORARY diagnostic probe for the planned dye feature (config LogVisualDiagnostics, on in this test build).
    /// Runs only when the local player's visible gear was actually rebuilt: VisEquipment.UpdateEquipmentVisuals calls
    /// UpdateLodgroup exclusively after a piece changed, so nothing is polled. It logs, once per
    /// material, which shader each piece uses and all its colour / float / texture properties, plus the body
    /// material that carries the chest and leg textures (_ChestTex, _LegsTex). Changes nothing.
    /// </summary>
    [HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
    internal static class VisualProbe
    {
        private static readonly HashSet<string> s_logged = new HashSet<string>();

        private static readonly string[] s_groups =
        {
            "m_helmetItemInstance", "m_chestItemInstances", "m_legItemInstances", "m_shoulderItemInstances",
            "m_leftItemInstance", "m_rightItemInstance", "m_utilityItemInstances", "m_trinketItemInstances",
        };

        private static void Postfix(VisEquipment __instance)
        {
            if (!Plugin.LogVisualDiagnostics.Value || Player.m_localPlayer == null
                || __instance.gameObject != Player.m_localPlayer.gameObject && !__instance.transform.IsChildOf(Player.m_localPlayer.transform))
            {
                return;
            }
            StringBuilder sb = new StringBuilder();
            if (__instance.m_bodyModel != null)
            {
                Describe(sb, "body", __instance.m_bodyModel);
            }
            Traverse t = Traverse.Create(__instance);
            foreach (string group in s_groups)
            {
                object value = t.Field(group).GetValue();
                if (value is GameObject go && go != null)
                {
                    DescribeObject(sb, group, go);
                }
                else if (value is List<GameObject> list)
                {
                    foreach (GameObject part in list)
                    {
                        if (part != null) DescribeObject(sb, group, part);
                    }
                }
            }
            if (sb.Length > 0)
            {
                Plugin.Log.LogInfo("[VisualProbe]" + sb);
            }
        }

        private static void DescribeObject(StringBuilder sb, string group, GameObject go)
        {
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                Describe(sb, group + "/" + go.name, renderer);
            }
        }

        private static void Describe(StringBuilder sb, string where, Renderer renderer)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null)
                {
                    continue;
                }
                string key = where + "|" + material.name;
                if (!s_logged.Add(key))
                {
                    continue;
                }
                Shader shader = material.shader;
                sb.Append("\n  ").Append(where).Append(" | renderer=").Append(renderer.name)
                  .Append(" | material=").Append(material.name).Append(" | shader=").Append(shader.name);
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    string name = shader.GetPropertyName(i);
                    ShaderPropertyType type = shader.GetPropertyType(i);
                    sb.Append("\n      ").Append(type).Append(' ').Append(name);
                    if (type == ShaderPropertyType.Color)
                    {
                        sb.Append(" = ").Append(material.GetColor(name));
                    }
                    else if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                    {
                        sb.Append(" = ").Append(material.GetFloat(name).ToString("0.###"));
                    }
                    else if (type == ShaderPropertyType.Texture)
                    {
                        Texture tex = material.GetTexture(name);
                        sb.Append(" = ").Append(tex != null ? tex.name : "-");
                    }
                }
            }
        }
    }
}
