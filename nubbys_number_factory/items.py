"""
Items for Nubby's Number Factory Archipelago Randomizer
V5: Shop items tagged by which of the 3 shops they belong to (for per-shop
    starting items). Perks added as a second, parallel randomized category
    (same InPerkItemPool/tier mechanism as shop items, confirmed via
    decompile - obj_PerkMGMT mirrors obj_ItemMGMT almost exactly). Filler
    is now permanent starting-money/starting-lives bonuses instead of a
    no-op, applied once at the start of every run via the game mod.

Shop items: game_id is the internal item id (A_Items / InItemPool index).
Perks: perk_id is the internal perk id (InPerkItemPool index).
"""

from typing import Dict, NamedTuple, Optional
from BaseClasses import ItemClassification

BASE_ID = 6_900_000


class NNFItemData(NamedTuple):
    code: int
    classification: ItemClassification
    game_id: int = -1        # internal item id (A_Items/InItemPool); -1 = n/a
    shop: Optional[str] = None  # "normal" | "black_market" | "cafe"; None = not a shop item
    perk_id: int = -1        # internal perk id (InPerkItemPool); -1 = n/a
    bonus: int = 0           # for filler: permanent amount granted (money or lives)
    tier: int = -1           # ItemTier/PerkTier from the game itself: 0=Common, 1=Rare, 2=Ultra Rare, -1=n/a
    # cut_content: bool = False  # DISABLED (kept for later re-wiring, not deleted) - was: restored cut content, only in the pool if NNFOptions.include_cut_content


# ── SUPERVISOR UNLOCKS ────────────────────────────────────────────────────────
SUPERVISOR_ITEMS: Dict[str, NNFItemData] = {
    f"Supervisor {i} Unlock": NNFItemData(
        code=BASE_ID + i,
        classification=ItemClassification.progression,
    )
    for i in range(1, 12)
}

# Items sent TO the player. code = BASE_ID + 2000 + sequential index.
# game_id is the internal item id that appears in A_Items / A_BoughtItems.
# Tier 0 = Common (filler/useful), Tier 1 = Rare (useful), Tier 2 = Ultra Rare (progression)

