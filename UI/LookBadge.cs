using UnityEngine;
using UnityEngine.UI;

namespace MordasGearhold.UI
{
    /// <summary>
    /// Small four-point sparkle (✦) in the top-left corner of an inventory slot element: this piece shows a
    /// transmog look (or is hidden). No marker when the item shows its own look.
    /// Drawn from images, not a text glyph, because the Valheim fonts do not reliably contain such symbols:
    /// two long thin rhombi crossed (a rotated square inside a squashed parent) and a bright core.
    /// </summary>
    internal static class LookBadge
    {
        private const string Name = "MGH_LookSparkle";
        private static readonly Color Glow = new Color(1f, 0.93f, 0.66f, 1f);
        private static readonly Color Core = new Color(1f, 1f, 0.92f, 1f);

        internal static void Show(Transform element, string look)
        {
            Transform marker = element.Find(Name);
            if (look == null)
            {
                if (marker != null && marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                return;
            }
            if (marker == null)
            {
                marker = Build(element);
            }
            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);
        }

        private static Transform Build(Transform element)
        {
            Rect parent = ((RectTransform)element).rect;
            float size = Mathf.Max(10f, parent.width * 0.26f);

            RectTransform root = UiKit.Rect(Name, element);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(size, size);
            root.anchoredPosition = new Vector2(size * 0.62f, -size * 0.62f);

            Ray(root, size, vertical: true);
            Ray(root, size, vertical: false);

            // bright core
            RectTransform core = UiKit.Rect("Core", root);
            core.anchorMin = core.anchorMax = new Vector2(0.5f, 0.5f);
            core.pivot = new Vector2(0.5f, 0.5f);
            core.sizeDelta = new Vector2(size * 0.2f, size * 0.2f);
            core.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image dot = core.gameObject.AddComponent<Image>();
            dot.color = Core;
            dot.raycastTarget = false;

            root.SetAsLastSibling();
            return root;
        }

        /// <summary>One pair of opposite points: a square rotated 45° inside a parent squashed to a thin rhombus.</summary>
        private static void Ray(RectTransform root, float size, bool vertical)
        {
            RectTransform arm = UiKit.Rect(vertical ? "RayV" : "RayH", root);
            arm.anchorMin = arm.anchorMax = new Vector2(0.5f, 0.5f);
            arm.pivot = new Vector2(0.5f, 0.5f);
            arm.sizeDelta = new Vector2(size, size);
            arm.localScale = vertical ? new Vector3(0.28f, 1f, 1f) : new Vector3(1f, 0.28f, 1f);

            RectTransform square = UiKit.Rect("Shape", arm);
            square.anchorMin = square.anchorMax = new Vector2(0.5f, 0.5f);
            square.pivot = new Vector2(0.5f, 0.5f);
            square.sizeDelta = new Vector2(size * 0.7071f, size * 0.7071f); // diagonal = size
            square.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image image = square.gameObject.AddComponent<Image>();
            image.color = Glow;
            image.raycastTarget = false;
            Shadow shadow = square.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(0.8f, -0.8f);
        }
    }
}
