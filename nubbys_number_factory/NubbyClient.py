"""
Nubby's Number Factory — Archipelago Client
V3: Save-native rebuild.

The compiled game's own AP hooks (reading connected.flag / incoming_items.txt,
writing checked_locs.txt / victory.flag) turned out to be unverifiable/dead in
the currently installed game build - the client would connect, but nothing in
the game ever reacted. Rather than depend on that, this client talks to the
game entirely through its own JSON save files, which it can read and write
directly with no cooperation required:

  - Receiving items: Supervisor Unlocks and coin/life resources are applied by
    editing NUBBY_Progression_F.save / NUBBY_Progression2_F.save directly.
  - Sending checks: Supervisor wins, Challenge completions, and Nubby Trials
    completions are detected by polling those same save files for state
    changes, and reported to the server as soon as they're seen.
  - Goal completion: tracked the same way, by counting wins across the seed's
    goal supervisors.

On top of that, this client also:
  - Launches the actual game .exe alongside itself (one click from the
    Archipelago Launcher starts both), timed to happen only after the AP
    handshake completes so the game never boots ahead of the connection.
  - Keeps a fully separate save-data slot per AP room (keyed by server
    address/port, disambiguated by seed), so different multiworld sessions
    (and vanilla play) never clobber each other's progress. A brand new room
    starts from a freshly *locked* copy of the save (all supervisor wins /
    challenges / trials cleared) so there's something real to unlock.
  - Takes an automatic timestamped save backup the moment a room goes live,
    plus a one-time permanent backup of whatever save existed before AP
    ever touched it.
"""

from __future__ import annotations
import os
import re
import copy
import json
import time
import random
import shutil
import asyncio
import subprocess

import Utils
from NetUtils import ClientStatus
from CommonClient import (CommonContext, server_loop, gui_enabled,
                          ClientCommandProcessor, get_base_parser)
from Utils import async_start

SAVE_FOLDER = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'NNF_FULLVERSION', 'NubbyAP')
SAVE_DIR = os.path.join(os.environ.get("LOCALAPPDATA", ""), "NNF_FullVersion")

BASE_ID = 6_900_000
POLL_INTERVAL = 2.0

# AP item code -> internal game item id (matches items.py's ITEM_ITEMS game_id
# field). Hardcoded so no import can break it.
_ID_MAP = {
    6902000: 0, 6902001: 2, 6902002: 3, 6902003: 5, 6902004: 9, 6902005: 10,
    6902006: 11, 6902007: 12, 6902008: 14, 6902009: 16, 6902010: 17, 6902011: 21,
    6902012: 22, 6902013: 23, 6902014: 24, 6902015: 30, 6902016: 33, 6902017: 35,
    6902018: 37, 6902019: 40, 6902020: 41, 6902021: 44, 6902022: 48, 6902023: 55,
    6902024: 65, 6902025: 67, 6902026: 149, 6902027: 4, 6902028: 8, 6902029: 20,
    6902030: 29, 6902031: 31, 6902032: 52, 6902033: 53, 6902034: 56, 6902035: 32,
    6902036: 59, 6902037: 6, 6902038: 27, 6902039: 34, 6902040: 43, 6902041: 47,
    6902042: 51, 6902043: 60, 6902044: 66, 6902045: 15, 6902046: 42, 6902047: 49,
    6902048: 54, 6902049: 58, 6902050: 151, 6902051: 7, 6902052: 36, 6902053: 45,
    6902054: 46, 6902055: 50, 6902056: 129, 6902057: 136, 6902058: 148, 6902059: 179,
    6902060: 1, 6902061: 13, 6902062: 18, 6902063: 38, 6902064: 61, 6902065: 137,
    6902066: 138, 6902067: 19, 6902068: 146,
    6902069: 57, 6902070: 26,  # Professor Palmy, Test Item 2 (restored cut content)
}
# Reverse lookup, for recognizing entries in A_BoughtItems as real AP items.
_GAME_ID_TO_AP_ITEM = {v: k for k, v in _ID_MAP.items()}

# game_id -> which of the 3 shops it's flavored for (mirrors items.py's
# ITEM_ITEMS "shop" field - hardcoded here for the same reason as _ID_MAP,
# so the client never depends on importing the apworld package). Used only
# for the per-shop AP-item purchase cap (see SHOP_PURCHASE_CAP below) -
# perks aren't shop-flavored, so they're not part of this lookup at all.
_GAME_ID_TO_SHOP = {
    0: "normal", 2: "normal", 3: "normal", 5: "normal", 9: "normal", 10: "normal",
    11: "normal", 12: "normal", 14: "normal", 16: "normal", 17: "normal", 21: "normal",
    22: "normal", 23: "normal", 24: "normal", 30: "normal", 33: "normal", 35: "normal",
    37: "normal", 40: "normal", 41: "normal", 44: "normal", 48: "normal", 55: "normal",
    65: "normal", 67: "normal", 149: "normal", 4: "normal", 8: "normal", 20: "normal",
    29: "normal", 31: "normal", 52: "normal", 53: "normal", 56: "normal", 32: "normal",
    59: "normal", 57: "normal", 26: "normal",
    6: "black_market", 27: "black_market", 34: "black_market", 43: "black_market",
    47: "black_market", 51: "black_market", 60: "black_market", 66: "black_market",
    15: "black_market", 42: "black_market", 49: "black_market", 54: "black_market",
    58: "black_market", 151: "black_market",
    7: "cafe", 36: "cafe", 45: "cafe", 46: "cafe", 50: "cafe", 129: "cafe",
    136: "cafe", 148: "cafe", 179: "cafe", 1: "cafe", 13: "cafe", 18: "cafe",
    38: "cafe", 61: "cafe", 137: "cafe", 138: "cafe", 19: "cafe", 146: "cafe",
}

# game_id -> AP classification, for the tier-sprite feature (see
# AP_ITEM_TIER_FILE below). Mirrors items.py's ITEM_ITEMS classification
# field - hardcoded, same self-contained reasoning as _ID_MAP/_GAME_ID_TO_SHOP.
# Listed explicitly since they're the minority; everything else defaults
# to "useful" (matches items.py: the vast majority of shop items are).
_FILLER_GAME_IDS = {0, 14, 16, 22, 24, 37, 65, 129, 148}
_PROGRESSION_GAME_IDS = {32, 19, 146}


def _item_classification(game_id):
    if game_id in _PROGRESSION_GAME_IDS:
        return "progression"
    if game_id in _FILLER_GAME_IDS:
        return "filler"
    return "useful"


# Cap on AP item PURCHASES per shop flavor (Normal/Black Market/Cafe each
# get their own count) - the item stays in its single dedicated slot (no
# change to where/how it's offered), this only limits how many times a
# player can buy one per shop before that shop's flavor stops being
# offered through the slot (falls back to the other shops/perks, or a
# real random item if every flavor + perks are exhausted/capped).
SHOP_PURCHASE_CAP = 5

# perk_id -> (display name, AP classification word), matching items.py's
# PERK_ITEMS exactly. Perks are sent through the same AP Item slot as shop
# items (not detected via chest pickups) - "add those checks as more ap
# items in the shop" instead of a separate obtain-from-chest trigger.
_AP_PERK_CATALOG = {
    1: ("Snake Eyes", "useful"), 3: ("Ray Gun", "useful"), 4: ("Speedy", "useful"),
    5: ("Battery", "progression"), 6: ("Cheesy", "useful"), 7: ("Waffle", "useful"),
    8: ("Chaotic", "useful"), 9: ("Zombie", "useful"), 10: ("Springy", "useful"),
    12: ("Mystery Box", "useful"), 13: ("Trophy", "useful"), 14: ("Penny", "progression"),
    15: ("Meaty", "useful"), 16: ("Cubey", "useful"), 17: ("Charity", "useful"),
    18: ("Candle", "useful"), 19: ("Tornado", "useful"), 20: ("Drainer", "useful"),
    21: ("Card Tower", "useful"), 22: ("Eggy", "useful"), 23: ("Buckshot", "useful"),
    24: ("Void", "useful"), 25: ("Enlightened", "progression"), 26: ("Archaic", "useful"),
    27: ("Warlock", "useful"), 28: ("Gourmet", "useful"), 29: ("Lunar", "useful"),
}

