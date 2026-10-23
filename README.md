![Morda's Gearhold – Inventory, Loadouts & Quickslots](https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/mordas-gearhold-banner.png)

# Morda's Gearhold
*Inventory, Loadouts & Quickslots* – Valheim 1.0.20, BepInEx 5.4.23.x · version 1.4.0

Three switchable equipment sets, a shared inventory, 8 hotkey quickslots, a second weapon on your back or hip, 3 ammo slots, up to 3 trinkets and utility items, transmog looks and armour dyes.

## At a glance
| | |
|---|---|
| <img src="https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/icon-sets.png" width="80" alt="Sets"> | **3 equipment sets** – switch your whole loadout with one click or hotkey. The inventory stays shared. |
| <img src="https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/icon-quickslots.png" width="80" alt="Quickslots"> | **8 quickslots** with your own hotkeys and a small bar on the HUD. |
| <img src="https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/icon-slots.png" width="80" alt="Weapon 2"> | **Weapon 2** – a second weapon worn on your back or hip; **R** swaps it with the one in your hand. |
| <img src="https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/icon-ammo.png" width="80" alt="Ammo"> | **3 ammo slots** – switch arrows instantly with Alt+1/2/3. |
| <img src="https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/icon-trinkets.png" width="80" alt="Trinkets"> | **Up to 3 trinkets and 3 utility items** worn at once (configurable 1–3). |
| <img src="https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/icon-transmog.png" width="80" alt="Transmog"> | **Transmog** – wear the look of any armour you know, per set. |
| <img src="https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/icon-dyes.png" width="80" alt="Dyes"> | **Dyes** – recolour your armour with swatches and fine controls, with a live 3D preview. |
| <img src="https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/icon-deathsafe.png" width="80" alt="Death safe"> | **Death safe** – only the active set goes into the grave; looting it puts everything back in place. |

## In game
**The Gearhold panel** – sets, equipment slots, the YOUR VIKING sheet, food, Weapon 2 and mead, and the quickslots with their hotkeys:

![The Gearhold panel in game](https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/Gearhold%20menu%20New.png)

**The look menu** – tabs for every piece, pick a look, hide it or dye it, with a 3D preview of your character and SAVE:

![The transmog and dye menu in game](https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/Transmog%20Menu.png)

![](https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/divider-norse.png)

## What it does
- **Three independent equipment sets.** A set is exactly what lies in the Gearhold panel: armour, shield, three utility items, three trinkets, three ammo slots and 8 quickslots (food, potions, ...).
- **Weapons and bows stay in your hotbar.** The inventory grid, hotbar included, is shared by all sets. A set switch never touches it.
- **Quickslots** with their own hotkeys (default `Z`, `B`, `N`, `H`), changeable right in the panel. Example: put food in, press the key, eat.
  - Unlike the vanilla hotbar, a quickslot key never hands its item to what you are looking at (item stand, mannequin, ...). Switch this with `QuickslotsUseOnTarget`.
  - All Gearhold hotkeys only work while you play in the world: not in menus, not in the build menu and never while you type into a text field (e.g. a search box).
- **Food and mead slots:** 3 food and 3 mead slots right above the quickslots, per set. Food slots take food only, mead slots meads and potions. Each has its own hotkey (set with the button under the slot, like the quickslots; unbound at first); right-click eats or drinks too. When one of your three food buffs runs out, Gearhold eats from the food slots on its own (`AutoEatFoodSlots`, on by default; `AutoEatEarly` eats again as soon as the game allows). On the HUD they get their own bars: food next to the food boxes at the bottom left, meads right of the quickslots.
- **Quickslot bar on the HUD:** at the bottom centre you see every occupied quickslot with its item and amount, and its hotkey on one line above it (also long keys like `Alt+1`). A changed key shows up immediately. The bar hides while the inventory is open; its size is set with `QuickslotBarScale`.
  - **Move the bars:** the **settings gear** in the bottom right corner of the panel opens a small window. Pick QUICKSLOTS, FOOD or MEAD, move that bar with the sliders (left/right, up/down), set its size and choose ROW or COLUMN for it. While it is open the panel steps aside and all three bars show on screen, so you see every move at once. RESET puts the chosen bar back to its default spot, size and layout.
- **Weapon 2** (between the food and mead slots, per set): put a second weapon there, melee, bow or crossbow.
  - The hide key (**R**) swaps it with the weapon in your hand. Press it again to swap back. With an empty slot, or no weapon in hand, R works like vanilla.
  - **Own swap key:** click the box under the slot and press a key (`Weapon2Swap`). Then only that key swaps and R is vanilla again. Delete clears it.
  - **Worn on the body:** the weapon hangs where vanilla puts a weapon away: one-handers at the hip, two-handers, atgeirs and bows on the back. Other players see it too (`ShowWeapon2OnBody`).
  - Switch the whole feature off with `EnableWeapon2`: the slot is gone, R is vanilla, a weapon still in the slot goes back into the inventory.
- **Three ammo slots:** `Alt+1`, `Alt+2`, `Alt+3` pick the ammo you shoot. Each key sits as a button under its slot: click it to set another one. Holding Alt keeps the vanilla hotbar from also using item 1–3.
  - While a bow is drawn, a small list above it in the hotbar shows the fitting arrows from your ammo slots with their amount; the ones in use are gold (`ShowBowAmmo`).
- **Up to three trinkets and three utility items at once** (switch slots 2 and 3 on with `TrinketSlots` / `UtilitySlots`; by default only the vanilla slot is shown):
  - Slot 1 is the normal vanilla slot. Slots 2 and 3 are always active while something lies in them.
  - Right-click a trinket or utility item in the inventory: when slot 1 is taken, it goes into a free slot 2 or 3.
  - On death all three go into the grave like the rest of the set.
- **YOUR VIKING** shows:
  - your armour and weight,
  - the equipment modifiers (movement, stamina costs, heat),
  - **WEAPON** (only while a weapon is drawn): damage per type, stamina/eitr use, block armour, block force, parry bonus, knockback, backstab, durability,
  - **SHIELD** (only while a shield is worn): block armour, block force, parry bonus,
  - **UTILITY** (only with worn utility items): each item by name with its while-worn effect, and with Path of Valheiman its fixed bonuses (belts, rings) and enchantments,
  - **TRINKETS** (only with active trinkets): total max adrenaline, and each effect with the game's own description,
    e.g. what happens at full adrenaline,
  - **RESISTANCES** (e.g. Frost: Resistant, Spirit: Immune), including effects active right now.
  - **ENCHANTMENTS** (with Path of Valheiman): the enchantments of your worn armour, shield, utility items and trinkets, added up (two "+5% Fire damage" become "+10% Fire damage"). The drawn weapon lists its own enchantments under WEAPON.

  Health, stamina and eitr are left out because the vanilla HUD already shows them.
- **Coin slot** (top right of the panel, next to the title): coins you pick up go in there on their own and **weigh nothing**. It is shared by all three sets and has **no limit**: one full stack lies in the slot, everything beyond is kept in a purse with your character, and the slot shows the total. Traders see and take the whole amount. Coins in the purse stay with you when you die; the stack in the slot goes into the grave as usual. Turn the automatic moving off with `AutoStoreCoins`.
- **Auto-equip:** new armour, shields, trinkets and ammo you pick up or craft go into an **empty** slot only. Gear you already wear is never replaced. A new trinket goes into Trinket 2 or 3 when Trinket 1 is taken.
- **Auto-shield:**
  - Equip a one-handed weapon from the hotbar, and the set's shield comes along. One-handed axes too, unless `AutoShieldWithAxes` is off.
  - Put the weapon away, and the shield goes too. Swapping one weapon for another, or eating, keeps it.
  - With a bow or two-hander, the shield is put away but stays in the shield slot.

## Transmog (looks)
Change how your gear **looks** without changing the gear itself.

- **Unlocking:** every piece whose recipe you know is available right away, the same list the crafting menu uses. You do not need to own it. Pieces you crafted count as well.
- **Armour and shield** (Head, Chest, Legs, Cape, Shield): right-click one of these slots in the Gearhold panel, or click the **transmog button** in its top left corner. The menu opens in the middle of the screen, with a tab for each piece, so you can dress your whole set without leaving it.
  - **OWN LOOK** shows the real item.
  - The first cell, **Hide** (a box with a red slash), makes the piece invisible. To take gear off, left-click the slot.
  - Or click one of your unlocked looks of the same kind.
  - Looks are saved **per set**, so each set can have its own outfit.
- **SAVE and BACK:** every choice shows on your character right away, but only **SAVE** (under the character) keeps it. **< BACK** (top left), Esc or closing the inventory throws the changes away.
- **Weapons and bows:** `Alt` + right-click the weapon in the inventory, or right-click it in a quickslot. Only looks of the same weapon kind are offered (axe as axe, sword as sword), so grip and animations always fit.
  - The look stays with that weapon, in every set.
  - Upgrading a weapon at a workbench makes a new item, so its look has to be picked again.
- **Marker:** a piece with a chosen look, colour or Hide shows a small gold sparkle (✦) in the top-left corner of its slot. This also works for weapons in the inventory and in quickslots.
- **Visibility:** other players see your looks too, because the game shares visible gear. A look only shows while real gear is worn in that slot.
- **Character selection:** the character on the selection screen already wears the looks and colours of its active set.

## Dyes (colours)
Recolour Head, Chest, Legs, Cape and Shield in the same menu: your character is shown in **3D** on the left, so every choice is visible at once (drag to turn it, mouse wheel to zoom); below the looks you find **COLOR**.

- **Colour zones:** a piece with several materials (metal, leather, cloth ...) is split into up to four zones by the colours in its texture. In the COLOR section every zone is a small chip: the ring is its original colour, the middle shows its colour now. Click a chip, then colour just that zone; **ALL** colours the whole piece the same, as before. Zones are found automatically and the same way every time, and soft edges keep the transitions smooth.
- **Colour swatches** (Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink, Black, White, Grey): the piece's main colour is measured and shifted towards the swatch, so "Red" really ends up reddish.
- **Fine controls:** **Color** turns the colour around the colour wheel (±15° per click), **Intensity** makes it stronger or greyer, **Brightness** lighter or darker (±10 % per click).
- **RESET** goes back to the original colours of that piece.
- Skin under and around the armour keeps its colour: the painted armour is only recoloured where it covers the body.
- Colours are saved **per set** and work together with looks, so a borrowed look can be dyed as well.
- **Weapons can be dyed too:** open the look menu of a weapon (`Alt` + right-click it in the inventory, or right-click it in a quickslot). It has the same COLOR section with colour zones. The colours belong to that weapon and follow it into every set; a dyed weapon gets the sparkle marker. The preview shows the weapon while you hold it; the zones are found from its model, so you can also dye one that is put away.
- **Shields and models without colour controls:** shaders that have no hue control of their own (many shields, weapons and some armour) are dyed by recolouring a copy of their texture, so every piece can take a colour. A shield that is put away can be dyed as well; it shows when you wield it.
- **How it works:** only your character's copy is recoloured, never the item itself or other players' copies.
  - One colour for the whole piece: helmet, cape, shield and the chest/leg models use the game's own colour controls (`_Hue`, `_Saturation`, `_Value`).
  - Several zones: a recoloured copy of the model's texture, made once per change.
  - Armour painted onto the body: a recoloured copy of its texture, made once per change.
- **Multiplayer:** for now only you see your colours. Showing them to other Gearhold players is planned.

## Weapon glow
Weapons with glow effects (fire, frost, lightning, glowing cores and runes) get a **GLOW** section in their look menu.

- **Colour swatches:** every coloured glow part takes the chosen colour: particles, lights, glowing materials and the swing trail. **White** makes them white.
- Brightness and strength stay, so fire still flickers like fire.
- Smoke, white shimmer and the hit effects keep their own colours.
- **RESET** gives the weapon its own colours back.
- The colour belongs to that weapon and follows it into every set. A weapon with a glow colour gets the sparkle marker too.
- Like the looks, it shows once you **SAVE**. Draw the weapon to see it in the preview.
- **Multiplayer:** for now only you see the colour.

![The weapon look menu with the GLOW colours](https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/Weapon%20Transmog.png)

**The HUD bars window** (settings gear in the bottom right corner of the panel) – move, resize and turn each of the quickslot, food and mead bars:

![The HUD bars window](https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/HUD%20Settings.png)

## Panel controls
| Action | Result |
|---|---|
| Left-click an item in the inventory, then left-click a slot | The item moves into the slot. Armour is equipped; ammo is equipped if you have none in use. |
| Left-click a filled slot | Equipment goes back into the inventory grid. A quickslot item or the coins are picked up: click another quickslot to move or swap it, or an inventory cell to put it back. |
| Right-click a slot | Head, Chest, Legs, Cape, Shield, or a weapon in a quickslot: open the look menu. Other slots: use the item (eat, drink, use this ammo). |
| Key button under a quickslot, food, mead, ammo or Weapon 2 slot | Click, then press the new key (Shift/Ctrl/Alt allowed). Esc cancels, Delete clears. |
| SET 1 / SET 2 / SET 3 tabs or hotkeys | Switch sets. |

Slots: Head, Chest, Legs, Cape, Utility 1–3 (left); Shield, Ammo 1–3, Trinket 1–3 (right); Food 1–3, Weapon 2 and Mead 1–3 (above the quickslots); Quickslot 1–8; the coin slot (top right). YOUR VIKING scrolls with the mouse wheel when it is longer than the panel.
Right-click on Trinket/Utility 2 or 3 makes it the main one (it swaps with slot 1).

**Key conflicts:**
- A key you assign is taken away from any other Gearhold hotkey that used it.
- A key that vanilla also uses (e.g. X = Sit, Q = Auto-run) shows a note.

**No set switch** while attacking, dodging or swimming, while armour is still being put on, or while you hold an item on the cursor.

## Death and tombstone
- **Into the grave** go the inventory grid and the active set's Gearhold slots, under the normal vanilla rules.
- **The other two sets** stay safely on the character.
- **The grave remembers its set.** If another set is active when you loot it, Gearhold switches back first. Items return to their old places, and gear in empty slots is worn again.
- **Whatever does not fit** stays in the grave, as in vanilla.

## Safety
- **Items of a missing mod** are never deleted. They stay as dormant records and come back when the mod is installed again.
- **A set switch** runs within one game frame: every record is checked first, with a journal and rollback.
- **Damaged data** is never overwritten. It is kept under `MordasGearhold.Quarantine.*`, and the log says why.

## Uninstalling – please read
- **The two inactive sets** stay stored in the save, but cannot be reached without the mod. They come back on reinstall.
- **Rows beyond the vanilla limit and the hidden slot rows** are dropped on the ground by vanilla at the next spawn.
- **Before uninstalling,** empty the panel slots of all three sets and the lower grid rows into a chest.

## Installation
- **Mod manager (recommended):** install *Morda's Gearhold* with r2modman or the Thunderstore Mod Manager.
- **Manual:** install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). Then put `MordasGearhold.dll` into `BepInEx/plugins/MordasGearhold/`.
- **Path of Valheiman:** works together. Gearhold switches Path of Valheiman's own equipment system (its slots beside the crafting window, purse and tool bar) off through Path of Valheiman's API, because both would use the same inventory rows. Its passive tree, loot and everything else keep working, and its item tooltips (enchantments, sockets, ...) also show on the Gearhold slots. It also never rebuilds its old inventory rows on load any more; anything still lying in a slot it does not belong in is moved into your inventory by Gearhold.
- **StoreAndCraft:** its dump (hotkey, hover-store, auto-stack) never takes anything out of the Gearhold slots (quickslots, food, meads, ammo, coins ...), and its Sort only sorts the grid.
- **Not compatible:** ExtraSlots and AzuExtendedPlayerInventory (they take over the same inventory rows).

