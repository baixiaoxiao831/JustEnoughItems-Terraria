# Just Enough Items: Terraria Edition

[简体中文（原文）](./README.md) · **English (machine translation)**

Just Enough Items: Terraria Edition is an in-game item and recipe browser for Terraria’s tModLoader, inspired by Just Enough Items for Minecraft. It automatically indexes content from your enabled mods. Look up an item to see how to obtain it, which ingredients it needs, and where to craft it. You can also start with an ingredient to find recipes that use it.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3845723114)

## 0.1.93 changelog (includes 0.1.92)

- On Windows, focusing the JEI search field switches the IME to direct English input so you can type filters such as `@cala`. You can switch back to Chinese while searching. Leaving the field restores the previous input mode.
- The system IME candidate window is anchored to the search cursor. During composition, the IME handles the arrow keys, Tab, Enter, and Esc so selecting a candidate does not trigger search history or mod autocomplete. Removed the duplicate in-game candidate bar.
- The `@` suggestion list now includes only sources with items in the JEI catalog and hides mods that tModLoader marks as libraries.
- Fixed cheat-mode pickup stopping as soon as the first item reached the cursor. Hold right-click over the same slot to pick up items progressively faster, as in the vanilla inventory.

## 0.1.90 changelog

- **On-demand textures.** JEI requests item, NPC, and crafting station textures as they appear on screen. This reduces texture loading and memory allocations at startup. Some icons may take a few frames to appear the first time you flip through pages quickly.
- **Expanded English support.** Crafting categories, missing ingredient and station hints, bookmark actions, and AE order prompts are available in English. Drop, shop, Shimmer, fishing, and harvesting pages also follow the game language.
- **Mod filter autocomplete.** Type `@` to browse enabled mods and vanilla Terraria in A–Z order. Add a prefix such as `@cala` to narrow the list. Scroll to browse, click to select, or press `Tab` repeatedly to autocomplete each match. Scrolling and autocomplete both stop at the last entry.
- **Adjustable item grid.** Set the number of columns and rows independently in JEI’s client settings. Set either to `0` for automatic sizing. The grid fits within the available screen space.

## What you can look up

| Topic | What JEI shows |
| --- | --- |
| Crafting and missing ingredients | Ingredient quantities, valid substitutes, crafting stations, environmental requirements, and ingredients you still need. |
| Enemy drops and NPC shops | Enemy loot, base drop rates, and drop conditions. Shop entries show base prices and conditions for each item to go on sale. If the vendor is present in your world, you can locate them on the map. |
| Treasure Bags and fishing crates | Possible contents of each container. For fishing crates, you can also check catch conditions and see estimated catch rates based on fishing power and where you fish. |
| Shimmer transformations and decrafting | What an item can turn into, which materials you can recover by decrafting it, and how the moon phase or world progression affects the result. |
| Creatures and boss summons | Spawn environments listed in the Bestiary and known boss-summoning items. You can also look up a summoning item to see which boss it summons. |
| Harvesting herbs | Harvest conditions for the plants covered by JEI. For example, Deathweed blooms during a Blood Moon or a full moon. Harvest it in bloom to collect seeds. |

## Search and bookmarks

- **Chinese and Pinyin search.** When playing in Chinese, search by item name, full Pinyin, or Pinyin initials. Press `↑` or `↓` to recall earlier searches.
- **Combine filters.** Use `@` to filter by mod and `#` to filter by item category or recipe group. Use `-` to exclude a term or `|` to match any of several alternatives.
- **Bookmark and reorder.** Bookmark items, NPCs, or individual recipes, then drag your bookmarks into the order you want. If an item has several recipes, you can save the exact recipe card you plan to use.

## Inventory crafting and mod integration

- **Terraria inventory.** Click `+` on a recipe page to select an available recipe in Terraria’s crafting menu. For longer crafting chains, enable **Multi-step backpack crafting** in your personal settings (off by default). With no item on your cursor, hold `Ctrl` and left-click `+` to craft the intermediate items and then the final item, provided you have the required materials and stations and meet the recipe conditions.
- **AE terminal.** A JEI-compatible AE terminal uses the same search filters and lets you place crafting orders directly from recipe pages.

## Sharing, Redistribution, and Credits

You’re welcome to feature this mod in guides, videos, and mod recommendations. Please include a link to the [official Steam Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3845723114) so players can find the latest version. If you’d like to upload the full mod to another platform, redistribute a repackaged copy, or publish a modified version, please contact the author first for permission.

This project is an unofficial Terraria adaptation of Just Enough Items for Minecraft. The interface artwork, in-game guide icon, and mod icon use artwork by mezz and the Just Enough Items contributors under the MIT License. [Original Just Enough Items project](https://github.com/mezz/JustEnoughItems)

The bundled Chinese glyphs are derived from Noto Sans SC and are licensed under the SIL Open Font License 1.1. The Unihan pronunciation data used for Pinyin search is licensed under the Unicode License V3. Third-party materials remain available under their respective licenses. Full copyright and license notices are included in the mod’s `Licenses` folder.

## Reporting an issue

<img src="images/issue-report-en.png" width="420" alt="Without logs or screenshots, all I can do is read your fortune.">
