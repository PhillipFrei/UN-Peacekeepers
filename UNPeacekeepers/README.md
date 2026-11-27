# UN Peacekeepers 0.3.1 alpha

Based on release/0.3.0-alpha (`45e8d5bf`) with only the two additional prisoner-crime categories and their rules/translations. This is an experimental playable build for testing, not a claim that every scenario in the development document is finished. Target: RimWorld 1.6.4871, Harmony and VEF; Royalty for shuttle missions, Biotech for pollution. Managed DLL suitable for RimWorld on Mac M3.

## Install

Download the complete `UN_Peacekeepers_v0.3.1-alpha.zip` from GitHub Actions. Extract `UNPeacekeepers` into the game's Mods folder, replacing the previous mod folder. Keep a copy of the old mod and save. Load a COPY of your save for alpha testing. No game DLLs are shipped. Existing v0.2.9 supply orders retain their original handover and rewards; new orders use the new pool.

## Core implementation

- One local UN Central console button, no power/pawn required. Four live, movable tabs: overview, prison, history, rules. The window does not force pause or camera lock. Settings only in the game's mod settings. Normal pawn-operated comms provides aid and mission acceptance.
- Persistent ten-slot offer pool: 10 ordinary at 0..49; 5 ordinary/5 advanced at 50..79; 5 ordinary/4 advanced/1 elite at 80+. Slots may remain empty if no valid faction/site/leader exists or their individual three-day refill is pending. Maximum five accepted main missions (legacy supply orders count); optional follow-ups do not count. Offers do not reroll when the window opens.
- Ten days without accepting an order costs 10 UN goodwill, stopping at +50. Migration starts a fresh clock. Existing goodwill, prison assessment, raids/aid, equipment authorization, packageId and original DefNames are preserved.
- Ordinary/advanced/elite rewards: 1,500/5,000/10,000 base silver and +10/+20/+30 goodwill. The supported valid non-UN faction receives the same requested goodwill subject to its own hard diplomacy rules. Excess earned UN goodwill above +100 becomes 100 silver per point. Actual silver is placed beside a home-map colonist; undeliverable amounts are saved and retried without paying already placed quantities again.
- Separate 25–500% mission difficulty. Existing offers update shown terms before acceptance when the setting changes; accepted contracts retain their values. No reroll of goods or targets. UN raid strength remains separate. +30 elite goodwill is a planning default.

## Seven core mission flows

Accept through normal UN radio contact or the native quest log with an eligible colonist. Offers show recipient, requirements, destination tile, risk and reward. Temporary sites are created only on acceptance.

1. Supply delivery: one ordinary or two/three advanced goods types. Carry real goods to the destination. At an existing friendly settlement, use the caravan handover command on its tile. At a temporary site, use the marked handover point, bringing colonists and goods within eight cells; a caravan can also hand over on the site's tile. Partial deliveries persist; only full delivery pays.
2. Medical assistance: generated patients at a temporary site must all recover from nonpermanent acute injuries, with sufficient roofed medical beds in safe temperature. A basic ward is generated. Review at the handover point completes the objective. This initial variant uses injuries rather than the full disease catalogue.
3. Evacuation: contact the exact civilians at the handover point, transport them to the requesting home map, then load all alive into the UN shuttle. No 80% completion.
4. Prisoner rescue: recover the exact people from a guarded camp and bring them home for the UN shuttle. Destroying the base is not an objective; reaching and rescuing the targets is sufficient.
5. Protection: preserve all named people and the handover installation for the agreed duration and defeat/incapacitate the mission attackers. Initial implementation uses a single budgeted wave.
6. Intervention: a temporary conflict map uses genuinely hostile existing factions, with supported defenders and UN allies. Enemy budget is storyteller ×1.5 × mission difficulty; supported defenders use 25%, UN support uses 10%. No global faction relation rewrites.
7. Elite arrest: reserves an available real hostile faction leader, never a clone. Capture that exact person alive, stabilize bleeding, return home as prisoner and load the UN shuttle. Budget ×3; UN support 10%. Offers can be unavailable when no suitable unreserved leader exists.

