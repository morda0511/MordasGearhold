using System;
using System.Collections.Generic;
using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// One item = one record. A record is a vanilla inventory package (format written by
    /// <see cref="Inventory.Save"/>) that contains exactly one item, so every field vanilla persists
    /// (durability, quality, variant, crafter, custom data, world level, flags) survives unchanged.
    /// Records are parsed with the static <c>ItemDrop.ItemData.Load</c>, which needs no prefab, so a
    /// record whose prefab is missing can be kept as raw bytes ("dormant") instead of being dropped
    /// the way <c>Inventory.Load</c> drops it.
    /// </summary>
    internal sealed class ItemRecord
    {
        public byte[] Raw;
        public int FormatVersion;
        public int PrefabHash;
        public ItemDrop.ItemData Header; // parsed fields only, m_shared is NOT set
        public string ParseError;

        public bool Parsed => ParseError == null;
        public Vector2i Pos => Header != null ? Header.m_gridPos : new Vector2i(-1, -1);

        /// <summary>Why the record can't become a live item right now, or null when it can.</summary>
        public string DormantReason()
        {
            if (!Parsed)
            {
                return ParseError;
            }
            if (ObjectDB.instance == null)
            {
                return "ObjectDB not ready";
            }
            GameObject prefab = ObjectDB.instance.GetItemPrefab(PrefabHash);
            if (prefab == null)
            {
                return "item prefab " + PrefabHash + " not loaded";
            }
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop == null)
            {
                return "prefab " + prefab.name + " has no ItemDrop";
            }
            // Inventory.Load clamps the stack to the prefab's current max stack (Stage-0 C.2): never let it.
            if (Header.m_stack > drop.m_itemData.m_shared.m_maxStackSize)
            {
                return "stack " + Header.m_stack + " exceeds max stack " + drop.m_itemData.m_shared.m_maxStackSize;
            }
            return null;
        }
    }

    internal static class ItemRecords
    {
        private static int s_inventoryFormat = -1;

        /// <summary>The item format version the running game writes (110 for Valheim 1.0.20).</summary>
        internal static int InventoryFormat
        {
            get
            {
                if (s_inventoryFormat < 0)
                {
                    ZPackage pkg = new ZPackage();
                    new Inventory("MordasGearhold.Probe", null, 1, 1).Save(pkg);
                    s_inventoryFormat = new ZPackage(pkg.GetArray()).ReadInt();
                }
                return s_inventoryFormat;
            }
        }

        internal static bool CanWrite(ItemDrop.ItemData item, out string reason)
        {
            // ItemData.Save writes no prefab when m_dropPrefab is null and the item could never be loaded again.
            if (item.m_dropPrefab == null)
            {
                reason = "item " + item.m_shared?.m_name + " has no prefab";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>Writes the item with <paramref name="storedPos"/> as its grid position (logical set position).</summary>
        internal static byte[] Write(ItemDrop.ItemData item, Vector2i storedPos)
        {
            Vector2i livePos = item.m_gridPos;
            try
            {
                item.m_gridPos = storedPos;
                ZPackage pkg = new ZPackage();
                pkg.Write(InventoryFormat);
                pkg.Write((ushort)1);
                item.Save(pkg);
                return pkg.GetArray();
            }
            finally
            {
                item.m_gridPos = livePos;
            }
        }

        internal static ItemRecord Parse(byte[] raw)
        {
            ItemRecord record = new ItemRecord { Raw = raw };
            try
            {
                ZPackage pkg = new ZPackage(raw);
                record.FormatVersion = pkg.ReadInt();
                if (record.FormatVersion > InventoryFormat)
                {
                    record.ParseError = "item format " + record.FormatVersion + " is newer than this game (" + InventoryFormat + ")";
                    return record;
                }
                if (record.FormatVersion < (int)Version.Item.Smaller)
                {
                    record.ParseError = "item format " + record.FormatVersion + " is not supported";
                    return record;
                }
                int count = pkg.ReadUShort();
                if (count != 1)
                {
                    record.ParseError = "record holds " + count + " items";
                    return record;
                }
                (int hash, ItemDrop.ItemData data) = ItemDrop.ItemData.Load(pkg, (Version.Item)record.FormatVersion);
                record.PrefabHash = hash;
                record.Header = data;
                if (hash == 0)
                {
                    record.ParseError = "record has no prefab";
                }
            }
            catch (Exception ex)
            {
                record.ParseError = "unreadable record: " + ex.Message;
            }
            return record;
        }

        /// <summary>
        /// Rebuilds a live item through the vanilla load path (temporary inventory + Inventory.Load),
        /// exactly like a character load would. Call only when <see cref="ItemRecord.DormantReason"/> is null.
        /// </summary>
        internal static ItemDrop.ItemData Instantiate(ItemRecord record)
        {
            Inventory scratch = new Inventory("MordasGearhold.Scratch", null, Topology.Width, 255);
            scratch.Load(new ZPackage(record.Raw));
            List<ItemDrop.ItemData> items = scratch.GetAllItems();
            return items.Count == 1 ? items[0] : null;
        }

        /// <summary>Field-by-field check that a record really describes the live item it was written from.</summary>
        internal static bool Matches(ItemDrop.ItemData live, ItemRecord record, Vector2i storedPos, out string difference)
        {
            difference = null;
            ItemDrop.ItemData h = record.Header;
            if (!record.Parsed) difference = record.ParseError;
            else if (live.m_dropPrefab == null || record.PrefabHash != live.m_dropPrefab.name.GetStableHashCode()) difference = "prefab";
            else if (h.m_stack != live.m_stack) difference = "stack";
            else if (h.m_quality != live.m_quality) difference = "quality";
            else if (h.m_variant != live.m_variant) difference = "variant";
            else if (h.m_crafterID != live.m_crafterID || h.m_crafterName != live.m_crafterName) difference = "crafter";
            else if (h.m_worldLevel != live.m_worldLevel) difference = "world level";
            else if (Mathf.Abs(h.m_durability - live.m_durability) > 0.011f) difference = "durability";
            else if (h.m_gridPos != storedPos) difference = "position";
            else if (!SameCustomData(h.m_customData, live.m_customData)) difference = "custom data";
            return difference == null;
        }

        private static bool SameCustomData(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            foreach (KeyValuePair<string, string> pair in a)
            {
                if (!b.TryGetValue(pair.Key, out string value) || value != pair.Value)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
