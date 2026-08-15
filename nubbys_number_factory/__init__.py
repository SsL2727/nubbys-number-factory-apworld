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
    ALL_ITEMS, ITEM_ITEMS, PERK_ITEMS, SUPERVISOR_ITEMS, FILLER_ITEMS, COSMETIC_ITEMS, ZONE_ITEMS,
    LOCK_ITEMS, TRAP_ITEMS, ITEM_NAME_TO_ID, ITEM_ID_TO_DATA, ITEMS_BY_SHOP, BASE_ID
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

    # Sphere-based shops: game_id/perk_id -> which of 3 groups it landed in,
    # for the game mod to restrict the Normal Shop's pool by round bracket.
    # See generate_early for how these are built.
    sphere_items: Dict[int, List[int]]
    sphere_perks: Dict[int, List[int]]

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
        # one a single global sample happens to land in. Cut-content items
        # are excluded from sampling unless include_cut_content is on -
        # otherwise a player without the option could still start with one.
        for shop_name, shop_items in ITEMS_BY_SHOP.items():
            eligible = shop_items if opts.include_cut_content else [
                n for n in shop_items if not ITEM_ITEMS[n].cut_content
            ]
            count = min(opts.starting_items.value, len(eligible))
            for item_name in self.random.sample(eligible, count):
                self.multiworld.push_precollected(self.create_item(item_name))
                self.precollected_names.add(item_name)

        # Pre-collect starting perks
        perk_names = list(PERK_ITEMS.keys())
        perk_start_count = min(opts.starting_perks.value, len(perk_names))
        for perk_name in self.random.sample(perk_names, perk_start_count):
            self.multiworld.push_precollected(self.create_item(perk_name))
            self.precollected_names.add(perk_name)

        # Sphere-based shops (if enabled): split every Common-tier item, and
        # separately every Common-tier perk, as evenly as possible into 3
        # groups. Rare/Ultra Rare items+perks aren't split - they're a
        # single combined "everything else" bucket the game mod opens up at
        # the last round bracket (65/70/75/79), not further divided.
        common_item_ids = [d.game_id for d in ITEM_ITEMS.values() if d.tier == 0]
        self.random.shuffle(common_item_ids)
        self.sphere_items = {1: [], 2: [], 3: []}
        for i, game_id in enumerate(common_item_ids):
            self.sphere_items[(i % 3) + 1].append(game_id)

        common_perk_ids = [d.perk_id for d in PERK_ITEMS.values() if d.tier == 0]
        self.random.shuffle(common_perk_ids)
        self.sphere_perks = {1: [], 2: [], 3: []}
        for i, perk_id in enumerate(common_perk_ids):
            self.sphere_perks[(i % 3) + 1].append(perk_id)

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
        # Cut-content items (Professor Palmy, Test Item 2) only exist in the
        # pool at all if include_cut_content is on.
        for name, data in ITEM_ITEMS.items():
            if data.cut_content and not self.options.include_cut_content:
                continue
            if name not in self.precollected_names:
                pool.append(self.create_item(name))

        # All perks, minus any already precollected as starting perks
        for name in PERK_ITEMS:
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
        total_locations = len(self.multiworld.get_unfilled_locations(self.player))
        filler_needed   = total_locations - len(pool)
        filler_names    = list(FILLER_ITEMS.keys()) * 3
        if self.options.include_traps:
            filler_names = filler_names + list(TRAP_ITEMS.keys())
        self.random.shuffle(filler_names)
        for i in range(max(0, filler_needed)):
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
        }
        if self.options.sphere_based_shops:
            data["sphere_based_shops"] = True
            data["sphere_items"] = self.sphere_items
            data["sphere_perks"] = self.sphere_perks
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