ITEM_ITEMS: Dict[str, NNFItemData] = {

    # ── Normal Shop ──────────────────────────────────────────────────────────
    # Common (tier 0)
    "Item: Pants":              NNFItemData(BASE_ID+2000, ItemClassification.filler,       0,   "normal", tier=0),
    "Item: Cactus":             NNFItemData(BASE_ID+2001, ItemClassification.useful,       2,   "normal", tier=0),
    "Item: Flame Thrower":      NNFItemData(BASE_ID+2002, ItemClassification.useful,       3,   "normal", tier=0),
    "Item: Monstrosity":        NNFItemData(BASE_ID+2003, ItemClassification.useful,       5,   "normal", tier=0),
    "Item: Crazy Straw":        NNFItemData(BASE_ID+2004, ItemClassification.useful,       9,   "normal", tier=0),
    "Item: Evil Goose":         NNFItemData(BASE_ID+2005, ItemClassification.useful,       10,  "normal", tier=0),
    "Item: Flaming Skull":      NNFItemData(BASE_ID+2006, ItemClassification.useful,       11,  "normal", tier=0),
    "Item: Sea Cucumber":       NNFItemData(BASE_ID+2007, ItemClassification.useful,       12,  "normal", tier=0),
    "Item: Pedro":              NNFItemData(BASE_ID+2008, ItemClassification.filler,       14,  "normal", tier=0),
    "Item: Finger Puppet":      NNFItemData(BASE_ID+2009, ItemClassification.filler,       16,  "normal", tier=0),
    "Item: Jacks":              NNFItemData(BASE_ID+2010, ItemClassification.useful,       17,  "normal", tier=0),
    "Item: Ton of Feathers":    NNFItemData(BASE_ID+2011, ItemClassification.useful,       21,  "normal", tier=0),
    "Item: Dancing Dinosaur":   NNFItemData(BASE_ID+2012, ItemClassification.filler,       22,  "normal", tier=0),
    "Item: Squirmy":            NNFItemData(BASE_ID+2013, ItemClassification.useful,       23,  "normal", tier=0),
    "Item: Broken Socket":      NNFItemData(BASE_ID+2014, ItemClassification.filler,       24,  "normal", tier=0),
    "Item: Kazoo":              NNFItemData(BASE_ID+2015, ItemClassification.useful,       30,  "normal", tier=0),
    "Item: Uranium Rod":        NNFItemData(BASE_ID+2016, ItemClassification.useful,       33,  "normal", tier=0),
    "Item: Happy Seal":         NNFItemData(BASE_ID+2017, ItemClassification.useful,       35,  "normal", tier=0),
    "Item: Friendly Rock":      NNFItemData(BASE_ID+2018, ItemClassification.filler,       37,  "normal", tier=0),
    "Item: Tardigrade":         NNFItemData(BASE_ID+2019, ItemClassification.useful,       40,  "normal", tier=0),
    "Item: Plastic Tree":       NNFItemData(BASE_ID+2020, ItemClassification.useful,       41,  "normal", tier=0),
    "Item: Cheese House":       NNFItemData(BASE_ID+2021, ItemClassification.useful,       44,  "normal", tier=0),
    "Item: Disguise Glasses":   NNFItemData(BASE_ID+2022, ItemClassification.useful,       48,  "normal", tier=0),
    "Item: Two-Headed Turtle":  NNFItemData(BASE_ID+2023, ItemClassification.useful,       55,  "normal", tier=0),
    "Item: Poop Butt":          NNFItemData(BASE_ID+2024, ItemClassification.filler,       65,  "normal", tier=0),
    "Item: E-Block":            NNFItemData(BASE_ID+2025, ItemClassification.useful,       67,  "normal", tier=0),
    "Item: T-Block":            NNFItemData(BASE_ID+2026, ItemClassification.useful,       149, "normal", tier=0),
    # Rare (tier 1)
    "Item: Lobster Claw":       NNFItemData(BASE_ID+2027, ItemClassification.useful,       4,   "normal", tier=1),
    "Item: Nose":               NNFItemData(BASE_ID+2028, ItemClassification.useful,       8,   "normal", tier=1),
    "Item: Rubber Chicken":     NNFItemData(BASE_ID+2029, ItemClassification.useful,       20,  "normal", tier=1),
    "Item: Yeti":               NNFItemData(BASE_ID+2030, ItemClassification.useful,       29,  "normal", tier=1),
    "Item: Squid":              NNFItemData(BASE_ID+2031, ItemClassification.useful,       31,  "normal", tier=1),
    "Item: Laser Pointer":      NNFItemData(BASE_ID+2032, ItemClassification.useful,       52,  "normal", tier=1),
    "Item: All-Seeing Pyramid": NNFItemData(BASE_ID+2033, ItemClassification.useful,       53,  "normal", tier=1),
    "Item: Inflatable Dolphin": NNFItemData(BASE_ID+2034, ItemClassification.useful,       56,  "normal", tier=1),
    # Ultra Rare (tier 2)
    "Item: 3D Glasses":         NNFItemData(BASE_ID+2035, ItemClassification.progression,  32,  "normal", tier=2),
    "Item: Horse Pill":         NNFItemData(BASE_ID+2036, ItemClassification.useful,       59,  "normal", tier=2),

    # ── Black Market ─────────────────────────────────────────────────────────
    # Common (tier 0)
    "Item: Snake":              NNFItemData(BASE_ID+2037, ItemClassification.useful,       6,   "black_market", tier=0),
    "Item: Toy Fish":           NNFItemData(BASE_ID+2038, ItemClassification.useful,       27,  "black_market", tier=0),
    "Item: Pointer":            NNFItemData(BASE_ID+2039, ItemClassification.useful,       34,  "black_market", tier=0),
    "Item: Asbestos":           NNFItemData(BASE_ID+2040, ItemClassification.useful,       43,  "black_market", tier=0),
    "Item: Wingless Fly":       NNFItemData(BASE_ID+2041, ItemClassification.useful,       47,  "black_market", tier=0),
    "Item: Mannequin Head":     NNFItemData(BASE_ID+2042, ItemClassification.useful,       51,  "black_market", tier=0),
    "Item: Ouroboros":          NNFItemData(BASE_ID+2043, ItemClassification.useful,       60,  "black_market", tier=0),
    "Item: Alien":              NNFItemData(BASE_ID+2044, ItemClassification.useful,       66,  "black_market", tier=0),
    # Rare (tier 1)
    "Item: Fly Agaric":         NNFItemData(BASE_ID+2045, ItemClassification.useful,       15,  "black_market", tier=1),
    "Item: Clay Man Guy":       NNFItemData(BASE_ID+2046, ItemClassification.useful,       42,  "black_market", tier=1),
    "Item: King Baby":          NNFItemData(BASE_ID+2047, ItemClassification.useful,       49,  "black_market", tier=1),
    "Item: Fish Man":           NNFItemData(BASE_ID+2048, ItemClassification.useful,       54,  "black_market", tier=1),
    "Item: Pregnancy Test":     NNFItemData(BASE_ID+2049, ItemClassification.useful,       58,  "black_market", tier=1),
    "Item: Flibby Flobbies":    NNFItemData(BASE_ID+2050, ItemClassification.useful,       151, "black_market", tier=1),

    # ── Cafe ─────────────────────────────────────────────────────────────────
    # Common (tier 0)
    "Item: Strawberry":         NNFItemData(BASE_ID+2051, ItemClassification.useful,       7,   "cafe", tier=0),
    "Item: Hot Dog Dog":        NNFItemData(BASE_ID+2052, ItemClassification.useful,       36,  "cafe", tier=0),
    "Item: Kebab":              NNFItemData(BASE_ID+2053, ItemClassification.useful,       45,  "cafe", tier=0),
    "Item: Kidney Bean":        NNFItemData(BASE_ID+2054, ItemClassification.useful,       46,  "cafe", tier=0),
    "Item: Pickle Rat":         NNFItemData(BASE_ID+2055, ItemClassification.useful,       50,  "cafe", tier=0),
    "Item: Jelly":              NNFItemData(BASE_ID+2056, ItemClassification.filler,       129, "cafe", tier=0),
    "Item: Chip":               NNFItemData(BASE_ID+2057, ItemClassification.useful,       136, "cafe", tier=0),
    "Item: Soup Crackers":      NNFItemData(BASE_ID+2058, ItemClassification.filler,       148, "cafe", tier=0),
    "Item: Tri-Bagel":          NNFItemData(BASE_ID+2059, ItemClassification.useful,       179, "cafe", tier=0),
    # Rare (tier 1)
    "Item: Noodle":             NNFItemData(BASE_ID+2060, ItemClassification.useful,       1,   "cafe", tier=1),
    "Item: Tart Lard":          NNFItemData(BASE_ID+2061, ItemClassification.useful,       13,  "cafe", tier=1),
    "Item: Croissant":          NNFItemData(BASE_ID+2062, ItemClassification.useful,       18,  "cafe", tier=1),
    "Item: Dave":               NNFItemData(BASE_ID+2063, ItemClassification.useful,       38,  "cafe", tier=1),
    "Item: Lentil":             NNFItemData(BASE_ID+2064, ItemClassification.useful,       61,  "cafe", tier=1),
    "Item: Avocado":            NNFItemData(BASE_ID+2065, ItemClassification.useful,       137, "cafe", tier=1),
    "Item: Fava Bean":          NNFItemData(BASE_ID+2066, ItemClassification.useful,       138, "cafe", tier=1),
    # Ultra Rare (tier 2)
    "Item: Donut":              NNFItemData(BASE_ID+2067, ItemClassification.progression,  19,  "cafe", tier=2),
    "Item: Starfruit":          NNFItemData(BASE_ID+2068, ItemClassification.progression,  146, "cafe", tier=2),

    # DISABLED (kept for later re-wiring, not deleted) - restored cut
    # content. Both fully implemented in vanilla (real Create_0/Alarm_0
    # object code, real name/desc text keys, real "+" upgrade pair) but
    # shipped with InItemPool hardcoded to 0 - i.e. deliberately excluded
    # from every shop's roll rather than left unfinished. Confirmed via
    # decompile of obj_ItemMGMT_Create_0's scr_Init_Item calls (game_id/
    # tier taken directly from arg0/arg5). To re-enable: uncomment these,
    # restore the cut_content field above, restore the matching GML #if
    # false block (item-side ApOriginalItemPool[57]/[26] override), and
    # restore include_cut_content in options.py/__init__.py/NubbyClient.py.
    # "Item: Professor Palmy":    NNFItemData(BASE_ID+2069, ItemClassification.useful,       57,  "normal", tier=0, cut_content=True),
    # "Item: Test Item 2":        NNFItemData(BASE_ID+2070, ItemClassification.useful,       26,  "normal", tier=1, cut_content=True),
}

