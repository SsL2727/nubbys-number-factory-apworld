# Nubby's Number Factory - Archipelago World

An [Archipelago](https://archipelago.gg) randomizer implementation for *Nubby's Number Factory*.

- `nubbys_number_factory/` - the apworld's Python source (items, locations, options, regions, and the in-game client).
- `nubbys_number_factory.apworld` - the built, installable apworld package.

Randomization is save-native: game logic is enforced via a companion Python client and a small set of binary patches applied to the game's own `data.win`, rather than by modifying game files that ship with this repo.