# The dedicated "AP Item" shop slot: game_id 181 (obj_I_HairyFingore) is a
# real, standalone item object that's otherwise unused (its "evil" trigger
# effect only fires under global.GameMode == 1, a special mode this
# randomizer never uses, so it's inert if actually bought/held). Buying any
# *regular* item never sends a check anymore - only this one does, cycling
# through the still-unchecked "AP Item Purchase" locations one at a time.
# Purchasing it is a single click (the GML patch writes ITEM_PURCHASED_SIGNAL_FILE
# directly, no board placement, no inventory entry - see _consume_ap_item_purchase_signal.
ITEM_POOL_FILE = os.path.join(SAVE_FOLDER, "ap_item_pool.txt")
AP_ITEM_NAME_FILE = os.path.join(SAVE_FOLDER, "ap_item_name.txt")
ITEM_PURCHASED_SIGNAL_FILE = os.path.join(SAVE_FOLDER, "ap_item_purchased.txt")
# Tier of the currently-offered AP item/perk ("filler"|"useful"|"progression")
# - the game mod picks a distinct sprite+description per tier from this, so
# a player can tell at a glance how important an unchecked AP slot's
# contents are without it revealing exactly what's there. A deliberate,
# explicit-user-requested exception to the earlier "never preview what's at
# an unchecked location" design (see _write_ap_item_name_file) - tier alone
# isn't a full reveal, and the user asked for this specifically.
AP_ITEM_TIER_FILE = os.path.join(SAVE_FOLDER, "ap_item_tier.txt")

# Sphere-based shops (optional): presence of the flag file tells the game
# mod to restrict the Normal Shop's pool at its 14 fixed checkpoint rounds;
# the numbered files list which game_ids belong to each of the 3 Common-
# tier groups (sent once via slot_data on connect - see NNFOptions.sphere_
# based_shops). Rare/Ultra Rare aren't split into their own files - the
# game mod treats "no sphere file matched this round" as its own signal to
# open up everything non-Common instead.
SPHERE_SHOPS_FLAG_FILE = os.path.join(SAVE_FOLDER, "ap_sphere_shops.txt")
SPHERE_ITEM_FILES = {
    1: os.path.join(SAVE_FOLDER, "ap_sphere1_items.txt"),
    2: os.path.join(SAVE_FOLDER, "ap_sphere2_items.txt"),
    3: os.path.join(SAVE_FOLDER, "ap_sphere3_items.txt"),
}

# Zone locks (optional): ZONE_LOCK_FLAG_FILE's presence tells the game mod
# to cap global.CurrentRnd at the last round of the player's currently-
# unlocked zone (20/40/60) instead of advancing past it into Zone 2/3/4;
# each ZONE_UNLOCK_FILES entry signals that specific zone's Zone Unlock
# item has been received. Zone 1 (rounds 1-20) needs no file - it's always
# accessible. See NNFOptions.lock_zones / items.py's ZONE_ITEMS.
ZONE_LOCK_FLAG_FILE = os.path.join(SAVE_FOLDER, "ap_zone_lock.txt")
# Zone 5 needs its own opt-in flag, separate from ZONE_LOCK_FLAG_FILE -
# without it, the GML round-cap check would see ap_zone5_unlocked.txt
# permanently absent (since _queue_next_zone_unlock never populates it
# unless lock_zone5 is on) and treat zone 5 as blocked even for seeds that
# never enabled lock_zone5 at all. Same "presence = restriction, safe
# default when absent" direction as every other flag file here.
ZONE5_LOCK_FLAG_FILE = os.path.join(SAVE_FOLDER, "ap_zone5_lock.txt")
ZONE_UNLOCK_FILES = {
    2: os.path.join(SAVE_FOLDER, "ap_zone2_unlocked.txt"),
    3: os.path.join(SAVE_FOLDER, "ap_zone3_unlocked.txt"),
    4: os.path.join(SAVE_FOLDER, "ap_zone4_unlocked.txt"),
    5: os.path.join(SAVE_FOLDER, "ap_zone5_unlocked.txt"),
}

# Feature locks (optional, one per NNFOptions.lock_* toggle - see
# LOCK_OPTION_TO_ITEM in __init__.py). Each flag file's PRESENCE means the
# feature is currently BLOCKED - written only while that seed's lock_*
# option is on AND the matching Unlock item hasn't arrived yet; absent by
# default (including for anyone who's never touched AP at all - no
# NubbyAP folder, no files, nothing blocked, exactly vanilla behavior).
# Deliberately the same "presence = restriction" direction as every other
# flag file in this module (SPHERE_SHOPS_FLAG_FILE, ZONE_LOCK_FLAG_FILE,
# ITEM_POOL_FILE, ...) rather than "presence = allowed" - that inverted
# design was tried first and rejected: it would leave every feature
# blocked by default for a player who's never connected to AP at all,
# since the flag files (and the whole NubbyAP folder) wouldn't exist yet.
# Challenges is deliberately absent here - it reuses the game's own
# obj_GAME.U_ChallengeMode save flag (same mechanism as Supervisors/Tony),
# not a GML file-check patch, so it has no flag file of its own.
FEATURE_LOCK_FLAG_FILES = {
    "grabatron":    os.path.join(SAVE_FOLDER, "ap_grabatron_blocked.txt"),
    "black_market": os.path.join(SAVE_FOLDER, "ap_blackmarket_blocked.txt"),
    "cafe":         os.path.join(SAVE_FOLDER, "ap_cafe_blocked.txt"),
    "nubby_trials": os.path.join(SAVE_FOLDER, "ap_nubbytrials_blocked.txt"),
    "freeze":       os.path.join(SAVE_FOLDER, "ap_freeze_blocked.txt"),
}
# slot_data key -> which FEATURE_LOCK_FLAG_FILES entry it controls.
FEATURE_LOCK_OPTION_KEYS = {
    "lock_grabatron":      "grabatron",
    "lock_black_market":   "black_market",
    "lock_cafe_nubby":     "cafe",
    "lock_nubby_trials":   "nubby_trials",
    "lock_freeze_ability": "freeze",
}

# AP item code -> feature key, for the ReceivedItems handler.
FEATURE_UNLOCK_AP_IDS = {
    BASE_ID + 6000: "grabatron",
    BASE_ID + 6001: "black_market",
    BASE_ID + 6002: "cafe",
    BASE_ID + 6003: "nubby_trials",
    BASE_ID + 6005: "freeze",
}

# Custom final round (optional): presence/content tells the game mod to
# relocate the boss fight + win condition from vanilla round 80 to a
# different round, and turn round 80 into a normal special event instead.
CUSTOM_FINAL_ROUND_FILE = os.path.join(SAVE_FOLDER, "ap_final_round.txt")

# Score goal (optional): target total accumulated across every run played
# in this room (the game's own per-run AllPoints, summed - AllPoints itself
# resets every run, so the client tracks a running cumulative total in
# score_state.json per room and only ever ADDS the delta when AllPoints
# goes up within the same run, never subtracts when a new run resets it).
SCORE_STATE_FILE_NAME = "score_state.json"

# Traps (optional, see NNFOptions.include_traps): a simple append-only
# queue, one trap-type code per line. The game mod polls this file
# periodically (same throttled-timer pattern as the item-pool refresh),
# applies every pending trap in order, then clears it - a queue rather than
# a single-slot signal so a burst of several traps received close together
# can't overwrite/lose each other before the game gets a chance to read them.
TRAP_QUEUE_FILE = os.path.join(SAVE_FOLDER, "ap_traps_pending.txt")
TRAP_AP_ID_TO_CODE = {
    BASE_ID + 7000: "item_steal",
    BASE_ID + 7001: "coin_theft",
    BASE_ID + 7002: "near_death",
    BASE_ID + 7003: "item_jam",
    BASE_ID + 7004: "chaos_event",
}

# In-game connection marker + check log. CONNECTION_INFO_FILE mirrors
# connected.flag's lifecycle exactly (written alongside it in the
# Connected handler, removed alongside it in disconnect()) but carries
# human-readable text for display instead of being a pure boolean. AP_LOG_
# FILE accumulates recent "ItemSend" PrintJSON lines (both sent and
# received checks - see on_print_json) for the game mod to show in an
# in-game log; capped at AP_LOG_MAX_LINES and reset on an actual room
# switch (not a same-room reconnect) in activate_room_save, not on every
# disconnect - a brief network blip shouldn't wipe visible history.
CONNECTION_INFO_FILE = os.path.join(SAVE_FOLDER, "ap_connection_info.txt")
AP_LOG_FILE = os.path.join(SAVE_FOLDER, "ap_log.txt")
AP_LOG_MAX_LINES = 40

