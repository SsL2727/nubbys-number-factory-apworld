"""
Locations for Nubby's Number Factory Archipelago Randomizer
V4: Save-native rebuild, items restored. Every location here has a real,
    pollable signal in the game's own save files:

      Win Supervisor N          <- NUBBY_Progression_F.save: SaveSvWinsN increases
      Complete Challenge N      <- NUBBY_Progression_F.save: SaveBeatChallengeN
      Beat Nubby Trials Level N <- NUBBY_NubbyTrials_F.save: SaveBeatNubbyTrialsLvlN
      AP Item Purchase N        <- one-shot purchase signal file from the dedicated
                                   AP Item shop slot (NubbyClient.py's
                                   ITEM_PURCHASED_SIGNAL_FILE), not a save-file read

    Zone-clear locations from the original design were dropped: there's no
    persisted save-file trace for them, so a save-native client has no way
    to detect them being completed.
"""

from typing import Dict, NamedTuple
from BaseClasses import LocationProgressType

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

# ── AP SHOP LOCATIONS ─────────────────────────────────────────────────────────
# Rewritten: shops only ever exist on real, decompile-confirmed vanilla shop
# rounds - round % 20 in (5, 10, 19), OR round % 20 == 15 in every OTHER
# 20-round zone (confirmed via obj_TimeLineMGMT's generator in
# scr_CalcWinRound: a switch on round % 20 sets TimeLine[round] = 1 (shop)
# at residues 5/10/15/19, but a later unconditional override -
# "if (round % 40 == 15) TimeLine[round] = 6" - turns the residue-15 shop
# back into a non-shop in zones 1, 3, 5, ... (round % 40 == 15), while
# leaving it alone in zones 2, 4, 6, ... (round % 40 == 35). Over rounds
# 1-80 this yields exactly 14 real shop rounds - 5, 10, 19, 25, 30, 35, 39,
# 45, 50, 59, 65, 70, 75, 79 - matching the user's own enumeration exactly).
# One dedicated AP slot per real shop round, permanently retired once
# bought, exactly like every other shop slot after a purchase - see
# NubbyClient.py's SHOP_ROUNDS/_pick_next_ap_offer. Past round 80 the same
# 20/40-round pattern keeps recurring forever, so any later real shop round
# offers whichever of these 14 hasn't been bought yet (not a 15th+ location -
# there are only ever 14 of these, period, regardless of how long a run
# goes). Generically named per location-naming convention elsewhere in this
# file: a location's name has no required relationship to what the
# multiworld's fill algorithm actually places there - in a randomizer,
# ANY item in the pool (this player's own, or another player's, in a real
# multiworld) can be the reward at any check, shop-based or not.
SHOP_ROUNDS = [5, 10, 19, 25, 30, 35, 39, 45, 50, 59, 65, 70, 75, 79]
SHOP_LOCATIONS: Dict[str, NNFLocationData] = {
    f"AP Item Purchase {i}": NNFLocationData(
        code=BASE_ID + 1000 + i,
        region="Shop",
    )
    for i in range(1, len(SHOP_ROUNDS) + 1)
}

# ── ROUND MILESTONE LOCATIONS ─────────────────────────────────────────────────
# First-time-ever completion of round 5, 10, 15, ... 80 (16 locations).
# code = BASE_ID + 400 + round_number (405-480) - doesn't collide with
# CHALLENGE_LOCATIONS (101-112), NUBBY_TRIALS_LOCATIONS (201-205), or
# SHOP_LOCATIONS (1001-1014). Detected via obj_GAME.RoundMS[] in
# NUBBY_Progression_F.save (lifetime flag, same pattern as SvWins/
# BeatChallenge - see the master GML script's V43 section).
ROUND_MILESTONE_LOCATIONS: Dict[str, NNFLocationData] = {
    f"Reach Round {n}": NNFLocationData(
        code=BASE_ID + 400 + n,
        region="Milestones",
    )
    for n in range(5, 81, 5)
}

# ── RESTOCK MILESTONE LOCATIONS ("points system") ─────────────────────────────
# First-time-ever reaching each restock-count threshold within a single run
# (per-run count - RestockCount itself resets every run). code = BASE_ID + 500
# + index (500-516) - clear of every other range. Detected via
# obj_GAME.RestockMS[] in NUBBY_Progression_F.save, same lifetime-flag
# pattern as ROUND_MILESTONE_LOCATIONS above (see the master GML script's
# V46 section).
RESTOCK_THRESHOLDS = [1, 2, 5, 10, 50, 100, 500, 1000, 2000, 3000, 4000, 5000,
                      6000, 7000, 8000, 9000, 9999]
RESTOCK_MILESTONE_LOCATIONS: Dict[str, NNFLocationData] = {
    f"Reach {n} Restocks": NNFLocationData(
        code=BASE_ID + 500 + i,
        region="Restocks",
    )
    for i, n in enumerate(RESTOCK_THRESHOLDS)
}

# ── POINTS LOCATIONS ("points system", second/additional source alongside ────
# RESTOCK_MILESTONE_LOCATIONS above - both stay active) ───────────────────────
# A single lifetime score (obj_GAME.ApScore) increases every round
# completion by ((round_just_completed - 1) div 5) + 1 - rounds 1-5 award 1
# point each, 6-10 award 2 each, 11-15 award 3 each, and so on - and never
# resets except for a genuinely new AP room (persists across runs). One
# location per integer score value 1..MAX_POINTS_CHECKS (only the first
# NNFOptions.points_check_count of these are actually placed - see
# regions.py). code = BASE_ID + 1200 + i (1200-1699) - clear of every other
# location range, including SHOP_LOCATIONS (1001-1014).
MAX_POINTS_CHECKS = 500
POINTS_LOCATIONS: Dict[str, NNFLocationData] = {
    f"Reach {i} Points": NNFLocationData(
        code=BASE_ID + 1200 + i - 1,
        region="Points",
    )
    for i in range(1, MAX_POINTS_CHECKS + 1)
}

# ── COMBINED LOOKUP ───────────────────────────────────────────────────────────
ALL_LOCATIONS: Dict[str, NNFLocationData] = {
    **SUPERVISOR_LOCATIONS,
    **CHALLENGE_LOCATIONS,
    **NUBBY_TRIALS_LOCATIONS,
    **SHOP_LOCATIONS,
    **ROUND_MILESTONE_LOCATIONS,
    **RESTOCK_MILESTONE_LOCATIONS,
    **POINTS_LOCATIONS,
}

LOCATION_NAME_TO_ID: Dict[str, int] = {
    name: data.code for name, data in ALL_LOCATIONS.items()
}