# Perks - a second, parallel randomized category (obj_PerkMGMT.InPerkItemPool,
# same locking mechanism as shop items but a single pool, not split by shop).
# Only perks that are normally available in vanilla (InPerkItemPool == 1) are
# tracked; a handful of special/hidden perks (InPerkItemPool == 0 in vanilla,
# e.g. upgrade variants) are left alone entirely, same as "evil" item variants.
PERK_ITEMS: Dict[str, NNFItemData] = {
    # Common (tier 0)
    "Perk: Ray Gun":       NNFItemData(BASE_ID+5003, ItemClassification.useful, perk_id=3, tier=0),
    "Perk: Speedy":        NNFItemData(BASE_ID+5004, ItemClassification.useful, perk_id=4, tier=0),
    "Perk: Cheesy":        NNFItemData(BASE_ID+5006, ItemClassification.useful, perk_id=6, tier=0),
    "Perk: Waffle":        NNFItemData(BASE_ID+5007, ItemClassification.useful, perk_id=7, tier=0),
    "Perk: Chaotic":       NNFItemData(BASE_ID+5008, ItemClassification.useful, perk_id=8, tier=0),
    "Perk: Zombie":        NNFItemData(BASE_ID+5009, ItemClassification.useful, perk_id=9, tier=0),
    "Perk: Springy":       NNFItemData(BASE_ID+5010, ItemClassification.useful, perk_id=10, tier=0),
    "Perk: Mystery Box":   NNFItemData(BASE_ID+5012, ItemClassification.useful, perk_id=12, tier=0),
    "Perk: Candle":        NNFItemData(BASE_ID+5018, ItemClassification.useful, perk_id=18, tier=0),
    "Perk: Drainer":       NNFItemData(BASE_ID+5020, ItemClassification.useful, perk_id=20, tier=0),
    "Perk: Card Tower":    NNFItemData(BASE_ID+5021, ItemClassification.useful, perk_id=21, tier=0),
    "Perk: Eggy":          NNFItemData(BASE_ID+5022, ItemClassification.useful, perk_id=22, tier=0),
    "Perk: Buckshot":      NNFItemData(BASE_ID+5023, ItemClassification.useful, perk_id=23, tier=0),
    "Perk: Void":          NNFItemData(BASE_ID+5024, ItemClassification.useful, perk_id=24, tier=0),
    "Perk: Archaic":       NNFItemData(BASE_ID+5026, ItemClassification.useful, perk_id=26, tier=0),
    "Perk: Warlock":       NNFItemData(BASE_ID+5027, ItemClassification.useful, perk_id=27, tier=0),
    "Perk: Gourmet":       NNFItemData(BASE_ID+5028, ItemClassification.useful, perk_id=28, tier=0),
    "Perk: Lunar":         NNFItemData(BASE_ID+5029, ItemClassification.useful, perk_id=29, tier=0),
    # Rare (tier 1)
    "Perk: Snake Eyes":    NNFItemData(BASE_ID+5001, ItemClassification.useful, perk_id=1, tier=1),
    "Perk: Trophy":        NNFItemData(BASE_ID+5013, ItemClassification.useful, perk_id=13, tier=1),
    "Perk: Meaty":         NNFItemData(BASE_ID+5015, ItemClassification.useful, perk_id=15, tier=1),
    "Perk: Cubey":         NNFItemData(BASE_ID+5016, ItemClassification.useful, perk_id=16, tier=1),
    "Perk: Charity":       NNFItemData(BASE_ID+5017, ItemClassification.useful, perk_id=17, tier=1),
    "Perk: Tornado":       NNFItemData(BASE_ID+5019, ItemClassification.useful, perk_id=19, tier=1),
    # Ultra Rare (tier 2)
    "Perk: Battery":       NNFItemData(BASE_ID+5005, ItemClassification.progression, perk_id=5, tier=2),
    "Perk: Penny":         NNFItemData(BASE_ID+5014, ItemClassification.progression, perk_id=14, tier=2),
    "Perk: Enlightened":   NNFItemData(BASE_ID+5025, ItemClassification.progression, perk_id=25, tier=2),

    # DISABLED (kept for later re-wiring, not deleted) - six restored
    # demo-exclusive perks (Gambley/Jittery/Lucky/Rocky/Wizardry/Silly),
    # per nubbysnumberfactory.wiki.gg/wiki/Cut_Content. Unlike Professor
    # Palmy/Test Item 2 above (real, pool-disabled objects that already
    # shipped in this build), these six have ZERO trace anywhere in this
    # game's decompiled corpus - genuinely demo-only content, rebuilt from
    # scratch (new GameObjects, ids 33-38, real wiki-sourced sprites) using
    # the wiki's own description text as the spec. To re-enable: uncomment
    # these, restore the cut_content field above, restore the matching GML
    # #if false blocks (six-perk object/sprite/code creation, the
    # obj_PerkMGMT_Create_0 registration + _apPerkIds extension, the
    # obj_LvlMGMT_Create_0 Gambley/Lucky hooks, the obj_ItemParent_Alarm_0
    # Wizardry hook, the scr_GameEv Silly hooks), and restore
    # include_cut_content in options.py/__init__.py/NubbyClient.py.
    # "Perk: The Gambley Perk":  NNFItemData(BASE_ID+5033, ItemClassification.useful, perk_id=33, tier=0, cut_content=True),
    # "Perk: The Jittery Perk":  NNFItemData(BASE_ID+5034, ItemClassification.useful, perk_id=34, tier=0, cut_content=True),
    # "Perk: The Lucky Perk":    NNFItemData(BASE_ID+5035, ItemClassification.useful, perk_id=35, tier=0, cut_content=True),
    # "Perk: The Rocky Perk":    NNFItemData(BASE_ID+5036, ItemClassification.useful, perk_id=36, tier=0, cut_content=True),
    # "Perk: The Wizardry Perk": NNFItemData(BASE_ID+5037, ItemClassification.useful, perk_id=37, tier=0, cut_content=True),
    # "Perk: The Silly Perk":    NNFItemData(BASE_ID+5038, ItemClassification.useful, perk_id=38, tier=1, cut_content=True),
}