# Perks: obj_PerkMGMT.InPerkItemPool works exactly like InItemPool but as a
# single 0/1 pool (perks aren't split across 3 shops). AP code -> internal
# perk id is a flat offset (code = BASE_ID + 5000 + perk_id), so no lookup
# table is needed. Locked/unlocked the same way as items, via a file the
# game mod reads. Matches items.py's PERK_ITEMS exactly.
PERK_ID_SET = {1, 3, 4, 5, 6, 7, 8, 9, 10, 12, 13, 14, 15, 16, 17, 18, 19,
                20, 21, 22, 23, 24, 25, 26, 27, 28, 29}
PERK_POOL_FILE = os.path.join(SAVE_FOLDER, "ap_perk_pool.txt")

# Permanent starting-money/starting-lives bonuses (filler items). These
# accumulate per room and get re-applied at the start of every run by the
# game mod - distinct from per-run A_Money/A_Lives, which reset normally.
BONUS_FILE = os.path.join(SAVE_FOLDER, "ap_bonus.txt")


def _read_gm_json(path):
    """Read a GameMaker buffer_string JSON save file (null-terminated string)."""
    with open(path, "rb") as f:
        data = f.read()
    if data and data[-1] == 0:
        data = data[:-1]
    return json.loads(data.decode("utf-8"))


def _write_gm_json(path, obj):
    """Write a GameMaker buffer_string JSON save file."""
    s = json.dumps(obj, separators=(',', ':'))
    with open(path, "wb") as f:
        f.write(s.encode("utf-8"))
        f.write(b"\x00")  # null terminator


def deliver_supervisor(sv_index):
    """Unlock a supervisor in NUBBY_Progression_F.save."""
    path = os.path.join(SAVE_DIR, "NUBBY_Progression_F.save")
    try:
        data = _read_gm_json(path)
        data[0][f"SaveU_SV{sv_index}"] = 1
        _write_gm_json(path, data)
        print(f"[NubbyAP] Unlocked supervisor {sv_index} in save file")
    except Exception as e:
        print(f"[NubbyAP] deliver_supervisor failed: {e}")


TONY_SKIN_INDEX = 15  # NubbySkinId[15] == "tonyfan", per obj_WarehouseMGMT_Create_0


def deliver_tony_skin():
    """Unlock the Tony (tonyfan) Nubby skin in NUBBY_Progression3_F.save."""
    path = os.path.join(SAVE_DIR, "NUBBY_Progression3_F.save")
    try:
        data = _read_gm_json(path)
        data[0][f"SaveU_Cosmo_NubbySkin{TONY_SKIN_INDEX}"] = 1
        _write_gm_json(path, data)
        print("[NubbyAP] Unlocked Tony skin in save file")
    except Exception as e:
        print(f"[NubbyAP] deliver_tony_skin failed: {e}")


def _queue_unlocked_item(room_dir, game_id):
    """
    Records a received shop item as unlocked for this room, and refreshes
    ap_item_pool.txt so the game mod picks it up (checked both at boot and
    every ~3s during play, so this reaches the shop without a restart).
    """
    path = os.path.join(room_dir, "unlocked_pool.json")
    pool = _read_json(path, default=[])
    if game_id not in pool:
        pool.append(game_id)
        _write_json(path, pool)
    _write_item_pool_file(pool)
    print(f"[NubbyAP] game_id={game_id} unlocked - back in its shop's pool "
          f"(takes effect next game launch)")


def _write_item_pool_file(unlocked_game_ids):
    with open(ITEM_POOL_FILE, "w") as f:
        for game_id in unlocked_game_ids:
            f.write(f"{game_id}\n")


def _queue_unlocked_perk(room_dir, perk_id):
    """Same idea as _queue_unlocked_item, for obj_PerkMGMT.InPerkItemPool."""
    path = os.path.join(room_dir, "unlocked_perks.json")
    pool = _read_json(path, default=[])
    if perk_id not in pool:
        pool.append(perk_id)
        _write_json(path, pool)
    _write_perk_pool_file(pool)
    print(f"[NubbyAP] perk_id={perk_id} unlocked")


def _write_perk_pool_file(unlocked_perk_ids):
    with open(PERK_POOL_FILE, "w") as f:
        for perk_id in unlocked_perk_ids:
            f.write(f"{perk_id}\n")


def _write_zone_unlock_files(unlocked_zones):
    for zone_num, path in ZONE_UNLOCK_FILES.items():
        if zone_num in unlocked_zones:
            open(path, "w").close()
        elif os.path.exists(path):
            os.remove(path)


def _queue_next_zone_unlock(room_dir, include_zone5):
    """Progressive Zone Unlock: each copy received opens whichever locked
    zone is next in sequence (2, then 3, then 4, then 5 if lock_zone5 is
    also on) - order tracked purely by which zones are already in
    unlocked_zones.json, no separate counter needed."""
    sequence = [2, 3, 4, 5] if include_zone5 else [2, 3, 4]
    path = os.path.join(room_dir, "unlocked_zones.json")
    zones = _read_json(path, default=[])
    for z in sequence:
        if z not in zones:
            zones.append(z)
            _write_json(path, zones)
            _write_zone_unlock_files(zones)
            print(f"[NubbyAP] Zone {z} unlocked (progressive)")
            return
    print("[NubbyAP] Progressive Zone Unlock received but every zone is already unlocked")


def _queue_unlocked_feature(room_dir, feature_key):
    """Records a received feature-Unlock item (Grab-A-Tron/Black Market/
    Cafe/Nubby Trials/Freeze Ability) as unlocked for this room, and clears
    its blocked-flag file so the game mod stops blocking it."""
    path = os.path.join(room_dir, "unlocked_features.json")
    features = _read_json(path, default=[])
    if feature_key not in features:
        features.append(feature_key)
        _write_json(path, features)
    blocked_path = FEATURE_LOCK_FLAG_FILES[feature_key]
    if os.path.exists(blocked_path):
        os.remove(blocked_path)
    print(f"[NubbyAP] Feature '{feature_key}' unlocked")


def _write_feature_lock_flags(slot_data, unlocked_features):
    """Writes (or clears) each feature's blocked-flag: blocked only if
    that seed enabled the corresponding lock_* option AND the matching
    Unlock item isn't in unlocked_features for this room yet."""
    for opt_key, feature_key in FEATURE_LOCK_OPTION_KEYS.items():
        path = FEATURE_LOCK_FLAG_FILES[feature_key]
        blocked = bool(slot_data.get(opt_key)) and feature_key not in unlocked_features
        if blocked:
            open(path, "w").close()
        elif os.path.exists(path):
            os.remove(path)


def deliver_challenges_unlock():
    """Unlock Challenge mode as a whole (obj_GAME.U_ChallengeMode) in the
    save file - same direct-edit mechanism as deliver_supervisor/
    deliver_tony_skin, since the game already has its own persisted flag
    for this rather than needing a new GML file-check patch."""
    path = os.path.join(SAVE_DIR, "NUBBY_Progression_F.save")
    try:
        data = _read_gm_json(path)
        data[0]["SaveU_ChallengeMode"] = 1
        _write_gm_json(path, data)
        print("[NubbyAP] Unlocked Challenge mode in save file")
    except Exception as e:
        print(f"[NubbyAP] deliver_challenges_unlock failed: {e}")


def _queue_trap(trap_code):
    """Appends a trap-type code to the pending-trap queue file (see
    TRAP_QUEUE_FILE) - the game mod applies and clears it on its own
    schedule, so this never overwrites an earlier still-unread trap."""
    with open(TRAP_QUEUE_FILE, "a") as f:
        f.write(trap_code + "\n")
    print(f"[NubbyAP] Trap queued: {trap_code}")


def _queue_bonus(room_dir, kind, amount):
    """Accumulate a permanent starting-money/starting-lives bonus for this room."""
    path = os.path.join(room_dir, "bonus_totals.json")
    totals = _read_json(path, default={"money": 0, "lives": 0})
    totals[kind] = totals.get(kind, 0) + amount
    _write_json(path, totals)
    _write_bonus_file(totals)
    print(f"[NubbyAP] Permanent {kind} bonus -> {totals[kind]}")


