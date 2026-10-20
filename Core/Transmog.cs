using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using ItemType = ItemDrop.ItemData.ItemType;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Transmog: change how worn gear LOOKS without changing the gear.
    ///  - Unlocked looks: every item whose recipe you know (like the crafting menu), plus every item you crafted:
    ///    recorded when crafting, and for items with you as crafter (ItemData.m_crafterID) in your inventory.
    ///  - Helmet, chest, legs, cape and shield looks are chosen per equipment set.
    ///  - Weapon looks belong to the weapon item itself (stored in its m_customData) and must be the same
    ///    weapon kind (item type + skill), so grip and animations always fit.
    ///  - "Hidden" makes a piece invisible.
    /// Vanilla builds the visible gear from prefab names in Humanoid.SetupVisEquipment and syncs it through
    /// the player ZDO (VisEquipment.Set*Item), so other players see the look as well.
    /// </summary>
    internal static class Transmog
    {
        internal const string Hidden = "-";
        internal const string ItemSkinKey = "MordasGearhold.Skin";
        private const string UnlockKey = GearholdState.KeyPrefix + "Looks";
        private static string LookKey(int set) => GearholdState.KeyPrefix + "Look." + set;

        internal enum Piece { Helmet, Chest, Legs, Shoulder, Shield }

        private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> s_vis =
            AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");

        private static readonly Action<Humanoid, VisEquipment, bool> s_setupVis =
            AccessTools.MethodDelegate<Action<Humanoid, VisEquipment, bool>>(
                AccessTools.Method(typeof(Humanoid), "SetupVisEquipment", new[] { typeof(VisEquipment), typeof(bool) }));

        internal static ItemType TypeOf(Piece piece)
        {
            switch (piece)
            {
                case Piece.Helmet: return ItemType.Helmet;
                case Piece.Chest: return ItemType.Chest;
                case Piece.Legs: return ItemType.Legs;
                case Piece.Shoulder: return ItemType.Shoulder;
                default: return ItemType.Shield;
            }
        }

        internal static Piece? PieceFor(GearSlot slot)
        {
            switch (slot)
            {
                case GearSlot.Helmet: return Piece.Helmet;
                case GearSlot.Chest: return Piece.Chest;
                case GearSlot.Legs: return Piece.Legs;
                case GearSlot.Shoulder: return Piece.Shoulder;
                case GearSlot.Shield: return Piece.Shield;
            }
            return null;
        }

        internal static bool IsWeapon(ItemDrop.ItemData item)
        {
            switch (item.m_shared.m_itemType)
            {
                case ItemType.OneHandedWeapon:
                case ItemType.TwoHandedWeapon:
                case ItemType.TwoHandedWeaponLeft:
                case ItemType.Bow:
                    return true;
            }
            return false;
        }

        private static bool Unlockable(ItemDrop.ItemData item)
        {
            switch (item.m_shared.m_itemType)
            {
                case ItemType.Helmet:
                case ItemType.Chest:
                case ItemType.Legs:
                case ItemType.Shoulder:
                case ItemType.Shield:
                    return true;
            }
            return IsWeapon(item);
        }

        // ------------------------------------------------------------------ unlocks

        internal static HashSet<string> Unlocked(Player player)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            if (player.m_customData.TryGetValue(UnlockKey, out string raw))
            {
                foreach (string name in raw.Split(','))
                {
                    if (name.Length > 0) set.Add(name);
                }
            }
            return set;
        }

        /// <summary>Adds the item's look to the unlocked looks. Returns true when it was new.</summary>
        internal static bool Unlock(Player player, ItemDrop.ItemData item)
        {
            if (item?.m_dropPrefab == null || !Unlockable(item))
            {
                return false;
            }
            HashSet<string> set = Unlocked(player);
            if (!set.Add(item.m_dropPrefab.name))
            {
                return false;
            }
            player.m_customData[UnlockKey] = string.Join(",", set.OrderBy(n => n, StringComparer.Ordinal));
            return true;
        }

        private static float s_nextScan;

        /// <summary>Every few seconds: items in the inventory that you crafted unlock their look.</summary>
        internal static void Tick(Player player)
        {
            if (Time.time < s_nextScan || !GearholdState.IsReady(player))
            {
                return;
            }
            s_nextScan = Time.time + 2f;
            long me = player.GetPlayerID();
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (item.m_crafterID == me)
                {
                    Unlock(player, item);
                }
            }
        }

        /// <summary>
        /// Looks that fit a piece (sorted by display name): every item whose recipe you know
        /// (Player.IsRecipeKnown, the same list the crafting menu uses), plus everything unlocked by crafting.
        /// </summary>
        internal static List<GameObject> LooksFor(Player player, ItemType type, Skills.SkillType? skill)
        {
            List<GameObject> result = new List<GameObject>();
            if (ObjectDB.instance == null)
            {
                return result;
            }
            HashSet<string> names = Unlocked(player);
            foreach (Recipe recipe in ObjectDB.instance.m_recipes)
            {
                if (recipe != null && recipe.m_enabled && recipe.m_item != null
                    && player.IsRecipeKnown(recipe.m_item.m_itemData.m_shared.m_name))
                {
                    names.Add(recipe.m_item.gameObject.name);
                }
            }
            foreach (string name in names)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(name);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData.m_shared.m_itemType != type)
                {
                    continue;
                }
                if (skill.HasValue && drop.m_itemData.m_shared.m_skillType != skill.Value)
                {
                    continue;
                }
                result.Add(prefab);
            }
            return result.OrderBy(p => Localization.instance.Localize(p.GetComponent<ItemDrop>().m_itemData.m_shared.m_name)).ToList();
        }

        // ------------------------------------------------------------------ chosen looks

        /// <summary>Look of a piece in the active set: null = own look, "-" = hidden, else a prefab name.</summary>
        internal static string GetLook(Player player, Piece piece)
        {
            int set = GearholdState.ActiveSet(player);
            if (!player.m_customData.TryGetValue(LookKey(set), out string raw))
            {
                return null;
            }
            foreach (string part in raw.Split(';'))
            {
                string[] kv = part.Split('=');
                if (kv.Length == 2 && kv[0] == piece.ToString() && kv[1].Length > 0)
                {
                    return kv[1];
                }
            }
            return null;
        }

        internal static void SetLook(Player player, Piece piece, string look)
        {
            int set = GearholdState.ActiveSet(player);
            Dictionary<string, string> looks = new Dictionary<string, string>();
            if (player.m_customData.TryGetValue(LookKey(set), out string raw))
            {
                foreach (string part in raw.Split(';'))
                {
                    string[] kv = part.Split('=');
                    if (kv.Length == 2 && kv[1].Length > 0) looks[kv[0]] = kv[1];
                }
            }
            if (string.IsNullOrEmpty(look)) looks.Remove(piece.ToString());
            else looks[piece.ToString()] = look;
            if (looks.Count == 0) player.m_customData.Remove(LookKey(set));
            else player.m_customData[LookKey(set)] = string.Join(";", looks.Select(kv => kv.Key + "=" + kv.Value));
            Refresh(player);
        }

        /// <summary>All looks of one set as stored (null = none); the look menu restores this when closed without saving.</summary>
        internal static string ReadSetLooks(Player player, int set) =>
            player.m_customData.TryGetValue(LookKey(set), out string raw) ? raw : null;

        internal static void WriteSetLooks(Player player, int set, string raw)
        {
            if (string.IsNullOrEmpty(raw)) player.m_customData.Remove(LookKey(set));
            else player.m_customData[LookKey(set)] = raw;
            Refresh(player);
        }

        /// <summary>Non-null when a weapon has its own look or glow colour: it then gets the sparkle marker.</summary>
        internal static string ItemMarker(ItemDrop.ItemData item) =>
            GetItemLook(item) ?? (Glow.Get(item).IsSet ? "glow" : null);

        internal static string GetItemLook(ItemDrop.ItemData item)
        {
            return item != null && item.m_customData.TryGetValue(ItemSkinKey, out string look) && look.Length > 0 ? look : null;
        }

        internal static void SetItemLook(Player player, ItemDrop.ItemData item, string look)
        {
            if (string.IsNullOrEmpty(look)) item.m_customData.Remove(ItemSkinKey);
            else item.m_customData[ItemSkinKey] = look;
            Refresh(player);
        }

        /// <summary>Redraws the player's visible gear (vanilla SetupVisEquipment, with the looks applied).</summary>
        internal static void Refresh(Player player)
        {
            VisEquipment vis = player != null ? s_vis(player) : null;
            if (vis != null)
            {
                s_setupVis(player, vis, false);
                DyeRenderer.Apply(player); // dyes are chosen per set too; pieces that did not change keep their old ones otherwise
            }
        }

        // ------------------------------------------------------------------ applying

        /// <summary>Hash to show for a worn item: its look, 0 when hidden, or the item's own prefab.</summary>
        private static int Hash(ItemDrop.ItemData worn, string look)
        {
            if (worn == null)
            {
                return 0; // a look never shows without real gear in that slot
            }
            if (look == Hidden)
            {
                return 0;
            }
            if (look != null && ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(look) != null)
            {
                return look.GetStableHashCode();
            }
            return worn.m_dropPrefab.name.GetStableHashCode();
        }

        private static string HandLook(ItemDrop.ItemData item, Player player)
        {
            if (item == null) return null;
            if (item.m_shared.m_itemType == ItemType.Shield) return GetLook(player, Piece.Shield);
            return IsWeapon(item) ? GetItemLook(item) : null;
        }

        /// <summary>Postfix of Humanoid.SetupVisEquipment for the local player.</summary>
        internal static void Apply(Player player, VisEquipment vis, bool isRagdoll, ItemDrop.ItemData hiddenLeft, ItemDrop.ItemData hiddenRight)
        {
            ItemDrop.ItemData left = Access.LeftItem(player);
            ItemDrop.ItemData right = Access.RightItem(player);
            if (!isRagdoll)
            {
                vis.SetLeftItem(Hash(left, HandLook(left, player)), left != null && HandLook(left, player) == null ? left.m_variant : 0, left?.m_quality ?? 0);
                vis.SetRightItem(Hash(right, HandLook(right, player)), right?.m_quality ?? 0);
                vis.SetLeftBackItem(Hash(hiddenLeft, HandLook(hiddenLeft, player)), hiddenLeft != null && HandLook(hiddenLeft, player) == null ? hiddenLeft.m_variant : 0, hiddenLeft?.m_quality ?? 0);
                vis.SetRightBackItem(Hash(hiddenRight, HandLook(hiddenRight, player)), hiddenRight?.m_quality ?? 0);
            }
            vis.SetChestItem(Hash(Access.ChestItem(player), GetLook(player, Piece.Chest)));
            vis.SetLegItem(Hash(Access.LegItem(player), GetLook(player, Piece.Legs)));
            vis.SetHelmetItem(Hash(Access.HelmetItem(player), GetLook(player, Piece.Helmet)));
            ItemDrop.ItemData cape = Access.ShoulderItem(player);
            string capeLook = GetLook(player, Piece.Shoulder);
            vis.SetShoulderItem(Hash(cape, capeLook), cape != null && capeLook == null ? cape.m_variant : 0, cape?.m_quality ?? 0);
        }
    }
}
