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
    // Loaded first when present: its own equipment rows sit where Gearhold keeps its slots, so Gearhold turns that
    // system off through Path of Valheiman's API before a character loads (Patches/CompatPatches.cs).
    [BepInDependency(Patches.PathOfValheiman.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.morda.gearhold";
        public const string ModName = "Morda's Gearhold";
        public const string ModVersion = "1.4.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        internal static ConfigEntry<int> ExtraRows;
        internal static ConfigEntry<int> TrinketSlots;
        internal static ConfigEntry<int> UtilitySlots;
        internal static readonly ConfigEntry<string>[] CustomSlotPrefabs = new ConfigEntry<string>[Topology.CustomSlotCount];
        internal static ConfigEntry<KeyboardShortcut> KeySet1;
        internal static ConfigEntry<KeyboardShortcut> Weapon2SwapKey;
        internal static ConfigEntry<KeyboardShortcut> KeySet2;
        internal static ConfigEntry<KeyboardShortcut> KeySet3;
        internal static ConfigEntry<KeyboardShortcut> KeyNextSet;
        internal static readonly ConfigEntry<KeyboardShortcut>[] KeyQuickslots = new ConfigEntry<KeyboardShortcut>[Topology.QuickslotCount];
        internal static readonly ConfigEntry<KeyboardShortcut>[] KeyAmmo = new ConfigEntry<KeyboardShortcut>[Topology.AmmoSlotCount];
        internal static readonly ConfigEntry<KeyboardShortcut>[] KeyFood = new ConfigEntry<KeyboardShortcut>[Topology.FoodSlotCount];
        internal static readonly ConfigEntry<KeyboardShortcut>[] KeyMead = new ConfigEntry<KeyboardShortcut>[Topology.MeadSlotCount];
        internal static readonly ConfigEntry<KeyboardShortcut>[] KeyCustom = new ConfigEntry<KeyboardShortcut>[Topology.CustomSlotCount];
        internal static ConfigEntry<float> FoodBarX;
        internal static ConfigEntry<float> FoodBarY;
        internal static ConfigEntry<float> MeadBarX;
        internal static ConfigEntry<float> MeadBarY;
        internal static ConfigEntry<bool> QuickslotBarVertical;
        internal static ConfigEntry<bool> FoodBarVertical;
        internal static ConfigEntry<bool> MeadBarVertical;
        internal static ConfigEntry<float> FoodBarScale;
        internal static ConfigEntry<float> MeadBarScale;
        internal static ConfigEntry<bool> AutoEquip;
        internal static ConfigEntry<bool> AutoShield;
        internal static ConfigEntry<bool> EnableWeapon2;
        internal static ConfigEntry<bool> ShowWeapon2OnBody;
        internal static ConfigEntry<bool> AutoShieldWithAxes;
        internal static ConfigEntry<bool> SharedFirstForCrafting;
        internal static ConfigEntry<bool> QuickslotsUseOnTarget;
        internal static ConfigEntry<bool> AutoEat;
        internal static ConfigEntry<bool> AutoEatFoodSlots;
        internal static ConfigEntry<bool> AutoEatEarly;
        internal static ConfigEntry<bool> AutoStoreCoins;
        internal static ConfigEntry<bool> ShowPanel;
        internal static ConfigEntry<bool> ShowQuickslotBar;
        internal static ConfigEntry<bool> ShowBowAmmo;
        internal static ConfigEntry<bool> ShowOthersColors;
        internal static ConfigEntry<float> QuickslotBarScale;
        internal static ConfigEntry<float> QuickslotBarX;
        internal static ConfigEntry<float> QuickslotBarY;

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

            for (int i = 0; i < CustomSlotPrefabs.Length; i++)
            {
                CustomSlotPrefabs[i] = Config.Bind("1 - Inventory", "CustomSlot" + (i + 1), "",
                    "Prefab names of the items custom slot " + (i + 1) + " takes, comma separated (e.g. Wishbone,Demister). " +
                    "Works for items of other mods too. Empty = the slot is not shown. " +
                    "Names are the prefab names, not the display names (a mod's page or the item's spawn command shows them).");
            }
            KeySet1 = Config.Bind("2 - Hotkeys", "Set1", KeyboardShortcut.Empty, "Switch to equipment set 1.");
            Weapon2SwapKey = Config.Bind("2 - Hotkeys", "Weapon2Swap", KeyboardShortcut.Empty,
                "Swaps the weapon in hand with the Weapon 2 slot. Empty: the vanilla hide key (R) swaps. Set: only this key swaps and R is vanilla again.");
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
            for (int i = 0; i < KeyFood.Length; i++)
            {
                KeyFood[i] = Config.Bind("2 - Hotkeys", "Food" + (i + 1), KeyboardShortcut.Empty,
                    "Eat the food in Gearhold food slot " + (i + 1) + " of the active set. Also editable in the panel.");
            }
            for (int i = 0; i < KeyMead.Length; i++)
            {
                KeyMead[i] = Config.Bind("2 - Hotkeys", "Mead" + (i + 1), KeyboardShortcut.Empty,
                    "Drink the mead in Gearhold mead slot " + (i + 1) + " of the active set. Also editable in the panel.");
            }
            for (int i = 0; i < KeyCustom.Length; i++)
            {
                KeyCustom[i] = Config.Bind("2 - Hotkeys", "Custom" + (i + 1), KeyboardShortcut.Empty,
                    "Use the item in custom slot " + (i + 1) + " of the active set (eat, drink, equip). Also editable in the panel.");
            }

            AutoEquip = Config.Bind("3 - Behaviour", "AutoEquip", true,
                "Equip newly picked-up or crafted gear when the matching slot of the active set is empty. Never replaces equipped gear.");
            AutoShield = Config.Bind("3 - Behaviour", "AutoShield", true,
                "Equip the active set's shield (shield cell) together with a one-handed weapon.");
            AutoShieldWithAxes = Config.Bind("3 - Behaviour", "AutoShieldWithAxes", true,
                "AutoShield also takes the shield for one-handed axes. Off: an axe stays alone in hand (other one-handers still get the shield).");
            EnableWeapon2 = Config.Bind("3 - Behaviour", "EnableWeapon2", true,
                "Weapon 2 slot between food and mead; the hide key (R) swaps it with the weapon in hand. Off: no slot, R is vanilla again (a weapon still lying in the slot goes back into the inventory).");
            ShowWeapon2OnBody = Config.Bind("3 - Behaviour", "ShowWeapon2OnBody", true,
                "Wear the weapon of the Weapon 2 slot on the body (hip or back, the mounts vanilla uses for a weapon put away with R). Other players see it too.");
            SharedFirstForCrafting = Config.Bind("3 - Behaviour", "SharedFirstForCrafting", true,
                "Crafting and building take resources from the inventory grid before touching the Gearhold slots (e.g. quickslot food).");
            QuickslotsUseOnTarget = Config.Bind("3 - Behaviour", "QuickslotsUseOnTarget", false,
                "Like the vanilla hotbar: a quickslot key used while looking at an item stand, mannequin, fire, pet, ... hands the item to it. " +
                "Off (default): quickslot keys only eat, drink or equip and never give an item away.");
            AutoEat = Config.Bind("3 - Behaviour", "AutoEat", false,
                "Eat food from the active set's quickslots automatically when one of your three food slots is empty.");
            AutoEatFoodSlots = Config.Bind("3 - Behaviour", "AutoEatFoodSlots", true,
                "Eat from the active set's food slots automatically when one of your three food slots is empty (food slots first, then quickslots with AutoEat).");
            AutoEatEarly = Config.Bind("3 - Behaviour", "AutoEatEarly", false,
                "With AutoEat: also eat as soon as the game lets you eat a food again (at half its time), so no food slot runs empty.");
            AutoStoreCoins = Config.Bind("3 - Behaviour", "AutoStoreCoins", true,
                "Move the coins you carry into the coin slot (top right of the panel) on their own. Coins there weigh nothing.");

            ShowPanel = Config.Bind("4 - UI", "ShowPanel", true, "Show the Gearhold panel in the middle of the inventory screen.");
            ShowQuickslotBar = Config.Bind("4 - UI", "ShowQuickslotBar", true,
                "Show the occupied quickslots of the active set with their hotkeys at the bottom centre of the screen.");
            ShowBowAmmo = Config.Bind("4 - UI", "ShowBowAmmo", true,
                "Under a bow in the hotbar, show the fitting arrows of the Gearhold ammo slots with their amount; the ammo in use is highlighted.");
            ShowOthersColors = Config.Bind("4 - UI", "ShowOthersColors", true,
                "See the dyes and weapon glow colours other players with Gearhold chose for their gear. Your own colours are sent to them regardless.");
            QuickslotBarScale = Config.Bind("4 - UI", "QuickslotBarScale", 0.75f,
                new ConfigDescription("Size of the quickslot bar on the HUD (1 = size of the vanilla hotbar).", new AcceptableValueRange<float>(QuickslotBarEditor.MinScale, QuickslotBarEditor.MaxScale)));
            QuickslotBarX = Config.Bind("4 - UI", "QuickslotBarX", 0f,
                new ConfigDescription("Left/right position of the quickslot bar (0 = centre). Easiest set with the settings gear in the panel (bottom right).",
                    new AcceptableValueRange<float>(-QuickslotBarEditor.RangeX, QuickslotBarEditor.RangeX)));
            QuickslotBarY = Config.Bind("4 - UI", "QuickslotBarY", 14f,
                new ConfigDescription("Height of the quickslot bar above the bottom edge. Easiest set with the settings gear in the panel (bottom right).",
                    new AcceptableValueRange<float>(0f, QuickslotBarEditor.RangeY)));
            // food: next to the vanilla food boxes at the bottom left; meads: right of the quickslots
            FoodBarX = Config.Bind("4 - UI", "FoodBarX", -700f, new ConfigDescription("Left/right position of the food bar on the HUD (0 = centre).",
                new AcceptableValueRange<float>(-QuickslotBarEditor.RangeX, QuickslotBarEditor.RangeX)));
            FoodBarY = Config.Bind("4 - UI", "FoodBarY", 40f, new ConfigDescription("Height of the food bar above the bottom edge.",
                new AcceptableValueRange<float>(0f, QuickslotBarEditor.RangeY)));
            MeadBarX = Config.Bind("4 - UI", "MeadBarX", 380f, new ConfigDescription("Left/right position of the mead bar on the HUD (0 = centre).",
                new AcceptableValueRange<float>(-QuickslotBarEditor.RangeX, QuickslotBarEditor.RangeX)));
            MeadBarY = Config.Bind("4 - UI", "MeadBarY", 14f, new ConfigDescription("Height of the mead bar above the bottom edge.",
                new AcceptableValueRange<float>(0f, QuickslotBarEditor.RangeY)));
            QuickslotBarVertical = Config.Bind("4 - UI", "QuickslotBarVertical", false, "Show the quickslot bar as a column instead of a row.");
            FoodBarVertical = Config.Bind("4 - UI", "FoodBarVertical", false, "Show the food bar as a column instead of a row.");
            MeadBarVertical = Config.Bind("4 - UI", "MeadBarVertical", false, "Show the mead bar as a column instead of a row.");
            FoodBarScale = Config.Bind("4 - UI", "FoodBarScale", 0.75f, new ConfigDescription("Size of the food bar on the HUD (1 = size of the vanilla hotbar).",
                new AcceptableValueRange<float>(QuickslotBarEditor.MinScale, QuickslotBarEditor.MaxScale)));
            MeadBarScale = Config.Bind("4 - UI", "MeadBarScale", 0.75f, new ConfigDescription("Size of the mead bar on the HUD (1 = size of the vanilla hotbar).",
                new AcceptableValueRange<float>(QuickslotBarEditor.MinScale, QuickslotBarEditor.MaxScale)));

            ExtraRows.SettingChanged += (_, __) => Topology.Apply(Player.m_localPlayer);

            Patches.PathOfValheiman.TakeOverEquipment();

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
            Topology.KeepHeight(player);
            Patches.AutoEat.Tick(player);
            Patches.Holster.Tick(player);
            Patches.HolsterVisual.Tick(player);
            Transmog.Tick(player);
            ColorSync.Tick(player);
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
            if (Patches.Holster.HasOwnKey && Pressed(Weapon2SwapKey.Value))
            {
                Patches.Holster.SwapByKey(player);
                return;
            }
            if (Pressed(KeySet1.Value)) SetSwitcher.TrySwitch(player, 0);
            else if (Pressed(KeySet2.Value)) SetSwitcher.TrySwitch(player, 1);
            else if (Pressed(KeySet3.Value)) SetSwitcher.TrySwitch(player, 2);
            else if (Pressed(KeyNextSet.Value)) SetSwitcher.TrySwitch(player, (GearholdState.ActiveSet(player) + 1) % Topology.SetCount);
            else
            {
                foreach (GearSlot slot in HotkeySlots)
                {
                    ConfigEntry<KeyboardShortcut> key = KeyFor(slot);
                    if (key != null && Pressed(key.Value))
                    {
                        UseSlot(player, slot);
                        break;
                    }
                }
            }
        }

        /// <summary>Every slot with its own use key: the 8 quickslots, the 3 food and the 3 mead slots.</summary>
        internal static readonly GearSlot[] HotkeySlots =
        {
            GearSlot.Quick1, GearSlot.Quick2, GearSlot.Quick3, GearSlot.Quick4, GearSlot.Quick5, GearSlot.Quick6, GearSlot.Quick7, GearSlot.Quick8,
            GearSlot.Food1, GearSlot.Food2, GearSlot.Food3, GearSlot.Mead1, GearSlot.Mead2, GearSlot.Mead3,
            GearSlot.Custom1, GearSlot.Custom2, GearSlot.Custom3,
        };

        /// <summary>The use key of a quickslot, food or mead slot; null for other slots.</summary>
        internal static ConfigEntry<KeyboardShortcut> KeyFor(GearSlot slot)
        {
            if (slot >= GearSlot.Quick1 && slot <= GearSlot.Quick8) return KeyQuickslots[slot - GearSlot.Quick1];
            if (Topology.IsFoodSlot(slot)) return KeyFood[slot - GearSlot.Food1];
            if (Topology.IsMeadSlot(slot)) return KeyMead[slot - GearSlot.Mead1];
            if (Topology.IsCustomSlot(slot)) return KeyCustom[slot - GearSlot.Custom1];
            return null;
        }

        internal static string KeyLabel(GearSlot slot)
        {
            ConfigEntry<KeyboardShortcut> key = KeyFor(slot);
            return key != null ? FormatKey(key.Value) : "";
        }

        internal static void UseQuickslot(Player player, int index) => UseSlot(player, Topology.QuickSlot(index));

        /// <summary>
        /// The vanilla hotbar call (Player.UseHotbarItem -> UseItem). With fromInventoryGui = false vanilla first
        /// offers the item to whatever the player looks at (item stand, mannequin, ...), which takes it away;
        /// by default quickslots use fromInventoryGui = true so they only eat, drink or equip.
        /// </summary>
        internal static void UseSlot(Player player, GearSlot slot)
        {
            ItemDrop.ItemData item = Patches.Equipment.ItemIn(player, slot);
            if (item != null && !player.IsDead() && !player.InCutscene() && !player.IsTeleporting())
            {
                player.UseItem(null, item, !QuickslotsUseOnTarget.Value);
            }
        }


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
                // short symbols so the key fits on the small key buttons
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.BackQuote: return "`";
                case KeyCode.KeypadPlus: return "Num+";
                case KeyCode.KeypadMinus: return "Num-";
                case KeyCode.KeypadMultiply: return "Num*";
                case KeyCode.KeypadDivide: return "Num/";
                case KeyCode.Mouse3: return "M4";
                case KeyCode.Mouse4: return "M5";
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
            foreach (ConfigEntry<KeyboardShortcut> entry in KeyFood) yield return entry;
            foreach (ConfigEntry<KeyboardShortcut> entry in KeyMead) yield return entry;
            foreach (ConfigEntry<KeyboardShortcut> entry in KeyCustom) yield return entry;
            yield return KeySet1;
            yield return KeySet2;
            yield return KeySet3;
            yield return KeyNextSet;
            yield return Weapon2SwapKey;
        }

        private void LateUpdate()
        {
            GearholdPanel.Tick();
            GearholdHud.Tick();
        }

        // protected bool Player.TakeInput(): false in every menu (inventory, chat, console, map, trader, text input,
        // the build menu's search field ...), the vanilla rule for "the player may act now"
        private static readonly System.Func<Player, bool> s_takeInput =
            AccessTools.MethodDelegate<System.Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));

        /// <summary>
        /// Gearhold hotkeys only work while you actually play in the world: not in any menu, not while the build
        /// piece selection is open, and never while a text field has the keyboard (search boxes of the game or of
        /// other mods), so typing "U" in a search never uses quickslot U.
        /// </summary>
        private static bool InputBlocked()
        {
            Player player = Player.m_localPlayer;
            if (player == null || !s_takeInput(player))
            {
                return true;
            }
            if ((Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible()
                || Menu.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible() || InventoryGui.IsVisible()
                || Hud.IsPieceSelectionVisible())
            {
                return true;
            }
            return TextFieldFocused();
        }

        /// <summary>True while any input field (Unity or TextMeshPro, any mod) is being typed into.</summary>
        internal static bool TextFieldFocused()
        {
            GameObject selected = UnityEngine.EventSystems.EventSystem.current != null
                ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            if (selected == null)
            {
                return false;
            }
            TMPro.TMP_InputField tmp = selected.GetComponent<TMPro.TMP_InputField>();
            if (tmp != null && tmp.isFocused)
            {
                return true;
            }
            UnityEngine.UI.InputField legacy = selected.GetComponent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
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