def _write_bonus_file(totals):
    with open(BONUS_FILE, "w") as f:
        f.write(f"money={totals.get('money', 0)}\n")
        f.write(f"lives={totals.get('lives', 0)}\n")


# ── Per-room save isolation + backups ───────────────────────────────────────

PROGRESS_SAVE_FILES = [
    "NUBBY_AutoSave_F.save",
    "NUBBY_NubbyTrials_F.save",
    "NUBBY_Progression_F.save",
    "NUBBY_Progression2_F.save",
    "NUBBY_Progression3_F.save",
    "NUBBY_Score_F.save",
]

SLOTS_DIR = os.path.join(SAVE_DIR, "AP_SaveSlots")
VANILLA_DIR = os.path.join(SLOTS_DIR, "_vanilla")
ACTIVE_ROOM_FILE = os.path.join(SLOTS_DIR, "active_room.json")
MAX_BACKUPS_PER_ROOM = 5


def _sanitize(name: str) -> str:
    return re.sub(r'[^A-Za-z0-9._-]+', '_', name or "").strip('_') or "unknown"


def _copy_save_files(src_dir, dst_dir):
    """
    Mirrors PROGRESS_SAVE_FILES from src_dir into dst_dir - including
    clearing a dst file that doesn't exist in src. That matters most for
    NUBBY_AutoSave_F.save: a fresh room legitimately has no such file (no
    run in progress), and loading that "absence" into the live save has to
    actually delete whatever run was sitting there, not just leave it.
    """
    os.makedirs(dst_dir, exist_ok=True)
    copied = []
    for fname in PROGRESS_SAVE_FILES:
        src = os.path.join(src_dir, fname)
        dst = os.path.join(dst_dir, fname)
        if os.path.exists(src):
            shutil.copy2(src, dst)
            copied.append(fname)
        elif os.path.exists(dst):
            os.remove(dst)
    return copied


def _read_json(path, default=None):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default


def _write_json(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2)


def ensure_vanilla_backup():
    """One-time, permanent snapshot of the save as it was before AP ever touched it."""
    marker = os.path.join(VANILLA_DIR, "manifest.json")
    if os.path.exists(marker):
        return
    copied = _copy_save_files(SAVE_DIR, VANILLA_DIR)
    _write_json(marker, {"captured": time.strftime("%Y-%m-%d %H:%M:%S"), "files": copied})
    print(f"[NubbyAP] Captured one-time vanilla save backup -> {VANILLA_DIR}")


def _room_key(server_address: str) -> str:
    host_port = re.sub(r'^\w+://', '', server_address or "unknown_host")
    return _sanitize(host_port)


def _resolve_room_dir(server_address, seed_name, slot_name):
    """
    Find (or create) the room folder for this seed. Rooms are keyed primarily
    by host:port for readability, but disambiguated by seed_name/slot so a
    reused port pointing at a different generated multiworld doesn't collide
    with the old one.
    """
    base_key = _room_key(server_address)
    candidate = os.path.join(SLOTS_DIR, base_key)
    suffix = 1
    while True:
        manifest = _read_json(os.path.join(candidate, "manifest.json"))
        if manifest is None:
            return candidate, {
                "server_address": server_address,
                "seed_name": seed_name,
                "slot": slot_name,
                "created": time.strftime("%Y-%m-%d %H:%M:%S"),
            }
        if manifest.get("seed_name") == seed_name and manifest.get("slot") == slot_name:
            return candidate, manifest
        suffix += 1
        candidate = os.path.join(SLOTS_DIR, f"{base_key}_{suffix}")


def _prune_backups(backups_dir):
    if not os.path.isdir(backups_dir):
        return
    entries = sorted(d for d in os.listdir(backups_dir)
                      if os.path.isdir(os.path.join(backups_dir, d)))
    while len(entries) > MAX_BACKUPS_PER_ROOM:
        victim = entries.pop(0)
        try:
            shutil.rmtree(os.path.join(backups_dir, victim))
        except Exception:
            pass


def _active_room_dir():
    return _read_json(ACTIVE_ROOM_FILE, default={}).get("room_dir")


def _item_index_path():
    room_dir = _active_room_dir()
    if room_dir:
        return os.path.join(room_dir, "item_index.txt")
    return os.path.join(SAVE_FOLDER, "item_index.txt")  # legacy fallback


def _lock_progression_for_fresh_room(room_save_dir, lock_challenges=False):
    """
    Zero out every win/beaten flag this randomizer cares about in a brand new
    room's save copy, so there's something real for AP to unlock. Starting
    Supervisors (if any) arrive moments later via the normal ReceivedItems
    delivery, same as any other received item.
    """
    prog_path = os.path.join(room_save_dir, "NUBBY_Progression_F.save")
    try:
        data = _read_gm_json(prog_path)
        p0 = data[0]
        # SV0 is "Tony" - the base/default supervisor, hardcoded unlocked
        # in vanilla (obj_GAME_Create_0: U_SV[0] = 1) and never part of the
        # randomized SV1-11 pool. Not currently obtainable through AP -
        # this only locks it, it does not add an unlock path.
        p0["SaveU_SV0"] = 0
        for i in range(1, 12):
            p0[f"SaveU_SV{i}"] = 0
            p0[f"SaveSvWins{i}"] = 0
        for i in range(12):
            p0[f"SaveBeatChallenge{i}"] = 0
        # Challenge mode as a whole (see NNFOptions.lock_challenges) - reuses
        # the game's own obj_GAME.U_ChallengeMode flag, same direct-edit
        # mechanism as SV0/Tony above, only applied when that option is on
        # (Challenges stay normally-accessible otherwise, matching vanilla).
        if lock_challenges:
            p0["SaveU_ChallengeMode"] = 0
        _write_gm_json(prog_path, data)
    except Exception as e:
        print(f"[NubbyAP] Failed to lock Progression save for fresh room: {e}")

    trials_path = os.path.join(room_save_dir, "NUBBY_NubbyTrials_F.save")
    try:
        data = _read_gm_json(trials_path)
        t0 = data[0]
        for i in range(1, 6):
            t0[f"SaveBeatNubbyTrialsLvl{i}"] = 0
            t0[f"SaveNubbyTrialsWinsLvl{i}"] = 0
        _write_gm_json(trials_path, data)
    except Exception as e:
        print(f"[NubbyAP] Failed to lock NubbyTrials save for fresh room: {e}")

    # Stars are out of AP entirely for this randomizer - start at 0, and
    # nothing in the item pool ever grants more (see items.py FILLER_ITEMS).
    stars_path = os.path.join(room_save_dir, "NUBBY_Progression2_F.save")
    try:
        data = _read_gm_json(stars_path)
        data[0]["SaveStars"] = 0
        _write_gm_json(stars_path, data)
    except Exception as e:
        print(f"[NubbyAP] Failed to zero stars for fresh room: {e}")

    # Tony (NubbySkinId 15) is unlocked by default in vanilla - lock it so
    # it has to be found like any other randomized item. Locking the unlock
    # flag alone isn't enough: SaveSelectedNubbySkin (which skin is
    # *currently equipped*) is a separate field, and if it was already
    # "tonyfan" (e.g. inherited from a vanilla save), Nubby keeps visibly
    # wearing it regardless of the lock - the lock only blocks re-selecting
    # it in the Warehouse. Reset the equipped skin to "default" too.
    prog3_path = os.path.join(room_save_dir, "NUBBY_Progression3_F.save")
    try:
        data = _read_gm_json(prog3_path)
        data[0][f"SaveU_Cosmo_NubbySkin{TONY_SKIN_INDEX}"] = 0
        if data[0].get("SaveSelectedNubbySkin") == "tonyfan":
            data[0]["SaveSelectedNubbySkin"] = "default"
        _write_gm_json(prog3_path, data)
    except Exception as e:
        print(f"[NubbyAP] Failed to lock Tony skin for fresh room: {e}")

    # NUBBY_AutoSave_F.save is the game's own "run in progress, resume this"
    # file - the exact same one its own New Game button deletes before
    # starting fresh (confirmed by decompiling obj_GONewGameBtn). Doing the
    # same thing here means a new AP room boots into a genuinely new run
    # instead of picking back up wherever a previous run left off.
    auto_path = os.path.join(room_save_dir, "NUBBY_AutoSave_F.save")
    if os.path.exists(auto_path):
        os.remove(auto_path)


