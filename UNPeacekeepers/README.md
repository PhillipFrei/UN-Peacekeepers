# UN Peacekeepers 0.2.8 alpha

RimWorld 1.6.4871; Harmony + Vanilla Expanded Framework. Managed DLL works with RimWorld on Apple Silicon. Based on v0.2.6 commit `4330b0a676bf98f955ac6344b8ac4e9b84773f0c`.

## Install
Download the complete `UN_Peacekeepers_v0.2.8.zip` from the successful GitHub Actions artifact `UN-Peacekeepers-v0.2.8-complete`. Extract its `UNPeacekeepers` folder into RimWorld's Mods folder. Replace the old folder rather than merging versions. Keep Harmony and VEF enabled. Back up the save before updating. The archive includes the compiled DLL, all original textures and sound; no game assemblies are redistributed.

## Changes
- UN is hidden from normal faction lists and default diplomacy/random faction selection. It is **not temporary**. Existing UN bases remain; hidden UN generates no ordinary settlements in new worlds.
- Only explicit UN mod goodwill mutations are accepted. Gifts, trade, prisoner release, incidental combat, vanilla goodwill drift and ordinary external diplomacy do not change UN goodwill. Vanilla's 25% goodwill adjustment is bypassed for UN, making −10 and +10 exact (subject to −100/+100 bounds). UN situational goodwill caps no longer change the displayed value.
- Migration retains the existing displayed player goodwill and relation kind, manufacturing authorization/probation, journal, raid schedule and aid cooldown. Old per-prisoner deadlines remain grace periods; a fresh shared assessment cycle starts on first update. Existing defeated UN factions are not resurrected. Other factions' relations are not rewritten.
- Vanilla settlement defeat, destruction, removal and ownership changes are blocked **only for live, spawned UN settlements during play**. Protection does not make soldiers immortal. Hidden faction checks reduce ordinary war-mod participation; this is not a universal compatibility guarantee.
- No bottom UN tab. Select an owned comms console for pawn-free prison report, goodwill/intervention report and settings. These local windows work without electricity. Actual radio contact still uses the normal powered-console/pawn workflow; UN is explicitly restored to its contact list. UN contact offers reports, settings, aid and assignment categories instead of vanilla gift/trader options.
- Equipment definitions, damage, armor, shields, troop composition and underlying storyteller raid scaling are unchanged. Aid still requires +75 goodwill and an alliance, a suitable map, a common active enemy and its existing cooldown.

## 0.2.8 intervention adjustment
Interventions now start at −50, with the first raid after one game day. The goodwill / multiplier / interval thresholds are −50 / 1.5 / 12 days, −65 / 2 / 9, −80 / 3 / 6, −90 / 4 / 4, −100 / 5 / 3. Values are linearly interpolated. The existing storyteller points and user strength multiplier are retained. The UN/player relation is explicitly synchronized to hostile at −50 or lower, and ceases to be hostile above −50; this does not change goodwill or other faction relations. Existing scheduled raid timing is retained and recalculated from the last successful raid where applicable. No gameplay or asset changes beyond this adjustment.

## Prison policy (chosen interpretation)
A single assessment occurs every 180,000 ticks (three game days). A newly observed prisoner receives at least three days' grace and is charged on the next shared assessment after that deadline (between three and six days). Existing v0.2.6 deadlines are retained as grace deadlines.

Mixed male/female prison-bed rooms cost **−10 once per entire assessment across all player-home maps**, regardless of room/prisoner count, when at least one affected prisoner has passed grace. Other existing negative conditions retain their per-prisoner scores. Compliant prisoners no longer give +3 each. Housing is determined by the bed found by RimWorld's existing bed lookup, not by a new room-ownership algorithm; pawns without a gender do not count as mixed.

Every three days, +10 recovery is granted only if no negative condition was observed in that whole period and no tracked acute violation occurred. Recovery stops at zero (−3 becomes 0; 0 and +50 do not increase). **Empty prisons qualify for recovery**, but never for prison penalties; an acute violation earlier in the period still blocks recovery. Conditions are sampled every 250 ticks on loaded player-home maps. Unloaded maps/caravans are outside this tracker. Acute surgery reporting still uses vanilla `RecipeWorker.ReportViolation`; mod surgeries bypassing that event cannot be guaranteed detectable.

## Mission limitation — explicit
v0.2.6 had no UN mission generator and **v0.2.7 does not add one**. Ordinary assignment acceptance is gated at 0, advanced acceptance at +50; the menu can show already existing compatible `UN_` quests (`UN_Advanced_` for advanced). With none present it explicitly says the generator is missing, rather than telling the player to wait. Standard third-party goodwill rewards remain blocked by the anti-bribery gate; compatible UN code must use the explicit UN goodwill API. Consequently this standalone version has **no normal progression from 0 to positive goodwill**, no built-in mission rewards, and no new route to +75 aid or +100 manufacturing authorization. Existing positive goodwill and existing authorization remain valid. Mission generation and objective-based rewards require a subsequent gameplay implementation.

## Validation and limits
GitHub Actions runs recovery, housing, assignment and intervention rule assertions, compiles the DLL against `Krafs.Rimworld.Ref` 1.6.4871, verifies XML and asset SHA-256 hashes, and tests the complete ZIP. `tools/package.py` refuses missing/changed assets or a missing DLL. See `API-REVIEW.md` for locally inspected game interfaces. No live game/save-load or mod-stack test has been performed; compilation and policy checks do not prove Harmony runtime compatibility.

Direct field/list writes, replacement world-object classes, or mods that ignore hidden factions can bypass the protection. Existing non-player UN relation data is retained for aid/common-enemy behavior, so this is isolation from ordinary selection and mutation, not a global rewrite of NPC hostility. Uninstalling after saving the hidden faction state is not a supported migration.

## Asset provenance
The 24 original PNG/WAV assets were copied byte-for-byte from the installed `UNPeacekeepers` mod on this Mac. Its About metadata says 0.2.5 and README says 0.2.4; v0.2.6's repository README explicitly says assets were unchanged from 0.2.5. Original hashes are stored in `tools/assets-sha256.json`. No graphics or sounds were regenerated. Repository source/equipment baseline is the latest v0.2.6 commit above.
