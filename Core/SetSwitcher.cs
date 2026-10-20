using System;
using System.Collections.Generic;
using System.Linq;

namespace MordasGearhold.Core
{
    /// <summary>
    /// Set switch as one synchronous call on the main thread. Character saves (Player.Save) also run on the
    /// main thread and serialise inventory and m_customData together, so a save can only ever see the state
    /// before or after a switch. The journal covers the remaining case: an exception in the middle.
    /// </summary>
    internal static class SetSwitcher
    {
        internal static bool Switching { get; private set; }

        internal static bool TrySwitch(Player player, int target)
        {
            if (Switching)
            {
                return false;
            }
            if (!CanSwitch(player, target, out string reason))
            {
                if (!string.IsNullOrEmpty(reason))
                {
                    player.Message(MessageHud.MessageType.Center, reason);
                }
                return false;
            }
            Switching = true;
            try
            {
                return Switch(player, target);
            }
            finally
            {
                Switching = false;
            }
        }

        internal static bool CanSwitch(Player player, int target, out string reason)
        {
            reason = null;
            if (player == null || player != Player.m_localPlayer || !GearholdState.IsReady(player))
            {
                reason = "";
                return false;
            }
            int active = GearholdState.ActiveSet(player);
            if (target < 0 || target >= Topology.SetCount || target == active)
            {
                return false;
            }
            if (player.IsDead() || player.IsTeleporting() || Access.IsLoading(player))
                reason = "Can't switch sets right now";
            else if (player.InAttack() || player.InDodge())
                reason = "Can't switch sets while attacking";
            else if (player.IsSwimming() && !player.IsOnGround())
                reason = "Can't switch sets while swimming";
            else if (Access.ActionQueue(player).Count > 0)
                reason = "Wait until your gear is equipped";
            else if (InventoryGui.instance != null && Access.DragItem(InventoryGui.instance) != null)
                reason = "Put down the item you are holding first";
            return reason == null;
        }