def activate_room_save(server_address, seed_name, slot_name, slot_data=None):
    """
    Swap the live save files to this room's private save set, isolating this
    AP session's progress from every other room (and from vanilla play).
    Safe to call on every "Connected" package, including reconnects to a
    room that's already active. slot_data (if given) is only used for the
    fresh-room lock decision (Challenges) - everything else slot_data-driven
    is written separately by the caller, after this returns.
    """
    slot_data = slot_data or {}
    os.makedirs(SLOTS_DIR, exist_ok=True)
    ensure_vanilla_backup()

    room_dir, manifest = _resolve_room_dir(server_address, seed_name, slot_name)
    room_save_dir = os.path.join(room_dir, "save")
    is_new_room = not os.path.isdir(room_save_dir)

    previous_room_dir = _active_room_dir()
    switching_rooms = previous_room_dir != room_dir

    if switching_rooms:
        # A genuinely different room's history shouldn't linger in the
        # in-game check log - but a same-room reconnect (the else branch
        # below) leaves it alone, since that's just a network blip. Wrapped
        # on its own: this is display-only housekeeping, and a failure here
        # (e.g. a transient Windows file-lock if the game happens to have
        # the file open for reading at this exact instant) must never abort
        # the rest of this function - everything below it, including the
        # item/perk-pool file writes the whole shop depends on, is far more
        # important than clearing a log.
        try:
            if os.path.exists(AP_LOG_FILE):
                os.remove(AP_LOG_FILE)
        except Exception as e:
            print(f"[NubbyAP] Failed to clear AP_LOG_FILE (non-fatal): {e}")

        if previous_room_dir and os.path.isdir(previous_room_dir):
            _copy_save_files(SAVE_DIR, os.path.join(previous_room_dir, "save"))

        if is_new_room:
            _copy_save_files(VANILLA_DIR, room_save_dir)
            _lock_progression_for_fresh_room(room_save_dir, lock_challenges=bool(slot_data.get("lock_challenges")))
            _write_json(os.path.join(room_dir, "manifest.json"), manifest)
            print(f"[NubbyAP] New AP room -> created isolated, freshly-locked save slot at {room_dir}")
        else:
            print(f"[NubbyAP] Switching to known AP room -> restoring save slot at {room_dir}")

        _copy_save_files(room_save_dir, SAVE_DIR)
    else:
        # Same room reconnecting (e.g. a network blip) - the live save is
        # already current, just make sure the room slot reflects it.
        _copy_save_files(SAVE_DIR, room_save_dir)
        print(f"[NubbyAP] Reconnected to already-active room {room_dir}")

    backups_dir = os.path.join(room_dir, "backups")
    snapshot_dir = os.path.join(backups_dir, time.strftime("%Y-%m-%d_%H%M%S"))
    _copy_save_files(SAVE_DIR, snapshot_dir)
    _prune_backups(backups_dir)

    _write_json(ACTIVE_ROOM_FILE, {"room_dir": room_dir, "seed_name": seed_name, "slot": slot_name})

    # Make sure the item/perk-pool and bonus files the game mod reads match
    # whichever room is active now, not whatever a previous room left behind.
    _write_item_pool_file(_read_json(os.path.join(room_dir, "unlocked_pool.json"), default=[]))
    _write_perk_pool_file(_read_json(os.path.join(room_dir, "unlocked_perks.json"), default=[]))
    _write_bonus_file(_read_json(os.path.join(room_dir, "bonus_totals.json"), default={"money": 0, "lives": 0}))
    _write_zone_unlock_files(_read_json(os.path.join(room_dir, "unlocked_zones.json"), default=[]))
    _write_feature_lock_flags(slot_data, _read_json(os.path.join(room_dir, "unlocked_features.json"), default=[]))

    # A leftover purchase signal from whatever room was active before this
    # swap must not be replayed as a check against the newly-activated
    # room's (different) current offer on the very next poll.
    if os.path.exists(ITEM_PURCHASED_SIGNAL_FILE):
        os.remove(ITEM_PURCHASED_SIGNAL_FILE)

    return room_dir


# ── Save-file polling: detecting checks with no game cooperation ───────────

def _read_progress_snapshot():
    """Everything the check-poller cares about, read straight from disk."""
    try:
        prog = _read_gm_json(os.path.join(SAVE_DIR, "NUBBY_Progression_F.save"))
        trials = _read_gm_json(os.path.join(SAVE_DIR, "NUBBY_NubbyTrials_F.save"))
    except Exception as e:
        print(f"[NubbyAP] progress snapshot read failed: {e}")
        return None

    p0 = prog[0] if prog else {}
    t0 = trials[0] if trials else {}
    snap = {}
    for i in range(1, 12):
        snap[f"sv_wins_{i}"] = float(p0.get(f"SaveSvWins{i}", 0) or 0)
    for i in range(12):
        snap[f"challenge_{i}"] = bool(p0.get(f"SaveBeatChallenge{i}", False))
    for i in range(1, 6):
        snap[f"trial_{i}"] = bool(t0.get(f"SaveBeatNubbyTrialsLvl{i}", False))
    return snap


def _read_current_all_points():
    """The live run's AllPoints (A_AllPoints in NUBBY_AutoSave_F.save) -
    0 if no run is currently in progress (file absent between runs).
    Separate from _read_progress_snapshot because this value resets every
    run and needs delta-only-on-increase handling, not baseline-diffing."""
    try:
        data = _read_gm_json(os.path.join(SAVE_DIR, "NUBBY_AutoSave_F.save"))
        return float(data[0].get("A_AllPoints", 0) or 0)
    except Exception:
        return 0.0


def _update_cumulative_score(room_dir):
    """Tracks a running cumulative score total across every run played in
    this room (see NNFOptions.score_goal) - AllPoints itself resets every
    run, so only INCREASES within the same run are added to the total; a
    decrease (a new run started) just resyncs the tracker without
    subtracting. Returns the updated cumulative total."""
    path = os.path.join(room_dir, SCORE_STATE_FILE_NAME)
    state = _read_json(path, default={"last_all_points": 0.0, "cumulative_score": 0.0})
    current = _read_current_all_points()
    if current > state["last_all_points"]:
        state["cumulative_score"] += current - state["last_all_points"]
    state["last_all_points"] = current
    _write_json(path, state)
    return state["cumulative_score"]


def _append_ap_log_line(text):
    """Appends one line to the in-game check log, capped at AP_LOG_MAX_LINES
    (oldest dropped first) so the game mod never has to read an unbounded
    file every refresh."""
    lines = []
    if os.path.exists(AP_LOG_FILE):
        with open(AP_LOG_FILE, "r", encoding="utf-8", errors="ignore") as f:
            lines = [ln.rstrip("\n") for ln in f.readlines()]
    lines.append(text)
    lines = lines[-AP_LOG_MAX_LINES:]
    with open(AP_LOG_FILE, "w", encoding="utf-8") as f:
        for ln in lines:
            f.write(ln + "\n")


def _write_sphere_files(slot_data):
    """
    Writes (or clears) the sphere-based-shops files from slot_data, once per
    connection - see NNFOptions.sphere_based_shops. slot_data's dict keys
    arrive as strings over the wire (JSON), so sphere numbers are looked up
    as str(n).
    """
    if not slot_data.get("sphere_based_shops"):
        for f in (SPHERE_SHOPS_FLAG_FILE, *SPHERE_ITEM_FILES.values()):
            if os.path.exists(f):
                os.remove(f)
        return

    open(SPHERE_SHOPS_FLAG_FILE, "w").close()
    sphere_items = slot_data.get("sphere_items", {})
    for sphere_num, path in SPHERE_ITEM_FILES.items():
        game_ids = sphere_items.get(str(sphere_num), [])
        with open(path, "w") as f:
            for game_id in game_ids:
                f.write(f"{game_id}\n")


def _write_zone_lock_flag(slot_data):
    """Writes (or clears) the zone-lock flag files from slot_data, once
    per connection - see NNFOptions.lock_zones / lock_zone5."""
    if slot_data.get("lock_zones"):
        open(ZONE_LOCK_FLAG_FILE, "w").close()
    elif os.path.exists(ZONE_LOCK_FLAG_FILE):
        os.remove(ZONE_LOCK_FLAG_FILE)
    if slot_data.get("lock_zone5"):
        open(ZONE5_LOCK_FLAG_FILE, "w").close()
    elif os.path.exists(ZONE5_LOCK_FLAG_FILE):
        os.remove(ZONE5_LOCK_FLAG_FILE)


