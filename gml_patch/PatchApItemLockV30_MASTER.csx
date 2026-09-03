using System;
using System.IO;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;

EnsureDataLoaded();

// NNF_DECOMP_FOLDER lets the AP client's automated /patch command point this
// at a per-run temp folder it decompiled itself on the end user's machine,
// without touching this default (my own dev-machine path, used for every
// manual rebuild this project's development has relied on all along).
string decompFolder = Environment.GetEnvironmentVariable("NNF_DECOMP_FOLDER") ?? @"F:\Nubby's Number Facotry AP\decompiled_source";

// --- CHANGED in V20: item-pool lock/unlock + empty-tier safety net, PLUS a
// Black-Market-specific safety net (V12, corrected here). V12 originally
// force-unlocked extra items to guarantee >=3 DISTINCT eligible items per
// BM tier, to stop scr_GiveItemBM's do...until (which requires all 3
// offered slots be distinct from each other) from spinning forever when
// too few real items were unlocked - same failure class as the V6 perk
// freeze, just 3-way instead of 2-way distinctness. V16 later fixed the
// actual root cause directly in scr_GiveItemBM (bypasses the distinctness
// requirement whenever fewer than 3 total eligible items exist, allowing
// the same item to legitimately repeat across all 3 slots) - but V12's
// safety net was never revisited, and kept unconditionally topping up to
// 3 distinct items regardless, which meant the precondition for V16's fix
// to ever activate (""fewer than 3 eligible"") could never actually occur -
// directly defeating the user's explicit ""start with 1 item, shown as 3
// copies"" request, reported as ""Black market still has 3 unique items
// before any items were sent"". Re-derived what the safety net actually
// needs to guarantee by re-reading scr_GiveItemBM's own tier-fallback
// logic: it already redirects an empty Rare pick to Common
// (""if (ds_list_size(_RareArrayITEM) <= 0) { _TarList = _ComnArrayITEM; }"")
// but has no equivalent fallback if Common itself is empty - so the ONLY
// real crash-safety invariant is ""Common (tier 0) must never be fully
// empty""; Rare (tier 1) needs no forced backfill at all, vanilla already
// handles that gracefully. Reduced accordingly: tier 0 only, threshold
// >=1 instead of >=3, tier 1 left completely untouched.
//
// --- CHANGED in V35: the general per-(shop,tier) ""coverage"" safety net
// below (the for _shopPool/_tier loop, distinct from the BM-specific one
// above) force-unlocks one real item whenever a shop+tier combo has zero
// AP-eligible items, to stop scr_GiveItem's own do...until from spinning on
// an empty candidate list (same failure class again). Root-caused a real
// report (""received Lobster Claw in the shop without unlocking it, tracker
// correctly says I don't have it"") to this exact mechanism - Lobster Claw
// (Normal shop, Rare tier) was almost certainly this loop's fallback pick
// whenever Normal-shop-Rare ran empty early in a seed. Same fix as V32/33's
// obj_RerollBtn_Step_0 fix and the perk fix just above: patched the actual
// consumer (scr_GiveItem, this session, see the giveItemTierFallback block
// below) to gracefully fall back across Common/Rare/Ultra Rare within the
// SAME shop instead of crashing on an empty tier - so Normal shop no longer
// needs this net's help at all. Disabled the force-unlock specifically for
// _shopPool == 1 (Normal) only; Black Market (2, already covered
// separately above) and Cafe (3, no equivalent consumer-side fix audited/
// applied this session) are left exactly as before, still relying on this
// net as their backstop.
string lockBlock = @"
    var _apItemIds = [0,2,3,5,9,10,11,12,14,16,17,21,22,23,24,30,33,35,37,40,41,44,48,55,65,67,149,4,8,20,29,31,52,53,56,32,59,6,27,34,43,47,51,60,66,15,42,49,54,58,151,7,36,45,46,50,129,136,148,179,1,13,18,38,61,137,138,19,146];
    if (!variable_instance_exists(id, ""ApOriginalItemPool""))
    {
        ApOriginalItemPool = array_create(array_length(InItemPool));
        for (var _oi = 0; _oi < array_length(InItemPool); _oi += 1)
        {
            ApOriginalItemPool[_oi] = InItemPool[_oi];
        }
    }
    if (file_exists(""NubbyAP/ap_item_pool.txt""))
    {
        for (var _ai = 0; _ai < array_length(_apItemIds); _ai += 1)
        {
            InItemPool[_apItemIds[_ai]] = 0;
        }
        var _apFile = file_text_open_read(""NubbyAP/ap_item_pool.txt"");
        while (!file_text_eof(_apFile))
        {
            var _apLine = string_trim(file_text_readln(_apFile));
            if (string_length(_apLine) > 0)
            {
                var _apId = real(_apLine);
                if (_apId >= 0 && _apId < array_length(InItemPool))
                {
                    InItemPool[_apId] = ApOriginalItemPool[_apId];
                }
            }
        }
        file_text_close(_apFile);

        for (var _shopPool = 1; _shopPool <= 3; _shopPool += 1)
        {
            for (var _tier = 0; _tier <= 2; _tier += 1)
            {
                var _covered = false;
                for (var _ci = 0; _ci < array_length(ItemTier); _ci += 1)
                {
                    var _matchesPool = (InItemPool[_ci] == _shopPool) || (_shopPool != 3 && InItemPool[_ci] == 4);
                    if (ItemTier[_ci] == _tier && _matchesPool)
                    {
                        _covered = true;
                        break;
                    }
                }
                if (!_covered && _shopPool != 1)
                {
                    // V51: was picking the first array match deterministically
                    // (same item every time this fallback fired) - confirmed
                    // root cause of ""first available item in every shop is
                    // always the same one repeated"" (starting_items defaults
                    // to just 1 per shop, so this fallback fires constantly
                    // early on, and scr_GiveItem's own random pick only ever
                    // had this one deterministically-chosen candidate to
                    // choose from). Collect every match, then pick randomly.
                    var _apCandidates = array_create(0);
                    for (var _fi = 0; _fi < array_length(_apItemIds); _fi += 1)
                    {
                        var _fid = _apItemIds[_fi];
                        var _origMatches = (ApOriginalItemPool[_fid] == _shopPool) || (_shopPool != 3 && ApOriginalItemPool[_fid] == 4);
                        if (ItemTier[_fid] == _tier && _origMatches)
                        {
                            array_push(_apCandidates, _fid);
                        }
                    }
                    if (array_length(_apCandidates) > 0)
                    {
                        var _apPick = _apCandidates[irandom_range(0, array_length(_apCandidates) - 1)];
                        InItemPool[_apPick] = ApOriginalItemPool[_apPick];
                    }
                }
            }
        }

        {
            // --- NEW in V36: fixed a real crash regression from V35's own
            // fix above - disabling the per-tier top-up for _shopPool == 1
            // entirely (relying on scr_GiveItem's new cross-tier fallback
            // instead) removed the ONLY thing that guaranteed the Normal
            // shop had ANY eligible item at all. scr_GiveItem's fallback
            // can gracefully move from an empty tier to a DIFFERENT
            // non-empty one, but if ALL THREE tiers are simultaneously
            // empty - completely plausible at the very first Normal Shop
            // opening (round 5) in a fresh room where nothing's been
            // unlocked yet - there's nothing left to fall back to, and the
            // exact same DoConv crash resurfaces one level down (confirmed
            // via a real report: ""got this error when loading the round 5
            // shop""). This does NOT reintroduce the Lobster Claw bug V35
            // fixed - that was one tier (Rare) running dry while others
            // still had eligible items, which scr_GiveItem now handles on
            // its own without needing this at all; this only fires in the
            // strictly worse ""the entire Normal shop has zero eligible
            // items anywhere"" case, which nothing can gracefully invent
            // its way around.
            var _normalCovered = false;
            for (var _nci = 0; _nci < array_length(ItemTier); _nci += 1)
            {
                if (InItemPool[_nci] == 1 || InItemPool[_nci] == 4)
                {
                    _normalCovered = true;
                    break;
                }
            }
            if (!_normalCovered)
            {
                // V51: random pick instead of deterministic first-match (see
                // the matching V51 note above).
                var _apNormalCandidates = array_create(0);
                for (var _nfi = 0; _nfi < array_length(_apItemIds); _nfi += 1)
                {
                    var _nfid = _apItemIds[_nfi];
                    if (ApOriginalItemPool[_nfid] == 1 || ApOriginalItemPool[_nfid] == 4)
                    {
                        array_push(_apNormalCandidates, _nfid);
                    }
                }
                if (array_length(_apNormalCandidates) > 0)
                {
                    var _apNormalPick = _apNormalCandidates[irandom_range(0, array_length(_apNormalCandidates) - 1)];
                    InItemPool[_apNormalPick] = ApOriginalItemPool[_apNormalPick];
                }
            }
        }

        {
            var _bmEligible = 0;
            for (var _ci = 0; _ci < array_length(ItemTier); _ci += 1)
            {
                if (ItemTier[_ci] == 0 && (InItemPool[_ci] == 2 || InItemPool[_ci] == 4))
                {
                    _bmEligible += 1;
                }
            }
            if (_bmEligible < 1)
            {
                // V51: random pick instead of deterministic first-match (see
                // the matching V51 note above).
                var _apBmCandidates = array_create(0);
                for (var _fi = 0; _fi < array_length(_apItemIds); _fi += 1)
                {
                    var _fid = _apItemIds[_fi];
                    var _origBM = (ApOriginalItemPool[_fid] == 2 || ApOriginalItemPool[_fid] == 4);
                    if (ItemTier[_fid] == 0 && _origBM && InItemPool[_fid] != 2 && InItemPool[_fid] != 4)
                    {
                        array_push(_apBmCandidates, _fid);
                    }
                }
                if (array_length(_apBmCandidates) > 0)
                {
                    var _apBmPick = _apBmCandidates[irandom_range(0, array_length(_apBmCandidates) - 1)];
                    InItemPool[_apBmPick] = ApOriginalItemPool[_apBmPick];
                    _bmEligible += 1;
                }
            }
        }
    }
";

// V58, SIMPLIFIED IN V59: was a random pool of 12 sprites across 3 tiers;
// per explicit user request after seeing it in game, now always the one
// specific sprite confirmed as correct - obj_I_MysteryBoxComnNM's real
// sprite (""the box with the ? on it"" - the Common-tier Mystery Box from
// the Normal Shop, the shop the AP slot actually lives in), resolved via
// object_get_sprite() rather than a guessed raw sprite name so a wrong
// name can't silently break rendering. No more tier-based variation - one
// consistent look for every AP slot. Reused at all three sites that can
// ever be the one to actually apply the AP flavor (scr_GiveItem's own
// tier-sprite patch below, the legacy one-time Create_0 copy, and
// stepPatch's sync block) so all three stay in sync automatically.
// image_index is explicitly reset to 0 alongside sprite_index - a classic
// GameMaker gotcha (and the confirmed cause of a real report that the
// sprite ""only shows up after mouse-over""): draw_sprite_ext in this
// cell's own Draw_0 always draws sprite_index at the cell's CURRENT
// image_index, which was left over from whatever real item's sprite this
// slot originally rolled - if that item's sprite has more frames than
// this one and image_index was sitting on a frame beyond this sprite's
// own frame count, it can render blank until something else (like the
// hover code elsewhere touching the instance) happens to reset it.
// Setting it explicitly here removes the dependency on that coincidence.
string ApSpritePickBlock(string targetExpr)
{
    return @"
        " + targetExpr + @".sprite_index = object_get_sprite(obj_I_MysteryBoxComnNM);
        " + targetExpr + @".image_index = 0;
";
}

// --- CHANGED in V15: dedicated AP Item slot, forced-cell fallback used by
// obj_ItemMGMT_Create_0 / Step_0 (redundant safety net; scr_GiveItem is the
// primary mechanism). Fixed a real bug: this only ever checked
// global.PauseGame == true, which is also true while the Black Market or
// Cafe is open (both set it), so the Step_0 timer could force an AP item
// cell into the Normal Shop's slot-1 screen position even while Black
// Market was the thing actually open - a visible, purchasable 4th BM item
// that was never supposed to exist there (item 181's own vanilla
// InItemPool is 0, confirmed via decompile, so it was never eligible
// through Black Market's own real roll - this safety net was the only
// source of the bug). Now also requires InItemSeq == true && InBMSeq ==
// false, the same ""is the Normal Shop specifically open"" condition already
// used by the click-purchase code. Also sets a fixed flavor description
// instead of item 181's real (irrelevant) vanilla one.
string apItemBlock = @"
    if (file_exists(""NubbyAP/ap_item_name.txt""))
    {
        var _apNameFile = file_text_open_read(""NubbyAP/ap_item_name.txt"");
        var _apName = string_trim(file_text_readln(_apNameFile));
        file_text_close(_apNameFile);
        if (string_length(_apName) > 0)
        {
            ItemID[181] = _apName;
            ItemDesc[181] = ""A mysterious finger. Who knows what world it is pointing to. Buy it to find out."";
            if (global.PauseGame == true && InItemSeq == true && InBMSeq == false)
            {
                var _apExisting = instance_position(790, 679, obj_ItemOfferCell);
                var _apNeedsCreate = true;
                if (_apExisting != noone)
                {
                    if (_apExisting.OfferHeldItem == 181)
                    {
                        _apNeedsCreate = false;
                    }
                    else
                    {
                        instance_destroy(_apExisting);
                    }
                }
                if (_apNeedsCreate)
                {
                    ItemOfferId1 = 181;
                    var _apCell = instance_create_depth(790, 679, depth - 2, obj_ItemOfferCell);
                    _apCell.ItemSlotNum = 1;
                    _apCell.IOCX = 790;
                    _apCell.IOCY = 679;
                    _apCell.OfferHeldItem = 181;
                    _apCell.IOCReveal = true;
                    _apCell.sprite_index = object_get_sprite(ItemObj[181]);
                }
            }
        }
    }
";

// --- REMOVED in V21: the permanent starting money/lives bonus used to be
// injected here (obj_ItemMGMT_Create_0), added relatively (""global.Money +=
// _bonusMoney""). Root-caused ""extra starting lives and coins don't work"":
// obj_ItemMGMT is created once per ROOM VISIT (it builds ~150 items via
// scr_Init_Item calls, far too much to redo every run), but global.Money/
// Lives get reset to their vanilla defaults on every INDIVIDUAL RUN/shift
// (obj_LvlMGMT_Create_0's own ""global.Money = 3;"", and New Game/Restart
// Run's own scr_EditLives/scr_EditMaxLives calls) - so the bonus applied
// correctly exactly once, on the room's first run, and was silently wiped
// by the vanilla reset on every run after that, with nothing re-applying
// it. Moved to obj_LvlMGMT_Create_0 below - the one object actually
// recreated on every run/shift regardless of which button triggered it
// (Start Shift/Challenge Go/Load Game/New Game/Restart Run all end up
// creating a fresh obj_LvlMGMT) - and rewritten to use ABSOLUTE sets
// instead of relative adds, since Start Shift/Challenge Go/Load Game don't
// separately pre-reset money/lives the way New Game/Restart Run do; a
// relative add running on every one of those would silently accumulate
// across shifts within the same room visit instead of applying once per
// run. Setting the full intended value directly (baseline + bonus) is
// idempotent regardless of how many times or from which flow this fires.

string createPath = Path.Combine(decompFolder, "gml_Object_obj_ItemMGMT_Create_0.gml");
string createOriginal = File.ReadAllText(createPath);
string createCombined = createOriginal.TrimEnd()
    + "\n// === NubbyAP item-pool lock (injected) ===\n" + lockBlock
    + "\n// === NubbyAP AP Item slot (injected) ===\n" + apItemBlock;

string stepPath = Path.Combine(decompFolder, "gml_Object_obj_ItemMGMT_Step_0.gml");
string stepOriginal = File.ReadAllText(stepPath);
// V48: dropped apItemBlock from this periodic re-check (kept only
// lockBlock). apItemBlock's job here was a "safety net" that kept
// re-forcing slot 1 back to item 181 every ~3 seconds for as long as
// ap_item_name.txt existed - originally covering a reroll bypass, which
// V32 already fixed directly at the reroll site (see rerollIdReplacement
// below), so this had become pure redundant re-assertion. The real,
// reported symptom: after buying the AP item, the slot should go empty
// like any other purchased shop slot until the next real restock/reroll -
// instead this timer was recreating a NEW AP-flavored cell there within
// 3 seconds regardless of whether a real restock happened, making the
// shop look like it sells unlimited AP items. scr_GiveItem's restock-time
// injection (giveItemPatch) and obj_RerollBtn_Step_0's reroll-time
// injection remain the only two ways slot 1 ever gets (re)populated with
// item 181 - both are genuine "a real refresh happened" events, unlike
// this timer.
string stepPatch = @"
// === NubbyAP periodic item-pool refresh (injected) ===
if (!variable_instance_exists(id, ""_apPoolTimer""))
{
    _apPoolTimer = 0;
}
_apPoolTimer += 1;
if (_apPoolTimer >= room_speed * 3)
{
    _apPoolTimer = 0;
" + lockBlock + @"
}
// === NubbyAP shop AP-slot sync (injected) ===
// V55 REWRITE: the V52-54 ""arm/disarm"" design (only re-check while
// obj_GAME._apShopCatchupRound had just been armed by a FAILED attempt in
// giveItemPatch) turned out to still leave the AP slot missing on a
// shop's first view in real play (confirmed via a user-provided video: 7+
// seconds with a completely normal item in slot 1, no AP flavor at all,
// until a manual reroll). Rather than continue chasing the exact reason
// that specific arm/disarm timing failed, this replaces it with a
// strictly simpler, timing-independent invariant: every ~0.1s, if the
// client has a check available for THIS shop visit AND we haven't
// already shown it yet this visit, show it - full stop, no arming step,
// nothing that depends on catching a failed attempt at exactly the right
// moment.
//
// obj_GAME._apShopSlotShown is the one-shot guard that makes this safe:
// giveItemPatch resets it to false at the TOP of every real restock (a
// new shop visit beginning), and this block - like giveItemPatch itself -
// sets it true the moment the AP flavor is actually applied. Once true,
// this check does nothing else for the rest of the visit, so it can
// never re-populate an already-shown-and-since-purchased slot (the exact
// ""unlimited AP items"" failure mode V51 fixed) - it only ever
// transitions false -> true, never back, and only the next real restock
// (a genuinely new visit) resets it.
//
// Per explicit user request, this is ALSO now the ONLY way the AP flavor
// can appear at all - obj_RerollBtn_Step_0's own separate re-injection
// (which used to force the AP flavor back onto slot 1 after every
// reroll) has been removed entirely (see the rerollIdReplacement section
// below), so once _apShopSlotShown flips true, even a later reroll
// within the same visit will NOT bring the AP flavor back - the shop
// settles into selling only normal, already-unlocked items for the rest
// of that visit, exactly like any other shop slot after its one chance
// has passed.
if (!variable_instance_exists(id, ""_apSyncTimer""))
{
    _apSyncTimer = 0;
}
_apSyncTimer += 1;
if (_apSyncTimer >= 6)
{
    _apSyncTimer = 0;
    // V56 TEMPORARY diagnostic - this heartbeat fires every ~0.1s
    // regardless of _apShopSlotShown, specifically so it can confirm
    // whether obj_ItemMGMT_Step_0 (and this code) is even running at all
    // during a shop visit, independent of whether the sync below has
    // already succeeded. Cross-reference against NubbyClient.py's
    // matching _debug_log calls in ap_debug.txt. Safe to remove once
    // root-caused; append-only, read by nothing.
    var _apDbgFile1 = file_text_open_append(""NubbyAP/ap_gml_debug.txt"");
    file_text_write_string(_apDbgFile1, ""stepSync round="" + string(global.CurrentRnd) + "" shown="" + string(obj_GAME._apShopSlotShown) + "" fileExists="" + string(file_exists(""NubbyAP/ap_item_name.txt"")) + ""\n"");
    file_text_close(_apDbgFile1);
    if (!obj_GAME._apShopSlotShown)
    {
        if (file_exists(""NubbyAP/ap_item_name.txt""))
        {
            var _apSyncFile = file_text_open_read(""NubbyAP/ap_item_name.txt"");
            var _apSyncName = string_trim(file_text_readln(_apSyncFile));
            file_text_close(_apSyncFile);
            if (string_length(_apSyncName) > 0)
            {
                ItemID[181] = _apSyncName;
                ItemDesc[181] = ""A mysterious finger. Who knows what world it is pointing to. Buy it to find out."";
                ItemOfferId1 = 181;
                FrozenItem[1] = -1;
                // Same OfferHeldItem desync fix as giveItemPatch below -
                // this path also mutates ItemOfferId1 on an
                // ALREADY-EXISTING cell, so the cell's own cached
                // OfferHeldItem (what the V11 click-purchase patch
                // actually gates on) needs the same explicit sync or
                // clicking it falls through to a normal purchase instead
                // of sending a check (""scary finger in my inventory"").
                // V57 FIX: was instance_position(ItemX[1], ItemY[1], ...) -
                // ItemX[]/ItemY[] are BOARD peg-slot coordinates, a
                // completely different array from IOCXCoord[]/IOCYCoord[]
                // (the actual shop OFFER CELL screen position used at cell
                // creation - see giveItemAnchor above). That meant this
                // instance_position call was searching the wrong
                // coordinates entirely and always found noone - so
                // ItemOfferId1/ItemID[181] got updated correctly (confirmed
                // via ap_gml_debug.txt: _apShopSlotShown DOES flip true
                // shortly after the file appears) but the actual visible
                // cell's OfferHeldItem never did, so the shop looked
                // completely unchanged and clicking it fell through to a
                // normal purchase of the real underlying item (181,
                // reflavored) instead of the AP click-purchase branch -
                // confirmed root cause of the ""got the finger again""
                // report, and why round_shop_state.json's consumed list
                // never gained an entry across an entire session's logs
                // despite passing through rounds 5, 10, and 19.
                var _apSyncCell = instance_position(IOCXCoord[1], IOCYCoord[1], obj_ItemOfferCell);
                if (_apSyncCell != noone)
                {
                    _apSyncCell.OfferHeldItem = 181;
                    " + ApSpritePickBlock("_apSyncCell") + @"
                    // V60 FIX: this was previously set unconditionally right
                    // after this if-block, regardless of whether the lookup
                    // above actually found the cell. On a real shop visit
                    // the lookup can fail on the tick(s) right after a
                    // restock (the cell/IOCXCoord[] briefly not lined up yet
                    // during the shop's own setup) - once _apShopSlotShown
                    // latched true, the outer gate above permanently skipped
                    // retrying for the rest of the visit even though nothing
                    // was ever actually applied. Confirmed root cause of a
                    // real report: the AP slot displayed as the real item it
                    // originally rolled (e.g. ""Finger Puppet"", not clickable
                    // as an AP purchase), yet dragging it into inventory
                    // still produced the AP item/name - because
                    // ItemOfferId1/ItemID[181] (plain globals, set
                    // unconditionally above) were correct all along, but
                    // this specific cell's own OfferHeldItem/sprite were
                    // never touched. Moving this inside the if-block means a
                    // failed lookup keeps retrying every ~0.1s until the
                    // cell is actually found and updated, instead of giving
                    // up forever after one unlucky tick.
                    obj_GAME._apShopSlotShown = true;
                }
            }
        }
    }
}
";
string stepCombined = stepOriginal.TrimEnd() + "\n" + stepPatch;

