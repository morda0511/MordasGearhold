using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MordasGearhold.Core;
using MordasGearhold.UI;
using UnityEngine;

namespace MordasGearhold
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    // Both mods take over the player inventory topology and the tombstone transfer (see Stage-0 report, section H).
    [BepInIncompatibility("shudnal.ExtraSlots")]
    [BepInIncompatibility("Azumatt.AzuExtendedPlayerInventory")]
    // Loaded first when present so its player-inventory sort can be made Gearhold-aware (Patches/CompatPatches.cs).
    [BepInDependency("com.morda.storeandcraft", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.morda.gearhold";
        public const string ModName = "Morda's Gearhold";
        public const string ModVersion = "1.0.1";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        internal static ConfigEntry<int> ExtraRows;
        internal static ConfigEntry<int> TrinketSlots;
        internal static ConfigEntry<int> UtilitySlots;
        internal static ConfigEntry<KeyboardShortcut> KeySet1;
        internal static ConfigEntry<KeyboardShortcut> KeySet2;
        internal static ConfigEntry<KeyboardShortcut> KeySet3;
        internal static ConfigEntry<KeyboardShortcut> KeyNextSet;
        internal static readonly ConfigEntry<KeyboardShortcut>[] KeyQuickslots = new ConfigEntry<KeyboardShortcut>[Topology.QuickslotCount];
        internal static readonly ConfigEntry<KeyboardShortcut>[] KeyAmmo = new ConfigEntry<KeyboardShortcut>[Topology.AmmoSlotCount];
        internal static ConfigEntry<bool> AutoEquip;
        internal static ConfigEntry<bool> AutoShield;
        internal static ConfigEntry<bool> SharedFirstForCrafting;
        internal static ConfigEntry<bool> QuickslotsUseOnTarget;
        internal static ConfigEntry<bool> AutoEat;
        internal static ConfigEntry<bool> AutoEatEarly;
        internal static ConfigEntry<bool> AutoStoreCoins;
        internal static ConfigEntry<bool> ShowPanel;
        internal static ConfigEntry<bool> ShowQuickslotBar;
        internal static ConfigEntry<float> QuickslotBarScale;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // New key in 0.3.0: the grid is no longer split into set and shared rows.
            ExtraRows = Config.Bind("1 - Inventory", "ExtraGridRows", Topology.DefaultExtraRows,
                new ConfigDescription("Rows added to the inventory grid on top of vanilla's 4 (and the rows bought from the trader). " +
                    "Reducing it is refused while the rows that would disappear still hold items.",
                    new AcceptableValueRange<int>(0, Topology.MaxExtraRows)));
            TrinketSlots = Config.Bind("1 - Inventory", "TrinketSlots", 1,
                new ConfigDescription("How many trinket slots the panel has (1 = only the vanilla slot). " +
                    "An item in a slot that is switched off goes back into the inventory grid.",
                    new AcceptableValueRange<int>(1, 3)));
            UtilitySlots = Config.Bind("1 - Inventory", "UtilitySlots", 1,
                new ConfigDescription("How many utility slots the panel has (1 = only the vanilla slot). " +
                    "An item in a slot that is switched off goes back into the inventory grid.",
                    new AcceptableValueRange<int>(1, 3)));

            KeySet1 = Config.Bind("2 - Hotkeys", "Set1", KeyboardShortcut.Empty, "Switch to equipment set 1.");
            KeySet2 = Config.Bind("2 - Hotkeys", "Set2", KeyboardShortcut.Empty, "Switch to equipment set 2.");
            KeySet3 = Config.Bind("2 - Hotkeys", "Set3", KeyboardShortcut.Empty, "Switch to equipment set 3.");
            KeyNextSet = Config.Bind("2 - Hotkeys", "NextSet", KeyboardShortcut.Empty, "Cycle to the next equipment set.");
            // X is vanilla "Sit", so the defaults use keys vanilla leaves free.
            KeyCode[] quickDefaults = { KeyCode.Z, KeyCode.B, KeyCode.N, KeyCode.H };
            for (int i = 0; i < KeyQuickslots.Length; i++)
            {
                KeyboardShortcut shortcut = i < quickDefaults.Length ? new KeyboardShortcut(quickDefaults[i]) : KeyboardShortcut.Empty;
                KeyQuickslots[i] = Config.Bind("2 - Hotkeys", "Quickslot" + (i + 1), shortcut,
                    "Use the item in Gearhold quickslot " + (i + 1) + " of the active set (eat, drink, equip).");
            }
            // Alt+1..3 by default. While Alt is held, the vanilla hotbar ignores 1..3 (Patches/HotbarPatches.cs).
            for (int i = 0; i < KeyAmmo.Length; i++)
            {
                KeyAmmo[i] = Config.Bind("2 - Hotkeys", "Ammo" + (i + 1), new KeyboardShortcut(KeyCode.Alpha1 + i, KeyCode.LeftAlt),
                    "Select the ammo in Gearhold ammo slot " + (i + 1) + " of the active set.");
            }

            AutoEquip = Config.Bind("3 - Behaviour", "AutoEquip", true,
                "Equip newly picked-up or crafted gear when the matching slot of the active set is empty. Never replaces equipped gear.");
            AutoShield = Config.Bind("3 - Behaviour", "AutoShield", true,
                "Equip the active set's shield (shield cell) together with a one-handed weapon.");
            SharedFirstForCrafting = Config.Bind("3 - Behaviour", "SharedFirstForCrafting", true,
                "Crafting and building take resources from the inventory grid before touching the Gearhold slots (e.g. quickslot food).");
            QuickslotsUseOnTarget = Config.Bind("3 - Behaviour", "QuickslotsUseOnTarget", false,
                "Like the vanilla hotbar: a quickslot key used while looking at an item stand, mannequin, fire, pet, ... hands the item to it. " +
                "Off (default): quickslot keys only eat, drink or equip and never give an item away.");
            AutoEat = Config.Bind("3 - Behaviour", "AutoEat", false,
                "Eat food from the active set's quickslots automatically when one of your three food slots is empty.");
            AutoEatEarly = Config.Bind("3 - Behaviour", "AutoEatEarly", false,
                "With AutoEat: also eat as soon as the game lets you eat a food again (at half its time), so no food slot runs empty.");
            AutoStoreCoins = Config.Bind("3 - Behaviour", "AutoStoreCoins", true,
                "Move the coins you carry into the coin slot (top right of the panel) on their own. Coins there weigh nothing.");

            ShowPanel = Config.Bind("4 - UI", "ShowPanel", true, "Show the Gearhold panel in the middle of the inventory screen.");
            ShowQuickslotBar = Config.Bind("4 - UI", "ShowQuickslotBar", true,
                "Show the occupied quickslots of the active set with their hotkeys at the bottom centre of the screen.");
            QuickslotBarScale = Config.Bind("4 - UI", "QuickslotBarScale", 0.75f,
                new ConfigDescription("Size of the quickslot bar on the HUD (1 = size of the vanilla hotbar).", new AcceptableValueRange<float>(0.4f, 1.2f)));

            ExtraRows.SettingChanged += (_, __) => Topology.Apply(Player.m_localPlayer);

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo(ModName + " v" + ModVersion + " loaded.");
        }

        private void Update()
        {
            if (KeyRebinder.Tick())
            {
                return; // capturing a new quickslot key: no hotkey may fire with that key press
            }
            Player player = Player.m_localPlayer;
            if (player == null || !GearholdState.IsReady(player))
            {
                return;
            }
            Patches.Equipment.LateShieldCheck(player);
            Patches.Trinkets.Tick(player);
            Patches.Coins.Tick(player);
            Patches.AutoEat.Tick(player);
            Transmog.Tick(player);
            if (InputBlocked())
            {
                return;
            }
            for (int i = 0; i < KeyAmmo.Length; i++)
            {
                if (Pressed(KeyAmmo[i].Value))
                {
                    Patches.Equipment.SelectAmmo(player, i);
                    return;
                }
            }
            if (Pressed(KeySet1.Value)) SetSwitcher.TrySwitch(player, 0);
            else if (Pressed(KeySet2.Value)) SetSwitcher.TrySwitch(player, 1);
            else if (Pressed(KeySet3.Value)) SetSwitcher.TrySwitch(player, 2);
            else if (Pressed(KeyNextSet.Value)) SetSwitcher.TrySwitch(player, (GearholdState.ActiveSet(player) + 1) % Topology.SetCount);
            else
            {
                for (int i = 0; i < KeyQuickslots.Length; i++)
                {
                    if (Pressed(KeyQuickslots[i].Value))
                    {
                        UseQuickslot(player, i);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The vanilla hotbar call (Player.UseHotbarItem -> UseItem). With fromInventoryGui = false vanilla first
        /// offers the item to whatever the player looks at (item stand, mannequin, ...), which takes it away;
        /// by default quickslots use fromInventoryGui = true so they only eat, drink or equip.
        /// </summary>
        internal static void UseQuickslot(Player player, int index)
        {
            ItemDrop.ItemData item = Patches.Equipment.ItemIn(player, Topology.QuickSlot(index));
            if (item != null && !player.IsDead() && !player.InCutscene() && !player.IsTeleporting())
            {
                player.UseItem(null, item, !QuickslotsUseOnTarget.Value);
            }
        }

        internal static string KeyLabel(int quickslot) => FormatKey(KeyQuickslots[quickslot].Value);

        internal static string AmmoKeyLabel(int ammoSlot) => FormatKey(KeyAmmo[ammoSlot].Value);

        /// <summary>"Alt+1" instead of BepInEx's "Alpha1 + LeftAlt"; empty when unbound.</summary>
        internal static string FormatKey(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None)
            {
                return "";
            }
            string text = "";
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                text += KeyName(modifier) + "+";
            }
            return text + KeyName(shortcut.MainKey);
        }

        private static string KeyName(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num" + (int)(key - KeyCode.Keypad0);
            switch (key)
            {
                case KeyCode.LeftAlt: case KeyCode.RightAlt: return "Alt";
                case KeyCode.LeftShift: case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftControl: case KeyCode.RightControl: return "Ctrl";
                default: return key.ToString();
            }
        }

        /// <summary>True while a Gearhold hotkey WITH modifiers on this main key is held (e.g. Alt+1).</summary>
        internal static bool ModifiedHotkeyHeld(KeyCode mainKey)
        {
            foreach (ConfigEntry<KeyboardShortcut> entry in AllHotkeys())
            {
                KeyboardShortcut shortcut = entry.Value;
                if (shortcut.MainKey != mainKey)
                {
                    continue;
                }
                bool hasModifier = false, allHeld = true;
                foreach (KeyCode modifier in shortcut.Modifiers)
                {
                    hasModifier = true;
                    allHeld &= ZInput.GetKey(modifier, false);
                }
                if (hasModifier && allHeld)
                {
                    return true;
                }
            }
            return false;
        }

        internal static System.Collections.Generic.IEnumerable<ConfigEntry<KeyboardShortcut>> AllHotkeys()
        {
            foreach (ConfigEntry<KeyboardShortcut> entry in KeyQuickslots) yield return entry;
            foreach (ConfigEntry<KeyboardShortcut> entry in KeyAmmo) yield return entry;
            yield return KeySet1;
            yield return KeySet2;
            yield return KeySet3;
            yield return KeyNextSet;
        }

        private void LateUpdate()
        {
            GearholdPanel.Tick();
            GearholdHud.Tick();
        }

        private static bool InputBlocked()
        {
            return (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible()
                || Menu.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible();
        }

        private static bool Pressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !ZInput.GetKeyDown(shortcut.MainKey, false))
            {
                return false;
            }
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!ZInput.GetKey(modifier, false))
                {
                    return false;
                }
            }
            return true;
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
