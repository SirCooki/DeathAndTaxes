# Death & Taxes

Become the feared master of a biome after killing the forsaken. This mod adds a tribute table for each biome once the forsaken has been defeated. The tribute consists of resources that are typically found in the biome. To build the tribute table you need only a few trophies. However, to gain more and rarer tribute, you must provide more trophies to unlock the table's true potential.

## Tables
Meadows (Eikthyr), Black Forest (The Elder), Swamp (Bonemass), Mountains (Moder), Plains (Yagluth),
Mistlands (The Queen) and Ashlands (Fader). Every table looks unique and will show the trophies you collected.

## Screenshots

### Meadows
![Meadows table, just built](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Meadows_ini.jpg)
![Meadows table, the tribute window](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Meadows_inventory.jpg)

### Black Forest
![Black Forest table, just built](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/BlackForest_ini.jpg)
![Black Forest table, all trophies offered](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/BlackForest_final.jpg)
![Black Forest table, the tribute window](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/BlackForest_inventory.jpg)

### Swamp
![Swamp table, just built](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Swamp_ini.jpg)
![Swamp table, all trophies offered](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Swamp_final.jpg)

### Mountains
![Mountains table, just built](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Mountains_ini.jpg)
![Mountains table, all trophies offered](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Mountains_final.jpg)
![Mountains table, the tribute window](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Mountains_inventory.jpg)

### Plains
![Plains table, just built](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Plains_ini.jpg)
![Plains table, all trophies offered](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Plains_final.jpg)
![Plains table, the tribute window](https://raw.githubusercontent.com/WouterVermeyen/DeathAndTaxes/main/img/Plains_inventory.jpg)

## How it works
- Carry a Forsaken's trophy and the new table unlocks in the build menu. The forsaken trophy is not a required ingredient for the table.
- Place it under a roof and it slowly produces that biome's resources.
- Offer creature trophies at the table. Trophies come in four rarities: common (up to 10 per type), uncommon (5),
  rare (3) and unique (1). More trophies make production faster: from 10 in-game hours per stack down to 1 in-game hour.
- Kill the boss again and spend its trophy with the "Remind them!" button to double production for an in-game hour
  (multiplier configurable).
- Configure to restrict items or change the generation speed.

## Important
- The table only holds what it produced. It can't be used as storage.
- One table of each type per player on a server (configurable).
- Mods that interact with inventory can't generally take out resources automatically.

## Multiplayer
Install on the server **and** every player (same version). Settings are server-side and synced to all players;
the config file reloads while the game or server runs.

## Settings (`BepInEx/config/SirCookie.DeathAndTaxes.cfg`)
Hours per stack (base/final), progression curve, overclock on/off and multiplier, generated resources per table,
tables per player, and an optional "no roof" mode.

## Building from source
- Needs the .NET SDK 8 and a Valheim install with BepInEx and Jötunn (game DLLs are referenced from it).
- `dotnet build -c Release` builds `DeathAndTaxes.dll` and copies it to `BepInEx/plugins/DeathAndTaxes/`.
  If Valheim isn't in the default Steam folder, pass `-p:ValheimDir="<your Valheim folder>"`.
- `package/` holds the Thunderstore manifest and icon; `build_configs/` the design sheets per biome.

Author: SirCookie — MIT License (see LICENSE).