        private static bool Switch(Player player, int target)
        {
            Inventory inventory = player.GetInventory();
            GearholdState.State state = GearholdState.ReadState(player);
            int from = state.Active;

            // extra trinkets are stored unflagged and switched on again by Trinkets.Tick after the switch;
            // otherwise the re-equip step below would push them into the single vanilla trinket slot
            Patches.Trinkets.ClearFlags(player);

            // ---- 1. read and validate everything before touching anything
            if (!GearholdState.TryReadSet(player, from, out SetPackage fromPackage, out string error))
            {
                return Fail(player, $"Set {from + 1} is damaged ({error})");
            }
            if (!GearholdState.TryReadSet(player, target, out SetPackage targetPackage, out error))
            {
                return Fail(player, $"Set {target + 1} is damaged ({error})");
            }

            List<ItemDrop.ItemData> outgoing = inventory.GetAllItems().Where(i => Topology.IsSetOwned(player, i.m_gridPos)).ToList();
            List<ItemDrop.ItemData> shared = inventory.GetAllItems().Where(i => !Topology.IsSetOwned(player, i.m_gridPos)).ToList();
            List<int> sharedStacks = shared.Select(i => i.m_stack).ToList();

            SetPackage outPackage = new SetPackage { SetIndex = from };
            foreach (ItemDrop.ItemData item in outgoing)
            {
                if (!ItemRecords.CanWrite(item, out string why))
                {
                    return Fail(player, "Set switch cancelled: " + why);
                }
                Vector2i logical = Topology.ToLogical(player, item.m_gridPos);
                byte[] raw = ItemRecords.Write(item, logical);
                if (!ItemRecords.Matches(item, ItemRecords.Parse(raw), logical, out string diff))
                {
                    return Fail(player, $"Set switch cancelled: {item.m_shared.m_name} could not be stored safely ({diff})");
                }
                outPackage.Records.Add(raw);
            }
            // dormant records of the outgoing set stay with it, untouched
            outPackage.Records.AddRange(fromPackage.Records);

            SetPackage targetRemainder = new SetPackage { SetIndex = target };
            List<Placement> toPlace = Plan(player, targetPackage, targetRemainder);
            int intoGrid = toPlace.Count(p => !p.IsSlot);

            GearholdState.Journal journal = new GearholdState.Journal
            {
                From = from,
                To = target,
                OutFingerprint = GearholdState.Fingerprint(player, outgoing),
                InFingerprint = GearholdState.Fingerprint(toPlace.Where(p => p.IsSlot).Select(p => p.Record)),
                OutPackage = outPackage.Encode(),
                InPackage = targetPackage.IsEmpty ? "" : player.m_customData[GearholdState.SetKey(target)],
            };
            GearholdState.WriteJournal(player, journal);

            // ---- 2. mutate
            List<ItemDrop.ItemData> placed = new List<ItemDrop.ItemData>();
            List<ItemDrop.ItemData> removed = new List<ItemDrop.ItemData>();
            try
            {
                foreach (ItemDrop.ItemData item in outgoing)
                {
                    if (item.m_equipped)
                    {
                        player.UnequipItem(item, false);
                    }
                }
                foreach (ItemDrop.ItemData item in outgoing)
                {
                    if (inventory.RemoveItem(item))
                    {
                        removed.Add(item);
                    }
                }
                foreach (Placement placement in toPlace)
                {
                    Vector2i pos = placement.Cell;
                    ItemDrop.ItemData item = ItemRecords.Instantiate(placement.Record);
                    if (item == null)
                    {
                        throw new InvalidOperationException("record could not be rebuilt");
                    }
                    if (inventory.GetItemAt(pos.x, pos.y) != null)
                    {
                        throw new InvalidOperationException("target cell occupied");
                    }
                    if (!Access.AddItemAt(inventory, item, item.m_stack, pos.x, pos.y))
                    {
                        throw new InvalidOperationException("could not place " + item.m_shared.m_name);
                    }
                    placed.Add(inventory.GetItemAt(pos.x, pos.y));
                }

                GearholdState.WriteSet(player, outPackage);
                GearholdState.WriteSet(player, targetRemainder);
                state.Active = target;
                GearholdState.WriteState(player, state);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Set switch failed, rolling back: " + ex);
                Rollback(player, inventory, placed, removed, journal, from, target);
                return Fail(player, "Set switch failed – your gear was restored");
            }

            // ---- 3. re-equip, like Player.EquipInventoryItems does after a load
            foreach (ItemDrop.ItemData item in placed)
            {
                if (item != null && item.m_equipped)
                {
                    item.m_equipped = false;
                    player.EquipItem(item, false);
                }
            }

            // ---- 4. verify and commit
            bool sharedIntact = shared.Select((item, i) => inventory.ContainsItem(item) && item.m_stack == sharedStacks[i]).All(ok => ok);
            if (!sharedIntact)
            {
                Plugin.Log.LogError("Shared inventory changed during a set switch – keeping journal for inspection.");
                GearholdState.Quarantine(player, GearholdState.JournalKeyName, "inventory grid changed during switch " + (from + 1) + "->" + (target + 1));
            }
            else
            {
                GearholdState.ClearJournal(player);
            }
            Access.Changed(inventory);
            player.Message(MessageHud.MessageType.TopLeft, $"Set {target + 1} active");
            Transmog.Refresh(player); // looks are chosen per set
            if (intoGrid > 0)
            {
                // sets from 0.1.0/0.2.0 also stored inventory rows; those items now simply join the inventory
                player.Message(MessageHud.MessageType.Center, $"{intoGrid} items stored with set {target + 1} by an older version were put into your inventory");
            }
            if (targetRemainder.Records.Count > 0)
            {
                Plugin.Log.LogWarning($"Set {target + 1}: {targetRemainder.Records.Count} items stay stored with the set (missing prefab or no free cell).");
            }
            Plugin.Log.LogInfo($"Switched set {from + 1} -> {target + 1}: stored {outgoing.Count}, restored {placed.Count}, dormant {targetRemainder.Records.Count}.");
            return true;
        }

        private sealed class Placement
        {
            public ItemRecord Record;
            public Vector2i Cell;
            public bool IsSlot;
        }

