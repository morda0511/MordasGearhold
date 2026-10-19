# Morda's Gearhold
*Inventory, Loadouts & Quickslots* – Valheim 1.0.20, BepInEx 5.4.23.x · version 0.15.1 (test build)

## What it does
- **Three independent equipment sets.** A set is exactly what lies in the Gearhold panel: armour, shield, three utility items, three trinkets, three ammo slots and 8 quickslots (food, potions, ...).
- **Weapons and bows stay in your hotbar.** The inventory grid, hotbar included, is shared by all sets. A set switch never touches it.
- **The Gearhold panel** sits in the middle of the inventory screen. Its slots are **real storage**: an item you put into *Head* or *Quickslot 3* leaves the inventory grid and lives in the panel.
- **Quickslots** with their own hotkeys (default `Z`, `B`, `N`, `H`), changeable right in the panel. Example: put food in, press the key, eat.
  - Unlike the vanilla hotbar, a quickslot key never hands its item to what you are looking at (item stand, mannequin, ...). Switch this with `QuickslotsUseOnTarget`.
- **Quickslot bar on the HUD:** at the bottom centre you see every occupied quickslot with its item and amount, and its hotkey on one line above it (also long keys like `Alt+1`). A changed key shows up immediately. The bar hides while the inventory is open; its size is set with `QuickslotBarScale`.
- **Three ammo slots:** `Alt+1`, `Alt+2`, `Alt+3` pick the ammo you shoot. Holding Alt keeps the vanilla hotbar from also using item 1–3.
- **Up to three trinkets and three utility items at once** (switch slots 2 and 3 on with `TrinketSlots` / `UtilitySlots`; by default only the vanilla slot is shown):
  - Slot 1 is the normal vanilla slot. Slots 2 and 3 are always active while something lies in them.
  - Right-click a trinket or utility item in the inventory: when slot 1 is taken, it goes into a free slot 2 or 3.
  - All three add their max adrenaline, and they wear down like the first one.
  - All three fire their full-adrenaline effect. Their while-worn effects apply too.
  - A broken trinket stops working but is never destroyed.
  - On death all three go into the grave like the rest of the set.
- **YOUR VIKING** shows:
  - your armour and weight,
  - the equipment modifiers (movement, stamina costs, heat),
  - **WEAPON** (only while a weapon is drawn): damage per type, stamina/eitr use, block armour, block force, parry bonus, knockback, backstab, durability,
  - **SHIELD** (only while a shield is worn): block armour, block force, parry bonus,
  - **TRINKETS** (only with active trinkets): total max adrenaline, and each effect with the game's own description,
    e.g. what happens at full adrenaline,
  - **RESISTANCES:** only the ones that actually differ from normal (e.g. Frost: Resistant, Spirit: Immune), including effects active right now.

  Health, stamina and eitr are left out because the vanilla HUD already shows them.
- **Auto-equip:** new armour, shields, trinkets and ammo you pick up or craft go into an **empty** slot only. Gear you already wear is never replaced. A new trinket goes into Trinket 2 or 3 when Trinket 1 is taken.
- **Auto-shield:**
  - Equip a one-handed weapon from the hotbar, and the set's shield comes along.
  - Put the weapon away, and the shield goes too. Swapping one weapon for another, or eating, keeps it.
  - With a bow or two-hander, the shield is put away but stays in the shield slot.

## Transmog (looks)
Change how your gear **looks** without changing the gear itself.

- **Unlocking:** every piece whose recipe you know is available right away, the same list the crafting menu uses. You do not need to own it. Pieces you crafted count as well.
- **Armour and shield** (Head, Chest, Legs, Cape, Shield): right-click the slot in the Gearhold panel.
  - **OWN LOOK** shows the real item.
  - The first cell, **Hide** (a box with a red slash), makes the piece invisible. To take gear off, left-click the slot.
  - Or click one of your unlocked looks of the same kind.
  - Looks are saved **per set**, so each set can have its own outfit.
- **Weapons and bows:** `Alt` + right-click the weapon in the inventory. Only looks of the same weapon kind are offered (axe as axe, sword as sword), so grip and animations always fit.
  - The look stays with that weapon, in every set.
  - Upgrading a weapon at a workbench makes a new item, so its look has to be picked again.