def _write_custom_final_round_file(slot_data):
    """Writes (or clears) the custom-final-round file from slot_data - see
    NNFOptions.custom_final_round. Presence + content tells the game mod
    which round to move the boss/win condition to instead of vanilla 80."""
    custom_round = slot_data.get("custom_final_round", 0)
    if custom_round:
        with open(CUSTOM_FINAL_ROUND_FILE, "w") as f:
            f.write(str(custom_round))
    elif os.path.exists(CUSTOM_FINAL_ROUND_FILE):
        os.remove(CUSTOM_FINAL_ROUND_FILE)


def _offer_location_code(offer):
    """None if offer is missing/malformed (e.g. a leftover file in the old
    {"game_id": X} shape from before items/perks shared this rotation) - the
    caller treats that as "needs a fresh pick" rather than crashing."""
    if not isinstance(offer, dict) or "id" not in offer:
        return None
    if offer.get("kind") == "perk":
        return BASE_ID + 300 + offer["id"]
    return BASE_ID + 1000 + offer["id"]


def _write_ap_item_name_file(offer):
    """
    Label the dedicated AP Item slot. The name/description text itself
    stays deliberately generic - Archipelago never reveals what's
    actually at an unchecked location (this player's own or anyone
    else's) until it's checked, so naming the specific item would be a
    false preview. Tier (filler/useful/progression) is the one exception,
    written alongside to AP_ITEM_TIER_FILE - explicitly requested by the
    user despite the above, and arguably not a full reveal the same way
    a name would be.
    """
    if _offer_location_code(offer) is None:
        if os.path.exists(AP_ITEM_NAME_FILE):
            os.remove(AP_ITEM_NAME_FILE)
        if os.path.exists(AP_ITEM_TIER_FILE):
            os.remove(AP_ITEM_TIER_FILE)
        return
    with open(AP_ITEM_NAME_FILE, "w") as f:
        f.write("AP: Archipelago Check\n")

    if offer.get("kind") == "perk":
        tier = _AP_PERK_CATALOG.get(offer.get("id"), (None, "useful"))[1]
    else:
        tier = _item_classification(offer.get("id"))
    with open(AP_ITEM_TIER_FILE, "w") as f:
        f.write(tier + "\n")


def _pick_next_ap_offer(room_dir, ctx):
    """
    Picks (and persists) which still-unchecked location - a shop item
    purchase or a perk - the AP Item slot currently represents. Stable
    until it's actually bought - repeated picks return the same one
    instead of shuffling. Both items and perks are sent through this same
    slot rather than items being bought normally and perks being detected
    via chest pickups.
    """
    offer_path = os.path.join(room_dir, "next_ap_offer.json")
    offer = _read_json(offer_path, default=None)
    checked = ctx.locations_checked or set()

    code = _offer_location_code(offer)
    if code is not None and code not in checked:
        return offer

    purchase_counts = _read_json(os.path.join(room_dir, "shop_purchase_counts.json"), default={})
    candidates = (
        [{"kind": "item", "id": g} for g in _GAME_ID_TO_AP_ITEM]
        + [{"kind": "perk", "id": p} for p in _AP_PERK_CATALOG]
    )
    candidates = [c for c in candidates if _offer_location_code(c) not in checked]
    candidates = [
        c for c in candidates
        if c["kind"] != "item"
        or purchase_counts.get(_GAME_ID_TO_SHOP.get(c["id"], ""), 0) < SHOP_PURCHASE_CAP
    ]
    if not candidates:
        _write_json(offer_path, {})
        _write_ap_item_name_file(None)
        return None

    offer = random.choice(candidates)
    _write_json(offer_path, offer)
    _write_ap_item_name_file(offer)
    return offer


def _consume_ap_item_purchase_signal(room_dir):
    """
    Detects a click-purchase of the AP Item via a one-shot signal file the
    game writes directly on click (single-click buy now - see
    obj_ItemOfferCell_Step_0's V11 patch - not a drag-into-inventory
    placement, since it isn't a real held item). Presence of the file means
    exactly one purchase happened since the last poll; it's deleted
    immediately so it can't be double-counted or replayed from a stale
    leftover after a room swap.
    """
    if not os.path.exists(ITEM_PURCHASED_SIGNAL_FILE):
        return []
    try:
        os.remove(ITEM_PURCHASED_SIGNAL_FILE)
    except OSError:
        pass

    offer = _read_json(os.path.join(room_dir, "next_ap_offer.json"), default=None)
    offer_code = _offer_location_code(offer)
    if offer_code is None:
        return []

    # Count this purchase against its shop's cap (see SHOP_PURCHASE_CAP) -
    # perks aren't shop-flavored, so only "item" offers count here.
    if isinstance(offer, dict) and offer.get("kind") == "item":
        shop = _GAME_ID_TO_SHOP.get(offer.get("id"))
        if shop:
            counts_path = os.path.join(room_dir, "shop_purchase_counts.json")
            counts = _read_json(counts_path, default={})
            counts[shop] = counts.get(shop, 0) + 1
            _write_json(counts_path, counts)
            print(f"[NubbyAP] {shop} shop AP purchases: {counts[shop]}/{SHOP_PURCHASE_CAP}")

    return [offer_code]


# ── Launching the game alongside the client ─────────────────────────────────

GAME_EXE_NAME = "NNF_FULLVERSION.exe"
GAME_PATH_CONFIG = os.path.join(SAVE_FOLDER, "game_path.txt")