        /// <summary>
        /// Decides where each stored record goes. Slot records return to their Gearhold slot. Records of grid
        /// cells (written by 0.1.0/0.2.0, which still stored inventory rows with a set) go to their old cell
        /// if it is free, else to any free grid cell. Everything that cannot be placed stays in the package.
        /// </summary>
        private static List<Placement> Plan(Player player, SetPackage package, SetPackage remainder)
        {
            Inventory inventory = player.GetInventory();
            int grid = Topology.GridRows(player);
            HashSet<int> claimed = new HashSet<int>();
            List<Placement> result = new List<Placement>();
            List<ItemRecord> gridRecords = new List<ItemRecord>();
            foreach (ItemRecord record in package.Records.Select(ItemRecords.Parse))
            {
                if (record.DormantReason() != null)
                {
                    remainder.Records.Add(record.Raw);
                }
                else if (Topology.IsSlotRecord(record.Pos))
                {
                    Vector2i cell = Topology.FromLogical(player, record.Pos);
                    if (claimed.Add(cell.y * Topology.Width + cell.x))
                        result.Add(new Placement { Record = record, Cell = cell, IsSlot = true });
                    else
                        remainder.Records.Add(record.Raw);
                }
                else
                {
                    gridRecords.Add(record);
                }
            }
            foreach (ItemRecord record in gridRecords)
            {
                Vector2i cell = record.Pos;
                bool own = cell.x >= 0 && cell.x < Topology.Width && cell.y >= 0 && cell.y < grid
                    && inventory.GetItemAt(cell.x, cell.y) == null && !claimed.Contains(cell.y * Topology.Width + cell.x);
                if (!own)
                {
                    cell = new Vector2i(-1, -1);
                    for (int y = 0; y < grid && cell.x < 0; y++)
                    {
                        for (int x = 0; x < Topology.Width; x++)
                        {
                            if (inventory.GetItemAt(x, y) == null && !claimed.Contains(y * Topology.Width + x))
                            {
                                cell = new Vector2i(x, y);
                                break;
                            }
                        }
                    }
                }
                if (cell.x >= 0)
                {
                    claimed.Add(cell.y * Topology.Width + cell.x);
                    result.Add(new Placement { Record = record, Cell = cell, IsSlot = false });
                }
                else
                {
                    remainder.Records.Add(record.Raw);
                }
            }
            return result;
        }

        private static void Rollback(Player player, Inventory inventory, List<ItemDrop.ItemData> placed, List<ItemDrop.ItemData> removed,
            GearholdState.Journal journal, int from, int target)
        {
            try
            {
                foreach (ItemDrop.ItemData item in placed)
                {
                    if (item != null)
                    {
                        if (item.m_equipped)
                        {
                            player.UnequipItem(item, false);
                        }
                        inventory.RemoveItem(item);
                    }
                }
                List<ItemDrop.ItemData> list = Access.InventoryItems(inventory);
                foreach (ItemDrop.ItemData item in removed)
                {
                    // put the very same objects back; their cells were freed above
                    if (inventory.GetItemAt(item.m_gridPos.x, item.m_gridPos.y) != null)
                    {
                        throw new InvalidOperationException("cell of " + item.m_shared.m_name + " is occupied");
                    }
                    item.m_equipped = false;
                    list.Add(item);
                }
                if (!string.IsNullOrEmpty(journal.InPackage))
                {
                    player.m_customData[GearholdState.SetKey(target)] = journal.InPackage;
                }
                GearholdState.State state = GearholdState.ReadState(player);
                state.Active = from;
                GearholdState.WriteState(player, state);
                if (GearholdState.TryReadSet(player, from, out SetPackage fromPackage, out _))
                {
                    // the active set's package may only hold dormant records: drop the outgoing copies again
                    SetPackage dormantOnly = new SetPackage { SetIndex = from };
                    dormantOnly.Records.AddRange(fromPackage.Records.Where(r => ItemRecords.Parse(r).DormantReason() != null));
                    if (fromPackage.Records.Count != dormantOnly.Records.Count && journal.OutFingerprint == GearholdState.Fingerprint(player, removed))
                    {
                        GearholdState.WriteSet(player, dormantOnly);
                    }
                }
                Access.Changed(inventory);
                GearholdState.ClearJournal(player);
            }
            catch (Exception ex)
            {
                // Leave the journal in place: it holds full copies of both sets and is checked on the next load.
                Plugin.Log.LogError("Rollback incomplete, journal kept for recovery: " + ex);
            }
        }

        private static bool Fail(Player player, string message)
        {
            Plugin.Log.LogWarning(message);
            player.Message(MessageHud.MessageType.Center, message);
            return false;
        }

        // ------------------------------------------------------------------ spawn-time checks

