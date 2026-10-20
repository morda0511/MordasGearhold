# Changelog

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