Source and issues: https://github.com/morda0511/MordasGearhold

![](https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/divider-norse.png)

## More mods by Morda

| Mod | What it does |
|:---|:---|
| **[StoreAndCraft](https://thunderstore.io/c/valheim/p/Morda/StoreAndCraft/)** | A base that works with you: auto-store, dump, craft and build from chests, storage displays, feed trough, F10 settings panel |
| **[WorkbenchesPlus](https://thunderstore.io/c/valheim/p/Morda/WorkbenchesPlus/)** | Sort and filter crafting menus: craftable first, categories, armor and weapon sets by biome tier, search, favorites, dismantle |
| **[Forager's Garden](https://thunderstore.io/c/valheim/p/Morda/ForagersGarden/)** | Berry bushes, wild herbs and mushrooms with the Cultivator, plus Ice, Royal Jelly and Crystal beehives |
| **[Valheim Mannequins](https://thunderstore.io/c/valheim/p/Morda/ValheimMannequins/)** | Player-shaped mannequins: dress them with real gear, pose and name them, keep several gear sets |
| **[ServerRates](https://thunderstore.io/c/valheim/p/Morda/ServerRates/)** | Server-side rates for dedicated servers (PC and console), live apply, presets. Joining players need no install |

![](https://raw.githubusercontent.com/morda0511/MordasGearhold/main/thunderstore-assets/mordas-gearhold/mordas-gearhold-footer.png)