def _steam_library_roots():
    default_steam = os.path.join(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"), "Steam")
    roots = [default_steam]
    vdf_path = os.path.join(default_steam, "steamapps", "libraryfolders.vdf")
    try:
        with open(vdf_path, "r", encoding="utf-8", errors="ignore") as f:
            content = f.read()
        for m in re.finditer(r'"path"\s*"([^"]+)"', content):
            roots.append(m.group(1).replace("\\\\", "\\"))
    except Exception:
        pass
    return roots


def _remember_game_exe(path):
    try:
        os.makedirs(os.path.dirname(GAME_PATH_CONFIG), exist_ok=True)
        with open(GAME_PATH_CONFIG, "w", encoding="utf-8") as f:
            f.write(path)
    except Exception:
        pass


def _find_game_exe():
    if os.path.exists(GAME_PATH_CONFIG):
        try:
            saved = open(GAME_PATH_CONFIG, "r", encoding="utf-8").read().strip()
        except Exception:
            saved = ""
        if saved and os.path.isfile(saved):
            return saved

    for root in _steam_library_roots():
        candidate = os.path.join(root, "steamapps", "common", "Nubby's Number Factory", GAME_EXE_NAME)
        if os.path.isfile(candidate):
            _remember_game_exe(candidate)
            return candidate

    try:
        import tkinter as tk
        from tkinter import filedialog, messagebox
        root = tk.Tk()
        root.withdraw()
        messagebox.showinfo(
            "Nubby's Number Factory",
            "Couldn't auto-detect the game install.\nPlease locate NNF_FULLVERSION.exe.",
        )
        path = filedialog.askopenfilename(
            title="Locate NNF_FULLVERSION.exe",
            filetypes=[("Nubby's Number Factory", GAME_EXE_NAME), ("Executable", "*.exe")],
        )
        root.destroy()
        if path:
            _remember_game_exe(path)
            return path
    except Exception as e:
        print(f"[NubbyAP] Game path picker failed: {e}")
    return None


def _game_already_running():
    try:
        out = subprocess.run(
            ["tasklist", "/FI", f"IMAGENAME eq {GAME_EXE_NAME}"],
            capture_output=True, text=True, timeout=5,
        )
        return GAME_EXE_NAME.lower() in out.stdout.lower()
    except Exception:
        return False


def launch_game_if_needed():
    if _game_already_running():
        print("[NubbyAP] Game already running, not launching a second copy.")
        return
    exe_path = _find_game_exe()
    if not exe_path:
        print("[NubbyAP] Could not locate NNF_FULLVERSION.exe - launch the game manually.")
        return
    try:
        subprocess.Popen([exe_path], cwd=os.path.dirname(exe_path))
        print(f"[NubbyAP] Launched game: {exe_path}")
    except Exception as e:
        print(f"[NubbyAP] Failed to launch game: {e}")


class NubbyCommandProcessor(ClientCommandProcessor):
    def _cmd_resync(self):
        """Force a full resync of received items."""
        if isinstance(self.ctx, NubbyContext):
            self.ctx.syncing = True
            self.output("Resyncing items...")

    def _cmd_backup_now(self):
        """Take an immediate backup snapshot of the current save."""
        room_dir = _active_room_dir()
        if not room_dir:
            self.output("No active AP room yet - connect first.")
            return
        backups_dir = os.path.join(room_dir, "backups")
        snapshot_dir = os.path.join(backups_dir, time.strftime("%Y-%m-%d_%H%M%S"))
        _copy_save_files(SAVE_DIR, snapshot_dir)
        _prune_backups(backups_dir)
        self.output(f"Backed up save to {snapshot_dir}")

    def _cmd_restore_vanilla(self):
        """Restore your pre-Archipelago save. Overwrites your CURRENT save - use with care."""
        if not os.path.isdir(VANILLA_DIR):
            self.output("No vanilla backup found yet.")
            return
        _copy_save_files(VANILLA_DIR, SAVE_DIR)
        self.output("Restored your pre-Archipelago save. Restart the game to see it take effect.")

    def _cmd_new_run(self):
        """Clear the in-progress run for the current AP room (keeps supervisor/challenge/trial progress). Restart the game after."""
        room_dir = _active_room_dir()
        if not room_dir:
            self.output("No active AP room yet - connect first.")
            return
        auto_path = os.path.join(SAVE_DIR, "NUBBY_AutoSave_F.save")
        room_auto_path = os.path.join(room_dir, "save", "NUBBY_AutoSave_F.save")
        for p in (auto_path, room_auto_path):
            if os.path.exists(p):
                os.remove(p)
        self.output("Cleared the in-progress run. Restart the game to start fresh.")


class NubbyContext(CommonContext):
    tags = {"AP"}
    game = "Nubby's Number Factory"
    command_processor = NubbyCommandProcessor
    items_handling = 0b111

    def __init__(self, server_address, password):
        super().__init__(server_address, password)
        self.finished_game = False
        self.syncing = False
        self.save_folder = SAVE_FOLDER
        self._pending_seed_name = None
        self._game_launched = False
        self.goal_supervisors: list[int] = []
        self.required_count = 0
        self.lock_zone5 = False
        self.score_goal = 0
        os.makedirs(self.save_folder, exist_ok=True)

    async def server_auth(self, password_requested: bool = False):
        if password_requested and not self.password:
            await super().server_auth(password_requested)
        await self.get_username()
        await self.send_connect()

    def on_package(self, cmd: str, args: dict):
        # The base implementation handles "PrintJSON" (chat + item send/receive
        # log lines) and other bookkeeping every AP client relies on. Nothing
        # here was calling it before, which silently ate that entire feed.
        super().on_package(cmd, args)
        async_start(self._handle_package(cmd, args))

    def on_print_json(self, args: dict):
        # Called directly by the framework for every PrintJSON packet
        # (independent of on_package/_handle_package above). "ItemSend" is
        # the one type covering both directions - the message text itself
        # says "sent X to Y" or "found their X" depending on whether this
        # player is the sender or receiver, same as the desktop AP client.
        # Mirrors the base class's own copy.deepcopy(args["data"]) pattern -
        # confirmed via the real CommonClient.py source that its parsers
        # consume/mutate the list, so each of the (now four, incl. ours)
        # consumers needs its own fresh copy.
        super().on_print_json(args)
        if args.get("type") == "ItemSend":
            try:
                text = self.rawjsontotextparser(copy.deepcopy(args["data"]))
                _append_ap_log_line(text)
            except Exception as e:
                print(f"[NubbyAP] on_print_json log append failed: {e}")

    async def _handle_package(self, cmd: str, args: dict):
        folder = self.save_folder

        if cmd == "RoomInfo":
            self._pending_seed_name = args.get("seed_name")

        elif cmd == "Connected":
            seed_name = (
                args.get("seed_name")
                or self._pending_seed_name
                or getattr(self, "seed_name", None)
                or "unknown_seed"
            )
            slot_name = self.username or "unknown_slot"
            server_address = getattr(self, "server_address", None)
            # Extracted here (not inside the slot_data try-block below,
            # where it used to live) purely so activate_room_save can see
            # lock_challenges in time to apply it to a brand-new room's
            # fresh-room lock - doesn't change the V19 write-ordering fix
            # below (connected.flag still goes out before anything else).
            sd = args.get("slot_data", {})
            try:
                activate_room_save(server_address, seed_name, slot_name, slot_data=sd)
            except Exception as e:
                import traceback
                print(f"[NubbyAP] activate_room_save FAILED: {e}")
                print(traceback.format_exc())

            # connected.flag (and its display companion) are written as
            # early and unconditionally as possible, deliberately BEFORE
            # any of the auxiliary writes below - they're what the in-game
            # connection marker reads, and a failure in some unrelated bit
            # of bookkeeping (sphere files, slot_data.txt, ...) must never
            # be able to leave the marker reporting "Disconnected" while
            # actually connected. Confirmed this exact failure mode: an
            # earlier version removed AP_LOG_FILE inside activate_room_save
            # with no error handling, which could throw (e.g. a transient
            # Windows file-lock) and silently abort not just this flag but
            # activate_room_save's own item/perk-pool file writes too -
            # fixed there as well, but this reordering is the real
            # structural fix so the same class of bug can't recur here.
            open(os.path.join(folder, "connected.flag"), "w").close()
            try:
                with open(CONNECTION_INFO_FILE, "w", encoding="utf-8") as f:
                    f.write(f"{slot_name} @ {server_address or 'unknown server'}\n")
            except Exception as e:
                print(f"[NubbyAP] Failed to write CONNECTION_INFO_FILE (non-fatal): {e}")
            print(f"[NubbyAP] Connected as {self.username}")

            try:
                self.goal_supervisors = sd.get("goal_supervisors", [])
                self.required_count = sd.get("required_count", 0)
                self.lock_zone5 = bool(sd.get("lock_zone5"))
                self.score_goal = sd.get("score_goal", 0)
                self.finished_game = False
                _write_sphere_files(sd)
                _write_zone_lock_flag(sd)
                _write_custom_final_round_file(sd)
                room_dir = _active_room_dir()
                if room_dir:
                    _write_feature_lock_flags(sd, _read_json(os.path.join(room_dir, "unlocked_features.json"), default=[]))
                with open(os.path.join(folder, "slot_data.txt"), "w") as f:
                    for k, v in sd.items():
                        f.write(f"{k}={v}\n")
                checked = args.get("checked_locations", [])
                if checked:
                    with open(os.path.join(folder, "checked_locs_server.txt"), "w") as f:
                        for loc_id in checked:
                            f.write(f"{loc_id}\n")
            except Exception as e:
                import traceback
                print(f"[NubbyAP] slot_data/sphere-file handling FAILED: {e}")
                print(traceback.format_exc())

            # Only launch the game once connected.flag/slot_data.txt actually
            # exist on disk. Launching any earlier races the game's own
            # startup check for AP state, which only ever runs once - if it
            # boots before these files exist it never notices AP at all.
            if not self._game_launched:
                self._game_launched = True
                launch_game_if_needed()

        elif cmd == "ReceivedItems":
            try:
                items = args.get("items", [])
                server_index = args.get("index", 0)

                index_file = _item_index_path()
                try:
                    last_index = int(open(index_file).read().strip())
                except Exception:
                    last_index = 0

                print(f"[NubbyAP] ReceivedItems: server_index={server_index}, {len(items)} items, last_index={last_index}")

                skip = max(0, last_index - server_index)
                new_items = items[skip:]

                if not new_items:
                    print(f"[NubbyAP] No new items")
                    return

                for item in new_items:
                    try:
                        ap_id = item[0] if isinstance(item, (list, tuple)) else item.item
                    except Exception as e:
                        print(f"[NubbyAP] Failed to get ap_id from {item}: {e}")
                        continue

                    if ap_id in range(BASE_ID + 1, BASE_ID + 12):  # Supervisor 1..11
                        deliver_supervisor(ap_id - BASE_ID)
                    elif ap_id == BASE_ID + 3000:
                        room_dir = _active_room_dir()
                        if room_dir:
                            _queue_bonus(room_dir, "money", 5)
                    elif ap_id == BASE_ID + 3001:
                        room_dir = _active_room_dir()
                        if room_dir:
                            _queue_bonus(room_dir, "lives", 1)
                    elif ap_id == BASE_ID + 4000:
                        deliver_tony_skin()
                    elif ap_id == BASE_ID + 4004:  # Progressive Zone Unlock
                        room_dir = _active_room_dir()
                        if room_dir:
                            _queue_next_zone_unlock(room_dir, self.lock_zone5)
                    elif ap_id == BASE_ID + 6004:  # Challenges Unlock (save-flag based, no room_dir needed)
                        deliver_challenges_unlock()
                    elif ap_id in FEATURE_UNLOCK_AP_IDS:
                        room_dir = _active_room_dir()
                        if room_dir:
                            _queue_unlocked_feature(room_dir, FEATURE_UNLOCK_AP_IDS[ap_id])
                    elif ap_id in TRAP_AP_ID_TO_CODE:
                        _queue_trap(TRAP_AP_ID_TO_CODE[ap_id])
                    elif ap_id in _ID_MAP:
                        game_id = _ID_MAP[ap_id]
                        room_dir = _active_room_dir()
                        if room_dir:
                            _queue_unlocked_item(room_dir, game_id)
                    elif (BASE_ID + 5000 + 1) <= ap_id <= (BASE_ID + 5000 + 29) and (ap_id - BASE_ID - 5000) in PERK_ID_SET:
                        perk_id = ap_id - BASE_ID - 5000
                        room_dir = _active_room_dir()
                        if room_dir:
                            _queue_unlocked_perk(room_dir, perk_id)
                    else:
                        print(f"[NubbyAP] No mapping for ap_id={ap_id}")

                new_last = server_index + len(items)
                with open(index_file, "w") as f:
                    f.write(str(new_last))
                print(f"[NubbyAP] Index updated to {new_last}")
            except Exception as e:
                import traceback
                print(f"[NubbyAP] ReceivedItems ERROR: {e}")
                print(traceback.format_exc())

        elif cmd == "RoomUpdate":
            checked = args.get("checked_locations", [])
            if checked:
                with open(os.path.join(folder, "checked_locs_server.txt"), "a") as f:
                    for loc_id in checked:
                        f.write(f"{loc_id}\n")

    async def disconnect(self, allow_autoreconnect: bool = False):
        flag = os.path.join(self.save_folder, "connected.flag")
        if os.path.exists(flag):
            os.remove(flag)
        if os.path.exists(CONNECTION_INFO_FILE):
            os.remove(CONNECTION_INFO_FILE)
        # Leaving these behind would lock AP items/perks out of vanilla play
        # too, and carry the AP room's bonus into vanilla saves, since the
        # mod only checks whether each file exists. AP_LOG_FILE is
        # deliberately NOT cleared here - a disconnect (e.g. a brief network
        # blip) shouldn't wipe the visible in-game history; it's only reset
        # on an actual room switch, in activate_room_save.
        for f in (ITEM_POOL_FILE, PERK_POOL_FILE, BONUS_FILE, AP_ITEM_NAME_FILE, AP_ITEM_TIER_FILE, ITEM_PURCHASED_SIGNAL_FILE,
                  SPHERE_SHOPS_FLAG_FILE, *SPHERE_ITEM_FILES.values(),
                  ZONE_LOCK_FLAG_FILE, ZONE5_LOCK_FLAG_FILE, *ZONE_UNLOCK_FILES.values(),
                  CUSTOM_FINAL_ROUND_FILE, *FEATURE_LOCK_FLAG_FILES.values()):
            if os.path.exists(f):
                os.remove(f)
        try:
            room_dir = _active_room_dir()
            if room_dir and os.path.isdir(room_dir):
                _copy_save_files(SAVE_DIR, os.path.join(room_dir, "save"))
        except Exception:
            pass
        await super().disconnect(allow_autoreconnect)

    def run_gui(self):
        from kvui import GameManager

        class NubbyManager(GameManager):
            logging_pairs = [("Client", "Archipelago")]
            base_title = "Archipelago - Nubby's Number Factory"

        self.ui = NubbyManager(self)
        self.ui_task = asyncio.create_task(self.ui.async_run(), name="UI")


async def progress_watcher(ctx: NubbyContext):
    """
    Polls the game's own save files for Supervisor wins / Challenge
    completions / Nubby Trials completions and reports them as location
    checks. Also tracks goal completion the same way. No cooperation from
    the game's own code is required for any of this.
    """
    while not ctx.exit_event.is_set():
        if ctx.syncing and ctx.server_task:
            sync_msg = [{"cmd": "Sync"}]
            if ctx.locations_checked:
                sync_msg.append({"cmd": "LocationChecks",
                                 "locations": list(ctx.locations_checked)})
            await ctx.send_msgs(sync_msg)
            ctx.syncing = False

        room_dir = _active_room_dir()
        if room_dir:
            baseline_path = os.path.join(room_dir, "progress_baseline.json")
            baseline = _read_json(baseline_path)
            snap = _read_progress_snapshot()

            # Keep the AP Item slot loaded with a still-unchecked offer, and
            # collect any checks that got sent by buying it since last poll.
            # The offer naturally advances on the *next* poll once its check
            # lands in ctx.locations_checked below.
            _pick_next_ap_offer(room_dir, ctx)
            purchase_checks = _consume_ap_item_purchase_signal(room_dir)

            new_checks = list(purchase_checks)
            goal_wins = 0
            cumulative_score = _update_cumulative_score(room_dir) if ctx.score_goal else 0

            if snap is not None:
                if baseline is None:
                    # Cold start for this room - record current state as the
                    # baseline rather than treating everything as a new check.
                    _write_json(baseline_path, snap)
                else:
                    for i in range(1, 12):
                        key = f"sv_wins_{i}"
                        if snap.get(key, 0) > baseline.get(key, 0):
                            new_checks.append(BASE_ID + i)
                        if i in ctx.goal_supervisors and snap.get(key, 0) > 0:
                            goal_wins += 1
                    for i in range(12):
                        key = f"challenge_{i}"
                        if snap.get(key) and not baseline.get(key):
                            new_checks.append(BASE_ID + 100 + i)
                    for i in range(1, 6):
                        key = f"trial_{i}"
                        if snap.get(key) and not baseline.get(key):
                            new_checks.append(BASE_ID + 200 + i)

                    supervisor_goal_met = bool(ctx.required_count) and goal_wins >= ctx.required_count
                    score_goal_met = bool(ctx.score_goal) and cumulative_score >= ctx.score_goal
                    if not ctx.finished_game and ctx.server_task and (supervisor_goal_met or score_goal_met):
                        await ctx.send_msgs([{"cmd": "StatusUpdate", "status": ClientStatus.CLIENT_GOAL}])
                        ctx.finished_game = True
                        reason = "score goal" if score_goal_met and not supervisor_goal_met else "supervisors beaten"
                        print(f"[NubbyAP] Goal complete! ({reason})")

                    if snap != baseline:
                        _write_json(baseline_path, snap)

            if new_checks and ctx.server_task:
                await ctx.send_msgs([{"cmd": "LocationChecks", "locations": new_checks}])
                ctx.locations_checked = set(ctx.locations_checked or []) | set(new_checks)
                print(f"[NubbyAP] Sent {len(new_checks)} check(s): {new_checks}")

        await asyncio.sleep(POLL_INTERVAL)


async def _launch_main():
    ctx = NubbyContext(None, None)
    ctx.server_task = asyncio.create_task(server_loop(ctx), name="server loop")
    asyncio.create_task(progress_watcher(ctx), name="NubbyProgressWatcher")
    if gui_enabled:
        ctx.run_gui()
    ctx.run_cli()
    await ctx.exit_event.wait()
    await ctx.shutdown()


def launch():
    import colorama
    colorama.init()
    asyncio.run(_launch_main())
    colorama.deinit()


def main():
    Utils.init_logging("NubbyClient", exception_logger="Client")
    launch()


if __name__ == "__main__":
    parser = get_base_parser(description="Nubby's Number Factory Archipelago Client")
    args = parser.parse_args()
    main()
