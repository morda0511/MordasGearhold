using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MordasGearhold.Core;
using MordasGearhold.Patches;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MordasGearhold.UI
{
    /// <summary>
    /// Right above the bow (or crossbow) in the vanilla hotbar while it is drawn: a small list of the arrows / bolts in the Gearhold
    /// ammo slots that fit it, one line each with icon, name and amount. The ammo in use is gold, the others are
    /// dimmed, so one look shows what is equipped and what else is ready (Alt+1/2/3). Config ShowBowAmmo.
    /// </summary>
    internal static class BowAmmoOverlay
    {
        private const float RowHeight = 15f;
        private const float Width = 130f;

        private static readonly FieldInfo s_elements = AccessTools.Field(typeof(HotkeyBar), "m_elements");
        private static FieldInfo s_elementGo;

        private sealed class Strip
        {
            public RectTransform Root;
            public RectTransform[] Rows = new RectTransform[Topology.AmmoSlotCount];
            public Image[] Icons = new Image[Topology.AmmoSlotCount];
            public TMP_Text[] Labels = new TMP_Text[Topology.AmmoSlotCount];
        }

        private static readonly Strip[] s_strips = new Strip[8];
        private static bool s_failed;
        private static Hud s_hudOwner;
        private static HotkeyBar s_hudBar;

        internal static void Tick(Hud hud, Player player)
        {
            if (s_failed)
            {
                return;
            }
            try
            {
                Update(hud, player);
            }
            catch (Exception ex)
            {
                s_failed = true;
                Plugin.Log.LogError("Gearhold bow ammo display failed, disabled until restart: " + ex);
                foreach (Strip strip in s_strips)
                {
                    if (strip?.Root != null) strip.Root.gameObject.SetActive(false);
                }
            }
        }

        private static void Update(Hud hud, Player player)
        {
            if (!Plugin.ShowBowAmmo.Value && s_hudBar == null)
            {
                return; // switched off: nothing to look up
            }
            if (s_hudBar == null || s_hudOwner != hud)
            {
                s_hudOwner = hud;
                s_hudBar = hud.GetComponentInChildren<HotkeyBar>(true); // searched once per HUD, not every frame
            }
            HotkeyBar bar = s_hudBar;
            IList elements = bar != null && s_elements != null ? s_elements.GetValue(bar) as IList : null;
            bool on = Plugin.ShowBowAmmo.Value && player != null && GearholdState.IsReady(player) && elements != null;
            Inventory inventory = on ? player.GetInventory() : null;
            for (int x = 0; x < s_strips.Length; x++)
            {
                ItemDrop.ItemData bow = on ? inventory.GetItemAt(x, 0) : null;
                GameObject element = bow != null && x < elements.Count ? ElementGo(elements[x]) : null;
                bool show = element != null && bow.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow && player.IsItemEquiped(bow)
                    && Fill(player, bow, Get(x, element));
                Strip strip = s_strips[x];
                if (strip?.Root != null && strip.Root.gameObject.activeSelf != show)
                {
                    strip.Root.gameObject.SetActive(show);
                }
            }
        }

        private static GameObject ElementGo(object element)
        {
            if (element == null)
            {
                return null;
            }
            if (s_elementGo == null)
            {
                s_elementGo = AccessTools.Field(element.GetType(), "m_go");
            }
            return s_elementGo?.GetValue(element) as GameObject;
        }

        /// <summary>One line per fitting ammo slot (slot 1 on top); false when none of them holds fitting ammo.</summary>
        private static bool Fill(Player player, ItemDrop.ItemData bow, Strip strip)
        {
            ItemDrop.ItemData inUse = Access.AmmoItem(player);
            int count = 0;
            for (int n = 0; n < Topology.AmmoSlotCount; n++)
            {
                ItemDrop.ItemData ammo = Equipment.ItemIn(player, Topology.AmmoSlot(n));
                if (ammo != null && ammo.m_shared.m_ammoType == bow.m_shared.m_ammoType)
                {
                    count++;
                }
            }
            int line = 0;
            for (int n = 0; n < Topology.AmmoSlotCount; n++)
            {
                ItemDrop.ItemData ammo = Equipment.ItemIn(player, Topology.AmmoSlot(n));
                bool fits = ammo != null && ammo.m_shared.m_ammoType == bow.m_shared.m_ammoType;
                strip.Rows[n].gameObject.SetActive(fits);
                if (!fits)
                {
                    continue;
                }
                bool active = ammo == inUse;
                // the list grows upwards from the slot: the first line is the top one
                strip.Rows[n].anchoredPosition = new Vector2(0f, (count - 1 - line) * RowHeight);
                strip.Icons[n].sprite = ammo.GetIcon();
                strip.Icons[n].color = active ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                string name = Localization.instance.Localize(ammo.m_shared.m_name);
                if (name.Length > 16) name = name.Substring(0, 15) + "…";
                strip.Labels[n].text = name + "  " + ammo.m_stack;
                strip.Labels[n].color = active ? UiKit.Gold : new Color(0.85f, 0.82f, 0.75f, 0.7f);
                line++;
            }
            strip.Root.sizeDelta = new Vector2(Width, count * RowHeight);
            return count > 0;
        }

        /// <summary>The list of one hotbar slot, sitting on top of that slot's element (rebuilt if vanilla recreated it).</summary>
        private static Strip Get(int x, GameObject element)
        {
            Strip strip = s_strips[x];
            if (strip?.Root != null && strip.Root.parent == element.transform)
            {
                return strip;
            }
            if (strip?.Root != null)
            {
                UnityEngine.Object.Destroy(strip.Root.gameObject);
            }
            strip = new Strip();
            RectTransform slot = (RectTransform)element.transform;
            strip.Root = UiKit.Rect("MordasGearholdBowAmmo", slot);
            // anchored to the top centre of the slot, growing upwards
            strip.Root.anchorMin = strip.Root.anchorMax = new Vector2(0.5f, 1f);
            strip.Root.pivot = new Vector2(0.5f, 0f);
            strip.Root.anchoredPosition = new Vector2(0f, 2f);
            TMP_Text font = element.transform.Find("amount")?.GetComponent<TMP_Text>();
            for (int n = 0; n < Topology.AmmoSlotCount; n++)
            {
                RectTransform row = UiKit.Rect("Ammo" + (n + 1), strip.Root);
                row.anchorMin = row.anchorMax = new Vector2(0f, 0f);
                row.pivot = new Vector2(0f, 0f);
                row.sizeDelta = new Vector2(Width, RowHeight);
                strip.Rows[n] = row;

                RectTransform icon = UiKit.Rect("Icon", row);
                icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.sizeDelta = new Vector2(RowHeight - 1f, RowHeight - 1f);
                icon.anchoredPosition = Vector2.zero;
                strip.Icons[n] = icon.gameObject.AddComponent<Image>();
                strip.Icons[n].preserveAspect = true;
                strip.Icons[n].raycastTarget = false;

                TMP_Text label = UiKit.Text(row, font, "", 11f, Color.white, TextAlignmentOptions.Left);
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = new Vector2(RowHeight + 2f, 0f);
                labelRect.offsetMax = Vector2.zero;
                label.outlineWidth = 0.25f;
                label.outlineColor = Color.black;
                strip.Labels[n] = label;
            }
            s_strips[x] = strip;
            return strip;
        }
    }
}
