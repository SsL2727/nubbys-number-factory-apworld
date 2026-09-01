# Nubby's Number Factory - Archipelago World

An [Archipelago](https://archipelago.gg) randomizer implementation for *Nubby's Number Factory*. Randomization is save-native: game logic is enforced via a companion Python client and a small set of binary patches applied to your **own, legally-owned** copy of the game's `data.win`. This repo does not, and will not, distribute the game itself - only the patch script needed to build it.

## What's in this repo

- `nubbys_number_factory/` - the apworld's Python source (items, locations, options, regions, and the in-game client).
- `nubbys_number_factory.apworld` - the built, installable apworld package.
- `gml_patch/PatchApItemLockV30_MASTER.csx` - the [UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool) script that patches your own `data.win`.
- `poptracker_pack/nnf_poptracker_pack.zip` - a [PopTracker](https://github.com/black-sliver/PopTracker) pack for tracking checks.

## Setup

### 1. Patch your own `data.win`

You need a legally-purchased copy of Nubby's Number Factory and [UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool) installed.

1. Back up your game's `data.win` (in the game's install folder) somewhere safe.
2. Open your `data.win` in UndertaleModTool and run **Decompile All Code** (or use `UndertaleModCli`'s decompile mode). This produces a folder of `.gml` files - point it somewhere on disk, e.g. `C:\NNF_decompiled\`.
3. Open `gml_patch/PatchApItemLockV30_MASTER.csx` and edit **line 10** so `decompFolder` points at that folder:
   ```csharp
   string decompFolder = @"C:\NNF_decompiled";
   ```
4. Run the script against your own `data.win` with UndertaleModCli:
   ```
   UndertaleModCli.exe load "data.win" -s "PatchApItemLockV30_MASTER.csx" -o "data.win.patched"
   ```
5. Replace the game's `data.win` with `data.win.patched` (keep your backup from step 1 - you'll want it to play vanilla again).

### 2. Install the apworld

Copy `nubbys_number_factory.apworld` into your Archipelago install's `custom_worlds` folder, then generate or join a game as usual.

### 3. Connect the in-game client

Launch **ArchipelagoLauncher.exe** and pick **Nubby's Number Factory Client** from the list. Enter your server address/port and slot name like any other Archipelago client - the client bridges between the server and your patched game automatically; no manual file editing required after that.

### 4. (Optional) PopTracker

Open [PopTracker](https://github.com/black-sliver/PopTracker), load `poptracker_pack/nnf_poptracker_pack.zip` as a pack, and connect it to the same session for a visual check tracker.

## Notes

- Every AP-specific behavior lives in the client and the patch script above - nothing here modifies or redistributes the base game's assets.
- If items you've received ever seem to go missing (e.g. after a rough reconnect), the client supports `/resync` to force a full re-application of everything you've received.
