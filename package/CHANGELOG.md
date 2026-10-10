# Changelog — Death & Taxes

Versions: the middle number goes up with each new biome; all biomes up to Ashlands = 0.9.0 (testing), 1.0.0 once
tested, Deep North = 1.1. The last number goes up for a chat session that changes the mod.

## 1.0.1 (2026-10-10)
- Config sections in a clearer order with two-digit numbers: 01 Production, 02 Building, 03 Overclock,
  04 Meadows ... 10 Ashlands. Values from the old section names are carried over automatically.
- New cheat command `dt_trophies` (devcommands): spawns every table trophy at its table maximum plus 2 of each
  boss trophy, for testing. Tip: `bind F7 dt_trophies`.
- "Add trophies" (and clicking a trophy slot) also takes matching trophies from nearby chests you may open, after
  your own inventory. New settings in 02 - Building: "Add trophies from nearby chests" (on), "Chest range" (20 m).

## 1.0.0 (2026-10-08)
- All tables approved in-game: Meadows, Black Forest, Swamp, Mountains, Plains, Mistlands, Ashlands.
- Item stands: one size on every table (the Drake's on the Mountains table) and in the colour of their table.
- Fenring trophy centred on its stand.
- Build menu icons show the tables themselves.
- Final descriptions for Swamp, Mountains, Plains, Mistlands and Ashlands.

## 0.9.0 (2026-10-06) — all biomes, first version — not released yet (1.0.0 once tested)
- New tables: **Tribute - Swamp** (unlocked by Bonemass), **Mountains** (Moder), **Plains** (Yagluth),
  **Mistlands** (The Queen) and **Ashlands** (Fader). Each works like the Black Forest table (same window,
  trophy wall, rarities); trophies and resources come from the build configs.
- Each table has its own look: Swamp ancient bark, Mountains stone, Plains fine wood, Mistlands Yggdrasil wood,
  Ashlands ashwood; and its own mix of biome items on the table top.
- Build cost: 10 of the biome's material (Ancient bark, Stone, Fine wood, Yggdrasil wood, Ashwood) + one of each
  common trophy.
- First version: descriptions are placeholders, trophy sizes and table-top items still need tuning in-game.

## 0.3.3 (2026-10-06)
- New item: **Lord Reto trophy**. Lord Reto (Ashlands miniboss) now always drops a trophy. For now it looks like a
  Charred warrior trophy in ember orange; it will be used by the Ashlands table later.

## 0.3.2 (2026-10-02) — rename
- The mod is now called **Death & Taxes** (was Lord of Biomes): ID SirCookie.DeathAndTaxes, DeathAndTaxes.dll in
  BepInEx/plugins/DeathAndTaxes/, config SirCookie.DeathAndTaxes.cfg (takes over the old config's values once).
- Tables already built, their trophies and stored production keep working (internal names unchanged).

## 0.3.1 (2026-10-01)
- Config `5 - Building` → `Tables per player` (default 1, 0 = no limit): how many tables of each type one
  character may build. The server counts them; existing tables keep working.
- "Remind them!" shows a random line ("They remember your wrath!", ...) instead of the multiplier and time.
- Optional (default off, not tested yet): `No roof allowed` and `No roof penalty` (0 = 4x slower, 0.5 = half speed,
  1 = full speed) let tables produce without a roof.
- Black Forest: new description ("The trees tremble at your passage. The forest sends tribute to its lord.")
  and its own "too many trophies" lines.
- Server compatibility: with ServersideQoL's AutoStore on the server, tribute tables are excluded from auto pickup,
  emote stacking and auto sorting (AutoProcess may still take from them).

## 0.3.0 (2026-09-30) — Black Forest
- New table "Tribute - Black Forest" (boss trophy: The Elder; cost 10 Corewood + Greydwarf, Skeleton, Troll trophy).
- Trophy rarities: common (10 per type), uncommon (5), rare (3), unique (1). Higher rarities unlock their own
  resources (uncommon at 6/15, rare at 2/6, unique at 1/1) and speed is mixed per rarity.
- Model: oak table scaled to 3 x 1.25 m, taller wall with three trophy rows; a trophy only shows once offered.
- Window rebuilt: one row per rarity (trophies left, resources right, rate per hour under each), title centred,
  small status line, wider buttons, "- x more trophies" on locked rarities.
- Meadows: stone moved to the Black Forest; Meadows now also makes Leather scraps and Feathers.
- Config sections renumbered (overclock values carried over automatically).

## 0.2.1 (2026-09-30)
- Window layout: buttons along the bottom, status line above them.

## 0.2.0 and earlier
- Tribute - Meadows: discovery with the Eikthyr trophy, kitbashed model (table, wall, trophies, offerings),
  trophy offering with quips, logarithmic speed 10 h → 1 h per stack, server-synced config with live reload,
  "Remind them!" overclock, ward rules, single-use, storage lock ("everybody can take, nobody can add").

