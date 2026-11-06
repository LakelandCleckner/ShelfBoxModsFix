# Shelf and Box Mods Fix

Compatibility fix for **What's in the Box?** (1.0.0) and **What's on the Shelf?** (1.0.1) on the current
TCG Card Shop Simulator build. Author: **JustChoochit**.

Both original mods are required and are not included or modified. Everything here works at load time
(a preloader patcher) or at runtime (Harmony patches).

## What it fixes
| Problem | Cause | Fix |
|---|---|---|
| Panels break on product shelves and boxes (`MissingFieldException: UI_PriceTag.m_Icon`) | The game moved price tags to an instanced atlas and removed `m_Icon` | Patcher re-adds `m_Icon` as an always-null field, so both mods take their existing no-label path |
| "In Storage" always 0 | Boxes on storage shelves are now data records (`StoredBoxRecord`), not objects | Adds stored records to both mods' storage totals |
| No readout on storage-shelf boxes, `ArgumentOutOfRangeException` spam | Same | Shows the box Take Box would grab next, via a hidden proxy box |
| Max shows `?` on stored boxes | Capacity is geometric and computed when a box is filled | Measures it on a hidden copy of the box prefab |
| No product picture | Product icons are not set for many products, including Enhanced Prefab Loader ones | Renders the product's 3D model; falls back to EPL's image or the price tag picture |

## Layout
```
src/Patcher   BepInEx 5 preloader patcher  -> BepInEx/patchers/ShelfBoxModsFix/
src/Plugin    BepInEx 5 plugin             -> BepInEx/plugins/ShelfBoxModsFix/
```

## Building
Requires the .NET SDK (6 or later). Point `GameDir` at your game install:
```
dotnet build src/Patcher -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\TCG Card Shop Simulator"
dotnet build src/Plugin  -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\TCG Card Shop Simulator"
```
If BepInEx is managed by Vortex, `BepInEx\core` holds links; pass `-p:BepInExCore=<real core folder>` if the build can't read them.

## Config (`BepInEx\config\justchoochit.shelfboxmodsfix.cfg`)
`[Product picture]` Use3DRender, Yaw, Pitch, Resolution. All apply live in Configuration Manager.
