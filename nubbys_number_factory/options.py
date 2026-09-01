"""
Options for Nubby's Number Factory Archipelago Randomizer
V3: Save-native rebuild. Options tied to undetectable/unapplicable content
    (shop purchases, starting money, death link) were removed - the client
    has no reliable way to honor them without in-game cooperation.
"""

from dataclasses import dataclass
from Options import Choice, PerGameCommonOptions, Range, Toggle


class SupervisorsRequired(Range):
    """
    How many Supervisor wins are required to complete the game.
    The server will randomly assign which Supervisors count toward the goal.
    Value must be between 1 and 11.
    """
    display_name = "Supervisors Required"
    range_start = 1
    range_end = 11
    default = 5


class SupervisorsInPool(Range):
    """
    How many Supervisor Unlock items are placed in the multiworld item pool.
    Must be >= SupervisorsRequired. Extra unlocks beyond the required count
    act as safety redundancy so the player isn't forced to receive every unlock.
    """
    display_name = "Supervisors In Pool"
    range_start = 1
    range_end = 11
    default = 8


class StartingSupervisors(Range):
    """
    How many Supervisors the player starts with already unlocked (chosen randomly
    from SV1..SV11 at generation time). Useful to ensure the player always has
    something to play on immediately.
    """
    display_name = "Starting Supervisors"
    range_start = 0
    range_end = 3
    default = 1


class StartingItems(Range):
    """
    How many items the player starts with already unlocked in EACH of the
    3 shops (Normal Shop, Black Market, Cafe) - chosen randomly and
    independently per shop, so every shop has real starting variety instead
    of only the one you happen to sample into.
    """
    display_name = "Starting Items Per Shop"
    range_start = 0
    range_end = 10
    default = 1


class StartingPerks(Range):
    """
    How many perks the player starts with already unlocked (chosen randomly
    at generation time).
    """
    display_name = "Starting Perks"
    range_start = 0
    range_end = 5
    default = 2


class IncludeChallenges(Toggle):
    """
    If enabled, Challenge completion locations are included in the randomizer.
    Challenges can be difficult; disable this for a shorter seed.
    """
    display_name = "Include Challenges"
    default = 1


class IncludeNubbyTrials(Toggle):
    """
    If enabled, Nubby Trials level completion locations are included.
    """
    display_name = "Include Nubby Trials"
    default = 1


class IncludePerks(Toggle):
    """
    If enabled, "AP Perk Purchase" locations are included for all tracked
    perks (sent by buying the dedicated AP Item shop slot while it
    represents that location - not detected via chest pickup), for up to
    27 additional checks.
    """
    display_name = "Include Perk Checks"
    default = 1


class IncludeRoundMilestones(Toggle):
    """
    If enabled, "Reach Round N" locations are included for the first-time
    completion of round 5, 10, 15, ... up to 80, for 16 additional checks.
    """
    display_name = "Include Round Milestone Checks"
    default = 1


class IncludeRestockMilestones(Toggle):
    """
    If enabled, locations are included for the first time a single round's
    restocks reach 1, 2, 5, 10, 50, 100, 500, 1000, 2000, 3000, 4000, 5000,
    6000, 7000, 8000, 9000, or 9999 - restocks only count together if
    obtained within the same round. 17 checks total.
    """
    display_name = "Include Restock Milestone Checks"
    default = 1


class PointsCheckCount(Range):
    """
    How many "Reach N Points" locations are included. Points accumulate
    from a lifetime total that persists across runs: completing a round
    awards 1 point for rounds 1-5, 2 points for rounds 6-10, 3 points for
    rounds 11-15, and so on, increasing by 1 every 5 rounds. Locations are
    placed for reaching 1, 2, 3, ... up to this many points.
    """
    display_name = "Points Check Count"
    range_start = 10
    range_end = 500
    default = 100


class ZoneBasedShops(Toggle):
    """
    If enabled, the Normal Shop's AP-tracked item pool is split as evenly
    as possible into 4 groups, one per zone (Zone 1 = rounds 1-20, Zone 2 =
    21-40, Zone 3 = 41-60, Zone 4 = 61-80) - only that zone's own group is
    purchasable while you're in it. Zone 5 (round 81+, endless) lifts the
    restriction entirely: every AP-tracked item becomes purchasable there.
    This paces which items can appear based on how far into a run the shop
    is, rather than the full unlocked pool being available from round 5
    onward. Only affects the Normal Shop - Black Market and Cafe are
    unaffected.
    """
    display_name = "Zone-Based Shops"
    default = 0


class LockZones(Toggle):
    """
    If enabled, advancing past round 20/40/60 (into Zone 2/3/4) requires a
    Progressive Zone Unlock item - each one received opens the next locked
    zone in sequence. Progress caps at the last round of your
    currently-unlocked zone until the next one arrives - you keep clearing
    that round's goal (with restocks) rather than getting stuck. Zone 1
    (rounds 1-20) is always accessible.
    """
    display_name = "Lock Zones Behind Items"
    default = 0


class LockZone5(Toggle):
    """
    If enabled (requires lock_zones), Zone 5 (round 81+, endless mode) is
    also gated behind the Progressive Zone Unlock sequence, as its final
    step. Has no effect if lock_zones is off.
    """
    display_name = "Also Lock Zone 5 (Endless)"
    default = 0