// --- Unchanged from V11, plus a fixed flavor description: force the AP
// Item into shop slot 1 at the moment the shop is actually (re)stocked. ---
string giveItemAnchor = "        var _OfferSlot1 = instance_create_depth(obj_ItemMGMT.IOCXCoord[1], obj_ItemMGMT.IOCYCoord[1], obj_ItemMGMT.depth - 2, obj_ItemOfferCell);";
string giveItemPatch = @"
        // V55: obj_GAME._apShopSlotShown is reset false here, at the top of
        // every real restock - marking ""a new shop visit is starting,
        // nothing shown yet"" - before attempting the injection below. See
        // stepPatch's matching sync block for the full explanation of why
        // this replaced the old arm/disarm design, and why this flag (not
        // ItemOfferId1's current value) is what makes repeated showings
        // impossible within one visit.
        obj_GAME._apShopSlotShown = false;
        // V56 TEMPORARY diagnostic - see NubbyClient.py's matching
        // _debug_log calls. Safe to remove once root-caused; append-only,
        // read by nothing.
        var _apDbgFile0 = file_text_open_append(""NubbyAP/ap_gml_debug.txt"");
        file_text_write_string(_apDbgFile0, ""giveItemPatch round="" + string(global.CurrentRnd) + "" fileExists="" + string(file_exists(""NubbyAP/ap_item_name.txt"")) + ""\n"");
        file_text_close(_apDbgFile0);
        if (file_exists(""NubbyAP/ap_item_name.txt""))
        {
            var _apNameFile = file_text_open_read(""NubbyAP/ap_item_name.txt"");
            var _apName = string_trim(file_text_readln(_apNameFile));
            file_text_close(_apNameFile);
            if (string_length(_apName) > 0)
            {
                obj_ItemMGMT.ItemID[181] = _apName;
                obj_ItemMGMT.ItemDesc[181] = ""A mysterious finger. Who knows what world it is pointing to. Buy it to find out."";
                obj_ItemMGMT.ItemOfferId1 = 181;
                obj_ItemMGMT.FrozenItem[1] = -1;
                obj_GAME._apShopSlotShown = true;
            }
        }
" + giveItemAnchor;

string giveItemPath = Path.Combine(decompFolder, "gml_GlobalScript_scr_GiveItem.gml");
string giveItemOriginal = File.ReadAllText(giveItemPath);

// --- NEW in V35: fixed the REAL root cause of the item-side equivalent of
// the perk bug above ("Lobster Claw usable in the shop, never shown
// unlocked on the tracker") - this is scr_GiveItem's own per-slot picking
// loop (identical shape to obj_RerollBtn_Step_0's, fixed in V32/33: pick a
// random tier by odds, then do...until an eligible item turns up in that
// tier's list, no fallback if the whole list is empty). AP's item-lock
// feature can legitimately leave one shop's one tier with zero eligible
// items early in a run, and with no fallback this would hit the exact same
// DoConv ""illegal undefined/null use"" crash already fixed in reroll -
// which is almost certainly WHY lockBlock (top of this file) has its own
// per-(shop,tier) coverage safety net force-unlocking one real item
// whenever a combination would otherwise be completely empty: it's not
// there to reveal anything, it's there because nothing downstream could
// otherwise survive that tier being empty. Same fix as everywhere else in
// this file that's hit this exact problem (obj_RerollBtn_Step_0's tier
// fallback, obj_Chest_Step_0's distinctness fallback): patch the actual
// consumer to tolerate an empty tier gracefully - here, fall back through
// Common -> Rare -> Ultra Rare (whichever is non-empty) instead of forcing
// a specific item to stay perpetually unlocked to avoid the crash. This
// doesn't remove lockBlock's coverage safety net (still a correct backstop
// for the truly-degenerate ""every tier of every shop is empty"" case,
// which nothing here can rule out with total certainty) but should mean it
// almost never actually needs to fire in practice, since the far more
// common ""this one shop's one tier is empty"" case no longer needs it.
string giveItemTarListAnchor = "                var _Option = ds_list_find_value(_TarList, irandom_range(0, ds_list_size(_TarList) - 1));";
ThrowIfMissingOrAmbiguous2(giveItemOriginal, giveItemTarListAnchor, "scr_GiveItem tier fallback anchor");
string giveItemTierFallback = @"                if (ds_list_size(_TarList) <= 0)
                {
                    if (ds_list_size(_ComnArrayITEM) > 0)
                    {
                        _TarList = _ComnArrayITEM;
                    }
                    else if (ds_list_size(_RareArrayITEM) > 0)
                    {
                        _TarList = _RareArrayITEM;
                    }
                    else if (ds_list_size(_UltraRareArrayITEM) > 0)
                    {
                        _TarList = _UltraRareArrayITEM;
                    }
                }
" + giveItemTarListAnchor;
string giveItemTierFixed = giveItemOriginal.Replace(giveItemTarListAnchor, giveItemTierFallback);

