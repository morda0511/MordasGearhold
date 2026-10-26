# Changelog

## 1.4.0
- Colour zones: a piece is split into up to four zones by the colours of its texture (metal, leather, cloth ...). In the COLOR section pick a zone chip (ring = original colour, middle = colour now) and dye just that zone; ALL still colours the whole piece.
- A dyed zone has one colour all over, so Red is red also where a small accent colour fell into the zone. Small accent colours get their own zone more often.
- The look menu redraws as soon as the colour zones of the shown piece change, so the zone chips show up right away after switching the piece or the look.
- Fixed armour or helmet vanishing while recolouring: recoloured textures that were still on the character could be freed by the cache.
- A failure while dyeing is logged and can no longer disturb vanilla gear update.
- Weapons can be dyed too (their look menu has the COLOR section with zones); the dye follows the weapon into every set.
- The Weapon 2 weapon on your body shows its chosen look (transmog) and colour, not the original model.
- Lights that a weapon's particles create themselves (a light copied from outside the weapon) are recoloured too. The log lists every effect part of a weapon once, with its colour and the weapon's name.
- GLOW reaches more effect parts: glow quads and other effect meshes, trails and lines, particle trails and every colour property of an effect material. The log lists the effect parts of each weapon model once.
- Weapons with the white upgrade shimmer get a SHINE section in their look menu: colour that shimmer separately from the GLOW (it shows on upgraded weapons).
- Pieces whose shader has no colour controls (many shields, some armour) are dyed by recolouring a copy of their texture, so more pieces react to a colour. A shield that is put away can be dyed as well.
- The log names a dyed piece whose colour changed nothing, with its shaders.
- Multiplayer: other players with Gearhold see your dyes (all zones), weapon dyes and GLOW / SHINE colours, also on the Weapon 2 weapon on your body. Players without Gearhold see the normal colours; the server needs nothing. Config `ShowOthersColors` (UI) switches seeing others' colours off.
- Panel is more compact: utility slots sit in a row below the armour, trinkets next to the armour, and the ammo slots moved into a drop-down opened by the AMMO button; less space between food and quickslots.
- The panel layout comes from a built-in table and can be overridden by a `MordasGearhold.layout.json` in the BepInEx config folder (made with tools/layout-editor.html).
- Right-click on a Gearhold slot equips / unequips like in the vanilla inventory; the look menu opens from the transmog button (Shift + right-click on a slot is a shortcut).
- Colour zones reworked: found in the OKLab colour space (up to 8 zones instead of 4, dark teal and dark brown stay apart), and a chosen colour keeps the texture's own light, shadow and fine colour variation instead of looking flat and plastic. Tunable live with the dye file (new keys wc, detail).
- The numbers behind the colouring can be changed while the game runs with a MordasGearhold.dye.json in the config folder (made by the dye lab tool).
- Colouring reworked: you pick the colour a zone gets (swatches, or the Hue / Saturation / Brightness sliders and a #RRGGBB field with a preview) instead of shifting it; the model keeps its own shading around the chosen colour. Old colours still work. Sliders recolour about five times a second while you drag.
- Typing a preset name no longer sends keys to the game (E closing the menu, the character moving).
- The look menu hides the inventory windows while it is open and brings them back when you leave it. New PRESETS button in the look menu: eight named slots (click the name to rename) that save the looks and colours of your five pieces (SAVE) and put them on the active set later (LOAD). Weapon looks and colours are not part of a preset.
- Custom slots have hotkeys (config Custom1-3, also set with the key button under each slot).
- Custom slots show the name of their item (or of the first item of their list) under the slot; items of other mods work as long as the prefab name is right.
- Custom slots: three slots under the shield that only take the items named in the config (CustomSlot1-3, prefab names, also items of other mods). The inventory gets one more hidden row for them.

## 1.3.8
- Weapon 2 slot between food and mead: put a second weapon there, the hide key (R) swaps it with the weapon in your hand. Empty slot: R works like vanilla.
- Own swap key Weapon2Swap (click the box under Weapon 2): when set, only it swaps and R is vanilla again. Empty: R swaps. A swap key that uses the hide key with a modifier (e.g. Alt+R) only swaps and does not put the weapon away as well.
- Config EnableWeapon2 (on by default): off removes the Weapon 2 slot and R works like vanilla again.
- The Weapon 2 weapon is worn on the body: one-handers at the hip, two-handers, atgeirs and bows on the back (config ShowWeapon2OnBody, on by default). Other players see it too.
- New config AutoShieldWithAxes (on by default): off keeps one-handed axes without the automatic shield.
- Ammo hotkeys are shown as key buttons like food, mead and quickslots (click to set a new key).
- Short key names on all key buttons: ] instead of RightBracket, M4 / M5 for mouse buttons.
- Transmog button in the top left corner of the panel opens the looks & colours menu.
- The HUD BAR button is gone: a settings gear in the bottom right corner opens the HUD bar window.
- Above the bow in the hotbar while it is drawn: a small list of the fitting arrows in your Gearhold ammo slots, one line each with icon, name and amount; the ammo in use is gold (config ShowBowAmmo).