        /// <summary>Runs after Player.Load + layout: resolves leftover journals and wakes dormant records.</summary>
        internal static void OnSpawned(Player player)
        {
            ResolveJournal(player);
            for (int index = 0; index < Topology.SetCount; index++)
            {
                if (player.m_customData.ContainsKey(GearholdState.SetKey(index))
                    && !GearholdState.TryReadSet(player, index, out _, out string error))
                {
                    GearholdState.Quarantine(player, GearholdState.SetKey(index), "unreadable set " + (index + 1) + ": " + error);
                    player.Message(MessageHud.MessageType.Center, $"Gearhold: set {index + 1} is damaged and was put aside (quarantine).");
                }
            }
            MoveOrphansToGrid(player);
            RestoreDormant(player);
            GearholdState.SetReady(player, true);
            Transmog.Refresh(player); // vanilla drew the gear before Gearhold was ready
            int quarantined = GearholdState.QuarantineCount(player);
            if (quarantined > 0)
            {
                Plugin.Log.LogWarning($"{quarantined} Gearhold quarantine entries are kept on this character.");
            }
        }

        private static void ResolveJournal(Player player)
        {
            if (!GearholdState.TryReadJournal(player, out GearholdState.Journal journal, out bool present))
            {
                if (present)
                {
                    GearholdState.Quarantine(player, GearholdState.JournalKeyName, "unreadable journal");
                }
                return;
            }
            string live = GearholdState.Fingerprint(player, player.GetInventory().GetAllItems().Where(i => Topology.IsSetOwned(player, i.m_gridPos)));
            int active = GearholdState.ActiveSet(player);
            bool finished = active == journal.To && live == journal.InFingerprint;
            bool untouched = active == journal.From && live == journal.OutFingerprint;
            if (finished || untouched)
            {
                GearholdState.ClearJournal(player);
                return;
            }
            GearholdState.Quarantine(player, GearholdState.JournalKeyName, "interrupted switch " + (journal.From + 1) + "->" + (journal.To + 1));
            player.Message(MessageHud.MessageType.Center, "Gearhold: an interrupted set switch was found – data was saved aside, please check your inventory.");
        }

        /// <summary>
        /// Items in a hidden cell that is no Gearhold slot (old weapon / ranged slot) go to a free grid
        /// cell. Without a free cell they simply stay where they are: still in the inventory, nothing is lost.
        /// </summary>
        private static void MoveOrphansToGrid(Player player)
        {
            Inventory inventory = player.GetInventory();
            int grid = Topology.GridRows(player);
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
                if (item.m_gridPos.y < grid || Topology.SlotAt(player, item.m_gridPos) != null)
                {
                    continue;
                }
                Vector2i free = Topology.FindFreeGridCell(player);
                if (free.x < 0)
                {
                    player.Message(MessageHud.MessageType.Center, $"Gearhold: no room to move {item.m_shared.m_name} out of a removed slot");
                    return;
                }
                item.m_gridPos = free;
                Plugin.Log.LogInfo($"Moved {item.m_shared.m_name} from a retired Gearhold slot into the inventory.");
            }
            Access.Changed(inventory);
        }

        /// <summary>Dormant records of the ACTIVE set become live again once their prefab is back (e.g. mod reinstalled).</summary>
        private static void RestoreDormant(Player player)
        {
            int active = GearholdState.ActiveSet(player);
            if (!GearholdState.TryReadSet(player, active, out SetPackage package, out _) || package.IsEmpty)
            {
                return;
            }
            Inventory inventory = player.GetInventory();
            SetPackage remainder = new SetPackage { SetIndex = active };
            int restored = 0;
            foreach (Placement placement in Plan(player, package, remainder))
            {
                Vector2i pos = placement.Cell;
                ItemDrop.ItemData item = inventory.GetItemAt(pos.x, pos.y) == null ? ItemRecords.Instantiate(placement.Record) : null;
                if (item != null && Access.AddItemAt(inventory, item, item.m_stack, pos.x, pos.y))
                {
                    ItemDrop.ItemData live = inventory.GetItemAt(pos.x, pos.y);
                    if (live != null)
                    {
                        live.m_equipped = false;
                    }
                    restored++;
                }
                else
                {
                    remainder.Records.Add(placement.Record.Raw);
                }
            }
            if (restored > 0)
            {
                GearholdState.WriteSet(player, remainder);
                Plugin.Log.LogInfo($"Restored {restored} dormant items into set {active + 1}.");
            }
        }
    }
}