if (!giveItemTierFixed.Contains(giveItemAnchor))
{
    throw new Exception("scr_GiveItem anchor line not found - decompiled source may have changed.");
}
if (giveItemTierFixed.Split(new[] { giveItemAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("scr_GiveItem anchor line is not unique - refusing to patch ambiguously.");
}
string giveItemStep1 = giveItemTierFixed.Replace(giveItemAnchor, giveItemPatch);

// --- REWRITTEN in V37: zone-based shop restriction, replacing the V15
// sphere-based system entirely (removed per explicit user request - the
// sphere system tied restriction to specific round numbers and only
// split Common-tier items, leaving Rare/Ultra Rare as a single always-
// later bucket; the user asked for it gone in favor of a flat split
// across zones with zone 5 fully open). Uses the exact same global.Zone
// formula already established by the zone-lock feature in
// scr_CalcWinRound (""(CurrentRnd-1) div 20) + 1""). ap_zoneshopN_items.txt
// (N=1..4) lists whichever mix of tiers randomly landed in that zone's
// even split of the WHOLE AP-tracked item pool (items.py's generate_early)
// - no tier stratification, unlike the old sphere files. Zone 5 (round
// 81+) is left completely unrestricted - the zone-number check itself
// only ever looks for a file for zones 1-4, so zone 5 (and any_apShopZone
// outside 1-4) just skips the whole apparatus, matching ""all items
// purchasable in zone 5"" exactly. Implemented as the same temporary
// InItemPool save/restrict/restore wrapper the sphere system used, with
// its own per-tier safety net for the same reason (a zone's random split
// can legitimately leave a tier with zero eligible items, which would
// otherwise reproduce the exact empty-candidate-list crash fixed in V3).
string zoneShopStartAnchor = "        global.PauseGame = true;";
string zoneShopStartBlock = @"
        var _apZoneShopActive = false;
        var _apZoneShopBackup = -1;
        if (file_exists(""NubbyAP/ap_zone_shops.txt""))
        {
            var _apShopZone = ((global.CurrentRnd - 1) div 20) + 1;
            if (_apShopZone >= 1 && _apShopZone <= 4)
            {
                var _apZoneShopFile = ""NubbyAP/ap_zoneshop"" + string(_apShopZone) + ""_items.txt"";
                if (file_exists(_apZoneShopFile))
                {
                    _apZoneShopActive = true;
                    _apZoneShopBackup = array_create(array_length(obj_ItemMGMT.InItemPool));
                    for (var _si = 0; _si < array_length(obj_ItemMGMT.InItemPool); _si += 1)
                    {
                        _apZoneShopBackup[_si] = obj_ItemMGMT.InItemPool[_si];
                    }
                    var _apAllowed = array_create(array_length(obj_ItemMGMT.InItemPool), false);
                    var _apZSF = file_text_open_read(_apZoneShopFile);
                    while (!file_text_eof(_apZSF))
                    {
                        var _apLine = string_trim(file_text_readln(_apZSF));
                        if (string_length(_apLine) > 0)
                        {
                            var _apId = real(_apLine);
                            if (_apId >= 0 && _apId < array_length(_apAllowed))
                            {
                                _apAllowed[_apId] = true;
                            }
                        }
                    }
                    file_text_close(_apZSF);
                    for (var _si2 = 0; _si2 < array_length(obj_ItemMGMT.InItemPool); _si2 += 1)
                    {
                        if (!_apAllowed[_si2])
                        {
                            obj_ItemMGMT.InItemPool[_si2] = 0;
                        }
                    }
                    for (var _sTier = 0; _sTier <= 2; _sTier += 1)
                    {
                        var _sCovered = false;
                        for (var _sci = 0; _sci < array_length(obj_ItemMGMT.ItemTier); _sci += 1)
                        {
                            if (obj_ItemMGMT.ItemTier[_sci] == _sTier && (obj_ItemMGMT.InItemPool[_sci] == 1 || obj_ItemMGMT.InItemPool[_sci] == 4))
                            {
                                _sCovered = true;
                                break;
                            }
                        }
                        if (!_sCovered)
                        {
                            for (var _sfi = 0; _sfi < array_length(obj_ItemMGMT.ItemTier); _sfi += 1)
                            {
                                if (obj_ItemMGMT.ItemTier[_sfi] == _sTier && (_apZoneShopBackup[_sfi] == 1 || _apZoneShopBackup[_sfi] == 4))
                                {
                                    obj_ItemMGMT.InItemPool[_sfi] = _apZoneShopBackup[_sfi];
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
";
if (!giveItemStep1.Contains(zoneShopStartAnchor))
{
    throw new Exception("scr_GiveItem zoneShopStartAnchor not found - decompiled source may have changed.");
}
if (giveItemStep1.Split(new[] { zoneShopStartAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("scr_GiveItem zoneShopStartAnchor is not unique - refusing to patch ambiguously.");
}
string giveItemStep2 = giveItemStep1.Replace(zoneShopStartAnchor, zoneShopStartAnchor + zoneShopStartBlock);

string zoneShopEndAnchor = "        ds_list_destroy(_OptionList);";
string zoneShopEndBlock = @"
        if (_apZoneShopActive)
        {
            for (var _si4 = 0; _si4 < array_length(obj_ItemMGMT.InItemPool); _si4 += 1)
            {
                obj_ItemMGMT.InItemPool[_si4] = _apZoneShopBackup[_si4];
            }
        }
";
if (!giveItemStep2.Contains(zoneShopEndAnchor))
{
    throw new Exception("scr_GiveItem zoneShopEndAnchor not found - decompiled source may have changed.");
}
if (giveItemStep2.Split(new[] { zoneShopEndAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("scr_GiveItem zoneShopEndAnchor is not unique - refusing to patch ambiguously.");
}
string giveItemCombined = giveItemStep2.Replace(zoneShopEndAnchor, zoneShopEndAnchor + zoneShopEndBlock);

// --- NEW in V16: let Black Market start with just 1 real unlocked item,
// shown as 3 copies, instead of the V12 safety net force-unlocking 2 more
// distinct items to satisfy scr_GiveItemBM's own distinctness requirement.
// That requirement (""_Option != BMItemOfferId1/2/3"") is baked into the
// single until-condition shared by all 3 slot picks (same code, run 3x via
// the surrounding for-loop) - patched to bypass distinctness specifically
// when fewer than 3 distinct eligible items exist at all (ds_list_size on
// _BMItemList, the exact same eligible-items list the candidate-building
// loop above this already builds), so with only 1 truly eligible item the
// same one gets picked for all 3 slots instead of infinite-looping.
string giveItemBMPath = Path.Combine(decompFolder, "gml_GlobalScript_scr_GiveItemBM.gml");
string giveItemBMOriginal = File.ReadAllText(giveItemBMPath);
string bmUntilAnchor = "                until ((obj_ItemMGMT.InItemPool[_Option] == 2 || obj_ItemMGMT.InItemPool[_Option] == 4) && _Option != obj_BlackMarketMGMT.BMItemOfferId1 && _Option != obj_BlackMarketMGMT.BMItemOfferId2 && _Option != obj_BlackMarketMGMT.BMItemOfferId3);";
if (!giveItemBMOriginal.Contains(bmUntilAnchor))
{
    throw new Exception("scr_GiveItemBM anchor not found - decompiled source may have changed.");
}
if (giveItemBMOriginal.Split(new[] { bmUntilAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("scr_GiveItemBM anchor is not unique - refusing to patch ambiguously.");
}
string bmUntilPatched = "                until ((obj_ItemMGMT.InItemPool[_Option] == 2 || obj_ItemMGMT.InItemPool[_Option] == 4) && (ds_list_size(_BMItemList) < 3 || (_Option != obj_BlackMarketMGMT.BMItemOfferId1 && _Option != obj_BlackMarketMGMT.BMItemOfferId2 && _Option != obj_BlackMarketMGMT.BMItemOfferId3)));";
string giveItemBMCombined = giveItemBMOriginal.Replace(bmUntilAnchor, bmUntilPatched);

// --- NEW in V34: fixed obj_Chest_Step_0's perk-choice picker (vanilla,
// never touched by any prior patch) so the perk-pool safety net's threshold
// can be safely tightened from >=2 to >=1 per tier, matching the item-side
// V20 fix. Root cause of a real reported bug (""Ray Gun perk usable in-game,
// never shown as unlocked on the tracker""): a chest opening rolls TWO
// perk tiers independently (_ChosenTier1/_ChosenTier2, each its own
// irandom(1000) roll) and picks one eligible perk per tier via a plain
// do-until - ChosenPerk2's until additionally requires ""!= ChosenPerk1""
// with NO fallback if the two tiers happen to coincide. If that tier then
// has only 1 real AP-eligible perk, that do-until spins forever (a hang,
// worse than a crash) - so the safety net below was written to always keep
// >=2 eligible perks per tier, unconditionally, specifically to keep this
// loop from ever hanging. That's what was force-unlocking a 2nd, never-
// received perk (Ray Gun happened to be the one picked, being early in
// _apPerkIds) well before it was actually granted via AP. The item side
// hit the identical shape of problem (scr_GiveItemBM's do-until requiring
// 3 distinct items) and fixed the CONSUMER logic itself (V16) to tolerate
// a duplicate when too few distinct items exist, rather than papering over
// it by keeping extra items artificially unlocked - doing the exact same
// thing here: count how many distinct AP-eligible perks exist for
// _ChosenTier2 before the loop runs, and only require ChosenPerk2 !=
// ChosenPerk1 when there are actually 2+ to choose from (otherwise the
// same perk can legitimately appear as both choices, an acceptable minor
// cosmetic redundancy, exactly matching the BM item fallback's own
// tradeoff) - so the safety net no longer needs a >=2 floor to avoid a
// hang, and can be tightened to >=1, matching the item pattern exactly.
string chestStepPath = Path.Combine(decompFolder, "gml_Object_obj_Chest_Step_0.gml");
string chestStepOriginal = File.ReadAllText(chestStepPath);
string chosenPerk1UntilAnchor = "                until (obj_PerkMGMT.InPerkItemPool[ChosenPerk1] == 1 && obj_PerkMGMT.PerkTier[ChosenPerk1] == _ChosenTier1);";
ThrowIfMissingOrAmbiguous2(chestStepOriginal, chosenPerk1UntilAnchor, "obj_Chest_Step_0 ChosenPerk1 until");
string chosenPerk2DistinctBlock = chosenPerk1UntilAnchor + @"
                var _apPerk2Distinct = 0;
                for (var _apPti = 1; _apPti < array_length(obj_PerkMGMT.PerkID); _apPti += 1)
                {
                    if (obj_PerkMGMT.InPerkItemPool[_apPti] == 1 && obj_PerkMGMT.PerkTier[_apPti] == _ChosenTier2)
                    {
                        _apPerk2Distinct += 1;
                    }
                }";
string chestStepStep1 = chestStepOriginal.Replace(chosenPerk1UntilAnchor, chosenPerk2DistinctBlock);

string chosenPerk2UntilAnchor = "                until (obj_PerkMGMT.InPerkItemPool[ChosenPerk2] == 1 && ChosenPerk2 != ChosenPerk1 && obj_PerkMGMT.PerkTier[ChosenPerk2] == _ChosenTier2);";
string chosenPerk2UntilPatched = "                until (obj_PerkMGMT.InPerkItemPool[ChosenPerk2] == 1 && (_apPerk2Distinct < 2 || ChosenPerk2 != ChosenPerk1) && obj_PerkMGMT.PerkTier[ChosenPerk2] == _ChosenTier2);";
ThrowIfMissingOrAmbiguous2(chestStepStep1, chosenPerk2UntilAnchor, "obj_Chest_Step_0 ChosenPerk2 until");
string chestStepCombined = chestStepStep1.Replace(chosenPerk2UntilAnchor, chosenPerk2UntilPatched);

// --- CHANGED in V34: perk-pool lock/unlock + safety net, threshold reduced
// from >=2 to >=1 per tier now that obj_Chest_Step_0 (above) no longer
// needs the extra margin to avoid a hang. ---
string perkPath = Path.Combine(decompFolder, "gml_Object_obj_PerkMGMT_Create_0.gml");
string perkOriginal = File.ReadAllText(perkPath);

#if false
// DISABLED (kept for later re-wiring, not deleted) - registers the six
// restored demo-exclusive perks (ids 33-38, following on immediately from
// vanilla's own last id 32) - see the matching #if false block near the
// top of the script for their backing GameObjects/code. Names/
// descriptions/tiers are the wiki's own text verbatim. InPerkItemPool
// (arg6) is set to 1 directly here (real/eligible from the start, same as
// every genuinely-tracked vanilla perk) - unlike Palmy/Test Item 2 on the
// item side, these don't need an ApOriginalPerkPool override hack, since
// scr_Init_Perk itself is what sets the starting value and this is a
// brand-new registration under our own control. To re-enable: remove
// this #if false/#endif AND swap the active `perkBlock`/`perkCombined`
// declarations just below back to use `perkWithNewRegistrations` and the
// 34-id `_apPerkIds` array (both shown here, currently unused while
// disabled).
string newPerkRegistrations = @"
scr_Init_Perk(33, ""The Gambley Perk"", obj_Perk_Gambley, ""PurchaseItem"", 0, 1, 1, 16753920, 0, ""The peg jumble button can be pressed an additional time."");
scr_Init_Perk(34, ""The Jittery Perk"", obj_Perk_Jittery, ""HalfSecond"", 0, 1, 1, 16711935, 0, ""20% chance for all time-based items to bonus-activate every half second. Works infinity times per round."");
scr_Init_Perk(35, ""The Lucky Perk"", obj_Perk_Lucky, ""PurchaseItem"", 0, 1, 1, 65280, 0, ""Gain +10% chance to find rare items in shop."");
scr_Init_Perk(36, ""The Rocky Perk"", obj_Perk_Rocky, ""PegFullPop"", 0, 1, 1, 8421504, 0, ""The item in slot #3 will bonus-activate when any peg is popped. Works 3 times per round."");
scr_Init_Perk(37, ""The Wizardry Perk"", obj_Perk_Wizardry, ""ItemTrigger"", 0, 1, 1, 8388736, 0, ""The item in slot #1 will bonus-activate when the item in slot #2 activates. Works 3 times per round."");
scr_Init_Perk(38, ""The Silly Perk"", obj_Perk_Silly, ""15Popped"", 1, 1, 1, 16777215, 0, ""The item in slot #4 will bonus-activate when you pop 5 pegs. Works 3 times per round."");
";
string perkWithNewRegistrations = perkOriginal.TrimEnd() + "\n" + newPerkRegistrations.Trim() + "\n";
string _apPerkIdsWithDemo = "[1,3,4,5,6,7,8,9,10,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,33,34,35,36,37,38]";
#endif

string perkBlock = @"
// === NubbyAP perk-pool lock (injected) ===
var _apPerkIds = [1,3,4,5,6,7,8,9,10,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29];
if (!variable_instance_exists(id, ""ApOriginalPerkPool""))
{
    ApOriginalPerkPool = array_create(array_length(InPerkItemPool));
    for (var _oi = 0; _oi < array_length(InPerkItemPool); _oi += 1)
    {
        ApOriginalPerkPool[_oi] = InPerkItemPool[_oi];
    }
}
if (file_exists(""NubbyAP/ap_perk_pool.txt""))
{
    for (var _ai = 0; _ai < array_length(_apPerkIds); _ai += 1)
    {
        InPerkItemPool[_apPerkIds[_ai]] = 0;
    }
    var _apFile = file_text_open_read(""NubbyAP/ap_perk_pool.txt"");
    while (!file_text_eof(_apFile))
    {
        var _apLine = string_trim(file_text_readln(_apFile));
        if (string_length(_apLine) > 0)
        {
            var _apId = real(_apLine);
            if (_apId >= 0 && _apId < array_length(InPerkItemPool))
            {
                InPerkItemPool[_apId] = ApOriginalPerkPool[_apId];
            }
        }
    }
    file_text_close(_apFile);

    for (var _tier = 0; _tier <= 2; _tier += 1)
    {
        var _eligibleCount = 0;
        for (var _ci = 0; _ci < array_length(PerkTier); _ci += 1)
        {
            if (PerkTier[_ci] == _tier && InPerkItemPool[_ci] == 1)
            {
                _eligibleCount += 1;
            }
        }
        if (_eligibleCount < 1)
        {
            for (var _fi = 0; _fi < array_length(_apPerkIds); _fi += 1)
            {
                var _fid = _apPerkIds[_fi];
                if (PerkTier[_fid] == _tier && ApOriginalPerkPool[_fid] == 1 && InPerkItemPool[_fid] != 1)
                {
                    InPerkItemPool[_fid] = 1;
                    _eligibleCount += 1;
                    if (_eligibleCount >= 1)
                    {
                        break;
                    }
                }
            }
        }
    }
}
";
string perkCombined = perkOriginal.TrimEnd() + "\n" + perkBlock;

// V59: perkBlock above only ever ran once, at obj_PerkMGMT_Create_0 (once
// per run) - unlike the item-pool equivalent (lockBlock), which ALSO gets
// re-run periodically from obj_ItemMGMT_Step_0's existing 3-second timer,
// so a newly-received item unlocks without needing a new run (per that
// code's own docstring: ""checked both at boot and every ~3s during
// play""). Perks had no such periodic re-check at all - obj_PerkMGMT has
// no Step event in vanilla to hook one into - confirmed as the cause of a
// real report (""Perks obtained during the round did not send until
// starting a new run""). Rather than create a brand-new Step event on
// obj_PerkMGMT (unprecedented for a whole event, only ever done for
// object CODE replacement or the six restored perk objects' own full
// registration elsewhere in this file), this reuses obj_ItemMGMT_Step_0's
// ALREADY-EXISTING periodic timer instead - wrapping the exact same
// perkBlock text in a with(obj_PerkMGMT) so every unprefixed reference in
// it (InPerkItemPool, PerkTier, ApOriginalPerkPool, id) correctly resolves
// against obj_PerkMGMT's own fields despite running from a different
// object's Step event, with no risk of transcribing it wrong by hand.
string perkPeriodicRefresh = "\nwith (obj_PerkMGMT)\n{\n" + perkBlock + "\n}\n";

// --- Unchanged from V11: pre-existing vanilla crash guard ---
string potUpgrPath = Path.Combine(decompFolder, "gml_GlobalScript_scr_Part_PotUpgr.gml");
string potUpgrOriginal = File.ReadAllText(potUpgrPath);
string potUpgrAnchor = "    part_particles_create(obj_ParticleSetupOv.PartSystemOver, _x, _y, obj_ParticleSetupOv.PartTypePotUpgrade, arg1);";
if (!potUpgrOriginal.Contains(potUpgrAnchor))
{
    throw new Exception("scr_Part_PotUpgr anchor line not found - decompiled source may have changed.");
}
string potUpgrCombined = potUpgrOriginal.Replace(
    potUpgrAnchor,
    "    if (instance_exists(obj_ParticleSetupOv))\n    {\n" + potUpgrAnchor + "\n    }"
);

// --- CHANGED in V14: force BOTH Progression and Progression3 reloads on
// the Game-Over "New Game" retry. Progression3 (cosmetics) was fixed in
// V10; extending to Progression too (supervisor unlocks, incl. the new SV0
// "Tony" lock below) proactively, before it independently surfaces the same
// staleness bug scr_Part_PotUpgr/cosmetics already showed - New Game/Start
// Shift/Challenge Go/Load Game/Restart Run are the only ways into gameplay,
// and none of them re-read Progression from disk otherwise. ---
string newGamePath = Path.Combine(decompFolder, "gml_Object_obj_GONewGameBtn_Step_0.gml");
string newGameOriginal = File.ReadAllText(newGamePath);
string newGameAnchor = "        if (file_exists(\"NUBBY_AutoSave_F.save\"))\n        {\n            file_delete(\"NUBBY_AutoSave_F.save\");\n        }";
if (!newGameOriginal.Contains(newGameAnchor))
{
    throw new Exception("obj_GONewGameBtn_Step_0 anchor block not found - decompiled source may have changed.");
}
string newGameCombined = newGameOriginal.Replace(
    newGameAnchor,
    newGameAnchor + "\n        scr_LoadData(\"Progression\");\n        scr_LoadData(\"Progression3\");"
);

// --- Unchanged from V11: single-click purchase for the AP Item slot ---
string offerCellPath = Path.Combine(decompFolder, "gml_Object_obj_ItemOfferCell_Step_0.gml");
string offerCellOriginal = File.ReadAllText(offerCellPath);
string offerCellAnchor = "                if (global.B_Press == true)\n                {";
if (!offerCellOriginal.Contains(offerCellAnchor))
{
    throw new Exception("obj_ItemOfferCell_Step_0 anchor line not found - decompiled source may have changed.");
}
if (offerCellOriginal.Split(new[] { offerCellAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("obj_ItemOfferCell_Step_0 anchor line is not unique - refusing to patch ambiguously.");
}
string apClickPurchase = @"                if (OfferHeldItem == 181 && global.B_Press == true && image_alpha == 1 && obj_ItemMGMT.AllowChoose == true)
                {
                    var _apPrice = obj_ItemMGMT.ItemPrice[181];
                    if (global.Money >= _apPrice)
                    {
                        if (obj_Debug.GodMode == 0)
                        {
                            if (obj_ItemMGMT.InItemSeq == true && obj_ItemMGMT.InBMSeq == false)
                            {
                                global.Money -= _apPrice;
                            }
                        }
                        audio_play_sound(au_MoneySound, 1, 0);
                        var _apPurchFile = file_text_open_write(""NubbyAP/ap_item_purchased.txt"");
                        file_text_write_string(_apPurchFile, ""1"");
                        file_text_close(_apPurchFile);
                        instance_destroy();
                    }
                    else
                    {
                        scr_TonyEmote(""IdleSad"", 8, 0);
                        instance_destroy(obj_ShopErr);
                        var _apOFErr = instance_create_layer(obj_Cursor.x, obj_Cursor.y, ""UnderCursor"", obj_ShopErr);
                        _apOFErr.OFMsg = 4;
                        audio_play_sound(au_TooExpensive, 2, 0);
                    }
                }
                if (OfferHeldItem != 181 && global.B_Press == true)
                {";
string offerCellCombined = offerCellOriginal.Replace(offerCellAnchor, apClickPurchase);

// --- NEW in V12: force a Progression3 (cosmetics) reload on the three
// remaining ways a player actually enters gameplay for the FIRST time in a
// session - obj_GONewGameBtn only covers RETRYING after a game over.
// obj_SVBtn_StartShift (pick a supervisor, Start Shift), obj_CHGoBtn
// (start a Challenge/Nubby Trial), and obj_LoadGameBtn (resume an existing
// NUBBY_AutoSave_F.save) are the actual entry points for a session's very
// first run, and none of them reload Progression3 either - same root
// cause as the V10 fix, just missed the other three doors into gameplay.
string startShiftPath = Path.Combine(decompFolder, "gml_Object_obj_SVBtn_StartShift_Alarm_1.gml");
string startShiftOriginal = File.ReadAllText(startShiftPath);
string roomGotoMainAnchor = "room_goto(Roo_Main);";
if (!startShiftOriginal.Contains(roomGotoMainAnchor))
{
    throw new Exception("obj_SVBtn_StartShift_Alarm_1 anchor not found - decompiled source may have changed.");
}
string startShiftCombined = startShiftOriginal.Replace(roomGotoMainAnchor, roomGotoMainAnchor + "\nscr_LoadData(\"Progression\");\nscr_LoadData(\"Progression3\");");

string chGoPath = Path.Combine(decompFolder, "gml_Object_obj_CHGoBtn_Alarm_1.gml");
string chGoOriginal = File.ReadAllText(chGoPath);
if (!chGoOriginal.Contains(roomGotoMainAnchor))
{
    throw new Exception("obj_CHGoBtn_Alarm_1 anchor not found - decompiled source may have changed.");
}
string chGoCombined = chGoOriginal.Replace(roomGotoMainAnchor, roomGotoMainAnchor + "\nscr_LoadData(\"Progression\");\nscr_LoadData(\"Progression3\");");

string loadGamePath = Path.Combine(decompFolder, "gml_Object_obj_LoadGameBtn_Alarm_1.gml");
string loadGameOriginal = File.ReadAllText(loadGamePath);
if (!loadGameOriginal.Contains(roomGotoMainAnchor))
{
    throw new Exception("obj_LoadGameBtn_Alarm_1 anchor not found - decompiled source may have changed.");
}
string loadGameCombined = loadGameOriginal.Replace(roomGotoMainAnchor, roomGotoMainAnchor + "\nscr_LoadData(\"Progression\");\nscr_LoadData(\"Progression3\");");

// --- NEW in V13: a FIFTH way to enter gameplay that also skipped the
// cosmetics reload - obj_RestartRun (the in-pause-menu "Restart" button,
// obj_RestartGameBtn), the fastest way to retry a run without navigating
// back through the main menu at all. It uses room_restart() directly, not
// room_goto(Roo_Main), so none of the V10/V12 button patches ever applied
// to it. This was very likely the actual flow being used for repeated
// testing, which is why Tony kept reappearing despite every other entry
// point being covered.
string restartRunPath = Path.Combine(decompFolder, "gml_Object_obj_RestartRun_Create_0.gml");
string restartRunOriginal = File.ReadAllText(restartRunPath);
string roomRestartAnchor = "room_restart();";
if (!restartRunOriginal.Contains(roomRestartAnchor))
{
    throw new Exception("obj_RestartRun_Create_0 anchor not found - decompiled source may have changed.");
}
if (restartRunOriginal.Split(new[] { roomRestartAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("obj_RestartRun_Create_0 anchor is not unique - refusing to patch ambiguously.");
}
string restartRunCombined = restartRunOriginal.Replace(roomRestartAnchor, roomRestartAnchor + "\nscr_LoadData(\"Progression\");\nscr_LoadData(\"Progression3\");");

// --- NEW in V13: pre-existing vanilla crash, unrelated to any AP code,
// triggered by eating Strawberry (item 7) or item 36 in the Cafe.
// scr_FoodEffect creates an obj_GhostCoin instance and immediately sets a
// property on it (_NewGC.GCSpd = 40) - but obj_GhostCoin_Create_0 self
// -destroys under certain conditions (room == Roo_Tutorial, or
// obj_SV9Manager/"Immortal" supervisor active), so the instance can already
// be dead by the time that assignment runs, producing exactly the "Variable
// set failed GCSpd - read only variable" error reported. Guarded both
// occurrences (case 7 and case 36 - identical code, same fix) with
// instance_exists, same defensive pattern as the V10 scr_Part_PotUpgr fix.
string foodEffectPath = Path.Combine(decompFolder, "gml_GlobalScript_scr_FoodEffect.gml");
string foodEffectOriginal = File.ReadAllText(foodEffectPath);
string ghostCoinAnchor = "                var _NewGC = instance_create_depth(940 + irandom_range(-64, 64), 563 + irandom_range(-64, 64), obj_ItemMGMT.depth - 1, obj_GhostCoin);\n                _NewGC.GCSpd = 40;";
int ghostCoinOccurrences = foodEffectOriginal.Split(new[] { ghostCoinAnchor }, StringSplitOptions.None).Length - 1;
if (ghostCoinOccurrences != 2)
{
    throw new Exception("scr_FoodEffect GhostCoin anchor found " + ghostCoinOccurrences + " times (expected 2) - decompiled source may have changed.");
}
string ghostCoinGuarded = "                var _NewGC = instance_create_depth(940 + irandom_range(-64, 64), 563 + irandom_range(-64, 64), obj_ItemMGMT.depth - 1, obj_GhostCoin);\n                if (instance_exists(_NewGC))\n                {\n                    _NewGC.GCSpd = 40;\n                }";
string foodEffectCombined = foodEffectOriginal.Replace(ghostCoinAnchor, ghostCoinGuarded);

// --- CHANGED in V18: in-game connection marker + check log, moved from
// obj_DrawHUD (V17) to obj_Cursor's Draw event. V17 missed the title
// screen because obj_DrawHUD only exists during an active run - confirmed
// via UndertaleModLib (Data.GameObjects[""obj_Cursor""].Persistent == true,
// vs obj_DrawHUD.Persistent == false) that obj_Cursor is a genuinely
// persistent, always-present object (the custom mouse cursor, which
// necessarily has to exist from the very first frame to be clickable at
// all), unlike obj_DrawHUD which is only created per-run. This covers
// everywhere obj_DrawHUD did (shop/Black Market/Cafe/Perks overlays, drawn
// within the same room) plus the title screen and hub it didn't.
// Throttled to re-read its files every 500ms (via current_time, a plain
// wall-clock check) rather than every single frame, caching results into
// instance variables - mirrors the existing _apPoolTimer pattern used
// elsewhere for the same reason (avoid needless disk I/O 60x/sec).
// V19: never explicitly set a font, so draw_text inherited whatever the
// last OTHER draw call that frame left global font state as (many objects
// call draw_set_font with different sizes - fnt_PegNum, fnt_ItemName,
// etc.) - explicit draw_set_font(fnt_ComicSansSmall) fixes the reported
// "text keeps changing size". Also made the log panel always visible
// (with a header + ""(no checks yet)"" placeholder) instead of only drawing
// when non-empty, since a genuinely empty log looked identical to a
// missing/broken feature.
// V21: text was overflowing the drawn box in both directions - vertically
// (every stored line, up to 40, was being drawn with no limit) and
// horizontally (long lines like full item-send sentences were never
// wrapped). Fixed by only keeping the last 6 log lines for display (stored
// into a temp array, sliced from the end) and switching the draw call to
// draw_text_ext with an explicit wrap width (610px, fitting inside the
// 635px-wide box) instead of plain draw_text.
// --- V31: re-added per explicit user clarification - they only ever
// wanted the "Archipelago: Connected/Disconnected" connection-status TEXT
// removed (V25), not the check log panel below it. This is a NEW, reduced
// version of the V21 block: the _apConnected/_apConnInfo tracking and the
// two draw_text calls for connection status are gone entirely (no longer
// even reads connected.flag/ap_connection_info.txt, since nothing here
// displays them anymore); only the log-reading and log-panel-drawing
// logic survives, shifted up ~50px to fill the vertical space the
// removed status text used to occupy instead of leaving a gap.
string drawHudPath = Path.Combine(decompFolder, "gml_Object_obj_Cursor_Draw_0.gml");
string drawHudOriginal = File.ReadAllText(drawHudPath);
string drawHudBlock = @"
// === NubbyAP check log (injected) - connection status text intentionally
// removed per user request, log panel kept ===
if (!variable_instance_exists(id, ""_apLastRefresh""))
{
    _apLastRefresh = -100000;
    _apLogLines = """";
}
if (current_time - _apLastRefresh >= 500)
{
    _apLastRefresh = current_time;
    _apLogLines = """";
    if (file_exists(""NubbyAP/ap_log.txt""))
    {
        var _apLogArr = array_create(0);
        var _apLogFile = file_text_open_read(""NubbyAP/ap_log.txt"");
        while (!file_text_eof(_apLogFile))
        {
            var _apLogLine = string_trim(file_text_readln(_apLogFile));
            if (string_length(_apLogLine) > 0)
            {
                array_push(_apLogArr, _apLogLine);
            }
        }
        file_text_close(_apLogFile);
        var _apLogStart = max(0, array_length(_apLogArr) - 6);
        for (var _li = _apLogStart; _li < array_length(_apLogArr); _li += 1)
        {
            if (_apLogLines != """")
            {
                _apLogLines += chr(10);
            }
            _apLogLines += _apLogArr[_li];
        }
    }
}
draw_set_font(fnt_ComicSansSmall);
draw_set_halign(fa_left);
draw_set_valign(fa_top);
draw_set_alpha(0.55);
draw_set_color(c_black);
draw_rectangle(15, 15, 650, 260, false);
draw_set_alpha(1);
draw_set_color(c_white);
draw_text(20, 20, ""Archipelago Log:"");
var _apLogDisplay = _apLogLines;
if (_apLogDisplay == """")
{
    _apLogDisplay = ""(no checks yet)"";
}
draw_text_ext(20, 40, _apLogDisplay, 18, 610);
draw_set_color(c_white);
draw_set_alpha(1);
";
string drawHudCombined = drawHudOriginal.TrimEnd() + "\n" + drawHudBlock;

// --- CHANGED in V21, CORRECTED in V52: obj_SupervisorMGMT_Create_0. V18
// fixed a genuine pre-existing vanilla crash that our own Tony (SV0) lock
// made reachable for the first time - obj_SupervisorMGMT_Create_0 reads
// SVCost[SelectSV] (SelectSV defaults to 0) before the SVCost[0..11] array
// literal is ever assigned later in the SAME event, dead in vanilla
// because that read only happens when the selected supervisor is locked,
// and Tony was always hardcoded unlocked - fixed by moving the SVCost[]
// block to the top, before anything reads it. V21 adds two more fixes on
// top: (1) vanilla has SVCost[0] = 0 (Tony/SV0's real vanilla unlock
// price - it was never meant to be bought, just always-unlocked) - now
// that Tony is AP-locked, a cost of 0 stars means it's still buyable for
// free via the in-game Buy button, completely bypassing the lock; changed
// to 25 to match the priciest real supervisor. (2) obj_SupervisorMGMT (the
// hub screen) has no Step event, only Create/Draw, so it never re-reads
// Progression after its own initial creation - if a Supervisor Unlock item
// arrives while the hub was already open before this screen was last
// (re)created, it won't show as unlocked until the player leaves and
// comes back; V21 tried to fix this with a scr_LoadData(""Progression"")
// call right before the very next line reads obj_GAME.U_SV[] - but
// scr_LoadData is ASYNC (scr_LoadData just creates an obj_Loader and does
// alarm_set(0,1) - confirmed via decompile of scr_LoadData.gml, mirroring
// scr_SaveData's identical obj_Saver pattern), so the actual reload into
// obj_GAME.U_SV[] doesn't land until the NEXT game step, one frame too
// late to affect the read on the SAME frame that follows it - meaning
// V21's fix was silently a no-op the entire time (this is very likely the
// real cause behind repeated ""supervisors still don't unlock"" reports
// even after that fix and later save-race throttling were both in place).
// V52 replaces it with apSyncSvReloadBlock: a synchronous inline
// buffer_load+json_parse read of just NUBBY_Progression_F.save's
// SaveU_SV0..12 fields directly into obj_GAME.U_SV[], same idiom
// obj_Loader_Alarm_0's own ""Progression"" case already uses, so the
// visibility check immediately after it on the SAME frame sees current
// data - no alarm/async round-trip involved. Reused as-is (not through
// scr_LoadData) at the two arrow-switch sites below for the same reason.
string apSyncSvReloadBlock = @"
        // V60 FIX: wrapped in try/catch. This runs synchronous file I/O +
        // JSON parsing (buffer_load/json_parse) at 3 different call sites -
        // obj_SupervisorMGMT_Create_0 (every time the supervisor screen
        // opens), the two arrow-switch buttons, and a ~3s periodic refresh
        // in obj_ItemMGMT_Step_0 - and every guard here (file_exists,
        // string_length, array_length, variable_struct_exists) protects
        // against the save being ABSENT or EMPTY, but nothing protected
        // against it being present-but-MID-WRITE: if this reads
        // NUBBY_Progression_F.save at the exact moment the game's own
        // async save system (obj_Saver) is writing it - plausible right
        // after a mode-select transition, which can itself trigger a save -
        // buffer_read can return a truncated/partial string, and json_parse
        // on malformed JSON throws. An uncaught throw here aborts the WHOLE
        // enclosing event, including obj_SupervisorMGMT_Create_0's own
        // vanilla code that runs after this block - which would surface as
        // an ""SVCost not set"" crash even though SVCost itself was already
        // correctly assigned earlier in the same event, because none of the
        // code after the throw (including the vanilla read of SVCost)
        // ever got to run this time. A real report matches this exactly:
        // the fix was independently re-verified correct and present via a
        // fresh decompile of the exact shipped build, so a transient
        // exception aborting this block was the most plausible remaining
        // explanation. This can never crash anything again regardless of
        // save-file state now - at worst it skips this one refresh.
        try
        {
            var _apSvBuf = -1;
            if (file_exists(""NUBBY_Progression_F.save""))
            {
                _apSvBuf = buffer_load(""NUBBY_Progression_F.save"");
                var _apSvStr = buffer_read(_apSvBuf, buffer_string);
                buffer_delete(_apSvBuf);
                if (string_length(_apSvStr) > 0)
                {
                    var _apSvArr = json_parse(_apSvStr);
                    if (array_length(_apSvArr) > 0)
                    {
                        var _apSvVal = _apSvArr[0];
                        for (var _apSvI = 0; _apSvI <= 12; _apSvI += 1)
                        {
                            var _apSvField = ""SaveU_SV"" + string(_apSvI);
                            if (variable_struct_exists(_apSvVal, _apSvField))
                            {
                                obj_GAME.U_SV[_apSvI] = variable_struct_get(_apSvVal, _apSvField);
                            }
                        }
                    }
                }
            }
        }
        catch (_apSvErr)
        {
        }
";
string supervisorMgmtPath = Path.Combine(decompFolder, "gml_Object_obj_SupervisorMGMT_Create_0.gml");
string supervisorMgmtOriginal = File.ReadAllText(supervisorMgmtPath);
string svCostBlockOriginal = @"SVCost[0] = 0;
SVCost[1] = 5;
SVCost[2] = 10;
SVCost[3] = 15;
SVCost[4] = 20;
SVCost[5] = 25;
SVCost[6] = 25;
SVCost[7] = 25;
SVCost[8] = 25;
SVCost[9] = 25;
SVCost[10] = 25;
SVCost[11] = 25;
";
string svCostBlockFixed = @"SVCost[0] = 25;
SVCost[1] = 5;
SVCost[2] = 10;
SVCost[3] = 15;
SVCost[4] = 20;
SVCost[5] = 25;
SVCost[6] = 25;
SVCost[7] = 25;
SVCost[8] = 25;
SVCost[9] = 25;
SVCost[10] = 25;
SVCost[11] = 25;
" + apSyncSvReloadBlock;
if (!supervisorMgmtOriginal.Contains(svCostBlockOriginal))
{
    throw new Exception("obj_SupervisorMGMT_Create_0 SVCost block not found verbatim - decompiled source may have changed.");
}
if (supervisorMgmtOriginal.Split(new[] { svCostBlockOriginal }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("obj_SupervisorMGMT_Create_0 SVCost block is not unique - refusing to patch ambiguously.");
}
string supervisorMgmtStep1 = supervisorMgmtOriginal.Replace(svCostBlockOriginal, "");
string selectSvAnchor = "SelectSV = 0;";
if (!supervisorMgmtStep1.Contains(selectSvAnchor))
{
    throw new Exception("obj_SupervisorMGMT_Create_0 SelectSV anchor not found - decompiled source may have changed.");
}
if (supervisorMgmtStep1.Split(new[] { selectSvAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("obj_SupervisorMGMT_Create_0 SelectSV anchor is not unique - refusing to patch ambiguously.");
}
string supervisorMgmtCombined = supervisorMgmtStep1.Replace(selectSvAnchor, selectSvAnchor + "\n" + svCostBlockFixed);

// --- CORRECTED in V23 (superseding V21's original version, which crashed -
// see below): the permanent starting money/lives bonus. V21 originally put
// BOTH money AND lives handling here, calling scr_EditMaxLives/scr_EditLives
// directly from Create_0 - both functions unconditionally call
// instance_create_layer(..., ""LevelEnt2"", ...) to rebuild the on-screen
// life-icon UI, and that layer isn't reliably ready yet during Create
// (confirmed via decompile that vanilla itself never calls either function
// from Create_0 - only from Other_4/Room Start, which fires after every
// instance's Create has run and the room's layers are built). Money has no
// such dependency and stays here; lives moved to Other_4 below.
string lvlMgmtPath = Path.Combine(decompFolder, "gml_Object_obj_LvlMGMT_Create_0.gml");
string lvlMgmtOriginal = File.ReadAllText(lvlMgmtPath);
string lvlMgmtMoneyAnchor = "global.Money = 3;";
if (!lvlMgmtOriginal.Contains(lvlMgmtMoneyAnchor))
{
    throw new Exception("obj_LvlMGMT_Create_0 money anchor not found - decompiled source may have changed.");
}
if (lvlMgmtOriginal.Split(new[] { lvlMgmtMoneyAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("obj_LvlMGMT_Create_0 money anchor is not unique - refusing to patch ambiguously.");
}
string lvlMgmtBonusBlock = @"
if (file_exists(""NubbyAP/ap_bonus.txt""))
{
    var _bonusFile = file_text_open_read(""NubbyAP/ap_bonus.txt"");
    var _bonusMoney = 0;
    global._apBonusJumbles = 0;
    global._apBonusRarity = 0;
    while (!file_text_eof(_bonusFile))
    {
        var _bonusLine = string_trim(file_text_readln(_bonusFile));
        if (string_pos(""money="", _bonusLine) == 1)
        {
            _bonusMoney = real(string_delete(_bonusLine, 1, 6));
        }
        if (string_pos(""jumbles="", _bonusLine) == 1)
        {
            global._apBonusJumbles = real(string_delete(_bonusLine, 1, 8));
        }
        if (string_pos(""rarity="", _bonusLine) == 1)
        {
            global._apBonusRarity = real(string_delete(_bonusLine, 1, 7));
        }
    }
    file_text_close(_bonusFile);
    global.Money = 3 + _bonusMoney;
}
// V56 TEMPORARY diagnostic - confirms what obj_LvlMGMT_Create_0 actually
// read for the jumble/rarity bonuses at run start (cross-reference
// against NubbyClient.py's queue_bonus debug lines in ap_debug.txt).
// Logged unconditionally (not just when the bonus file exists) so a
// ""file never existed"" case is visible too, not just silent. Safe to
// remove once root-caused; append-only, read by nothing.
var _apDbgFile3 = file_text_open_append(""NubbyAP/ap_gml_debug.txt"");
file_text_write_string(_apDbgFile3, ""lvlMgmtCreate bonusFileExists="" + string(file_exists(""NubbyAP/ap_bonus.txt"")) + "" apBonusJumbles="" + string(global._apBonusJumbles) + "" apBonusRarity="" + string(global._apBonusRarity) + ""\n"");
file_text_close(_apDbgFile3);
";
string lvlMgmtCombined = lvlMgmtOriginal.Replace(lvlMgmtMoneyAnchor, lvlMgmtMoneyAnchor + lvlMgmtBonusBlock);

// V52: two new permanent filler bonuses, same "read ap_bonus.txt, apply at
// run start" pattern as money/lives above - a permanent +N JumbleCharges
// (the real in-game "board shuffle" resource) and a permanent +N*10
// RareOdds (out of the game's own 1000-point Comn/Rare/UltraRare scale -
// confirmed via decompile: ComnOdds=950/RareOdds=50/UltraRareOdds=2 out of
// 1000, and the wiki's own Avocado effect text, "+1% odds for rare items",
// confirms 1% == 10 points on this scale - matches the already-disabled
// Lucky perk's own ComnOdds-=100/RareOdds+=100 pairing, just scaled down).
// global._apBonusJumbles/_apBonusRarity are set by lvlMgmtBonusBlock above,
// which always runs first (JumbleCharges/RareOdds are initialized later in
// the same Create_0 event).
string jumbleBonusAnchor = "JumbleCharges = 1;";
ThrowIfMissingOrAmbiguous2(lvlMgmtCombined, jumbleBonusAnchor, "obj_LvlMGMT_Create_0 JumbleCharges anchor (permanent bonus)");
string jumbleBonusReplacement = @"JumbleCharges = 1 + global._apBonusJumbles;
var _apDbgFile4 = file_text_open_append(""NubbyAP/ap_gml_debug.txt"");
file_text_write_string(_apDbgFile4, ""lvlMgmtCreate JumbleCharges_final="" + string(JumbleCharges) + ""\n"");
file_text_close(_apDbgFile4);";
lvlMgmtCombined = lvlMgmtCombined.Replace(jumbleBonusAnchor, jumbleBonusReplacement);

// Plain "RareOdds = 50;" is NOT unique here - it's also a substring of
// "PerkRareOdds = 50;" two lines below (no true second RareOdds
// assignment, just an unrelated field name that happens to end the same
// way) - confirmed via decompile and the exact failure this caused on
// first compile. Anchoring on the leading newline excludes it, since
// PerkRareOdds has "k", not a newline, immediately before "RareOdds".
string rarityBonusAnchor = "\nRareOdds = 50;";
ThrowIfMissingOrAmbiguous2(lvlMgmtCombined, rarityBonusAnchor, "obj_LvlMGMT_Create_0 RareOdds anchor (permanent bonus)");
lvlMgmtCombined = lvlMgmtCombined.Replace(rarityBonusAnchor, @"
RareOdds = 50 + (global._apBonusRarity * 10);
ComnOdds -= (global._apBonusRarity * 10);");

#if false
// DISABLED (kept for later re-wiring, not deleted) - passive effects for
// the two restored demo perks that aren't queue/Alarm_0-triggered at all
// (Gambley, Lucky) - applied once per run start, same object/timing as
// the money/lives bonus above, using the same instance_exists(obj_Perk_X)
// idiom this whole codebase already uses everywhere else to mean "is X
// currently active/owned" (e.g. instance_exists(obj_SV5Manager)/
// (obj_CH1Manager) elsewhere). Needs the matching #if false block near
// the top of the script (six-perk object/sprite/code creation) re-enabled
// too, since obj_Perk_Gambley/obj_Perk_Lucky need to exist for this to
// compile and mean anything.
string gambleyAnchor = "\nJumbleCharges = 1;";
ThrowIfMissingOrAmbiguous2(lvlMgmtCombined, gambleyAnchor, "obj_LvlMGMT_Create_0 JumbleCharges anchor (Gambley)");
string gambleyBlock = @"
JumbleCharges = 1;
if (instance_exists(obj_Perk_Gambley))
{
    JumbleCharges += 1;
}";
lvlMgmtCombined = lvlMgmtCombined.Replace(gambleyAnchor, gambleyBlock);

string luckyAnchor = "\nUltraRareOdds = 2;";
ThrowIfMissingOrAmbiguous2(lvlMgmtCombined, luckyAnchor, "obj_LvlMGMT_Create_0 UltraRareOdds anchor (Lucky)");
string luckyBlock = @"
UltraRareOdds = 2;
if (instance_exists(obj_Perk_Lucky))
{
    ComnOdds -= 100;
    RareOdds += 100;
}";
lvlMgmtCombined = lvlMgmtCombined.Replace(luckyAnchor, luckyBlock);
#endif

// --- NEW in V23: the lives-bonus portion, moved to obj_LvlMGMT_Other_4
// (""Other: Room Start""), replacing vanilla's own two lines with
// bonus-aware versions using the same self-referential composition
// vanilla already relies on (scr_EditLives reads global.MaxLives AFTER
// scr_EditMaxLives on the line above already updated it).
string other4Path = Path.Combine(decompFolder, "gml_Object_obj_LvlMGMT_Other_4.gml");
string other4Original = File.ReadAllText(other4Path);
string livesAnchor = "    scr_EditMaxLives(0, 0, 1, global.DefLives);\n    scr_EditLives(0, 0, 1, global.MaxLives);";
if (!other4Original.Contains(livesAnchor))
{
    throw new Exception("obj_LvlMGMT_Other_4 lives anchor not found - decompiled source may have changed.");
}
if (other4Original.Split(new[] { livesAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("obj_LvlMGMT_Other_4 lives anchor is not unique - refusing to patch ambiguously.");
}
string livesReplacement = @"    var _apBonusLives = 0;
    if (file_exists(""NubbyAP/ap_bonus.txt""))
    {
        var _apBonusFile = file_text_open_read(""NubbyAP/ap_bonus.txt"");
        while (!file_text_eof(_apBonusFile))
        {
            var _apBonusLine = string_trim(file_text_readln(_apBonusFile));
            if (string_pos(""lives="", _apBonusLine) == 1)
            {
                _apBonusLives = real(string_delete(_apBonusLine, 1, 6));
            }
        }
        file_text_close(_apBonusFile);
    }
    scr_EditMaxLives(0, 0, 1, global.DefLives + _apBonusLives);
    scr_EditLives(0, 0, 1, global.MaxLives);";
string other4Combined = other4Original.Replace(livesAnchor, livesReplacement);

// --- NEW in V21: stars are supposed to be entirely out of AP scope (fixed
// at 0, never granted - per explicit early direction), but that was only
// ever enforced at fresh-room creation (_lock_progression_for_fresh_room
// zeroes SaveStars once). Found the actual in-game award points via
// decompile: obj_GameWinMGMT_Alarm_3 grants a flat 5 stars on clearing a
// run (""global.Stars += _Amt"" with _Amt = 5), and obj_GameOverMGMT_Alarm_2
// grants 1-3 depending on how far the round got on a loss. Both neutralized
// by zeroing _Amt right before it's used (both the display counter AND the
// actual global.Stars increment read the same _Amt, so this correctly
// zeroes what the UI shows too, not just the real effect). Combined with
// fixing SVCost[0] above (was 0, now 25), this closes the same backdoor
// for every supervisor, not just Tony: with stars permanently stuck at 0,
// no SVCost (5-25) can ever be affordable via the in-game Buy button, so
// every supervisor unlock has to come through AP.
string gameWinPath = Path.Combine(decompFolder, "gml_Object_obj_GameWinMGMT_Alarm_3.gml");
string gameWinOriginal = File.ReadAllText(gameWinPath);
string gameWinAnchor = "var _Amt = 5;";
if (!gameWinOriginal.Contains(gameWinAnchor))
{
    throw new Exception("obj_GameWinMGMT_Alarm_3 anchor not found - decompiled source may have changed.");
}
if (gameWinOriginal.Split(new[] { gameWinAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("obj_GameWinMGMT_Alarm_3 anchor is not unique - refusing to patch ambiguously.");
}
string gameWinCombined = gameWinOriginal.Replace(gameWinAnchor, "var _Amt = 0;");

string gameOverPath = Path.Combine(decompFolder, "gml_Object_obj_GameOverMGMT_Alarm_2.gml");
string gameOverOriginal = File.ReadAllText(gameOverPath);
string gameOverAnchor = "_IGStarCounter.StarsToGive = _Amt;";
if (!gameOverOriginal.Contains(gameOverAnchor))
{
    throw new Exception("obj_GameOverMGMT_Alarm_2 anchor not found - decompiled source may have changed.");
}
if (gameOverOriginal.Split(new[] { gameOverAnchor }, StringSplitOptions.None).Length - 1 != 1)
{
    throw new Exception("obj_GameOverMGMT_Alarm_2 anchor is not unique - refusing to patch ambiguously.");
}
string gameOverCombined = gameOverOriginal.Replace(gameOverAnchor, "_Amt = 0;\n            " + gameOverAnchor);

// obj_Cursor_Draw_0 / drawHudCombined IS included below again as of V31 -
// see the block above where it's built (log panel only, connection status
// text removed) for the full history of why.

void ThrowIfMissingOrAmbiguous2(string source, string anchor, string label)
{
    if (!source.Contains(anchor))
    {
        throw new Exception(label + " anchor not found - decompiled source may have changed.");
    }
    if (source.Split(new[] { anchor }, StringSplitOptions.None).Length - 1 != 1)
    {
        throw new Exception(label + " anchor is not unique - refusing to patch ambiguously.");
    }
}

// ============================================================
// V22: Zone-locking (progressive, see NNFOptions.lock_zones/lock_zone5)
// ============================================================
string zoneAnchor = @"if (_apNextZone > _apCurZone && _apNextZone <= 4)
                {
                    var _apZoneFile = ""NubbyAP/ap_zone"" + string(_apNextZone) + ""_unlocked.txt"";
                    if (!file_exists(_apZoneFile))
                    {
                        _apZoneBlocked = true;
                    }
                }
            }";
// scr_CalcWinRound has no prior touch in this master script - inject the
// full V22 zone-cap block fresh, then V26's zone5 extension on top.
string calcWinPath = Path.Combine(decompFolder, "gml_GlobalScript_scr_CalcWinRound.gml");
string calcWinOriginal = File.ReadAllText(calcWinPath);
string calcWinZoneAnchor = "global.CurrentRnd += 1;";
ThrowIfMissingOrAmbiguous2(calcWinOriginal, calcWinZoneAnchor, "scr_CalcWinRound CurrentRnd increment");
string calcWinZoneBlock = @"var _apZoneBlocked = false;
            if (file_exists(""NubbyAP/ap_zone_lock.txt""))
            {
                var _apNextRnd = global.CurrentRnd + 1;
                var _apNextZone = ((_apNextRnd - 1) div 20) + 1;
                var _apCurZone = ((global.CurrentRnd - 1) div 20) + 1;
                if (_apNextZone > _apCurZone && _apNextZone <= 4)
                {
                    var _apZoneFile = ""NubbyAP/ap_zone"" + string(_apNextZone) + ""_unlocked.txt"";
                    if (!file_exists(_apZoneFile))
                    {
                        _apZoneBlocked = true;
                    }
                }
                if (_apNextZone == 5 && _apNextZone > _apCurZone && file_exists(""NubbyAP/ap_zone5_lock.txt""))
                {
                    if (!file_exists(""NubbyAP/ap_zone5_unlocked.txt""))
                    {
                        _apZoneBlocked = true;
                    }
                }
            }
            if (!_apZoneBlocked)
            {
                if (global.CurrentRnd <= 80 && (global.CurrentRnd mod 5) == 0)
                {
                    var _apMsIdx = (global.CurrentRnd div 5) - 1;
                    if (obj_GAME.RoundMS[_apMsIdx] == 0)
                    {
                        obj_GAME.RoundMS[_apMsIdx] = 1;
                        scr_SaveData(""Progression"");
                    }
                }
                global.CurrentRnd += 1;
            }";
string calcWinCombined = calcWinOriginal.Replace(calcWinZoneAnchor, calcWinZoneBlock);

// ============================================================
// V52: chaos_event trap fix - the trap's old direct write straight to
// obj_TimeLineMGMT.TimeLine[CurrentRnd+1] (from the unthrottled
// obj_ItemMGMT_Step_0 trapBlock) had no way to survive this exact
// function's own TimeLine[] regeneration a few lines above (the "extend
// the frontier by 1 round" logic - starts firing every single round
// once global.CurrentRnd reaches 77 (WinRound-4) and keeps firing every
// round after that forever, since RoundLimit is bumped 1-for-1 right
// alongside it). Long AP sessions routinely reach round 77+ (see
// RESTOCK_MILESTONE_LOCATIONS, which needs a run long enough to bank
// 9999 restocks), so this wasn't a rare edge case - it silently
// overwrote the trap's effect on most rounds where a real player could
// ever receive one. Fixed by having the trap handler no longer touch
// TimeLine[] directly at all - it just raises a one-shot
// ap_chaos_pending.txt marker instead (see trapBlock further down).
// Consumed here: immediately after this function's own regeneration has
// already run for this transition, and before global.CurrentRnd is
// incremented a few lines below (confirmed via decompile offset
// comparison - the increment comes strictly later in this same
// function), so global.CurrentRnd + 1 still correctly means "the round
// about to be entered" - same target/selection logic as the original
// direct write, just moved to the one point nothing downstream can
// clobber again before that round is actually entered. Re-checks
// _NextRnd itself (rather than relying on the surrounding scope) so it's
// correct regardless of exactly how the enclosing if-blocks nest.
// ============================================================
string calcWinChaosAnchor = "TimeLine[81] = 4;\n                        alarm_set(1, 1);\n                    }\n                }\n            }";
ThrowIfMissingOrAmbiguous2(calcWinCombined, calcWinChaosAnchor, "scr_CalcWinRound TimeLine[81] anchor (chaos_event fix)");
string calcWinChaosBlock = @"
            if (_NextRnd == true && file_exists(""NubbyAP/ap_chaos_pending.txt""))
            {
                file_delete(""NubbyAP/ap_chaos_pending.txt"");
                var _apChaosRnd = global.CurrentRnd + 1;
                var _apChaosCur = obj_TimeLineMGMT.TimeLine[_apChaosRnd];
                if (_apChaosCur != 0 && _apChaosCur != 1 && _apChaosCur != 7)
                {
                    _apChaosRnd += 1;
                }
                obj_TimeLineMGMT.TimeLine[_apChaosRnd] = choose(2, 5, 6);
            }
            // item_jam expiry - same _NextRnd==true per-round-transition
            // point as the chaos_event fix above. obj_GAME._apJamInst[]/
            // _apJamExpireRound[] are the small fixed-size pool trapBlock's
            // item_jam handler fills in (see obj_ItemMGMT_Step_0). Slot -1
            // means empty/free.
            if (_NextRnd == true)
            {
                for (var _apJi3 = 0; _apJi3 < 8; _apJi3 += 1)
                {
                    if (obj_GAME._apJamInst[_apJi3] != -1)
                    {
                        if (!instance_exists(obj_GAME._apJamInst[_apJi3]))
                        {
                            obj_GAME._apJamInst[_apJi3] = -1;
                        }
                        else if ((global.CurrentRnd + 1) >= obj_GAME._apJamExpireRound[_apJi3])
                        {
                            scr_EnableItem(obj_GAME._apJamInst[_apJi3], obj_GAME._apJamInst[_apJi3].WhatSlot);
                            obj_GAME._apJamInst[_apJi3] = -1;
                        }
                    }
                }
            }";
calcWinCombined = calcWinCombined.Replace(calcWinChaosAnchor, calcWinChaosAnchor + calcWinChaosBlock);

// ============================================================
// V43: Round milestone locations - first-time-ever completion of round
// 5,10,15,...,80 (16 checks total, see locations.py
// ROUND_MILESTONE_LOCATIONS, code = BASE_ID + 400 + round_number). The
// increment check above fires this the instant global.CurrentRnd (the
// round just finished, pre-increment) hits a multiple of 5, guarded by a
// lifetime obj_GAME.RoundMS[] flag array so it only fires once ever per
// milestone - same persistence pattern as SvWins/BeatChallenge (init in
// obj_GAME_Create_0, saved/loaded as part of the existing "Progression"
// SaveID, same NUBBY_Progression_F.save the save-native client already
// polls for every other lifetime stat). Index i corresponds to round
// (i+1)*5, i.e. RoundMS[0] = round 5 ... RoundMS[15] = round 80.
// ============================================================
string gameCreatePath = Path.Combine(decompFolder, "gml_Object_obj_GAME_Create_0.gml");
string gameCreateOriginal = File.ReadAllText(gameCreatePath);
string roundMsCreateAnchor = "SvWins[12] = 0;";
ThrowIfMissingOrAmbiguous2(gameCreateOriginal, roundMsCreateAnchor, "obj_GAME_Create_0 SvWins[12] anchor (RoundMS init)");
string roundMsCreateBlock = roundMsCreateAnchor;
for (int i = 0; i < 16; i++)
{
    roundMsCreateBlock += "\nRoundMS[" + i + "] = 0;";
}
string gameCreateCombined = gameCreateOriginal.Replace(roundMsCreateAnchor, roundMsCreateBlock);

string saverPath = Path.Combine(decompFolder, "gml_Object_obj_Saver_Alarm_0.gml");
string saverOriginal = File.ReadAllText(saverPath);
string roundMsSaveAnchor = "SaveCheckedChallenges: obj_GAME.CheckedChallenges";
ThrowIfMissingOrAmbiguous2(saverOriginal, roundMsSaveAnchor, "obj_Saver_Alarm_0 Progression struct anchor (RoundMS save)");
string roundMsSaveBlock = roundMsSaveAnchor + ",";
for (int i = 0; i < 16; i++)
{
    roundMsSaveBlock += "\n            SaveRoundMS" + i + ": obj_GAME.RoundMS[" + i + "]" + (i < 15 ? "," : "");
}
string saverCombined = saverOriginal.Replace(roundMsSaveAnchor, roundMsSaveBlock);

string loaderPath = Path.Combine(decompFolder, "gml_Object_obj_Loader_Alarm_0.gml");
string loaderOriginal = File.ReadAllText(loaderPath);
string roundMsLoadReadAnchor = "Save_CheckedChallenges = _LoadVal.SaveCheckedChallenges;";
ThrowIfMissingOrAmbiguous2(loaderOriginal, roundMsLoadReadAnchor, "obj_Loader_Alarm_0 Progression read anchor (RoundMS load)");
string roundMsLoadReadBlock = roundMsLoadReadAnchor;
for (int i = 0; i < 16; i++)
{
    // ?? 0 covers pre-V43 saves that predate these fields entirely -
    // GML struct access on a missing key returns undefined, and undefined
    // must never reach obj_GAME.RoundMS[] (it would poison later == 0 / ==
    // 1 checks in the scr_CalcWinRound hook above rather than behaving as
    // "not yet completed").
    roundMsLoadReadBlock += "\n                    Save_RoundMS" + i + " = _LoadVal.SaveRoundMS" + i + " ?? 0;";
}
string loaderStep1 = loaderOriginal.Replace(roundMsLoadReadAnchor, roundMsLoadReadBlock);

string roundMsLoadApplyAnchor = "obj_GAME.CheckedChallenges = Save_CheckedChallenges;";
ThrowIfMissingOrAmbiguous2(loaderStep1, roundMsLoadApplyAnchor, "obj_Loader_Alarm_0 Progression apply anchor (RoundMS load)");
string roundMsLoadApplyBlock = roundMsLoadApplyAnchor;
for (int i = 0; i < 16; i++)
{
    roundMsLoadApplyBlock += "\n                    obj_GAME.RoundMS[" + i + "] = Save_RoundMS" + i + ";";
}
string loaderCombined = loaderStep1.Replace(roundMsLoadApplyAnchor, roundMsLoadApplyBlock);

// ============================================================
// V46: Restock-count milestones ("points system") - first-time-ever
// reaching restock-count thresholds 1,2,5,10,50,100,500,1000,2000,3000,
// 4000,5000,6000,7000,8000,9000,9999 WITHIN A SINGLE ROUND TRANSITION
// (a "burst" - see _apBurstRestockCount below). Explicitly NOT the same
// as the vanilla RestockCount stat, which accumulates across the WHOLE
// RUN - per the user, getting 1 restock in round A and 1 more in round B
// must NOT combine into "2 restocks"; only restocks banked from the SAME
// board-clear (the BonusRestocks a single massive overshoot generates,
// confirmed via decompile: scr_CalcWinRound can bank many BonusRestocks
// in one call while global.CurrentRnd only advances once, then
// obj_LvlMGMT_Alarm_1/obj_SkipMerge_Alarm_1 drain them one at a time)
// count toward these thresholds. Same lifetime-flag pattern as V43's
// RoundMS[] (obj_GAME.RestockMS[0..16]) for the "first ever" checks.
// See locations.py RESTOCK_MILESTONE_LOCATIONS (code = BASE_ID + 500 + i).
// ============================================================
string restockMsCreateAnchor = "RoundMS[15] = 0;";
ThrowIfMissingOrAmbiguous2(gameCreateCombined, restockMsCreateAnchor, "obj_GAME_Create_0 RoundMS[15] anchor (RestockMS init)");
string restockMsCreateBlock = restockMsCreateAnchor;
for (int i = 0; i < 17; i++)
{
    restockMsCreateBlock += "\nRestockMS[" + i + "] = 0;";
}
gameCreateCombined = gameCreateCombined.Replace(restockMsCreateAnchor, restockMsCreateBlock);

// _apBurstRestockCount lives on obj_LvlMGMT (recreated fresh every run/
// shift, same lifecycle as RestockCount itself) and gets reset to 0 at
// the top of every round transition (see the scr_CalcWinRound hook
// below) - so it always reflects "restocks banked by THIS round's own
// board-clear", never carrying over from a previous round.
string burstRestockCreateAnchor = "RestockCount = 0;";
ThrowIfMissingOrAmbiguous2(lvlMgmtCombined, burstRestockCreateAnchor, "obj_LvlMGMT_Create_0 RestockCount anchor (burst restock init)");
lvlMgmtCombined = lvlMgmtCombined.Replace(burstRestockCreateAnchor, burstRestockCreateAnchor + "\n_apBurstRestockCount = 0;");

// Reset the burst counter at the top of every round transition (inside
// the same "if (!_apZoneBlocked)" block the V43 round-milestone check
// already hooks - a genuine round advance, not a zone-blocked no-op).
string burstRestockResetAnchor = @"if (!_apZoneBlocked)
            {
                if (global.CurrentRnd <= 80 && (global.CurrentRnd mod 5) == 0)";
ThrowIfMissingOrAmbiguous2(calcWinCombined, burstRestockResetAnchor, "scr_CalcWinRound zone-block anchor (burst restock reset)");
string burstRestockResetBlock = @"if (!_apZoneBlocked)
            {
                _apBurstRestockCount = 0;
                if (global.CurrentRnd <= 80 && (global.CurrentRnd mod 5) == 0)";
calcWinCombined = calcWinCombined.Replace(burstRestockResetAnchor, burstRestockResetBlock);

// Shared check block, inlined at both restock-drain sites. This master
// script has no existing precedent for creating a brand-new *global
// script* asset (only new *object event* code, e.g. the six perk objects
// earlier), so duplicating this small, self-contained, fully-qualified
// block at both call sites is the lower-risk choice over an unverified
// new-script-registration path. Batches every newly-crossed threshold
// into a single scr_SaveData call per hook firing (not one call per
// threshold) - a single massive batch-drain could cross several
// thresholds at once, and scr_SaveData spawns a brand-new obj_Saver
// instance every call, so this avoids piling up redundant simultaneous
// saves for one drain event.
string RestockMsCheckBlock(string incrementExpr)
{
    return @"
        obj_LvlMGMT._apBurstRestockCount += " + incrementExpr + @";
        var _apRestockThresh = [1, 2, 5, 10, 50, 100, 500, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 9999];
        var _apRestockNewlyCrossed = false;
        for (var _apRi = 0; _apRi < array_length(_apRestockThresh); _apRi += 1)
        {
            if (obj_GAME.RestockMS[_apRi] == 0 && obj_LvlMGMT._apBurstRestockCount >= _apRestockThresh[_apRi])
            {
                obj_GAME.RestockMS[_apRi] = 1;
                _apRestockNewlyCrossed = true;
            }
        }
        if (_apRestockNewlyCrossed)
        {
            scr_SaveData(""Progression"");
        }";
}
// V59: obj_LvlMGMT_Alarm_1's slow path genuinely fires once per single
// real restock, so += 1 there is correct - kept as restockMsCheckBlock
// for that one site.
string restockMsCheckBlock = RestockMsCheckBlock("1");

string lvlAlarm1Path = Path.Combine(decompFolder, "gml_Object_obj_LvlMGMT_Alarm_1.gml");
string lvlAlarm1Original = File.ReadAllText(lvlAlarm1Path);
string lvlAlarm1Anchor = "RestockCount += 1;\n        scr_UpdateDesc(\"obj_LvlMGMT.RestockCount\", 1);";
ThrowIfMissingOrAmbiguous2(lvlAlarm1Original, lvlAlarm1Anchor, "obj_LvlMGMT_Alarm_1 RestockCount anchor");
string lvlAlarm1Combined = lvlAlarm1Original.Replace(lvlAlarm1Anchor, lvlAlarm1Anchor + restockMsCheckBlock);

// V59 FIX: hooked AFTER the batch-drain loop finishes (not inside it),
// which is fine for WHEN the check runs (level-triggered >=, so one pass
// after the loop catches every threshold crossed in that batch just as
// correctly as checking after every single increment) - but the ORIGINAL
// version still incremented _apBurstRestockCount by a flat +1 per tick
// here, confusing ""check once per tick"" with ""count once per tick"".
// This loop can drain up to FrameSkipLimit restocks (clamped up to 500 -
// see obj_LvlMGMT_Alarm_1) in a SINGLE tick for a large burst, and
// FastRestocks (incremented once per actual drained restock inside the
// loop, reset to 0 only at the very end of this same event - confirmed
// via decompile) already holds exactly how many that was. A real report
// with a live save (RestockMS capped at the ""10"" threshold despite
// A_RestockCount showing 10009 for the run) confirmed this exact
// undercounting - a burst that size only ever incremented
// _apBurstRestockCount by the number of ALARM TICKS it took to drain
// (roughly 10009/500 ~= 20), never by anything close to the real total,
// so every threshold from 50 up could never be reached. Using
// FastRestocks instead makes the counted amount match the real number of
// restocks banked in this round, regardless of how many ticks the drain
// took to finish.
string skipMergeAlarm1Path = Path.Combine(decompFolder, "gml_Object_obj_SkipMerge_Alarm_1.gml");
string skipMergeAlarm1Original = File.ReadAllText(skipMergeAlarm1Path);
string skipMergeAlarm1Anchor = "scr_UpdateDesc(\"obj_LvlMGMT.RestockCount\", 1);";
ThrowIfMissingOrAmbiguous2(skipMergeAlarm1Original, skipMergeAlarm1Anchor, "obj_SkipMerge_Alarm_1 RestockCount anchor");
string skipMergeAlarm1Combined = skipMergeAlarm1Original.Replace(skipMergeAlarm1Anchor, skipMergeAlarm1Anchor + RestockMsCheckBlock("FastRestocks"));

string restockMsSaveAnchor = "SaveRoundMS15: obj_GAME.RoundMS[15]";
ThrowIfMissingOrAmbiguous2(saverCombined, restockMsSaveAnchor, "obj_Saver_Alarm_0 RoundMS15 anchor (RestockMS save)");
string restockMsSaveBlock = restockMsSaveAnchor + ",";
for (int i = 0; i < 17; i++)
{
    restockMsSaveBlock += "\n            SaveRestockMS" + i + ": obj_GAME.RestockMS[" + i + "]" + (i < 16 ? "," : "");
}
saverCombined = saverCombined.Replace(restockMsSaveAnchor, restockMsSaveBlock);

string restockMsLoadReadAnchor = "Save_RoundMS15 = _LoadVal.SaveRoundMS15 ?? 0;";
ThrowIfMissingOrAmbiguous2(loaderCombined, restockMsLoadReadAnchor, "obj_Loader_Alarm_0 RoundMS15 read anchor (RestockMS load)");
string restockMsLoadReadBlock = restockMsLoadReadAnchor;
for (int i = 0; i < 17; i++)
{
    restockMsLoadReadBlock += "\n                    Save_RestockMS" + i + " = _LoadVal.SaveRestockMS" + i + " ?? 0;";
}
loaderCombined = loaderCombined.Replace(restockMsLoadReadAnchor, restockMsLoadReadBlock);

string restockMsLoadApplyAnchor = "obj_GAME.RoundMS[15] = Save_RoundMS15;";
ThrowIfMissingOrAmbiguous2(loaderCombined, restockMsLoadApplyAnchor, "obj_Loader_Alarm_0 RoundMS15 apply anchor (RestockMS load)");
string restockMsLoadApplyBlock = restockMsLoadApplyAnchor;
for (int i = 0; i < 17; i++)
{
    restockMsLoadApplyBlock += "\n                    obj_GAME.RestockMS[" + i + "] = Save_RestockMS" + i + ";";
}
loaderCombined = loaderCombined.Replace(restockMsLoadApplyAnchor, restockMsLoadApplyBlock);

// ============================================================
// V48/V51: Round-completion score ("points system", second/additional
// source alongside V46's restock-burst milestones - both stay active).
// obj_GAME.ApScore increases by ((round_just_completed - 1) div 5) + 1
// every round transition: rounds 1-5 award 1 point each, 6-10 award 2
// each, 11-15 award 3 each, and so on. V51 change: per the user, each
// time the running total reaches the CURRENT goal (goal N = N points),
// ApScore RESETS TO 0 and the goal count (obj_GAME.ApGoalsReached, the
// lifetime/persists-across-runs counter the client actually polls) ticks
// up by one, so goal N+1 has to be built back up from zero - matching
// Hades' own score system ("getting a new high-score gives an item and
// resets the score"), not a purely-cumulative-forever total. Both fields
// persist across runs (only zeroed for a genuinely new AP room).
//
// V51 ALSO fixes a real regression: the original version called
// scr_SaveData("Progression") on literally every single round completion
// (since score changes every round) - confirmed via the user's report
// ("supervisors don't unlock again") to have made the pre-existing save/
// load race (a client-side direct-save-file edit for a received item can
// get clobbered by the game's own next independent save before the game
// ever reloads that edit - see NubbyClient.py's _apply_save_field) far
// more likely to actually lose, since the competing writer now fires
// nearly every round instead of only on rare events (supervisor wins,
// challenge completions, every-5th-round milestones). Only saving when a
// goal is actually reached restores the original, much rarer save
// cadence while keeping the reset-based design.
// See locations.py POINTS_LOCATIONS (code = BASE_ID + 1200 + i).
// ============================================================
string apScoreCreateAnchor = "RestockMS[16] = 0;";
ThrowIfMissingOrAmbiguous2(gameCreateCombined, apScoreCreateAnchor, "obj_GAME_Create_0 RestockMS[16] anchor (ApScore init)");
string apJamCreateBlock = "\n_apShopSlotShown = false;";
for (int i = 0; i < 8; i++)
{
    apJamCreateBlock += "\n_apJamInst[" + i + "] = -1;\n_apJamExpireRound[" + i + "] = 0;";
}
gameCreateCombined = gameCreateCombined.Replace(apScoreCreateAnchor, apScoreCreateAnchor + "\nApScore = 0;\nApGoalsReached = 0;" + apJamCreateBlock);

string apScoreIncrementAnchor = "_apBurstRestockCount = 0;";
ThrowIfMissingOrAmbiguous2(calcWinCombined, apScoreIncrementAnchor, "scr_CalcWinRound burst-reset anchor (ApScore increment)");
string apScoreIncrementBlock = @"_apBurstRestockCount = 0;
                obj_GAME.ApScore += ((global.CurrentRnd - 1) div 5) + 1;
                if (obj_GAME.ApScore >= (obj_GAME.ApGoalsReached + 1))
                {
                    obj_GAME.ApGoalsReached += 1;
                    obj_GAME.ApScore = 0;
                    scr_SaveData(""Progression"");
                }";
calcWinCombined = calcWinCombined.Replace(apScoreIncrementAnchor, apScoreIncrementBlock);

string apScoreSaveAnchor = "SaveRestockMS16: obj_GAME.RestockMS[16]";
ThrowIfMissingOrAmbiguous2(saverCombined, apScoreSaveAnchor, "obj_Saver_Alarm_0 RestockMS16 anchor (ApScore save)");
saverCombined = saverCombined.Replace(apScoreSaveAnchor, apScoreSaveAnchor + ",\n            SaveApScore: obj_GAME.ApScore,\n            SaveApGoalsReached: obj_GAME.ApGoalsReached");

string apScoreLoadReadAnchor = "Save_RestockMS16 = _LoadVal.SaveRestockMS16 ?? 0;";
ThrowIfMissingOrAmbiguous2(loaderCombined, apScoreLoadReadAnchor, "obj_Loader_Alarm_0 RestockMS16 read anchor (ApScore load)");
loaderCombined = loaderCombined.Replace(apScoreLoadReadAnchor, apScoreLoadReadAnchor + "\n                    Save_ApScore = _LoadVal.SaveApScore ?? 0;\n                    Save_ApGoalsReached = _LoadVal.SaveApGoalsReached ?? 0;");

string apScoreLoadApplyAnchor = "obj_GAME.RestockMS[16] = Save_RestockMS16;";
ThrowIfMissingOrAmbiguous2(loaderCombined, apScoreLoadApplyAnchor, "obj_Loader_Alarm_0 RestockMS16 apply anchor (ApScore load)");
loaderCombined = loaderCombined.Replace(apScoreLoadApplyAnchor, apScoreLoadApplyAnchor + "\n                    obj_GAME.ApScore = Save_ApScore;\n                    obj_GAME.ApGoalsReached = Save_ApGoalsReached;");

string arrowLPath = Path.Combine(decompFolder, "gml_Object_obj_SVBtn_SwitchArrowL_Alarm_0.gml");
string arrowRPath = Path.Combine(decompFolder, "gml_Object_obj_SVBtn_SwitchArrowR_Alarm_0.gml");
string arrowLOriginal = File.ReadAllText(arrowLPath);
string arrowROriginal = File.ReadAllText(arrowRPath);
string buySvAnchor = "if (instance_exists(obj_BuySVBtn))\n{\n    instance_destroy(obj_BuySVBtn);\n}";
ThrowIfMissingOrAmbiguous2(arrowLOriginal, buySvAnchor, "obj_SVBtn_SwitchArrowL_Alarm_0");
ThrowIfMissingOrAmbiguous2(arrowROriginal, buySvAnchor, "obj_SVBtn_SwitchArrowR_Alarm_0");
// V52: same async-scr_LoadData timing bug as obj_SupervisorMGMT_Create_0
// above (see its comment) - swapped for the same synchronous
// apSyncSvReloadBlock so switching supervisors with the arrow buttons
// also sees current unlock state on the very same click, not one click
// later.
string svReloadPatch = apSyncSvReloadBlock + buySvAnchor;
string arrowLCombined = arrowLOriginal.Replace(buySvAnchor, svReloadPatch);
string arrowRCombined = arrowROriginal.Replace(buySvAnchor, svReloadPatch);

// ============================================================
// V24: second star-grant path (obj_GameOverMGMT_Step_0)
// ============================================================
string gameOverStepPath = Path.Combine(decompFolder, "gml_Object_obj_GameOverMGMT_Step_0.gml");
string gameOverStepOriginal = File.ReadAllText(gameOverStepPath);
string starsAnchor = "_IGStarCounter.StarsToGive = _Amt;";
ThrowIfMissingOrAmbiguous2(gameOverStepOriginal, starsAnchor, "obj_GameOverMGMT_Step_0 stars");
string gameOverStepCombined = gameOverStepOriginal.Replace(starsAnchor, "_Amt = 0;\n                    " + starsAnchor);

// ============================================================
// V26: feature locks, cut-content pool-restore override, 4 traps
// ============================================================
string alarm8Path = Path.Combine(decompFolder, "gml_Object_obj_LvlMGMT_Alarm_8.gml");
string alarm8Original = File.ReadAllText(alarm8Path);
string bmAnchor = "if (instance_exists(obj_Unq_SuspiciousKey))";
ThrowIfMissingOrAmbiguous2(alarm8Original, bmAnchor, "obj_LvlMGMT_Alarm_8 Black Market");
string alarm8Step1 = alarm8Original.Replace(bmAnchor, "if (instance_exists(obj_Unq_SuspiciousKey) && !file_exists(\"NubbyAP/ap_blackmarket_blocked.txt\"))");
string gatAnchor = "    case 6:\n        scr_GiveClawMachine();\n        ShopsHit += 1;\n        break;";
ThrowIfMissingOrAmbiguous2(alarm8Step1, gatAnchor, "obj_LvlMGMT_Alarm_8 Grab-A-Tron");
string gatReplacement = @"    case 6:
        if (!file_exists(""NubbyAP/ap_grabatron_blocked.txt""))
        {
            scr_GiveClawMachine();
        }
        else
        {
            scr_GiveItem(-1);
        }
        ShopsHit += 1;
        break;";
string alarm8Combined = alarm8Step1.Replace(gatAnchor, gatReplacement);

string cafePath = Path.Combine(decompFolder, "gml_Object_obj_GoToCafeBtn_Step_0.gml");
string cafeOriginal = File.ReadAllText(cafePath);
string cafeAnchor = "if (global.B_Press == true)\n            {\n                audio_play_sound(au_CafeDoorCreate, 12, 0, 1, 0, 0.6);";
ThrowIfMissingOrAmbiguous2(cafeOriginal, cafeAnchor, "obj_GoToCafeBtn_Step_0");
string cafeCombined = cafeOriginal.Replace(cafeAnchor, "if (global.B_Press == true && !file_exists(\"NubbyAP/ap_cafe_blocked.txt\"))\n            {\n                audio_play_sound(au_CafeDoorCreate, 12, 0, 1, 0, 0.6);");

string chGoStepPath = Path.Combine(decompFolder, "gml_Object_obj_CHGoBtn_Step_0.gml");
string chGoStepOriginal = File.ReadAllText(chGoStepPath);
string ntAnchor = "else if (scr_ReturnNubbyTrialsWholeUnlocked() == true)\n{\n    _Proceed = scr_ReturnNubbyTrialLevelUnlocked();\n}";
ThrowIfMissingOrAmbiguous2(chGoStepOriginal, ntAnchor, "obj_CHGoBtn_Step_0");
string chGoStepCombined = chGoStepOriginal.Replace(ntAnchor, "else if (scr_ReturnNubbyTrialsWholeUnlocked() == true)\n{\n    _Proceed = scr_ReturnNubbyTrialLevelUnlocked() && !file_exists(\"NubbyAP/ap_nubbytrials_blocked.txt\");\n}");

string freezePath = Path.Combine(decompFolder, "gml_Object_obj_FreezeItemBtn_Step_1.gml");
string freezeOriginal = File.ReadAllText(freezePath);
string freezeAnchor = "if (obj_LvlMGMT.FreezeCharge == 0)";
ThrowIfMissingOrAmbiguous2(freezeOriginal, freezeAnchor, "obj_FreezeItemBtn_Step_1");
string freezeCombined = freezeOriginal.Replace(freezeAnchor, "if (obj_LvlMGMT.FreezeCharge == 0 && !file_exists(\"NubbyAP/ap_freeze_blocked.txt\"))");

#if false
// DISABLED (kept for later re-wiring, not deleted) - cut-content pool-
// restore override for Professor Palmy/Test Item 2, layered onto the
// already-V21-lockBlock'd createCombined/stepCombined. Both share the
// EXACT SAME raw lockBlock string (Step_0's version just concatenates it,
// unmodified, inside its own throttle block) - so the capture block's
// indentation (4-space base, as hand-written in lockBlock) is IDENTICAL
// in both, not different as an earlier attempt assumed by comparing
// against decompiler-reformatted output instead of the raw source string
// actually being built here.
string captureAnchor = "    if (!variable_instance_exists(id, \"ApOriginalItemPool\"))\n    {\n        ApOriginalItemPool = array_create(array_length(InItemPool));\n        for (var _oi = 0; _oi < array_length(InItemPool); _oi += 1)\n        {\n            ApOriginalItemPool[_oi] = InItemPool[_oi];\n        }\n    }";
string cutContentBlock = @"
    if (file_exists(""NubbyAP/ap_cut_content.txt""))
    {
        ApOriginalItemPool[57] = 1;
        ApOriginalItemPool[26] = 1;
    }";
ThrowIfMissingOrAmbiguous2(createCombined, captureAnchor, "obj_ItemMGMT_Create_0 cut content capture");
createCombined = createCombined.Replace(captureAnchor, captureAnchor + cutContentBlock);

ThrowIfMissingOrAmbiguous2(stepCombined, captureAnchor, "obj_ItemMGMT_Step_0 cut content capture");
stepCombined = stepCombined.Replace(captureAnchor, captureAnchor + cutContentBlock);
#endif

// All 5 traps (item_jam added V52 - see its own comment below for why
// the original "needs a round-tracked deactivate/reactivate array,
// deferred" note no longer applies), appended unthrottled at the very
// end of Step_0.
string trapBlock = @"
if (file_exists(""NubbyAP/ap_traps_pending.txt""))
{
    var _apTrapFile = file_text_open_read(""NubbyAP/ap_traps_pending.txt"");
    while (!file_text_eof(_apTrapFile))
    {
        var _apTrapLine = string_trim(file_text_readln(_apTrapFile));
        if (_apTrapLine == ""coin_theft"")
        {
            global.Money = 0;
        }
        if (_apTrapLine == ""near_death"")
        {
            scr_EditLives(0, 0, 1, 1);
        }
        if (_apTrapLine == ""item_steal"")
        {
            var _apItemCount = instance_number(obj_ItemParent);
            if (_apItemCount > 0)
            {
                var _apTarget = instance_find(obj_ItemParent, irandom(_apItemCount - 1));
                if (instance_exists(_apTarget))
                {
                    instance_destroy(_apTarget);
                }
            }
        }
        if (_apTrapLine == ""item_jam"")
        {
            // V52: was a documented no-op since V26 (""needs a round-tracked
            // deactivate/reactivate array"", deferred as architecturally
            // heavier than the other traps). Turns out the game already
            // ships exactly that primitive: DisableItem (obj_ItemParent_
            // Create_0) plus scr_DisableItem/scr_EnableItem, the same pair
            // vanilla item 177's own ""jam another item"" effect and the
            // Dwarf Slate/Circuit enchants already use - real visual
            // indicator included for free. obj_ItemMGMT.ItemInst[] (1-
            // indexed, bounded by array_length) maps board slot -> placed
            // item instance, same lookup those vanilla effects use. Collect
            // every currently-enabled placed item, pick one at random
            // (matching this file's established ""collect then irandom_
            // range"" fix elsewhere, not vanilla's own first-match bias),
            // disable it, and remember it in obj_GAME's small fixed-size
            // _apJamInst/_apJamExpireRound pool so scr_CalcWinRound's
            // per-round hook (calcWinChaosBlock, extended below) can
            // re-enable it again automatically 5 rounds later. Tracked by
            // instance id (not slot number) so a mid-jam merge/replacement
            // of that board slot can never cause the wrong item to get
            // re-enabled - re-enabling an already-destroyed instance is a
            // safe no-op (scr_EnableItem itself guards on instance_exists).
            var _apJamCandidates = array_create(0);
            for (var _apJi2 = 1; _apJi2 < array_length(obj_ItemMGMT.ItemInst); _apJi2 += 1)
            {
                if (instance_exists(obj_ItemMGMT.ItemInst[_apJi2]) && obj_ItemMGMT.ItemInst[_apJi2] != -1)
                {
                    if (obj_ItemMGMT.ItemInst[_apJi2].DisableItem == false)
                    {
                        array_push(_apJamCandidates, obj_ItemMGMT.ItemInst[_apJi2]);
                    }
                }
            }
            if (array_length(_apJamCandidates) > 0)
            {
                var _apJamPick = _apJamCandidates[irandom_range(0, array_length(_apJamCandidates) - 1)];
                scr_DisableItem(_apJamPick, _apJamPick.WhatSlot);
                var _apJamFreeSlot = -1;
                for (var _apJs = 0; _apJs < 8; _apJs += 1)
                {
                    if (obj_GAME._apJamInst[_apJs] == -1 || !instance_exists(obj_GAME._apJamInst[_apJs]))
                    {
                        _apJamFreeSlot = _apJs;
                        break;
                    }
                }
                if (_apJamFreeSlot != -1)
                {
                    obj_GAME._apJamInst[_apJamFreeSlot] = _apJamPick;
                    obj_GAME._apJamExpireRound[_apJamFreeSlot] = global.CurrentRnd + 5;
                }
            }
        }
        if (_apTrapLine == ""chaos_event"")
        {
            // V52: no longer writes TimeLine[] directly from here - see
            // the scr_CalcWinRound hook below (calcWinChaosBlock) for why.
            // Just raises a one-shot pending marker; the actual write
            // happens at round-transition time instead.
            var _apChaosMarker = file_text_open_write(""NubbyAP/ap_chaos_pending.txt"");
            file_text_close(_apChaosMarker);
        }
    }
    file_text_close(_apTrapFile);
    var _apTrapClearFile = file_text_open_write(""NubbyAP/ap_traps_pending.txt"");
    file_text_close(_apTrapClearFile);
}
";
stepCombined = stepCombined.TrimEnd() + "\n" + trapBlock;

// ============================================================
// V58: apply permanent money/lives/jumbles/rarity bonuses to the CURRENT
// run immediately on receipt, not just at the next run's start. Every
// run's obj_LvlMGMT_Create_0 already computes an ABSOLUTE baseline+bonus
// value from ap_bonus.txt's cumulative total (money = 3 + total,
// JumbleCharges = 1 + total, etc.) - this doesn't touch that at all, so
// there's no double-counting risk for future runs, which always
// recompute from scratch regardless of what happened mid-run. This just
// reads NubbyClient.py's new one-shot BONUS_DELTA_FILE queue (same
// append-then-clear shape as the trap queue above) and applies the
// INCREMENTAL amount straight to the live run's current state, once,
// same unthrottled per-Step check as the trap queue so it lands within a
// frame of being written. Guarded on instance_exists(obj_LvlMGMT) (no
// run in progress = nothing to apply to right now - the cumulative total
// was already updated regardless, so the next run's own Create_0 still
// picks it up correctly either way).
string bonusDeltaBlock = @"
if (file_exists(""NubbyAP/ap_bonus_delta.txt""))
{
    var _apBonusDeltaFile = file_text_open_read(""NubbyAP/ap_bonus_delta.txt"");
    while (!file_text_eof(_apBonusDeltaFile))
    {
        var _apBonusDeltaLine = string_trim(file_text_readln(_apBonusDeltaFile));
        if (string_length(_apBonusDeltaLine) > 0 && instance_exists(obj_LvlMGMT))
        {
            if (string_pos(""money="", _apBonusDeltaLine) == 1)
            {
                global.Money += real(string_delete(_apBonusDeltaLine, 1, 6));
                global.Money = clamp(global.Money, 0, obj_LvlMGMT.MaxMoney);
            }
            if (string_pos(""lives="", _apBonusDeltaLine) == 1)
            {
                var _apDeltaLives = real(string_delete(_apBonusDeltaLine, 1, 6));
                scr_EditMaxLives(1, 0, 0, _apDeltaLives);
                scr_EditLives(1, 0, 0, _apDeltaLives);
            }
            if (string_pos(""jumbles="", _apBonusDeltaLine) == 1)
            {
                obj_LvlMGMT.JumbleCharges += real(string_delete(_apBonusDeltaLine, 1, 8));
            }
            if (string_pos(""rarity="", _apBonusDeltaLine) == 1)
            {
                var _apDeltaRarity = real(string_delete(_apBonusDeltaLine, 1, 7));
                obj_LvlMGMT.RareOdds += _apDeltaRarity * 10;
                obj_LvlMGMT.ComnOdds -= _apDeltaRarity * 10;
            }
        }
    }
    file_text_close(_apBonusDeltaFile);
    var _apBonusDeltaClearFile = file_text_open_write(""NubbyAP/ap_bonus_delta.txt"");
    file_text_close(_apBonusDeltaClearFile);
}
";
stepCombined = stepCombined.TrimEnd() + "\n" + bonusDeltaBlock;

// ============================================================
// V57: periodic supervisor-unlock refresh, closing the real
// lost-update race behind repeated "supervisors don't unlock" reports.
// apSyncSvReloadBlock (defined above) was previously only ever run when
// the supervisor screen itself was opened/navigated - but a real, live
// session's own debug log confirmed the actual failure mode: an AP
// server correctly sent "Supervisor 10 Unlock" (and "Supervisor 2
// Unlock"), NubbyClient.py's deliver_supervisor wrote SaveU_SV10/SaveU_
// SV2 = 1 to disk (its own verify-retry loop wouldn't return successfully
// otherwise), and yet the live save file, read hours into the same
// session, still showed both as 0 - meaning the game's OWN independent
// autosave cycle clobbered the disk write with its still-stale in-memory
// obj_GAME.U_SV[] BEFORE the player ever happened to open the supervisor
// screen again, and nothing ever corrected the in-memory copy afterward,
// so every later autosave just kept re-persisting the wrong value
// forever. A screen-open trigger can only ever be as reliable as ""the
// player happens to look at that specific screen before the next
// autosave"" - not reliable at all over a long session. Instead,
// refreshing obj_GAME.U_SV[] periodically (here, every ~3 seconds,
// piggybacking on the same throttle _apPoolTimer already uses) means
// U_SV[] is essentially never stale for more than a few seconds
// regardless of what screen is open, so by the time ANY autosave fires,
// it's re-persisting the already-correct value instead of a stale one -
// closing the race instead of just narrowing its window. Safe to run this
// often since it's a synchronous inline buffer_load/json_parse (like
// apSyncSvReloadBlock already is elsewhere), not scr_LoadData's
// obj_Loader-instance-per-call approach - nothing here leaks instances
// over time.
string apSvPeriodicRefresh = @"
if (!variable_instance_exists(id, ""_apSvRefreshTimer""))
{
    _apSvRefreshTimer = 0;
}
_apSvRefreshTimer += 1;
if (_apSvRefreshTimer >= room_speed * 3)
{
    _apSvRefreshTimer = 0;
" + apSyncSvReloadBlock + @"
}
";
stepCombined = stepCombined.TrimEnd() + "\n" + apSvPeriodicRefresh;

// V59: periodic perk-pool refresh (see perkPeriodicRefresh's own comment
// above, near perkBlock/perkCombined, for the full explanation). Own
// dedicated timer/cadence rather than sharing _apPoolTimer, to avoid any
// risk of touching the already-working item-pool timer block.
string perkPoolPeriodicCheck = @"
if (!variable_instance_exists(id, ""_apPerkRefreshTimer""))
{
    _apPerkRefreshTimer = 0;
}
_apPerkRefreshTimer += 1;
if (_apPerkRefreshTimer >= room_speed * 3)
{
    _apPerkRefreshTimer = 0;
" + perkPeriodicRefresh + @"
}
";
stepCombined = stepCombined.TrimEnd() + "\n" + perkPoolPeriodicCheck;

// ============================================================
// V27: AP-item tier sprites/descriptions
// ============================================================
string descAnchor = "ItemDesc[181] = \"A mysterious finger. Who knows what world it is pointing to. Buy it to find out.\";";
string descReplacement = @"var _apTier = """";
        if (file_exists(""NubbyAP/ap_item_tier.txt""))
        {
            var _apTierFile = file_text_open_read(""NubbyAP/ap_item_tier.txt"");
            _apTier = string_trim(file_text_readln(_apTierFile));
            file_text_close(_apTierFile);
        }
        ItemDesc[181] = ""A big cluster of eggs. Hatch them to release something useful and watch them fly away to another world."";
        if (_apTier == ""filler"")
        {
            ItemDesc[181] = ""An unimportant rock. Throw it to another dimension."";
        }
        if (_apTier == ""progression"")
        {
            ItemDesc[181] = ""A capsule marked with a warning label. Whatever's sealed inside might just save the day."";
        }";
string spriteAnchor = "_apCell.sprite_index = object_get_sprite(ItemObj[181]);";
// V58: random pool per tier instead of one fixed icon per tier - see
// ApSpritePickBlock (accepts the tiny redundant tier-file re-read for
// consistency with the other two call sites, rather than hand-duplicating
// the pool logic here against the _apTier already in scope).
string spriteReplacement = ApSpritePickBlock("_apCell");
ThrowIfMissingOrAmbiguous2(createCombined, descAnchor, "obj_ItemMGMT_Create_0 tier desc");
createCombined = createCombined.Replace(descAnchor, descReplacement);
ThrowIfMissingOrAmbiguous2(createCombined, spriteAnchor, "obj_ItemMGMT_Create_0 tier sprite");
createCombined = createCombined.Replace(spriteAnchor, spriteReplacement);

// V48: no longer applied to stepCombined - apItemBlock (the block these
// tier-desc/sprite anchors live inside) was removed from the periodic
// Step_0 re-check entirely (see stepPatch above), so it no longer appears
// there at all. Create_0's one-time copy above still gets the tier
// treatment; scr_GiveItem's own copy (below) is unaffected either way.

string giveDescAnchor = "obj_ItemMGMT.ItemDesc[181] = \"A mysterious finger. Who knows what world it is pointing to. Buy it to find out.\";";
string giveDescReplacement = @"var _apTier = """";
                if (file_exists(""NubbyAP/ap_item_tier.txt""))
                {
                    var _apTierFile = file_text_open_read(""NubbyAP/ap_item_tier.txt"");
                    _apTier = string_trim(file_text_readln(_apTierFile));
                    file_text_close(_apTierFile);
                }
                obj_ItemMGMT.ItemDesc[181] = ""A big cluster of eggs. Hatch them to release something useful and watch them fly away to another world."";
                if (_apTier == ""filler"")
                {
                    obj_ItemMGMT.ItemDesc[181] = ""An unimportant rock. Throw it to another dimension."";
                }
                if (_apTier == ""progression"")
                {
                    obj_ItemMGMT.ItemDesc[181] = ""A capsule marked with a warning label. Whatever's sealed inside might just save the day."";
                }";
string giveSpriteAnchor = "_OfferSlot1.sprite_index = object_get_sprite(obj_ItemMGMT.ItemObj[_OfferSlot1.OfferHeldItem]);";
// V58: random pool per tier instead of one fixed icon per tier - see
// ApSpritePickBlock.
string giveSpriteReplacement = giveSpriteAnchor + @"
        if (_OfferSlot1.OfferHeldItem == 181)
        {
            " + ApSpritePickBlock("_OfferSlot1") + @"
        }";
ThrowIfMissingOrAmbiguous2(giveItemCombined, giveDescAnchor, "scr_GiveItem tier desc");
giveItemCombined = giveItemCombined.Replace(giveDescAnchor, giveDescReplacement);
ThrowIfMissingOrAmbiguous2(giveItemCombined, giveSpriteAnchor, "scr_GiveItem tier sprite");
giveItemCombined = giveItemCombined.Replace(giveSpriteAnchor, giveSpriteReplacement);

// --- V32 added a forced-181 override here (matching scr_GiveItem's own),
// so rerolling the shop wouldn't spoil the AP slot back to a real random
// item. V55 REMOVES that override entirely, per explicit user request:
// once the AP flavor has been shown once this shop visit (see
// obj_GAME._apShopSlotShown, set by giveItemPatch/stepPatch above),
// rerolling should behave exactly like vanilla always did - a real random
// item into slot 1 - and should NOT bring the AP flavor back. Reroll no
// longer has any AP-specific behavior at all; obj_RerollBtn_Step_0 is left
// completely unpatched.
string rerollPath = Path.Combine(decompFolder, "gml_Object_obj_RerollBtn_Step_0.gml");
string rerollOriginal = File.ReadAllText(rerollPath);
string rerollCombined = rerollOriginal;

// --- NEW in V33: fixed a real crash reported after V32 - clicking Reroll
// could throw ""DoConv :1: illegal undefined/null use"" in
// gml_Object_obj_RerollBtn_Step_0. Root cause confirmed via decompile: this
// is a PRE-EXISTING vanilla weakness, not something V32 introduced - the
// reroll's own candidate-list-building loop only adds item i to its tier's
// list (_ComnArrayITEM/_RareArrayITEM/_UltraRareArrayITEM) when
// InItemPool[i] is 1 or 4, with no fallback if that leaves a tier's list
// completely empty. The picking loop then does
// ds_list_find_value(_TarList, irandom_range(0, ds_list_size(_TarList) -
// 1)) - with an empty list that's irandom_range(0, -1), which returns
// undefined, and the very next line indexes
// obj_ItemMGMT.InItemPool[undefined] - exactly the illegal-undefined-use
// DoConv crash. Vanilla never hits this because vanilla never restricts
// InItemPool enough to empty a whole tier, but AP's item-lock feature
// (lockBlock, forcing ~69 uncollected check items' InItemPool to 0) can
// legitimately leave a tier with zero eligible items early in a run - the
// exact same failure class already solved elsewhere (scr_GiveItem's sphere
// -restriction fallback, scr_GiveItemBM's own Rare-empty-falls-back-to
// -Common downgrade) but obj_RerollBtn_Step_0 itself was never protected since no
// prior patch version touched it before V32. Fixed the same way the
// sphere-restriction fallback does it: right before the picking loop
// starts, for any of the 3 tier lists that came up empty, permanently
// restore (InItemPool = 1) the first item of that tier and add it to the
// list - guarantees every tier has at least one always-eligible item to
// reroll into, so the list is never empty AND the pick loop's own ""until
// InItemPool[_Option] == 1 || == 4"" check can succeed immediately (just
// adding the item to the list without also restoring its InItemPool would
// leave the do-until looping forever on a single ineligible candidate -
// a hang instead of a crash, not an improvement).
// --- CHANGED in V38: real bug found - "Pants" (game_id 0, tier 0, the
// lowest id of any tier) kept showing up purchasable without ever being
// unlocked. Root cause: this fallback unconditionally did
// `InItemPool[_apFbN] = 1` on the FIRST item of an empty tier by raw id
// order, regardless of whether that item was ever actually AP-unlocked -
// a real, permanent, global unlock of whatever item happens to sit first
// in that tier (id 0/Pants for tier 0), bypassing the AP lock entirely.
// Confirmed via decompile of the live build - not new this session, dates
// back to V32/33, just never reported against zone-based-shops before
// (this room predates zone-based-shops entirely, so that new feature was
// ruled out as the cause via the room's own save state before looking
// here). Fixed the same way every other safety net in this codebase
// already handles this: search for an item that's ALREADY genuinely
// unlocked (InItemPool == 1 || 4) rather than writing a new unlock -
// never fabricates access to anything. If a given tier has truly nothing
// unlocked at all, borrow one real candidate from whichever OTHER tier
// does have coverage (mirrors scr_GiveItem's own established Common ->
// Rare -> Ultra Rare redirect, V35) so the do-until still has a real,
// already-owned item to land on instead of crashing on an empty list -
// still never touches InItemPool, so nothing is ever shown before it's
// actually earned.
string rerollFallbackAnchor = "                        _OptionList = ds_list_create();";
string rerollFallbackBlock = @"                        if (ds_list_size(_ComnArrayITEM) <= 0)
                        {
                            for (var _apFb0 = 0; _apFb0 < array_length(obj_ItemMGMT.ItemTier); _apFb0 += 1)
                            {
                                if (obj_ItemMGMT.ItemTier[_apFb0] == 0 && (obj_ItemMGMT.InItemPool[_apFb0] == 1 || obj_ItemMGMT.InItemPool[_apFb0] == 4))
                                {
                                    ds_list_add(_ComnArrayITEM, _apFb0);
                                    break;
                                }
                            }
                        }
                        if (ds_list_size(_RareArrayITEM) <= 0)
                        {
                            for (var _apFb1 = 0; _apFb1 < array_length(obj_ItemMGMT.ItemTier); _apFb1 += 1)
                            {
                                if (obj_ItemMGMT.ItemTier[_apFb1] == 1 && (obj_ItemMGMT.InItemPool[_apFb1] == 1 || obj_ItemMGMT.InItemPool[_apFb1] == 4))
                                {
                                    ds_list_add(_RareArrayITEM, _apFb1);
                                    break;
                                }
                            }
                        }
                        if (ds_list_size(_UltraRareArrayITEM) <= 0)
                        {
                            for (var _apFb2 = 0; _apFb2 < array_length(obj_ItemMGMT.ItemTier); _apFb2 += 1)
                            {
                                if (obj_ItemMGMT.ItemTier[_apFb2] == 2 && (obj_ItemMGMT.InItemPool[_apFb2] == 1 || obj_ItemMGMT.InItemPool[_apFb2] == 4))
                                {
                                    ds_list_add(_UltraRareArrayITEM, _apFb2);
                                    break;
                                }
                            }
                        }
                        if (ds_list_size(_ComnArrayITEM) <= 0)
                        {
                            if (ds_list_size(_RareArrayITEM) > 0)
                            {
                                ds_list_add(_ComnArrayITEM, ds_list_find_value(_RareArrayITEM, 0));
                            }
                            else if (ds_list_size(_UltraRareArrayITEM) > 0)
                            {
                                ds_list_add(_ComnArrayITEM, ds_list_find_value(_UltraRareArrayITEM, 0));
                            }
                        }
                        if (ds_list_size(_RareArrayITEM) <= 0)
                        {
                            if (ds_list_size(_ComnArrayITEM) > 0)
                            {
                                ds_list_add(_RareArrayITEM, ds_list_find_value(_ComnArrayITEM, 0));
                            }
                            else if (ds_list_size(_UltraRareArrayITEM) > 0)
                            {
                                ds_list_add(_RareArrayITEM, ds_list_find_value(_UltraRareArrayITEM, 0));
                            }
                        }
                        if (ds_list_size(_UltraRareArrayITEM) <= 0)
                        {
                            if (ds_list_size(_ComnArrayITEM) > 0)
                            {
                                ds_list_add(_UltraRareArrayITEM, ds_list_find_value(_ComnArrayITEM, 0));
                            }
                            else if (ds_list_size(_RareArrayITEM) > 0)
                            {
                                ds_list_add(_UltraRareArrayITEM, ds_list_find_value(_RareArrayITEM, 0));
                            }
                        }
" + rerollFallbackAnchor;
ThrowIfMissingOrAmbiguous2(rerollCombined, rerollFallbackAnchor, "obj_RerollBtn_Step_0 empty-tier fallback");
rerollCombined = rerollCombined.Replace(rerollFallbackAnchor, rerollFallbackBlock);

#if false
// ============================================================
// DISABLED (kept for later re-wiring, not deleted) - the six restored
// demo-exclusive perks (Gambley/Jittery/Lucky/Rocky/Wizardry/Silly) and
// their supporting hooks. Everything below this line through the matching
// #endif is fully working code (it built and was verified via decompile
// and a real generation test before being disabled) - re-enabling is just
// removing the #if false / #endif wrapper. See the matching #if false
// blocks elsewhere in this script (obj_PerkMGMT_Create_0's registration +
// _apPerkIds extension, obj_LvlMGMT_Create_0's Gambley/Lucky hooks, and
// the item-side cut-content override) - all of them need to come back
// together for this to work again, plus the matching Python-side
// re-enables in items.py/options.py/__init__.py/NubbyClient.py (also
// preserved as commented-out blocks, not deleted).
// ============================================================

// --- NEW in V37: six restored demo-exclusive perks (Gambley/Jittery/
// Lucky/Rocky/Wizardry/Silly), per the Cut Content wiki page. Unlike
// Professor Palmy/Test Item 2 (real, pool-disabled vanilla objects
// restored in V26), these have ZERO trace anywhere in this game's own
// decompiled corpus (confirmed via a full-corpus grep for each name/
// object) - they were genuinely demo-only content that never shipped in
// this build at all, same situation as The Grabber/Test Item 1/Test Item
// 3 (which had no game logic to restore). Built here as six brand-new
// GameObjects from scratch - the first time this project creates new
// assets rather than only patching existing ones - de-risked first via a
// standalone throwaway script that round-tripped a test object through
// save/reload/decompile before any of this was written. No dedicated
// sprites exist for any of the six (confirmed via a full sprite-name
// sweep of the sprite chunk) - each borrows an existing perk's sprite as
// a placeholder, the same precedent set by Test Item 2's borrowed icon.
// Mechanics reconstructed from the wiki's per-perk description text
// (the wiki itself has no game code, only names/images) and implemented
// using the game's OWN existing, real mechanisms wherever one exists:
// scr_ForceTrigger(slot, count) is a genuine vanilla function (used by
// Cheesy Perk) that force-activates whichever item currently occupies a
// given board slot - exactly what Rocky/Wizardry/Silly's "bonus-activate"
// wording describes. Gambley/Lucky are passive stat modifiers (jumble
// charge count / shop rare-item odds) applied once at run start, mirroring
// the existing money/lives bonus pattern in obj_LvlMGMT_Create_0 below,
// rather than routed through the queue/Alarm_0 activation system at all.
UndertaleGameObject perkDonor = Data.GameObjects.ByName("obj_Perk_Cheesy");
if (perkDonor == null)
{
    throw new Exception("obj_Perk_Cheesy donor not found - cannot build new perk objects.");
}

// --- NEW in V39: real icons for the six restored demo perks, sourced
// from the wiki's own perk images (the user saved these directly to
// F:\Nubby's Number Facotry AP\Perk Gifs\ and pointed at the folder -
// replaces the borrowed-donor-icon placeholders from V37). Each source
// GIF's first frame was pre-extracted to a static PNG in the scratchpad
// (perk_png\spr_Perk_<Name>Wiki.png) since GameMaker sprite import here
// doesn't need to preserve animation - every other icon in this pack
// (items, zones, features) is a single static image too, so this stays
// consistent. Imported as brand-new standalone sprites (their own
// embedded texture page + texture page item, not packed into any
// existing atlas) - deliberately minimal compared to the bundled
// ImportGraphics.csx sample (which does a full atlas repack of
// everything) since only 6 small, wholly new, independent images need
// adding; no existing sprite/atlas is touched. Verified via a standalone
// prototype (imported one image, saved, reloaded, confirmed dimensions/
// texture page round-tripped correctly) before writing this for real.
// NOTE: the source PNGs lived in the SESSION SCRATCHPAD (perkPngDir
// below) - that path will not exist in a future session; the original
// PNGs are also archived alongside this project (re-locate/re-extract
// them from F:\Nubby's Number Facotry AP\Perk Gifs\ if re-enabling this
// after the scratchpad has been cleared).
string perkPngDir = @"C:\Users\JAKECO~1\AppData\Local\Temp\claude\F--Nubby-s-Number-Facotry-AP\5b0be79f-b07c-483c-8a55-474d2ce44dc3\scratchpad\perk_png";

UndertaleSprite ImportWikiSprite(string spriteName, string pngFileName)
{
    string pngPath = Path.Combine(perkPngDir, pngFileName);
    if (!File.Exists(pngPath))
    {
        throw new Exception("Wiki perk PNG not found: " + pngPath);
    }
    byte[] pngBytes = File.ReadAllBytes(pngPath);
    UndertaleModLib.Util.GMImage img = UndertaleModLib.Util.GMImage.FromPng(pngBytes);

    UndertaleEmbeddedTexture tex = new UndertaleEmbeddedTexture();
    tex.Name = Data.Strings.MakeString("Texture " + Data.EmbeddedTextures.Count);
    tex.TextureData.Image = img;
    Data.EmbeddedTextures.Add(tex);

    UndertaleTexturePageItem tpi = new UndertaleTexturePageItem();
    tpi.Name = Data.Strings.MakeString("PageItem " + Data.TexturePageItems.Count);
    tpi.SourceX = 0;
    tpi.SourceY = 0;
    tpi.SourceWidth = (ushort)img.Width;
    tpi.SourceHeight = (ushort)img.Height;
    tpi.TargetX = 0;
    tpi.TargetY = 0;
    tpi.TargetWidth = (ushort)img.Width;
    tpi.TargetHeight = (ushort)img.Height;
    tpi.BoundingWidth = (ushort)img.Width;
    tpi.BoundingHeight = (ushort)img.Height;
    tpi.TexturePage = tex;
    Data.TexturePageItems.Add(tpi);

    UndertaleSprite spr = new UndertaleSprite();
    spr.Name = Data.Strings.MakeString(spriteName);
    spr.Width = (uint)img.Width;
    spr.Height = (uint)img.Height;
    spr.MarginLeft = 0;
    spr.MarginRight = img.Width - 1;
    spr.MarginTop = 0;
    spr.MarginBottom = img.Height - 1;
    spr.OriginX = 0;
    spr.OriginY = 0;
    UndertaleSprite.TextureEntry texEntry = new UndertaleSprite.TextureEntry();
    texEntry.Texture = tpi;
    spr.Textures.Add(texEntry);
    Data.Sprites.Add(spr);
    return spr;
}

ImportWikiSprite("spr_Perk_GambleyWiki",  "spr_Perk_GambleyWiki.png");
ImportWikiSprite("spr_Perk_JitteryWiki",  "spr_Perk_JitteryWiki.png");
ImportWikiSprite("spr_Perk_LuckyWiki",    "spr_Perk_LuckyWiki.png");
ImportWikiSprite("spr_Perk_RockyWiki",    "spr_Perk_RockyWiki.png");
ImportWikiSprite("spr_Perk_WizardryWiki", "spr_Perk_WizardryWiki.png");
ImportWikiSprite("spr_Perk_SillyWiki",    "spr_Perk_SillyWiki.png");

UndertaleGameObject NewPerkObject(string name, string spriteName)
{
    UndertaleGameObject o = new UndertaleGameObject();
    o.Name = Data.Strings.MakeString(name);
    UndertaleSprite spr = Data.Sprites.ByName(spriteName);
    if (spr == null)
    {
        throw new Exception("Sprite not found for new perk object " + name + ": " + spriteName);
    }
    o.Sprite = spr;
    o.Visible = perkDonor.Visible;
    o.Solid = perkDonor.Solid;
    o.Depth = perkDonor.Depth;
    o.Persistent = perkDonor.Persistent;
    Data.GameObjects.Add(o);
    for (int oi = 0; oi < perkDonor.Events.Count; oi += 1)
    {
        o.Events.Add(new UndertalePointerList<UndertaleGameObject.Event>());
    }
    return o;
}

UndertaleGameObject.EventAction NewPerkAction(UndertaleGameObject o, int eventType, uint subtype)
{
    UndertaleGameObject.Event evt = new UndertaleGameObject.Event();
    evt.EventSubtype = subtype;
    o.Events[eventType].Add(evt);
    UndertaleGameObject.EventAction act = new UndertaleGameObject.EventAction();
    evt.Actions.Add(act);
    return act;
}

void FinishPerkCode(UndertaleGameObject.EventAction act, string codeName)
{
    act.CodeId = Data.Code.ByName(codeName);
    if (act.CodeId == null)
    {
        throw new Exception("New perk code entry was not created: " + codeName);
    }
    if (Data.CodeLocals is not null && Data.CodeLocals.ByName(codeName) == null)
    {
        UndertaleCodeLocals locals = new UndertaleCodeLocals();
        locals.Name = Data.Strings.MakeString(codeName);
        UndertaleCodeLocals.LocalVar argLocal = new UndertaleCodeLocals.LocalVar();
        argLocal.Name = Data.Strings.MakeString("arguments");
        argLocal.Index = 0;
        locals.Locals.Add(argLocal);
        Data.CodeLocals.Add(locals);
        act.CodeId.LocalsCount = 1;
    }
}

UndertaleGameObject perkGambleyObj  = NewPerkObject("obj_Perk_Gambley",  "spr_Perk_GambleyWiki");
UndertaleGameObject perkJitteryObj  = NewPerkObject("obj_Perk_Jittery",  "spr_Perk_JitteryWiki");
UndertaleGameObject perkLuckyObj    = NewPerkObject("obj_Perk_Lucky",    "spr_Perk_LuckyWiki");
UndertaleGameObject perkRockyObj    = NewPerkObject("obj_Perk_Rocky",    "spr_Perk_RockyWiki");
UndertaleGameObject perkWizardryObj = NewPerkObject("obj_Perk_Wizardry", "spr_Perk_WizardryWiki");
UndertaleGameObject perkSillyObj    = NewPerkObject("obj_Perk_Silly",    "spr_Perk_SillyWiki");

UndertaleGameObject.EventAction perkGambleyCreateAction  = NewPerkAction(perkGambleyObj,  0, 0);
UndertaleGameObject.EventAction perkGambleyAlarmAction   = NewPerkAction(perkGambleyObj,  2, 0);
UndertaleGameObject.EventAction perkJitteryCreateAction  = NewPerkAction(perkJitteryObj,  0, 0);
UndertaleGameObject.EventAction perkJitteryAlarmAction   = NewPerkAction(perkJitteryObj,  2, 0);
UndertaleGameObject.EventAction perkLuckyCreateAction    = NewPerkAction(perkLuckyObj,    0, 0);
UndertaleGameObject.EventAction perkLuckyAlarmAction     = NewPerkAction(perkLuckyObj,    2, 0);
UndertaleGameObject.EventAction perkRockyCreateAction    = NewPerkAction(perkRockyObj,    0, 0);
UndertaleGameObject.EventAction perkRockyAlarmAction     = NewPerkAction(perkRockyObj,    2, 0);
UndertaleGameObject.EventAction perkWizardryCreateAction = NewPerkAction(perkWizardryObj, 0, 0);
UndertaleGameObject.EventAction perkWizardryAlarmAction  = NewPerkAction(perkWizardryObj, 2, 0);
UndertaleGameObject.EventAction perkSillyCreateAction    = NewPerkAction(perkSillyObj,    0, 0);
UndertaleGameObject.EventAction perkSillyAlarmAction     = NewPerkAction(perkSillyObj,    2, 0);

string perkGambleyCreateGml = @"MyPerkID = 33;
EvType = obj_PerkMGMT.PerkTrigger[MyPerkID];
MyDesc = obj_PerkMGMT.PerkDesc[MyPerkID];
RndFireNum = 0;
GameFireNum = 0;
DisablePerk = 0;
PerkQueue = ds_list_create();
WhatSlot = -1;";
string perkGambleyAlarmGml = @"scr_PerkQueue();";

string perkJitteryCreateGml = @"MyPerkID = 34;
EvType = obj_PerkMGMT.PerkTrigger[MyPerkID];
MyDesc = obj_PerkMGMT.PerkDesc[MyPerkID];
RndFireNum = 0;
GameFireNum = 0;
DisablePerk = 0;
PerkQueue = ds_list_create();
WhatSlot = -1;";
string perkJitteryAlarmGml = @"if (DisablePerk == false)
{
    if (irandom_range(1, 100) <= 20)
    {
        scr_ItemMetaOrder(""HalfSecond"");
        scr_ItemMetaOrder(""1Second"");
        scr_ItemMetaOrder(""1andHalfSecond"");
        scr_ItemMetaOrder(""2Second"");
        scr_ItemMetaOrder(""2andHalfSecond"");
        scr_ItemMetaOrder(""3Second"");
        scr_ItemMetaOrder(""3andHalfSecond"");
        scr_ItemMetaOrder(""4Second"");
        scr_ItemMetaOrder(""5Second"");
    }
}
scr_PerkQueue();";

string perkLuckyCreateGml = @"MyPerkID = 35;
EvType = obj_PerkMGMT.PerkTrigger[MyPerkID];
MyDesc = obj_PerkMGMT.PerkDesc[MyPerkID];
RndFireNum = 0;
GameFireNum = 0;
DisablePerk = 0;
PerkQueue = ds_list_create();
WhatSlot = -1;";
string perkLuckyAlarmGml = @"scr_PerkQueue();";

string perkRockyCreateGml = @"MyPerkID = 36;
EvType = obj_PerkMGMT.PerkTrigger[MyPerkID];
MyDesc = obj_PerkMGMT.PerkDesc[MyPerkID];
RndFireNum = 0;
GameFireNum = 0;
DisablePerk = 0;
PerkQueue = ds_list_create();
WhatSlot = -1;
RoundFireCount = 0;
LastFireRound = -1;";
string perkRockyAlarmGml = @"if (DisablePerk == false)
{
    if (LastFireRound != global.CurrentRnd)
    {
        LastFireRound = global.CurrentRnd;
        RoundFireCount = 0;
    }
    if (RoundFireCount < 3)
    {
        RoundFireCount += 1;
        scr_ForceTrigger(3, 1);
    }
}
scr_PerkQueue();";

string perkWizardryCreateGml = @"MyPerkID = 37;
EvType = obj_PerkMGMT.PerkTrigger[MyPerkID];
MyDesc = obj_PerkMGMT.PerkDesc[MyPerkID];
RndFireNum = 0;
GameFireNum = 0;
DisablePerk = 0;
PerkQueue = ds_list_create();
WhatSlot = -1;
RoundFireCount = 0;
LastFireRound = -1;";
string perkWizardryAlarmGml = @"scr_PerkQueue();";

string perkSillyCreateGml = @"MyPerkID = 38;
EvType = obj_PerkMGMT.PerkTrigger[MyPerkID];
MyDesc = obj_PerkMGMT.PerkDesc[MyPerkID];
RndFireNum = 0;
GameFireNum = 0;
DisablePerk = 0;
PerkQueue = ds_list_create();
WhatSlot = -1;";
string perkSillyAlarmGml = @"if (DisablePerk == false)
{
    scr_ForceTrigger(4, 1);
}
scr_PerkQueue();";

var newPerkCodeGroup = new CodeImportGroup(Data);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Gambley_Create_0", perkGambleyCreateGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Gambley_Alarm_0", perkGambleyAlarmGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Jittery_Create_0", perkJitteryCreateGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Jittery_Alarm_0", perkJitteryAlarmGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Lucky_Create_0", perkLuckyCreateGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Lucky_Alarm_0", perkLuckyAlarmGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Rocky_Create_0", perkRockyCreateGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Rocky_Alarm_0", perkRockyAlarmGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Wizardry_Create_0", perkWizardryCreateGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Wizardry_Alarm_0", perkWizardryAlarmGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Silly_Create_0", perkSillyCreateGml);
newPerkCodeGroup.QueueReplace("gml_Object_obj_Perk_Silly_Alarm_0", perkSillyAlarmGml);
newPerkCodeGroup.Import();

FinishPerkCode(perkGambleyCreateAction,  "gml_Object_obj_Perk_Gambley_Create_0");
FinishPerkCode(perkGambleyAlarmAction,   "gml_Object_obj_Perk_Gambley_Alarm_0");
FinishPerkCode(perkJitteryCreateAction,  "gml_Object_obj_Perk_Jittery_Create_0");
FinishPerkCode(perkJitteryAlarmAction,   "gml_Object_obj_Perk_Jittery_Alarm_0");
FinishPerkCode(perkLuckyCreateAction,    "gml_Object_obj_Perk_Lucky_Create_0");
FinishPerkCode(perkLuckyAlarmAction,     "gml_Object_obj_Perk_Lucky_Alarm_0");
FinishPerkCode(perkRockyCreateAction,    "gml_Object_obj_Perk_Rocky_Create_0");
FinishPerkCode(perkRockyAlarmAction,     "gml_Object_obj_Perk_Rocky_Alarm_0");
FinishPerkCode(perkWizardryCreateAction, "gml_Object_obj_Perk_Wizardry_Create_0");
FinishPerkCode(perkWizardryAlarmAction,  "gml_Object_obj_Perk_Wizardry_Alarm_0");
FinishPerkCode(perkSillyCreateAction,    "gml_Object_obj_Perk_Silly_Create_0");
FinishPerkCode(perkSillyAlarmAction,     "gml_Object_obj_Perk_Silly_Alarm_0");

// --- NEW in V37: Wizardry's actual effect ("item in slot #1 bonus-
// activates when the item in slot #2 activates, 3x/round") - injected at
// the top of obj_ItemParent_Alarm_0, the one universal "this item is now
// running its effect" handler shared by every item instance regardless
// of type (confirmed via decompile: it's what scr_ItemQueue's popped
// entries actually run). WhatSlot here is the activating item's OWN
// field (self), so this fires only when the item currently in slot 2 is
// the one activating - no scr_GameEv event exists for this, so it can't
// go through the normal perk EvType/PerkQueue dispatch the other new
// perks use. instance_exists(obj_Perk_Wizardry) is how the whole
// codebase already spells "is this perk currently active" elsewhere.
string itemParentAlarmPath = Path.Combine(decompFolder, "gml_Object_obj_ItemParent_Alarm_0.gml");
string itemParentAlarmOriginal = File.ReadAllText(itemParentAlarmPath);
string itemParentAlarmAnchor = "with (obj_ParPeg)";
ThrowIfMissingOrAmbiguous2(itemParentAlarmOriginal, itemParentAlarmAnchor, "obj_ItemParent_Alarm_0 top anchor (Wizardry)");
string wizardryHook = @"if (WhatSlot == 2 && instance_exists(obj_Perk_Wizardry) && obj_Perk_Wizardry.DisablePerk == false)
{
    with (obj_Perk_Wizardry)
    {
        if (LastFireRound != global.CurrentRnd)
        {
            LastFireRound = global.CurrentRnd;
            RoundFireCount = 0;
        }
        if (RoundFireCount < 3)
        {
            RoundFireCount += 1;
            scr_ForceTrigger(1, 1);
        }
    }
}
with (obj_ParPeg)";
string itemParentAlarmCombined = itemParentAlarmOriginal.Replace(itemParentAlarmAnchor, wizardryHook);

// --- NEW in V37: Silly's two extra checkpoints ("5th"/"10th" peg popped
// this round) - the perk's own EvType/Alarm_0 handles the 3rd checkpoint
// ("15Popped") the normal way via scr_Init_Perk's registration above;
// scr_PerkMetaOrder only ever matches a SINGLE EvType string per perk, so
// the other two checkpoints are injected directly into scr_GameEv's own
// dispatch instead of trying to register three separate perk triggers
// for one perk. Same instance_exists(obj_Perk_Silly) idiom as Wizardry.
string gameEvPath = Path.Combine(decompFolder, "gml_GlobalScript_scr_GameEv.gml");
string gameEvOriginal = File.ReadAllText(gameEvPath);
string silly5PoppedAnchor = @"            case ""5Popped"":
                scr_ItemMetaOrder(arg0);
                scr_PerkMetaOrder(arg0);
                scr_StatusMetaOrder(arg0);
                break;";
ThrowIfMissingOrAmbiguous2(gameEvOriginal, silly5PoppedAnchor, "scr_GameEv 5Popped case (Silly)");
string silly5PoppedPatched = @"            case ""5Popped"":
                scr_ItemMetaOrder(arg0);
                scr_PerkMetaOrder(arg0);
                scr_StatusMetaOrder(arg0);
                if (instance_exists(obj_Perk_Silly) && obj_Perk_Silly.DisablePerk == false)
                {
                    scr_ForceTrigger(4, 1);
                }
                break;";
string gameEvStep1 = gameEvOriginal.Replace(silly5PoppedAnchor, silly5PoppedPatched);

string silly10PoppedAnchor = @"            case ""10Popped"":
                scr_ItemMetaOrder(arg0);
                scr_PerkMetaOrder(arg0);
                scr_StatusMetaOrder(arg0);
                break;";
ThrowIfMissingOrAmbiguous2(gameEvStep1, silly10PoppedAnchor, "scr_GameEv 10Popped case (Silly)");
string silly10PoppedPatched = @"            case ""10Popped"":
                scr_ItemMetaOrder(arg0);
                scr_PerkMetaOrder(arg0);
                scr_StatusMetaOrder(arg0);
                if (instance_exists(obj_Perk_Silly) && obj_Perk_Silly.DisablePerk == false)
                {
                    scr_ForceTrigger(4, 1);
                }
                break;";
string gameEvCombined = gameEvStep1.Replace(silly10PoppedAnchor, silly10PoppedPatched);

importGroup.QueueReplace("gml_Object_obj_ItemParent_Alarm_0", itemParentAlarmCombined);
importGroup.QueueReplace("gml_GlobalScript_scr_GameEv", gameEvCombined);
#endif

var importGroup = new CodeImportGroup(Data)
{
    AutoCreateAssets = false,
};
importGroup.QueueReplace("gml_Object_obj_ItemMGMT_Create_0", createCombined);
importGroup.QueueReplace("gml_Object_obj_ItemMGMT_Step_0", stepCombined);
importGroup.QueueReplace("gml_GlobalScript_scr_GiveItem", giveItemCombined);
importGroup.QueueReplace("gml_Object_obj_PerkMGMT_Create_0", perkCombined);
importGroup.QueueReplace("gml_GlobalScript_scr_Part_PotUpgr", potUpgrCombined);
importGroup.QueueReplace("gml_Object_obj_GONewGameBtn_Step_0", newGameCombined);
importGroup.QueueReplace("gml_Object_obj_ItemOfferCell_Step_0", offerCellCombined);
importGroup.QueueReplace("gml_Object_obj_SVBtn_StartShift_Alarm_1", startShiftCombined);
importGroup.QueueReplace("gml_Object_obj_CHGoBtn_Alarm_1", chGoCombined);
importGroup.QueueReplace("gml_Object_obj_LoadGameBtn_Alarm_1", loadGameCombined);
importGroup.QueueReplace("gml_Object_obj_RestartRun_Create_0", restartRunCombined);
importGroup.QueueReplace("gml_GlobalScript_scr_FoodEffect", foodEffectCombined);
importGroup.QueueReplace("gml_GlobalScript_scr_GiveItemBM", giveItemBMCombined);
importGroup.QueueReplace("gml_Object_obj_SupervisorMGMT_Create_0", supervisorMgmtCombined);
importGroup.QueueReplace("gml_Object_obj_LvlMGMT_Create_0", lvlMgmtCombined);
importGroup.QueueReplace("gml_Object_obj_LvlMGMT_Other_4", other4Combined);
importGroup.QueueReplace("gml_Object_obj_GameWinMGMT_Alarm_3", gameWinCombined);
importGroup.QueueReplace("gml_Object_obj_GameOverMGMT_Alarm_2", gameOverCombined);
importGroup.QueueReplace("gml_GlobalScript_scr_CalcWinRound", calcWinCombined);
importGroup.QueueReplace("gml_Object_obj_SVBtn_SwitchArrowL_Alarm_0", arrowLCombined);
importGroup.QueueReplace("gml_Object_obj_SVBtn_SwitchArrowR_Alarm_0", arrowRCombined);
importGroup.QueueReplace("gml_Object_obj_GameOverMGMT_Step_0", gameOverStepCombined);
importGroup.QueueReplace("gml_Object_obj_LvlMGMT_Alarm_8", alarm8Combined);
importGroup.QueueReplace("gml_Object_obj_GoToCafeBtn_Step_0", cafeCombined);
importGroup.QueueReplace("gml_Object_obj_CHGoBtn_Step_0", chGoStepCombined);
importGroup.QueueReplace("gml_Object_obj_FreezeItemBtn_Step_1", freezeCombined);
importGroup.QueueReplace("gml_Object_obj_Cursor_Draw_0", drawHudCombined);
importGroup.QueueReplace("gml_Object_obj_RerollBtn_Step_0", rerollCombined);
importGroup.QueueReplace("gml_Object_obj_Chest_Step_0", chestStepCombined);
importGroup.QueueReplace("gml_Object_obj_GAME_Create_0", gameCreateCombined);
importGroup.QueueReplace("gml_Object_obj_Saver_Alarm_0", saverCombined);
importGroup.QueueReplace("gml_Object_obj_Loader_Alarm_0", loaderCombined);
importGroup.QueueReplace("gml_Object_obj_LvlMGMT_Alarm_1", lvlAlarm1Combined);
importGroup.QueueReplace("gml_Object_obj_SkipMerge_Alarm_1", skipMergeAlarm1Combined);
importGroup.Import();