## 1.3.7
- Food slots are eaten from automatically when one of your three food buffs runs out (config AutoEatFoodSlots, on by default).
- StoreAndCraft: dumping (hotkey, hover-store, auto-stack) never takes items out of the Gearhold slots.

## 1.3.6
- Gearhold hotkeys only work while you play in the world: not in menus or the build menu, and never while typing into a text field (typing U into the build search no longer uses quickslot U).

## 1.3.5
- Size slider in the HUD BAR window: each bar (quickslots, food, mead) has its own size, 30 % to 200 %.

## 1.3.4
- The HUD BAR sliders reach the real screen edges on any screen width (they stopped at 900 before).

## 1.3.3
- Gearhold slots show the same item tooltip as inventory cells, so tooltip mods like Path of Valheiman (enchantments, sockets, compare) work on them too.

## 1.3.2
- YOUR VIKING, UTILITY: every worn utility item is listed by name with what it does, including the fixed bonuses and enchantments of Path of Valheiman belts and rings (they were missing before). Trinkets show their Path of Valheiman bonuses too.

## 1.3.1
- Each HUD bar (quickslots, food, mead) can be a row or a column: ROW / COLUMN in the HUD BAR window.

## 1.3.0
- Food and mead slots get their own hotkeys, set with a button under each slot (unbound at first).
- Food and mead get their own HUD bars: food next to the food boxes at the bottom left, meads right of the quickslots.
- The HUD BAR window moves each bar on its own: pick QUICKSLOTS, FOOD or MEAD, then use the sliders.

## 1.2.0
- 3 food and 3 mead slots per set, right above the quickslots (right-click to eat or drink).
- YOUR VIKING: ENCHANTMENTS section with the added-up enchantments of worn gear (Path of Valheiman); the drawn weapon lists its own.
- Coin slot without the 999 limit: everything beyond one stack is kept in a purse, shown as one total and usable at traders.
- Quickslot bar lower by default (it overlapped the stamina bar) and movable: HUD BAR button with left/right and up/down sliders.

## 1.1.1
- Path of Valheiman: its equipment rows are never rebuilt on load any more, even when the character was saved with them (that moved items into its slots although its equipment system was off).
- On load, items lying in a Gearhold slot they do not belong in are moved into the inventory.

## 1.1.0
- Works with Path of Valheiman: Gearhold turns its own equipment system off through Path of Valheiman's API, because both mods used the same inventory rows (set switches failed, gear landed in the wrong slots). The rest of Path of Valheiman keeps working.
- The inventory height is kept at grid + slot rows if another mod changes it.

## 1.0.2
- A set switch no longer fails as a whole because one item cannot go back into its slot: that item goes into a free inventory cell (or stays stored with the set), and the rest of the set is switched.
- When a switch does fail, the message names the reason.

## 1.0.1
- When a set switch fails and is rolled back, the gear you wore is put on again (it stayed in its slots, but unworn).

## 1.0.0
First public release.
- Three switchable equipment sets with a shared inventory grid.
- Gearhold panel: armour, shield, up to 3 utility items and 3 trinkets, 3 ammo slots, 8 quickslots with own hotkeys, coin slot.
- Quickslot bar on the HUD, auto-equip, auto-shield, optional auto-eat.
- YOUR VIKING sheet with armour, modifiers, weapon and shield stats, trinket effects and resistances.
- Transmog looks per set (armour, shield) and per weapon, with Hide.
- Armour dyes and weapon glow colours with a live 3D preview.
- Death and grave safe: only the active set goes into the grave and returns to its slots.