class LockGrabATron(Toggle):
    """
    If enabled, the Grab-A-Tron claw machine minigame (a random round
    event) does not appear until its Unlock item is received - the round
    that would have triggered it plays out as a normal round instead.
    """
    display_name = "Lock Grab-A-Tron Behind Item"
    default = 0


class LockBlackMarket(Toggle):
    """
    If enabled, the Black Market (triggered by using a found Suspicious
    Key on its round event) stays inert until its Unlock item is
    received - that round falls back to a normal shop instead, same as
    when the player has no key at all.
    """
    display_name = "Lock Black Market Behind Item"
    default = 0


class LockCafeNubby(Toggle):
    """
    If enabled, the Cafe door in the main room does not respond to clicks
    until its Unlock item is received.
    """
    display_name = "Lock Cafe Nubby Behind Item"
    default = 0


class LockNubbyTrials(Toggle):
    """
    If enabled, the Nubby Trials tile (Challenge slot 10) cannot be
    entered until its Unlock item is received, on top of the game's own
    5-challenges-beaten requirement.
    """
    display_name = "Lock Nubby Trials Behind Item"
    default = 0


class LockChallenges(Toggle):
    """
    If enabled, Challenge mode as a whole is locked from a fresh room
    (mirrors the existing Supervisor/Tony lock mechanism - edits the
    game's own obj_GAME.U_ChallengeMode save flag directly) until its
    Unlock item is received.
    """
    display_name = "Lock Challenges Behind Item"
    default = 0


class LockFreezeAbility(Toggle):
    """
    If enabled, the game's own shop-offer-freeze ability (drag an offer
    onto the freeze button to keep it from rerolling next visit) is
    disabled until its Unlock item is received.
    """
    display_name = "Lock Freeze Ability Behind Item"
    default = 0


class IncludeTraps(Toggle):
    """
    If enabled, trap items are added to the pool: deletes an item from
    inventory, deletes all coins, reduces lives to 1, disables a random
    item for 5 rounds, or forces a random special event next round
    (delayed a round if the next round is already a special event).
    """
    display_name = "Include Traps"
    default = 0


class NubbyFillerPercent(Range):
    """
    What percentage of the pool's padding slots are "Filler: Nubby" -
    a no-op filler item that does nothing on arrival.
    """
    display_name = "Nubby Filler Percent"
    range_start = 0
    range_end = 100
    default = 20


# DISABLED (kept for later re-wiring, not deleted) - see the matching
# blocks in items.py/__init__.py/NubbyClient.py and the #if false blocks
# in the GML master script. To re-enable: uncomment this class, add
# `include_cut_content: IncludeCutContent` back to NNFOptions below, and
# restore the matching blocks in the other files.
# class IncludeCutContent(Toggle):
#     """
#     If enabled, two fully-implemented but pool-disabled vanilla items
#     (Professor Palmy, Test Item 2 - shipped with InItemPool hardcoded to
#     0, excluding them from every shop) and six restored demo-exclusive
#     perks (Gambley/Jittery/Lucky/Rocky/Wizardry/Silly, rebuilt from the
#     wiki since they have no trace in this game's own files) are added to
#     the randomized pool as real obtainable items/perks.
#     """
#     display_name = "Include Cut Content"
#     default = 0


class CustomFinalRound(Range):
    """
    If set above 80, the final boss encounter and win condition move to
    this round instead of the vanilla round 80, and round 80 itself
    becomes a regular special event round instead of the boss fight.
    0 (default) keeps vanilla behavior (boss/win at round 80).
    """
    display_name = "Custom Final Round"
    range_start = 0
    range_end = 300
    default = 0


class ScoreGoal(Range):
    """
    If set above 0, completing the game also requires accumulating this
    many total points (the game's own AllPoints run score, summed across
    every run played in this room) as an alternative path to victory
    alongside beating the required Supervisors. 0 (default) disables this
    goal entirely.
    """
    display_name = "Score Goal"
    range_start = 0
    range_end = 1_000_000_000
    default = 0


class IncludeItemPurchases(Toggle):
    """
    If enabled, each round that hosts a shop sells exactly one Archipelago
    item; once bought, that round's shop never offers another for the rest
    of the game. Up to 69 checks total.
    """
    display_name = "Include Shop Item Checks"
    default = 1


@dataclass
class NNFOptions(PerGameCommonOptions):
    supervisors_required: SupervisorsRequired
    supervisors_in_pool: SupervisorsInPool
    starting_supervisors: StartingSupervisors
    starting_items: StartingItems
    starting_perks: StartingPerks
    include_challenges: IncludeChallenges
    include_nubby_trials: IncludeNubbyTrials
    include_item_purchases: IncludeItemPurchases
    include_perks: IncludePerks
    include_round_milestones: IncludeRoundMilestones
    include_restock_milestones: IncludeRestockMilestones
    points_check_count: PointsCheckCount
    # include_cut_content: IncludeCutContent  # DISABLED (kept for later re-wiring, not deleted)
    zone_based_shops: ZoneBasedShops
    lock_zones: LockZones
    lock_zone5: LockZone5
    lock_grabatron: LockGrabATron
    lock_black_market: LockBlackMarket
    lock_cafe_nubby: LockCafeNubby
    lock_nubby_trials: LockNubbyTrials
    lock_challenges: LockChallenges
    lock_freeze_ability: LockFreezeAbility
    include_traps: IncludeTraps
    nubby_filler_percent: NubbyFillerPercent
    custom_final_round: CustomFinalRound
    score_goal: ScoreGoal
