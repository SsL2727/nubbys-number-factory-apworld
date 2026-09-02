"""
Regions for Nubby's Number Factory Archipelago Randomizer
V4: Save-native rebuild - Zone regions removed (no persisted signal exists
    for them). Shop is back, since purchases are now detected via a one-shot
    purchase signal file from the dedicated AP Item shop slot.

Region structure:
  Menu
    └─ Supervisor 1..11  (each requires its Supervisor Unlock item)
    └─ Challenges        (accessible once U_ChallengeMode is seen)
    └─ Nubby Trials      (accessible once U_NubbyTrials is seen)
    └─ Shop              (accessible from the start)
"""

from typing import Dict
from BaseClasses import MultiWorld, Region, Entrance, Location
from .locations import (
    NNFLocationData, SUPERVISOR_LOCATIONS, CHALLENGE_LOCATIONS,
    NUBBY_TRIALS_LOCATIONS, SHOP_LOCATIONS,
    ROUND_MILESTONE_LOCATIONS, RESTOCK_MILESTONE_LOCATIONS, POINTS_LOCATIONS
)
from .options import NNFOptions


class NNFLocation(Location):
    game: str = "Nubby's Number Factory"


def create_regions(world: MultiWorld, player: int, options: NNFOptions, goal_supervisors) -> None:
    """
    Build all regions and connect them with access rules.

    goal_supervisors is generate_early's random.sample(range(1, 12), ...) -
    the SV indices actually in scope for this seed. Only those get a region/
    location/entrance: create_items only ever creates "Supervisor N Unlock"
    items for indices in this set, so a supervisor left out of it can never
    be unlocked - building its "Win Supervisor N" location and entrance
    anyway (the old unconditional range(1, 12) here) left that location
    permanently unreachable whenever supervisors_in_pool < 11, which the
    fuzzer caught immediately as a hard FillError ("Could not access
    required locations for accessibility check").
    """

    # ── Create region objects ─────────────────────────────────────────────────
    menu_region = Region("Menu", player, world)

    supervisor_regions: Dict[int, Region] = {}
    for sv_num in goal_supervisors:
        supervisor_regions[sv_num] = Region(f"Supervisor {sv_num}", player, world)

    challenges_region = Region("Challenges", player, world)
    trials_region     = Region("Nubby Trials", player, world)
    shop_region       = Region("Shop", player, world)
    milestones_region = Region("Milestones", player, world)
    restocks_region   = Region("Restocks", player, world)
    points_region     = Region("Points", player, world)

    # ── Add location objects to regions ──────────────────────────────────────
    def _add_locations(region: Region, loc_dict: Dict[str, NNFLocationData]) -> None:
        for name, data in loc_dict.items():
            loc = NNFLocation(player, name, data.code, region)
            region.locations.append(loc)

    # Supervisor win locations — one per SV region
    for sv_num, region in supervisor_regions.items():
        sv_locs = {
            k: v for k, v in SUPERVISOR_LOCATIONS.items()
            if v.region == f"Supervisor {sv_num}"
        }
        _add_locations(region, sv_locs)

    # Optional location pools
    if options.include_challenges:
        _add_locations(challenges_region, CHALLENGE_LOCATIONS)

    if options.include_nubby_trials:
        _add_locations(trials_region, NUBBY_TRIALS_LOCATIONS)

    if options.include_item_purchases:
        _add_locations(shop_region, SHOP_LOCATIONS)

    if options.include_round_milestones:
        _add_locations(milestones_region, ROUND_MILESTONE_LOCATIONS)

    if options.include_restock_milestones:
        _add_locations(restocks_region, RESTOCK_MILESTONE_LOCATIONS)

    points_subset = {
        name: data for name, data in list(POINTS_LOCATIONS.items())[:options.points_check_count.value]
    }
    _add_locations(points_region, points_subset)

    # ── Connect Menu → Supervisor regions (require unlock item) ───────────────
    for sv_num, sv_region in supervisor_regions.items():
        entrance = Entrance(player, f"Unlock Supervisor {sv_num}", menu_region)
        entrance.access_rule = lambda state, n=sv_num: \
            state.has(f"Supervisor {n} Unlock", player)
        menu_region.exits.append(entrance)
        entrance.connect(sv_region)

    # ── Connect Menu → Challenges / Trials / Shop / Milestones / Restocks / Points (always accessible) ─
    for region in [challenges_region, trials_region, shop_region, milestones_region, restocks_region, points_region]:
        entrance = Entrance(player, f"Access {region.name}", menu_region)
        entrance.access_rule = lambda state: True
        menu_region.exits.append(entrance)
        entrance.connect(region)

    # ── Register all regions with the multiworld ──────────────────────────────
    world.regions += [menu_region, challenges_region, trials_region, shop_region, milestones_region, restocks_region, points_region]
    world.regions += list(supervisor_regions.values())
