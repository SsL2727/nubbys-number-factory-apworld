"""
Locations for Nubby's Number Factory Archipelago Randomizer
V4: Save-native rebuild, items restored. Every location here has a real,
    pollable signal in the game's own save files:

      Win Supervisor N          <- NUBBY_Progression_F.save: SaveSvWinsN increases
      Complete Challenge N      <- NUBBY_Progression_F.save: SaveBeatChallengeN
      Beat Nubby Trials Level N <- NUBBY_NubbyTrials_F.save: SaveBeatNubbyTrialsLvlN
      AP Item Purchase N        <- NUBBY_AutoSave.save: A_BoughtItems (item id appears)

    Zone-clear locations from the original design were dropped: there's no
    persisted save-file trace for them, so a save-native client has no way
    to detect them being completed.
"""

from typing import Dict, NamedTuple
from BaseClasses import LocationProgressType
from .items import ITEM_ITEMS, PERK_ITEMS

BASE_ID = 6_900_000


class NNFLocationData(NamedTuple):
    code: int
    region: str
    progress_type: LocationProgressType = LocationProgressType.DEFAULT


# ── SUPERVISOR WIN LOCATIONS ──────────────────────────────────────────────────
SUPERVISOR_LOCATIONS: Dict[str, NNFLocationData] = {
    f"Win Supervisor {i}": NNFLocationData(
        code=BASE_ID + i,
        region=f"Supervisor {i}",
    )
    for i in range(1, 12)
}

# ── CHALLENGE LOCATIONS ───────────────────────────────────────────────────────
CHALLENGE_LOCATIONS: Dict[str, NNFLocationData] = {
    f"Complete Challenge {i}": NNFLocationData(
        code=BASE_ID + 100 + i,
        region="Challenges",
    )
    for i in range(12)
}

# ── NUBBY TRIALS LOCATIONS ────────────────────────────────────────────────────
NUBBY_TRIALS_LOCATIONS: Dict[str, NNFLocationData] = {
    f"Beat Nubby Trials Level {i}": NNFLocationData(
        code=BASE_ID + 200 + i,
        region="Nubby Trials",
    )
    for i in range(1, 6)
}

# ── AP OFFER PURCHASE LOCATIONS ───────────────────────────────────────────────
# code = BASE_ID + 1000 + internal_item_id (matches A_BoughtItems entries).
# Named generically ("AP Item Purchase N") rather than after the vanilla
# item that lives in that shop slot - a location's name has no required
# relationship to what the multiworld's fill algorithm actually places
# there, and naming it after the slot's own flavor item made AP's send-log
# lines read as if that flavor item were what got sent, which it usually
# isn't. Order is stable (ITEM_ITEMS' definition order), so this is a safe
# rename that doesn't touch any location `code`.
ITEM_PURCHASE_LOCATIONS: Dict[str, NNFLocationData] = {
    f"AP Item Purchase {i}": NNFLocationData(
        code=BASE_ID + 1000 + data.game_id,
        region="Shop",
    )
    for i, (name, data) in enumerate(ITEM_ITEMS.items(), start=1)
}

# ── PERK OBTAINED LOCATIONS ───────────────────────────────────────────────────
# code = BASE_ID + 300 + internal_perk_id. Sent through the same dedicated AP
# Item shop slot as ITEM_PURCHASE_LOCATIONS (not detected via chest pickup -
# picking up a perk in-run never sends a check by itself). Named generically
# for the same reason as ITEM_PURCHASE_LOCATIONS: the location's name isn't a
# preview of what gets sent when it's checked, and "Obtain Perk: X" read as
# if picking up perk X in a chest were what triggered the check.
PERK_LOCATIONS: Dict[str, NNFLocationData] = {
    f"AP Perk Purchase {i}": NNFLocationData(
        code=BASE_ID + 300 + data.perk_id,
        region="Perks",
    )
    for i, (name, data) in enumerate(PERK_ITEMS.items(), start=1)
}

# ── COMBINED LOOKUP ───────────────────────────────────────────────────────────
ALL_LOCATIONS: Dict[str, NNFLocationData] = {
    **SUPERVISOR_LOCATIONS,
    **CHALLENGE_LOCATIONS,
    **NUBBY_TRIALS_LOCATIONS,
    **ITEM_PURCHASE_LOCATIONS,
    **PERK_LOCATIONS,
}

LOCATION_NAME_TO_ID: Dict[str, int] = {
    name: data.code for name, data in ALL_LOCATIONS.items()
}
