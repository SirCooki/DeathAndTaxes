# Death & Taxes — optional features

Optional parts of the mod: built or planned, outside the main body (the main tables, production, window and config).
Each entry has an outline from the user and its status. Test these later, when the mod is further along.

## Decaying trophy system
- Status: idea, not built.
- Outline: every hour a random trophy decays from each table, never the unique ones.

## No roof influences the speed
- Status: built in 0.3.1, default off, not tested yet.
- Outline: players can choose to turn off the requirement for a roof, with an associated speed penalty.
- Config: `1 - Production` → `No roof allowed` (default false) + `No roof penalty` (0..1, default 0.5;
  speed = 4 ^ (value - 1), so 0 = four times slower, 1 = no penalty).

## Ocean table
- Status: idea, not built. Design sheet: `build_configs/build_config_08_Ocean.cfg`.
- Build cost: 2 Serpent trophies (`TrophySerpent`) + 5 Wood + 5 Corewood (`RoundLog`) + 5 Fine wood (`FineWood`).
- Unlock: the Ocean has much more limited resources and no boss, so the table unlocks after acquiring Bonemass.
- Speed: in the window, 2 stacks of 3 trophies: Serpent (`TrophySerpent`) and Bonemaw (`TrophyBonemawSerpent`).
- Fish: each fish is unlocked by adding 2 trophies of the creature linked to the fishing bait that lures it
  (inspired by https://valheim.fandom.com/wiki/Bait, not exactly like it; e.g. cold fishing bait = Fenring
  trophy, so 2 Fenring trophies unlock Tetra).

| Unlock | Trophy (prefab) | Fish (prefab) |
|---|---|---|
| Base table | — | Perch (`Fish1`), Pike (`Fish2`) |
| 2 trophies | Troll trophy (`TrophyFrostTroll`) | Trollfish (`Fish5`) |
| 2 trophies | Abomination trophy (`TrophyAbomination`) | Giant herring (`Fish6`) |
| 2 trophies | Fuling trophy (`TrophyGoblin`) | Grouper (`Fish7`) |
| 2 trophies | Hare trophy (`TrophyHare`) | Tuna (`Fish3`) |
| 2 trophies | Seeker soldier trophy (`TrophySeekerBrute`) | Coral cod (`Fish8`) |
| 2 trophies | Lox trophy (`TrophyLox`) | Pufferfish (`Fish12`) |
| 2 trophies | Wolf trophy (`TrophyWolf`) | Anglerfish (`Fish9`) |
| 2 trophies | Charred warrior trophy (`TrophyCharredMelee`, wiki "Warrior trophy") | Magmafish (`Fish11`) |
| 2 trophies | Drake trophy (`TrophyHatchling`) | Northern salmon (`Fish10`) |
| 2 trophies | Fenring trophy (`TrophyFenring`) | Tetra (`Fish4_cave`) |

- How hard each fish is to get (wiki Fish page, 2026-10-02; no player guide ranks them, so this is built from
  the wiki data). Stamina drain = stamina per second while the fish struggles (quality 1), the main catch difficulty.

| Fish | Where | Bait | Stamina drain | Notes |
|---|---|---|---|---|
| Perch | Meadows, Black Forest | Fishing bait (Haldor) | 10 | easiest |
| Pike | Meadows, Black Forest (semi-deep) | Fishing bait / Cold bait | 15 | |
| Tuna | Ocean (deep) | Heavy (Serpent) | 20 | wanders a very wide range |
| Trollfish | Black Forest shores | Mossy (Troll) | 25 | |
| Tetra | Frost Cave lakes (Mountains) | Cold (Fenring) | 28 | lakes are very rare; once found, Tetra spawn there reliably |
| Giant herring | Swamp shores | Sticky (Abomination) | 30 | |
| Grouper | Plains shores | Stingy (Fuling) | 32 | |
| Coral cod | Ocean (deep) | Heavy (Serpent) | 34 | |
| Anglerfish | Mistlands shores | Misty (Lox) | 38 | |
| Pufferfish | Ocean (very deep), Mistlands shores | Misty (Lox) | 40 | |
| Magmafish | Ashlands shores | Hot (Charred warrior) | 50 | lower level-up chance (15%) |
| Northern salmon | Deep North shores | Frosty (Drake) | 60 | hardest; lower level-up chance (15%) |

- Speed trophies: Serpent trophy (`TrophySerpent`), Bonemaw trophy (`TrophyBonemawSerpent`); unlock boss trophy
  Bonemass (`TrophyBonemass`).
- Checked 2026-10-02 against the wiki pages Trophies, Fish and Bait: all trophy IDs correct; the setup covers all
  12 fish in the game (Fish1..Fish12, Fish4_cave = Tetra). Fish IDs matched by the wiki's per-fish rows.
- Model: the hot tub with fish inside it (vanilla Hot tub, prefab `piece_bathtub`, confirmed in
  the game files and on the wiki; 3.2 x 2.3 x 3.6 m).