# Filler - permanent, small, run-independent bonuses applied once at the
# start of every run by the game mod (not per-run currency/lives, which
# reset - these stack permanently across the whole AP session).
FILLER_ITEMS: Dict[str, NNFItemData] = {
    "Permanent Coins":      NNFItemData(BASE_ID+3000, ItemClassification.filler, bonus=10),
    "Permanent Extra Life": NNFItemData(BASE_ID+3001, ItemClassification.filler, bonus=1),
}

# Two more permanent, stacking bonuses - same "applied once at run start"
# mechanism as FILLER_ITEMS above, kept in a separate dict purely so they
# were easy to add without touching FILLER_ITEMS's own definition.
# create_items() weights these identically to FILLER_ITEMS in the padding
# pool.
#   - Permanent Board Shuffle: +1 stacking JumbleCharges (the real in-game
#     "jumbling"/board-shuffle resource, confirmed via decompile).
#   - Permanent Item Rarity: +2% stacking odds for rare shop items (the
#     game's own Comn/Rare/UltraRare odds are out of 1000 - confirmed via
#     decompile ComnOdds=950/RareOdds=50/UltraRareOdds=2 - and the wiki's
#     own Avocado effect, "+1% odds for rare items", which is this same
#     mechanic; bonus=2 below is in the same "percent" unit as Avocado's
#     own effect, translated to the 1000-point scale in the GML patch).
EXTRA_FILLER_ITEMS: Dict[str, NNFItemData] = {
    "Permanent Board Shuffle": NNFItemData(BASE_ID+3003, ItemClassification.filler, bonus=1),
    "Permanent Item Rarity":   NNFItemData(BASE_ID+3004, ItemClassification.filler, bonus=2),
}

