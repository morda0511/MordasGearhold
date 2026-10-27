using System.Collections.Generic;
using UnityEngine;

namespace MordasGearhold.UI
{
    /// <summary>
    /// Makes every window next to a Gearhold window invisible while that one is open (the bar position window,
    /// the look menu, the slots window), so the character and the HUD behind can be seen; Restore brings them back
    /// exactly as they were. Two ways at once: a CanvasGroup (alpha 0, no clicks) and a switched-off Canvas on the
    /// window. The second one holds when the game animates the group's alpha itself.
    /// </summary>
    internal static class InventoryHider
    {
        private sealed class Entry
        {
            public CanvasGroup Group;
            public float Alpha;
            public bool Interactable, Blocks, GroupAdded;
            public Canvas Canvas;
            public bool CanvasEnabled, CanvasAdded;
        }

        private static readonly List<Entry> s_hidden = new List<Entry>();

        /// <summary>Hides all siblings of <paramref name="keep"/> except those named in <paramref name="alsoKeep"/>. Does nothing when already hidden (a redraw of the open window).</summary>
        internal static void Hide(RectTransform keep, params string[] alsoKeep)
        {
            if (keep == null || keep.parent == null || s_hidden.Count > 0)
            {
                return;
            }
            foreach (Transform child in keep.parent)
            {
                if (child == keep || !child.gameObject.activeSelf || System.Array.IndexOf(alsoKeep, child.name) >= 0)
                {
                    continue;
                }
                Entry e = new Entry();
                e.Group = child.GetComponent<CanvasGroup>();
                e.GroupAdded = e.Group == null;
                if (e.GroupAdded) e.Group = child.gameObject.AddComponent<CanvasGroup>();
                e.Alpha = e.Group.alpha;
                e.Interactable = e.Group.interactable;
                e.Blocks = e.Group.blocksRaycasts;
                e.Group.alpha = 0f;
                e.Group.interactable = false;
                e.Group.blocksRaycasts = false;

                e.Canvas = child.GetComponent<Canvas>();
                e.CanvasAdded = e.Canvas == null;
                if (e.CanvasAdded) e.Canvas = child.gameObject.AddComponent<Canvas>();
                e.CanvasEnabled = e.Canvas.enabled;
                e.Canvas.enabled = false;
                s_hidden.Add(e);
                Plugin.Log.LogInfo("Gearhold hides window \"" + child.name + "\" (own CanvasGroup: " + !e.GroupAdded + ", own Canvas: " + !e.CanvasAdded + ", animator: " + (child.GetComponent<Animator>() != null) + ")");
            }
        }

        internal static void Restore()
        {
            foreach (Entry e in s_hidden)
            {
                if (e.Canvas != null)
                {
                    if (e.CanvasAdded) Object.Destroy(e.Canvas);
                    else e.Canvas.enabled = e.CanvasEnabled;
                }
                if (e.Group == null) continue;
                if (e.GroupAdded)
                {
                    Object.Destroy(e.Group);
                    continue;
                }
                e.Group.alpha = e.Alpha;
                e.Group.interactable = e.Interactable;
                e.Group.blocksRaycasts = e.Blocks;
            }
            s_hidden.Clear();
        }
    }
}
