using System.Collections.Generic;
using UnityEngine;

namespace MordasGearhold.UI
{
    /// <summary>
    /// Makes every window next to a Gearhold window invisible while that one is open (the bar position window,
    /// the look menu), so the character and the HUD behind can be seen; Restore brings them back exactly as they were.
    /// </summary>
    internal static class InventoryHider
    {
        private static readonly List<(CanvasGroup group, float alpha, bool interactable, bool blocks, bool added)> s_hidden =
            new List<(CanvasGroup, float, bool, bool, bool)>();

        /// <summary>Hides all siblings of <paramref name="keep"/>. Does nothing when already hidden (a redraw of the open window).</summary>
        internal static void Hide(RectTransform keep)
        {
            if (keep == null || keep.parent == null || s_hidden.Count > 0)
            {
                return;
            }
            foreach (Transform child in keep.parent)
            {
                if (child == keep || !child.gameObject.activeSelf)
                {
                    continue;
                }
                CanvasGroup group = child.GetComponent<CanvasGroup>();
                bool added = group == null;
                if (added) group = child.gameObject.AddComponent<CanvasGroup>();
                s_hidden.Add((group, group.alpha, group.interactable, group.blocksRaycasts, added));
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
        }

        internal static void Restore()
        {
            foreach (var entry in s_hidden)
            {
                if (entry.group == null) continue;
                if (entry.added)
                {
                    Object.Destroy(entry.group);
                    continue;
                }
                entry.group.alpha = entry.alpha;
                entry.group.interactable = entry.interactable;
                entry.group.blocksRaycasts = entry.blocks;
            }
            s_hidden.Clear();
        }
    }
}