# Pure no-op filler - does nothing on arrival, just occupies a pool slot.
# Kept separate from FILLER_ITEMS (rather than folded in with a 0 bonus)
# since its share of the padding is its own configurable percentage (see
# NNFOptions.nubby_filler_percent) instead of the flat weighting the other
# two filler/trap types get in create_items().
NUBBY_FILLER_ITEMS: Dict[str, NNFItemData] = {
    "Filler: Nubby": NNFItemData(BASE_ID+3002, ItemClassification.filler),
}

# Cosmetic unlocks - not shop items, not tracked by game_id/InItemPool.
# Delivered by editing NUBBY_Progression3_F.save's SaveU_Cosmo_NubbySkin{n}
# flags directly. "Tony" (NubbySkinId 15, save key SaveU_Cosmo_NubbySkin15)
# is unlocked by default in vanilla; randomizing it into the pool means it
# now has to be found like anything else instead of always being available.
COSMETIC_ITEMS: Dict[str, NNFItemData] = {
    "Item: Tony Skin": NNFItemData(BASE_ID+4000, ItemClassification.filler),
}

# Zone locks (optional, see NNFOptions.lock_zones) - gate advancing past
# round 20/40/60 (into Zone 2/3/4) behind a single progressive item. Zone 1
# (rounds 1-20) is always accessible and needs no item. Each copy received
# opens the next locked zone in sequence (2, then 3, then 4, then 5 if
# NNFOptions.lock_zone5 is also on) - AP's own ItemClassification.progression
# + the client tracking "how many received so far" handles the ordering, no
# per-zone naming needed. Matches global.Zone's own formula,
# ((CurrentRnd-1) div 20)+1, confirmed via decompile.
ZONE_ITEMS: Dict[str, NNFItemData] = {
    "Progressive Zone Unlock": NNFItemData(BASE_ID+4004, ItemClassification.progression),
}