Shuttle rewards require successful execution of the physical launch, not merely the earlier loading signal. Dead/missing required people or expiry cause failure (−5). Cancellation after acceptance also costs −5; this is the explicit alpha default, with confirmation. Already handed-over goods are not refunded. Temporary civilian custody restores original factions on cleanup. Sites are removed only through their explicit safe-close action, never by deleting player belongings automatically.

After a medical/protection success, one optional medicine delivery may be offered at the same site. It expires unaccepted after three days without penalty. If accepted it has its own full tier reward/deadline/failure result, no main slot, and cannot recurse. The main result is independent.

## Conventions and pollution

Existing prison penalties/recovery stay unchanged. New reliable event hooks cover deliberately ordered unprovoked pawn attacks, deliberate killing of already downed opponents or noncombatant unarmed civilians, execution, abuse, harvest/removal intent, prisoner recruitment, enslavement, unauthorized UN gear, vanilla toxic-gas release and toxic-gas projectiles. Highest applicable aggression penalty is counted once per encounter window, not per bullet. Pre-damage status prevents counting a killing blow that merely downs an active fighter as an execution. Defence and mission combat authorization are local to registered attackers.

Own pollution is tracked only through proven source contexts: owned machine emission, traceable waste decay and player-instigated explosion cell processing. Previously polluted cells and unassigned/mod-bypassed effects are not attributed retroactively. Cleaning removes ownership. One weighted periodic penalty covers loaded home maps every three days. Explicit caravan waste abandonment accumulates in groups of five, preserving the remainder. Safe storage/normal disposal is not charged.

Prisoner experiments and proven fatal starvation are added in 0.3.1. Completed player-owned gene extraction, completed softscan, destructive ripscan (including fatal cancellation), and configured prisoner surgeries such as InstallMindscrew cost 20 points. Only a confirmed actual death from the explicitly lethal Malnutrition culprit, without a damage event, costs 30. Ordinary diseases, blood loss, regeneration coma and deathrest never automatically count as negligent deaths. The optional XML rule list allows exact RecipeDef or direct-target AbilityDef names and scanner ThingDefs to be added; unconfigured actions are not treated as experiments. A configured surgery that actually enters its worker is counted even if surgery fails; the overlapping generic surgery or harvest penalty is suppressed for that recipe. Unknown or ambiguous causes are not charged. Existing penalties, journal, recovery and ultimatum logic are reused. The new hooks are compile-tested but not exercised in-game. Unknown mod events are conservatively ignored. The event hooks are not a guarantee of coverage for every modded weapon, surgery, recruitment mechanism or attack source.

## Ultimatum

100 unconsumed raw crime points within ten days create a separate mandatory three-day quest, independent of current goodwill/clamping and the five slots. Select a leader in UN Central. The deadline runs even without selection; a locked target cannot be swapped. Successful living shuttle surrender grants +20; refusal/expiry costs −20 and schedules a targeted capture detail. If no leader has been selected, the arrest remains pending and the UI permits selection. Triggering points are consumed once. Capture troops pursue the selected person with clubs and kidnap if downed, rather than receiving a colony-destruction job; combat can still be lethal. A new regular UN raid waits while the capture operation is active, without resetting its timer.

## Validation and unfinished scope

Local compile and pure rule tests pass. CI additionally checks localization parity, definition references, XML, original asset hashes and complete ZIP integrity. Static tests are NOT engine integration tests. No real save load or full in-game mission has been run here. Test the three-way bridge between quest log, caravan/map objectives and shuttle carefully on a copied save.

Still outside this first alpha: full scenario weights and complications, all four fully furnished UN base variants, the complete optional-combination matrix, multi-wave protection, existing-base/Custom Base Framework combat placement, medical home-treatment variants and dedicated vehicle-framework arrival support. Vanilla caravans and site visit paths are used; modded vehicles/transport need separate verification. Ordinary missions currently have no random combat complication. NPC conflicts use existing hostility rather than a diplomacy-mod war API. Forecasts are current-state previews, not guarantees.

Compatibility with direct field writes, mods replacing native combat/transport behavior, and the entire supplied mod list is not guaranteed. Do not downgrade/remove this version after saving its new quest/component classes. All 24 original textures/sounds and existing equipment definitions are unchanged; no assets were regenerated.
