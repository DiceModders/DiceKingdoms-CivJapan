# DiceKingdoms CivJapan (`DiceModders.CivJapan.dll`)

Japan as a new civilization for **Dice Kingdoms** (BepInEx 6 IL2CPP).

## What it does

Adds **Japan** to the civilization choice in the lobby, next to the game's own civilizations. The game's civilizations stay as they are.

* **Buildings, units, ship and ragdolls**: Japanese 3D models (castle, walls and gates, houses, temple, farms, barracks, lighthouse, and every unit type).
* **Pictures**: the building and unit pictures (build menu, lobby, scoreboard and the small pictures inside texts) are taken in the game from the new models, in every player colour. The first start after installing takes a little longer while they are made; after that they are read from the cache.
* **Texts**: the civilization's name and the unit, building and technology texts (name in English, German, French, Russian, Ukrainian and Chinese; the other texts in English, Russian, Ukrainian and Chinese).
* **Ground colours**: Japanese ground and nature colours on its island.
* **Tech tree**: its own layout of the technologies.
* **AI**: build orders written for this tree, so computer players can play Japan.

Japan plays like the civilization it is built on (Viking): same unit stats and building rules, so the match stays balanced. Only the look, the texts, the tech tree layout and the AI build orders are new.

Every player in the lobby needs this mod in the same version: a player without it does not know the civilization.

## Files

| Path | Job |
|---|---|
| `src/Plugin.cs` | the plugin, and reading `assets/japan.json` |
| `src/Civilization.cs` | registers the civilization with ModCore; copies the look-alike's buildings, units, ship and ragdolls and puts our models and pictures on them |
| `src/TechTree.cs` | Japan's tech tree from `assets/techtree.json` |
| `src/Models.cs` | loads the `.dkmesh` models (cached after the first start) |
| `src/Swapper.cs` | puts the models on the right parts of each building or unit |
| `assets/japan.json` | key, look-alike civilization, names, texts, ground colours and which model goes on which game object |
| `assets/techtree.json` | slot, technology, position and what each technology unlocks |
| `assets/build_orders.txt` | the AI build orders |
| `assets/models/*.dkmesh` | the models: plain text, one `v x y z u v` per vertex and `t a b c` per triangle, in the game's prefab space; UVs point into the game's 8x8 colour palette |

## Requirements

* Dice Kingdoms with **BepInEx 6 IL2CPP (x64)**: see the wiki's [Mods Installation](https://dicemodders.github.io/DiceKingdoms-Modding-Wiki/guides/mod-installation/) guide. Start the game once so BepInEx creates its folders.
* [ModCore](https://github.com/DiceModders/DiceKingdoms-ModCore) (`DiceModders.ModCore.dll`) in `BepInEx\plugins`. This mod does not contain a copy of it.

## Install (players)

Copy the whole `DiceModders.CivJapan` folder (the DLL **and** its `assets` folder) into `BepInEx\plugins`:

```
BepInEx\plugins\DiceModders.ModCore.dll
BepInEx\plugins\DiceModders.CivJapan\DiceModders.CivJapan.dll
BepInEx\plugins\DiceModders.CivJapan\assets\...
```

## Build (developers)

Needs the .NET SDK (6 or newer) and PowerShell. Game files are never committed: the script links `BepInEx\interop` from your game folder and copies ModCore from `BepInEx\plugins` into `libs`.

```powershell
.\scripts\build.ps1 -GameDir "<your Dice-Kingdoms folder>"   # first time: remembers the folder
.\scripts\build.ps1                                           # build + install (Release, console and log OFF)
.\scripts\build.ps1 -Dev -Run                                 # Debug build, console and log ON, then start the game
```

The script installs the DLL and a fresh copy of `assets` into `BepInEx\plugins\DiceModders.CivJapan\`. Close the game before building: it locks the DLLs. If PowerShell refuses to run scripts: `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`.

## License

MIT, see [LICENSE](LICENSE).