# New unlockable features (see corresponding NNFOptions.lock_* toggles) -
# each gates access to a real in-game feature/ability behind a received
# item. Grab-A-Tron/Black Market/Cafe are random round-timeline overlay
# events (not hub buttons); Nubby Trials is Challenge slot 10 within the
# existing Challenges roster; Challenges itself reuses the game's own
# obj_GAME.U_ChallengeMode save-backed unlock flag (same mechanism as
# Supervisors/Tony - no GML patch needed for enforcement, only for
# receiving); Freeze Ability gates the game's own pre-existing
# obj_FreezeItemBtn/FrozenItem[] shop-offer-freeze mechanic (confirmed via
# decompile it's fully implemented already, not something built from
# scratch). All confirmed via decompile this session.
LOCK_ITEMS: Dict[str, NNFItemData] = {
    "Grab-A-Tron Unlock":    NNFItemData(BASE_ID+6000, ItemClassification.progression),
    "Black Market Unlock":   NNFItemData(BASE_ID+6001, ItemClassification.progression),
    "Cafe Nubby Unlock":     NNFItemData(BASE_ID+6002, ItemClassification.progression),
    "Nubby Trials Unlock":   NNFItemData(BASE_ID+6003, ItemClassification.progression),
    "Challenges Unlock":     NNFItemData(BASE_ID+6004, ItemClassification.progression),
    "Freeze Ability Unlock": NNFItemData(BASE_ID+6005, ItemClassification.progression),
}

# Traps (see NNFOptions.include_traps) - negative-effect filler, sent to the
# player like any other item but with a one-time punishing effect on
# arrival instead of a permanent grant.
TRAP_ITEMS: Dict[str, NNFItemData] = {
    "Trap: Item Steal":  NNFItemData(BASE_ID+7000, ItemClassification.trap),  # deletes an item from inventory
    "Trap: Coin Theft":  NNFItemData(BASE_ID+7001, ItemClassification.trap),  # deletes all coins
    "Trap: Near Death":  NNFItemData(BASE_ID+7002, ItemClassification.trap),  # reduces lives to 1
    "Trap: Item Jam":    NNFItemData(BASE_ID+7003, ItemClassification.trap),  # disables a random item for 5 rounds
    "Trap: Chaos Event": NNFItemData(BASE_ID+7004, ItemClassification.trap),  # forces a random special event next round
}

ALL_ITEMS: Dict[str, NNFItemData] = {
    **SUPERVISOR_ITEMS,
    **ITEM_ITEMS,
    **PERK_ITEMS,
    **FILLER_ITEMS,
    **EXTRA_FILLER_ITEMS,
    **NUBBY_FILLER_ITEMS,
    **COSMETIC_ITEMS,
    **ZONE_ITEMS,
    **LOCK_ITEMS,
    **TRAP_ITEMS,
}

ITEM_NAME_TO_ID: Dict[str, int] = {name: data.code for name, data in ALL_ITEMS.items()}
ITEM_ID_TO_DATA: Dict[int, NNFItemData] = {data.code: data for data in ALL_ITEMS.values()}

# AP item code -> internal game item id, for shop items only.
AP_CODE_TO_GAME_ID: Dict[int, int] = {
    data.code: data.game_id for data in ITEM_ITEMS.values()
}

# Shop name -> list of item names belonging to it (for per-shop starting items).
ITEMS_BY_SHOP: Dict[str, list] = {
    "normal": [n for n, d in ITEM_ITEMS.items() if d.shop == "normal"],
    "black_market": [n for n, d in ITEM_ITEMS.items() if d.shop == "black_market"],
    "cafe": [n for n, d in ITEM_ITEMS.items() if d.shop == "cafe"],
}