- **Marker:** a piece with a chosen look, colour or Hide shows a small gold sparkle (✦) in the top-left corner of its slot. This also works for weapons in the inventory.
- **Visibility:** other players see your looks too, because the game shares visible gear. A look only shows while real gear is worn in that slot.
- **Character selection:** the character on the selection screen already wears the looks and colours of its active set.

## Dyes (colours)
Recolour Head, Chest, Legs, Cape and Shield: right-click the slot. The menu shows only your character in **3D** on the left, so every choice is visible at once (drag to turn it, mouse wheel to zoom); below the looks you find **COLOR**.

- **Colour swatches** (Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink, Black, White, Grey): the piece's main colour is measured and shifted towards the swatch, so "Red" really ends up reddish.
- **Fine controls:** **Color** turns the colour around the colour wheel (±15° per click), **Intensity** makes it stronger or greyer, **Brightness** lighter or darker (±10 % per click).
- **RESET COLOR** goes back to the original colours.
- Skin under and around the armour keeps its colour: the painted armour is only recoloured where it covers the body.
- Colours are saved **per set** and work together with looks, so a borrowed look can be dyed as well.
- **How it works:** only your character's copy is recoloured, never the item itself or other players' copies.
  - Helmet, cape, shield and the chest/leg models: the game's own colour controls (`_Hue`, `_Saturation`, `_Value`).
  - Armour painted onto the body: a recoloured copy of its texture, made once per change.
- **Multiplayer:** for now only you see your colours. Showing them to other Gearhold players is planned.
- Weapons and trinkets use a shader without colour controls and cannot be dyed yet.

## Inventory layout
| Area | Belongs to |
|---|---|
| Inventory grid (vanilla 4 rows + `ExtraGridRows`, default 7; row 1 = hotbar with your weapons and bows) | shared by all sets |
| Gearhold panel slots (hidden rows of the vanilla inventory) | the active set |

- **To make an item part of a set, put it into a panel slot.**
- **Crafting and building** take from the grid first and from the Gearhold slots last.

**Upgrading from older test builds:**
- 0.1.0/0.2.0 also stored grid rows with a set. 0.3.0 had a weapon slot, and up to 0.6.0 there was a ranged slot.
- Those items are put back into free grid cells, on load for the active set and on the next switch for the others.
- Anything that does not fit stays safely stored until there is room.

## Panel controls
| Action | Result |
|---|---|
| Left-click an item in the inventory, then left-click a slot | The item moves into the slot. Armour is equipped; ammo is equipped if you have none in use. |
| Left-click a filled slot | Equipment goes back into the inventory grid. A quickslot item is picked up: click another quickslot to move or swap it, or an inventory cell to put it back. |
| Right-click a slot | Head, Chest, Legs, Cape, Shield: open the look menu. Other slots: use the item (eat, drink, use this ammo). |
| Key button under a quickslot | Click, then press the new key (Shift/Ctrl/Alt allowed). Esc cancels, Delete clears. |
| SET 1 / SET 2 / SET 3 tabs or hotkeys | Switch sets. |

Slots: Head, Chest, Legs, Cape, Utility 1–3 (left); Shield, Ammo 1–3, Trinket 1–3 (right); Quickslot 1–8. YOUR VIKING scrolls with the mouse wheel when it is longer than the panel.
Right-click on Trinket/Utility 2 or 3 makes it the main one (it swaps with slot 1).

**Key conflicts:**
- A key you assign is taken away from any other Gearhold hotkey that used it.
- A key that vanilla also uses (e.g. X = Sit, Q = Auto-run) shows a note.

**No set switch** while attacking, dodging or swimming, while armour is still being put on, or while you hold an item on the cursor.

