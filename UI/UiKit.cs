using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MordasGearhold.UI
{
    /// <summary>Small helpers that build UI out of vanilla assets so everything keeps the Valheim look.</summary>
    internal static class UiKit
    {
        internal static readonly Color Gold = new Color(1f, 0.79f, 0.43f, 1f);
        internal static readonly Color Parchment = new Color(0.92f, 0.86f, 0.74f, 1f);
        internal static readonly Color Dim = new Color(0.75f, 0.70f, 0.60f, 0.85f);
        internal static readonly Color Rule = new Color(1f, 0.79f, 0.43f, 0.35f);
        internal static readonly Color PanelFallback = new Color(0.11f, 0.09f, 0.075f, 0.97f);

        internal static RectTransform Rect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>Places a rect by its centre in "design" coordinates: origin top-left of the parent, y down.</summary>
        internal static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        internal static TMP_Text Text(Transform parent, TMP_Text template, string value, float size, Color color, TextAlignmentOptions align)
        {
            RectTransform rect = Rect("Text", parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
            }
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        /// <summary>
        /// The vanilla wooden board of a panel: an image that covers (nearly) the whole panel, preferring
        /// sprites named like boards ("wood", "panel", "bkg"). Images of grids and buttons and nearly
        /// transparent ones (masks, shadows) are skipped. 0.8.x took the panel's own plain image instead.
        /// </summary>
        internal static Image FindBackground(RectTransform panel)
        {
            if (panel == null)
            {
                return null;
            }
            Rect panelRect = panel.rect;
            float panelArea = Mathf.Max(1f, panelRect.width * panelRect.height);
            Image best = null;
            float bestScore = 0f;
            foreach (Image image in panel.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null || image.color.a < 0.5f
                    || image.GetComponentInParent<InventoryGrid>() != null || image.GetComponentInParent<Button>() != null)
                {
                    continue;
                }
                // 0.8.2 picked the yellow "selected" frame (woodpanel_crafting_240_mask, tinted FFD800)
                string spriteName = image.sprite.name.ToLowerInvariant();
                if (spriteName.Contains("mask") || image.name.ToLowerInvariant().Contains("selected")
                    || image.color.r < 0.8f || image.color.g < 0.8f || image.color.b < 0.8f)
                {
                    continue;
                }
                Rect r = image.rectTransform.rect;
                float coverage = r.width * r.height / panelArea;
                if (coverage < 0.5f)
                {
                    continue;
                }
                float score = Mathf.Min(coverage, 1.1f) + (LooksLikeBoard(image.sprite.name) ? 0.6f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = image;
                }
            }
            return best;
        }

        /// <summary>A named vanilla image inside a panel (e.g. "Bkg", "Darken"), or null.</summary>
        internal static Image Named(RectTransform panel, string path)
        {
            Transform t = panel != null ? panel.Find(path) : null;
            Image image = t != null ? t.GetComponent<Image>() : null;
            return image != null && image.sprite != null ? image : null;
        }

        /// <summary>A copy of a vanilla image's look as a new child, stretched over the parent with a margin.</summary>
        internal static Image Fill(Transform parent, string name, Image source, float margin)
        {
            RectTransform rect = Rect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-margin, -margin);
            rect.offsetMax = new Vector2(margin, margin);
            Image image = rect.gameObject.AddComponent<Image>();
            CopyLook(image, source, Color.clear);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>The vanilla braided divider line (or a thin rule when the template is missing).</summary>
        internal static Image Divider(Transform parent, Image template, float x, float y, float width)
        {
            if (template == null)
            {
                return Line(parent, x, y, width, 2f, Rule);
            }
            RectTransform rect = Rect("Divider", parent);
            Place(rect, x, y, width, template.rectTransform.rect.height > 1f ? template.rectTransform.rect.height : 18f);
            Image image = rect.gameObject.AddComponent<Image>();
            CopyLook(image, template, Rule);
            image.raycastTarget = false;
            return image;
        }

        private static bool LooksLikeBoard(string name)
        {
            name = name.ToLowerInvariant();
            return name.Contains("wood") || name.Contains("panel") || name.Contains("bkg") || name.Contains("background");
        }

        /// <summary>One log line per image of a panel: path, sprite, size, colour. For finding vanilla assets.</summary>
        internal static string DescribeImages(RectTransform panel)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (panel == null)
            {
                return "(none)";
            }
            foreach (Image image in panel.GetComponentsInChildren<Image>(true))
            {
                Rect r = image.rectTransform.rect;
                sb.Append("\n  ").Append(Path(image.transform, panel)).Append(" | sprite=").Append(image.sprite != null ? image.sprite.name : "-")
                  .Append(" | ").Append(r.width.ToString("0")).Append('x').Append(r.height.ToString("0"))
                  .Append(" | type=").Append(image.type).Append(" | color=").Append(ColorUtility.ToHtmlStringRGBA(image.color));
            }
            return sb.ToString();
        }

        private static string Path(Transform t, Transform root)
        {
            string path = t.name;
            while (t != root && t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        internal static void CopyLook(Image target, Image source, Color fallback)
        {
            if (source == null)
            {
                target.color = fallback;
                return;
            }
            target.sprite = source.sprite;
            target.type = source.type;
            target.material = source.material;
            target.color = source.color;
            target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        }

        internal static Image Line(Transform parent, float x, float y, float width, float height, Color color)
        {
            RectTransform rect = Rect("Line", parent);
            Place(rect, x, y, width, height);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
