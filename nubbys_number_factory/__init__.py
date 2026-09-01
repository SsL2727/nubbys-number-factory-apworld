"""
Nubby's Number Factory — Archipelago World Definition
======================================================

Game:    Nubby's Number Factory
Engine:  GameMaker Studio 2

V3: Save-native rebuild. This apworld no longer assumes any in-game AP hook
exists inside the compiled game - the bundled NubbyClient instead reads and
writes the game's own JSON save files directly:
  - Receiving items: Supervisor Unlocks and the Tony cosmetic skin are
    applied by editing NUBBY_Progression_F.save / NUBBY_Progression3_F.save
    directly. Stars are fixed at 0 and never granted by AP.
  - Sending checks: Supervisor wins, Challenge completions, and Nubby Trials
    completions are detected by polling those same save files for state
    changes - no cooperation from the game's own code is required.
  - Win condition: beat a configurable number of randomized supervisors.
"""

from typing import Any, ClassVar, Dict, List

from BaseClasses import Item, ItemClassification, MultiWorld, Region, Tutorial
from worlds.AutoWorld import WebWorld, World

from .items import (
    ALL_ITEMS, ITEM_ITEMS, PERK_ITEMS, SUPERVISOR_ITEMS, FILLER_ITEMS, EXTRA_FILLER_ITEMS,
    NUBBY_FILLER_ITEMS, COSMETIC_ITEMS, ZONE_ITEMS, LOCK_ITEMS, TRAP_ITEMS, ITEM_NAME_TO_ID,
    ITEM_ID_TO_DATA, ITEMS_BY_SHOP, BASE_ID
)
from .locations import ALL_LOCATIONS, LOCATION_NAME_TO_ID, SUPERVISOR_LOCATIONS
from .options import NNFOptions
from .regions import create_regions, NNFLocation


# ── AP Item class ─────────────────────────────────────────────────────────────

class NNFItem(Item):
    game: ClassVar[str] = "Nubby's Number Factory"


# Option attr name -> the single LOCK_ITEMS entry it gates. Used by both
# create_items() (conditional pool inclusion) and fill_slot_data() (telling
# the game mod which locks are active) so the two can't drift apart.
LOCK_OPTION_TO_ITEM: Dict[str, str] = {
    "lock_grabatron":      "Grab-A-Tron Unlock",
    "lock_black_market":   "Black Market Unlock",
    "lock_cafe_nubby":     "Cafe Nubby Unlock",
    "lock_nubby_trials":   "Nubby Trials Unlock",
    "lock_challenges":     "Challenges Unlock",
    "lock_freeze_ability": "Freeze Ability Unlock",
}


# ── Web/documentation settings ────────────────────────────────────────────────

class NNFWeb(WebWorld):
    theme = "ocean"
    tutorials = [
        Tutorial(
            tutorial_name="Setup Guide",
            description="A guide to setting up the Nubby's Number Factory AP randomizer.",
            language="English",
            file_name="setup_en.md",
            link="setup/en",
            authors=["kali"],
        )
    ]


# ── Main World class ──────────────────────────────────────────────────────────