## Configuration (`BepInEx/config/com.morda.gearhold.cfg`, or in game via BepInEx ConfigurationManager)
| Setting | Default | Meaning |
|---|---|---|
| `ExtraGridRows` | 3 (0–5) | rows added to the inventory grid on top of vanilla's 4 |
| `TrinketSlots` / `UtilitySlots` | 1 (1–3) | how many trinket and utility slots the panel shows; 1 = only the vanilla slot. An item in a slot you switch off goes back into the grid (or waits there, inactive, while the grid is full) |
| `Set1` / `Set2` / `Set3` / `NextSet` | none | set hotkeys |
| `Quickslot1` … `Quickslot8` | Z, B, N, H, –, –, –, – | quickslot hotkeys, also editable in the panel |
| `Ammo1` … `Ammo3` | Alt+1, Alt+2, Alt+3 | select the ammo of that ammo slot; click the label under an ammo slot to change it |
| `AutoEquip` / `AutoShield` | on | see above |
| `SharedFirstForCrafting` | on | crafting pays from the grid before the Gearhold slots |
| `QuickslotsUseOnTarget` | off | on = quickslot keys behave like the hotbar and give items to what you look at |
| `AutoEat` | off | eat food from the quickslots when one of your three food slots is empty |
| `AutoEatEarly` | off | with AutoEat: eat again as soon as the game allows it (half the food time) |
| `ShowPanel` | on | the Gearhold panel in the inventory screen |
| `ShowQuickslotBar` | on | occupied quickslots with their hotkeys at the bottom centre |
| `QuickslotBarScale` | 0.75 (0.4–1.2) | size of that bar; 1 = vanilla hotbar size |

- **Inventory rows bought from the trader** are added to the grid.
- **Lowering `ExtraGridRows`** is refused while a row that would disappear still holds items. Nothing is ever dropped.

## Death and tombstone
- **Into the grave** go the inventory grid and the active set's Gearhold slots, under the normal vanilla rules.
- **The other two sets** stay safely on the character.
- **The grave remembers its set.** If another set is active when you loot it, Gearhold switches back first. Items return to their old places, and gear in empty slots is worn again.
- **Whatever does not fit** stays in the grave, as in vanilla.

## Safety
- **Storage:** Inactive sets are stored in the character (`Player.m_customData`, keys `MordasGearhold.*`). Each item is its own vanilla item record, with a SHA-256 checksum per set.
- **Items of a missing mod** are never deleted. They stay as dormant records and come back when the mod is installed again.
- **A set switch** runs within one game frame: every record is checked first, with a journal and rollback.
- **Damaged data** is never overwritten. It is kept under `MordasGearhold.Quarantine.*`, and the log says why.

## Uninstalling – please read
- **The two inactive sets** stay stored in the save, but cannot be reached without the mod. They come back on reinstall.
- **Rows beyond the vanilla limit and the hidden slot rows** are dropped on the ground by vanilla at the next spawn.
- **Before uninstalling,** empty the panel slots of all three sets and the lower grid rows into a chest.

## Compatibility
- **Incompatible:** ExtraSlots (shudnal) and AzuExtendedPlayerInventory (Azumatt). Both take over the player inventory and the tombstone; Gearhold does not load next to them.
- **StoreAndCraft:** the bag "Sort" button only sorts the grid and never touches the Gearhold slots.
- **Valheim Mannequins:** works. Quickslot keys no longer dress a mannequin by accident.
- **Not tested yet:** EpicLoot, Backpacks, Morda-Graveyard.

## Build
```
dotnet build -c Release
```
Copies the DLL into the Thunderstore profile "Solo Gameplay" (`-p:Deploy=false` to only build). Translations via JSON files are planned.

## Test round (use a character copy!)
1. **Load the character.** A bow that 0.6.0 kept in the ranged slot is back in the grid. Nothing lies on the ground.
2. **Ammo:** put wood arrows into Ammo 1 and fire arrows into Ammo 2. `Alt+2` selects the fire arrows (message top left), and hotbar item 2 is *not* used. `Alt+1` switches back.
3. **Shield:**
   - Shield in Shield, sword in the hotbar. Draw the sword with its hotbar key: the shield comes along.
   - Press the hotbar key again (sword away): the shield goes away too.
   - Swap sword ↔ axe: the shield stays. Eat with the sword drawn: the shield stays.
4. **Quickslots:**
   - Food in Quickslot 1, press Z → you eat.
   - Look at a mannequin and press a quickslot key → nothing is handed over.
   - Rebind Quickslot 2 to `K` with the button below it. The HUD bar shows K.
5. **YOUR VIKING** with sword and shield drawn shows WEAPON and SHIELD values, and the list fits above the quickslots.
6. **Fill the slots of set 1, switch to set 2 and back.** The slots are back. The grid did not change.
7. **Die, switch set, loot the grave.** Everything returns to its place.
8. **Log out and in after each step.** Check `BepInEx/LogOutput.log` for `Gearhold` errors.
