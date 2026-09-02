# Nubby's Number Factory - Archipelago World

An [Archipelago](https://archipelago.gg) randomizer implementation for *Nubby's Number Factory*. Randomization is save-native: game logic is enforced via a companion Python client and a small set of binary patches applied to your **own, legally-owned** copy of the game's `data.win`. This repo does not, and will not, distribute the game itself.

## What's in this repo

- `nubbys_number_factory/` - the apworld's Python source (items, locations, options, regions, and the in-game client). The client also bundles the patch scripts it needs to patch your `data.win` for you - see Setup below.
- `nubbys_number_factory.apworld` - the built, installable apworld package.
- `gml_patch/PatchApItemLockV30_MASTER.csx` - the raw [UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool) patch script, for anyone who wants to inspect it or apply it manually instead of using `/patch` (see Advanced setup below).
- `poptracker_pack/nnf_poptracker_pack.zip` - a [PopTracker](https://github.com/black-sliver/PopTracker) pack for tracking checks.

## Setup

### 1. Install the apworld

Copy `nubbys_number_factory.apworld` into your Archipelago install's `custom_worlds` folder, then generate or join a game as usual.

### 2. Connect the client

Launch **ArchipelagoLauncher.exe** and pick **Nubby's Number Factory Client** from the list. Enter your server address/port and slot name like any other Archipelago client.

### 3. Patch your `data.win`

In the client, run:

```
/patch
```

This locates your game install, downloads UndertaleModCli the first time it's needed (a small one-time download from UndertaleModTool's own GitHub releases), backs up your original `data.win`, and applies the AP patch automatically. Close the game first if it's running - `/patch` won't touch `data.win` while it's open. Once it finishes, fully restart the game.

### 4. (Optional) PopTracker

Open [PopTracker](https://github.com/black-sliver/PopTracker), load `poptracker_pack/nnf_poptracker_pack.zip` as a pack, and connect it to the same session for a visual check tracker.

## Advanced: patching manually

If you'd rather not have the client download/run UndertaleModCli for you, you can do the same thing by hand with [UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool):

1. Back up your game's `data.win` (in the game's install folder) somewhere safe.
2. Open your `data.win` in UndertaleModTool and run **Decompile All Code** (or use `UndertaleModCli`'s decompile mode). This produces a folder of `.gml` files - point it somewhere on disk, e.g. `C:\NNF_decompiled\`.
3. Open `gml_patch/PatchApItemLockV30_MASTER.csx` and either set the `NNF_DECOMP_FOLDER` environment variable to that folder, or edit **line 13** directly:
   ```csharp
   string decompFolder = Environment.GetEnvironmentVariable("NNF_DECOMP_FOLDER") ?? @"C:\NNF_decompiled";
   ```
4. Run the script against your own `data.win` with UndertaleModCli:
   ```
   UndertaleModCli.exe load "data.win" -s "PatchApItemLockV30_MASTER.csx" -o "data.win.patched"
   ```
5. Replace the game's `data.win` with `data.win.patched` (keep your backup from step 1 - you'll want it to play vanilla again).

## Notes

- Every AP-specific behavior lives in the client and the patch script above - nothing here modifies or redistributes the base game's assets.
- If items you've received ever seem to go missing (e.g. after a rough reconnect), the client supports `/resync` to force a full re-application of everything you've received.