class NubbyNumberFactoryWorld(World):
    """
    Nubby's Number Factory is a number-manipulation pegboard roguelite.
    In this randomizer, Supervisor modes are shuffled behind AP checks awarded
    for winning them, plus Challenge and Nubby Trials completions. Beat a
    configurable number of Supervisors to win.
    """

    game = "Nubby's Number Factory"
    options_dataclass = NNFOptions
    options: NNFOptions

    web = NNFWeb()

    item_name_to_id = ITEM_NAME_TO_ID
    location_name_to_id = LOCATION_NAME_TO_ID

    # Filled in generate_early — which SV indices count toward the win
    goal_supervisors: List[int]
    required_count: int

    # Names precollected in generate_early - create_items() must skip these,
    # or the same item ends up both in the player's starting inventory AND
    # placed at a real location, effectively duplicating it (confirmed via
    # a real send-log: a starting perk was later sent again from a
    # different location's check).
    precollected_names: set

    # Zone-based shops: game_id -> which of 4 equal-split zone groups it
    # landed in, for the game mod to restrict the Normal Shop's pool by
    # global.Zone. See generate_early for how this is built.
    zone_items: Dict[int, List[int]]

    # ── Generation entry point ────────────────────────────────────────────────

    def generate_early(self) -> None:
        """
        Decide which Supervisors are in-scope and which start unlocked.
        Runs before item/location placement.
        """
        opts = self.options
        self.precollected_names = set()

        # Clamp: can't require more supervisors than are in the pool
        self.required_count = min(
            opts.supervisors_required.value,
            opts.supervisors_in_pool.value
        )

        # Pick which SV indices (1..11) go into the multiworld pool
        pool_count = min(opts.supervisors_in_pool.value, 11)
        self.goal_supervisors = self.random.sample(range(1, 12), pool_count)

        # Pre-collect starting supervisors (not placed in the pool)
        start_count = min(opts.starting_supervisors.value, pool_count)
        start_svs = self.random.sample(self.goal_supervisors, start_count)
        for sv_idx in start_svs:
            name = f"Supervisor {sv_idx} Unlock"
            self.multiworld.push_precollected(self.create_item(name))
            self.precollected_names.add(name)

        # Pre-collect starting shop items, independently per shop, so each
        # of the 3 shops has real starting variety instead of only whichever
        # one a single global sample happens to land in.
        # DISABLED (kept for later re-wiring, not deleted) - when
        # include_cut_content existed, starting items were sampled from
        # only the non-cut-content subset (`eligible` below) so a player
        # without the option couldn't start with one. To re-enable: swap
        # the two lines under the for-loop for the commented ones.
        for shop_name, shop_items in ITEMS_BY_SHOP.items():
            # eligible = shop_items if opts.include_cut_content else [
            #     n for n in shop_items if not ITEM_ITEMS[n].cut_content
            # ]
            # count = min(opts.starting_items.value, len(eligible))
            # for item_name in self.random.sample(eligible, count):
            count = min(opts.starting_items.value, len(shop_items))
            for item_name in self.random.sample(shop_items, count):
                self.multiworld.push_precollected(self.create_item(item_name))
                self.precollected_names.add(item_name)

        # Pre-collect starting perks
        # DISABLED (kept for later re-wiring, not deleted) - same
        # cut-content exclusion as starting shop items above.
        # perk_names = list(PERK_ITEMS.keys()) if opts.include_cut_content else [
        #     n for n in PERK_ITEMS if not PERK_ITEMS[n].cut_content
        # ]
        perk_names = list(PERK_ITEMS.keys())
        perk_start_count = min(opts.starting_perks.value, len(perk_names))
        for perk_name in self.random.sample(perk_names, perk_start_count):
            self.multiworld.push_precollected(self.create_item(perk_name))
            self.precollected_names.add(perk_name)

        # Zone-based shops (if enabled): split every AP-tracked item as
        # evenly as possible into 4 groups, one per zone (1-4) - the game
        # mod restricts the Normal Shop's pool to a zone's own group while
        # global.Zone is 1-4, and leaves zone 5 (round 81+, endless)
        # completely unrestricted. Flat split across every tier (no
        # Common/Rare/Ultra-Rare stratification) - replaces the old
        # sphere-based system, which only split Common-tier items across 3
        # round-number brackets and left Rare/Ultra Rare as a single
        # always-later bucket.
        all_item_ids = [d.game_id for d in ITEM_ITEMS.values()]
        self.random.shuffle(all_item_ids)
        self.zone_items = {1: [], 2: [], 3: [], 4: []}
        for i, game_id in enumerate(all_item_ids):
            self.zone_items[(i % 4) + 1].append(game_id)

    # ── create_regions: AP calls this with only self ──────────────────────────

    def create_regions(self) -> None:
        create_regions(self.multiworld, self.player, self.options)

    # ── Item creation ─────────────────────────────────────────────────────────

    def create_item(self, name: str) -> NNFItem:
        data = ALL_ITEMS[name]
        return NNFItem(name, data.classification, data.code, self.player)

    def create_items(self) -> None:
        """
        Build the item pool:
          - Supervisor Unlock items (only those chosen for this seed)
          - All shop items
          - All perks
          - Cosmetic unlocks (currently just the Tony skin)
          - Permanent coin/life bonus filler to pad to location count
        """
        pool: List[NNFItem] = []

        # Supervisor unlocks for this seed's pool only - skipping any already
        # precollected as a starting supervisor (generate_early already gave
        # the player one; creating another here would place a duplicate at
        # a real location and send it a second time on check).
        for sv_idx in self.goal_supervisors:
            name = f"Supervisor {sv_idx} Unlock"
            if name not in self.precollected_names:
                pool.append(self.create_item(name))

        # All shop items, minus any already precollected as starting items.
        # DISABLED (kept for later re-wiring, not deleted) - cut-content
        # items (Professor Palmy, Test Item 2) only existed in the pool if
        # include_cut_content was on. To re-enable: swap the for-loop body
        # for the commented lines.
        for name in ITEM_ITEMS:
            # for name, data in ITEM_ITEMS.items():
            #     if data.cut_content and not self.options.include_cut_content:
            #         continue
            if name not in self.precollected_names:
                pool.append(self.create_item(name))

        # All perks, minus any already precollected as starting perks
        # DISABLED (kept for later re-wiring, not deleted) - same
        # cut-content gate as shop items above, for the six demo perks.
        for name in PERK_ITEMS:
            # for name, data in PERK_ITEMS.items():
            #     if data.cut_content and not self.options.include_cut_content:
            #         continue
            if name not in self.precollected_names:
                pool.append(self.create_item(name))

        # Cosmetic unlocks
        for name in COSMETIC_ITEMS:
            pool.append(self.create_item(name))

        # Progressive Zone Unlock: one copy per locked zone (3 for 2/3/4, a
        # 4th if lock_zone5 is also on). Each copy received opens whichever
        # locked zone is next in sequence - see NubbyClient.py.
        if self.options.lock_zones:
            copies = 4 if self.options.lock_zone5 else 3
            for _ in range(copies):
                pool.append(self.create_item("Progressive Zone Unlock"))

        # New feature locks - one Unlock item per enabled lock_* option.
        for opt_name, item_name in LOCK_OPTION_TO_ITEM.items():
            if getattr(self.options, opt_name):
                pool.append(self.create_item(item_name))

        # Pad remaining slots with filler - permanent-bonus filler always,
        # traps mixed in alongside it (not as a fixed extra count) if
        # include_traps is on, so trap frequency scales naturally with
        # however many filler slots this seed happens to have. Filler is
        # weighted 3x relative to each trap type (~38% trap rate when both
        # are present) rather than an even split, so traps stay a real but
        # minority risk rather than dominating every shop visit; shuffled
        # once so the round-robin fill below doesn't always start on the
        # same entry.
        #
        # nubby_filler_percent carves out its own share of the padding
        # first (a flat percentage of the total padding slots, rounded),
        # entirely separate from the filler/trap weighting above - "Filler:
        # Nubby" never competes for a slot against Permanent Coins/Extra
        # Life/traps, it just claims its configured percentage up front and
        # the remainder is split among the other filler/trap types exactly
        # as before.
        total_locations = len(self.multiworld.get_unfilled_locations(self.player))
        filler_needed   = max(0, total_locations - len(pool))
        nubby_count     = round(filler_needed * (self.options.nubby_filler_percent.value / 100))
        nubby_count     = min(nubby_count, filler_needed)
        for _ in range(nubby_count):
            pool.append(self.create_item("Filler: Nubby"))

        filler_names = (list(FILLER_ITEMS.keys()) + list(EXTRA_FILLER_ITEMS.keys())) * 3
        if self.options.include_traps:
            # Item Steal/Item Jam weighted well above the other 3 traps (6
            # of 9 trap entries, ~67% of the trap pool specifically) per
            # explicit request - Near Death/Coin Theft/Chaos Event stay at
            # their original 1x weight.
            filler_names = filler_names + (["Trap: Item Steal"] * 3 + ["Trap: Item Jam"] * 3
                                            + ["Trap: Coin Theft", "Trap: Near Death", "Trap: Chaos Event"])
        self.random.shuffle(filler_names)
        for i in range(filler_needed - nubby_count):
            pool.append(self.create_item(filler_names[i % len(filler_names)]))

        # Supervisors + shop items + perks + cosmetics can add up to more
        # than this seed's own location count (confirmed: AP's generator
        # does not tolerate a per-player pool larger than that player's own
        # locations - it fails to place the overflow, which can even eat
        # into *other* players' placements in a multiworld). If it's over,
        # trim randomly rather than always cutting the same tail of the
        # dict, so every item has an equal chance of making a given seed.
        if len(pool) > total_locations:
            self.random.shuffle(pool)
        pool = pool[:total_locations]

        self.multiworld.itempool += pool

    # ── Win condition ─────────────────────────────────────────────────────────

    def set_rules(self) -> None:
        """
        Goal: reach + complete at least `required_count` of the goal Supervisor
        win locations.
        """
        goal_location_names = [f"Win Supervisor {i}" for i in self.goal_supervisors]
        required            = self.required_count
        player              = self.player

        def _can_win(state) -> bool:
            reachable = sum(
                1 for loc_name in goal_location_names
                if state.can_reach_location(loc_name, player)
            )
            return reachable >= required

        self.multiworld.completion_condition[player] = _can_win

    # ── Slot data (sent to the game client on connect) ────────────────────────

    def fill_slot_data(self) -> Dict[str, Any]:
        data: Dict[str, Any] = {
            "goal_supervisors": self.goal_supervisors,
            "required_count":   self.required_count,
            # Always sent (not gated on truthiness like the toggles below) -
            # both default ON, so a client that only checked for presence
            # would wrongly treat an absent key as "off" for the common case.
            "include_item_purchases": bool(self.options.include_item_purchases),
            "include_perks": bool(self.options.include_perks),
            "points_check_count": self.options.points_check_count.value,
        }
        if self.options.zone_based_shops:
            data["zone_based_shops"] = True
            data["zone_items"] = self.zone_items
        if self.options.lock_zones:
            data["lock_zones"] = True
            data["lock_zone5"] = bool(self.options.lock_zone5)
        for opt_name in LOCK_OPTION_TO_ITEM:
            if getattr(self.options, opt_name):
                data[opt_name] = True
        if self.options.custom_final_round:
            data["custom_final_round"] = self.options.custom_final_round.value
        if self.options.score_goal:
            data["score_goal"] = self.options.score_goal.value
        if self.options.include_traps:
            data["include_traps"] = True
        return data

    # ── Item name groups (for hints, etc.) ────────────────────────────────────

    def get_item_name_groups(self) -> Dict[str, Any]:
        return {
            "Shop Items":         set(ITEM_ITEMS.keys()),
            "Perks":              set(PERK_ITEMS.keys()),
            "Supervisor Unlocks": set(SUPERVISOR_ITEMS.keys()),
            "Cosmetics":          set(COSMETIC_ITEMS.keys()),
            "Zone Unlocks":       set(ZONE_ITEMS.keys()),
            "Feature Unlocks":    set(LOCK_ITEMS.keys()),
            "Traps":              set(TRAP_ITEMS.keys()),
        }


# ── Launcher client registration ──────────────────────────────────────────────
from worlds.LauncherComponents import Component, components, Type, launch_subprocess, icon_paths


def launch_client():
    from .NubbyClient import launch
    launch_subprocess(launch, name="NubbyClient")


icon_paths["nnf"] = f"ap:{__name__}/icons/nubby.png"

components.append(Component(
    "Nubby's Number Factory Client",
    "NubbyClient",
    func=launch_client,
    component_type=Type.CLIENT,
    icon="nnf",
))
