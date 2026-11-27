# API review for 0.2.7

Inspected the project-supplied `sources/Assembly-CSharp.dll` locally with ILSpy 9.1; no decompiled game code is included here. Build target remains the repository's RimWorld reference package 1.6.4871.

- `Faction.Hidden`: instance override or def flag. `HasGoodwill` normally returns false for hidden/temporary factions. A UN-only getter postfix restores goodwill. `ShouldHaveLeader` also excludes hidden/temporary; a UN-only postfix preserves leader maintenance.
- `FactionManager.FirstFactionOfDef` searches the full backing list, including hidden factions. Default faction selection excludes hidden factions. UN's existing defName stays intact.
- `FactionManager.FactionCanBeRemoved` allows automatic cleanup of temporary factions. UN remains non-temporary. No periodic resurrection or recreation is performed.
- `Building_CommsConsole.GetCommTargets(Pawn)` uses visible, non-temporary factions. The postfix appends the available UN exactly once. `CommFloatMenuOption` preserves normal leader availability and use-comms job handling. No global faction-list patch is used.
- `Building_CommsConsole` inherits `Building.GetGizmos`; the postfix checks the console type and player ownership before adding actions.
- `Faction.CanChangeGoodwillFor` and `TryAffectGoodwillWith` gate both orientations of UN relations. `CalculateAdjustedGoodwillChange` normally amplifies some changes by 25%; exact UN values bypass this. `CheckReachNaturalGoodwill` is skipped only for UN.
- `GoodwillWith` normally caps base goodwill through the situation manager. Migration snapshots the displayed value once, before enabling the UN-only base-goodwill path. Existing relation kinds are not reinitialized. Initial new-game goodwill goes through the explicit UN context as well.
- `Faction.SetRelationDirect` is not an appropriate goodwill implementation; it is blocked for UN external direct diplomacy. This cannot prevent arbitrary writes to `FactionRelation` fields.
- `SettlementDefeatUtility.CheckDefeated` can replace the map parent, create a destroyed base and mark the last-base faction defeated. Its UN-only prefix prevents that entire operation before side effects. Lower-level `WorldObject.SetFaction`, `Destroy` and `WorldObjectsHolder.Remove` protect live spawned UN settlements only.
- Explicit UN enemy raid parameters bypass random faction selection in `IncidentWorker_RaidEnemy.TryResolveRaidFaction`; v0.2.6's UN random-source exclusion remains. All point/interval calculations are unchanged.
- `FactionGenerator` still creates required hidden factions and leaders but skips ordinary settlement generation for hidden factions. Existing settlements are not removed.

Runtime acceptance checks still needed: load an existing save and compare goodwill/probation; gifted silver leaves goodwill unchanged; compare −10 mixed housing with two versus ten prisoners; save/reload halfway through the cycle and after acute surgery; check empty-prison recovery at −3 and +50; select a powered/unpowered console; contact UN at neutral/hostile/allied goodwill; attempt settlement conquest with the user's diplomacy mods; exercise scheduled raids and aid.


# v0.2.9 API and compatibility review

Inspected the supplied game assembly again with ILSpy and compiled against the unchanged 1.6.4871 reference package. Game code is not redistributed.

- `Quest.MakeRaw()` allocates an ID and appearance tick. Set a real `QuestScriptDef` root before registering; cleanup dereferences `root.hideOnCleanup`. Both new defs have real roots and are excluded from random selection.
- `QuestManager.Add` registers signal receivers before `Quest.Accept(pawn)` emits `InitiateSignal`. `QuestPartActivable` enables on that signal; `Quest.QuestTick` ticks enabled parts. The ten-day deadline also runs during delivery to reject boundary-time submissions.
- `Quest.AddPart` assigns ownership; deep quest serialization restores the custom part. Base ExposeData preserves activation/signal fields; custom fields persist tier, delivered count and reward guard. Existing mod Scribe keys and defaults are unchanged.
- `Quest.End(Success/Fail)` provides native completion/history/cleanup. One active supply part and acceptance-time history enforce shared tier limits. No new global save component or goodwill migration is introduced.
- `Thing.SplitOff`, `Destroy(Vanish)`, spawned-map filtering, `IsForbidden` and `Pawn.CanReach` implement an explicit resource handover. Partial deliveries consume only needed goods. Reward guard is set before calling the existing `ApplyPrisonGoodwill` scoped API (its historical name is retained).
- `CompGetGizmosExtra` is the standard ThingWithComps path. The XML patch adds one comp to CommsConsole, and three ungroupable local commands replace the previous Building postfix. This avoids depending on that broad patch and allows existing buildings to acquire the component during load. The reported missing settings button's precise mod-stack cause has not been reproduced live.
- Prison inspection and penalty/recovery mutations, intervention formulas, UN isolation and equipment defs remain unchanged; report-only additions read existing state.

Validation: compile, policy/lifecycle tests (partial stacks, save-state restoration, over-delivery, duplicate payout, exact expiry), XML/translation checks, original asset hashes and full ZIP validation. These are not a live engine integration test. Required in-game acceptance: load a copy of v0.2.8 save; compare goodwill/probation; select powered/unpowered existing console; check all three commands; use both order tiers; save/reload after partial delivery and after reward; test expiry, cancellation, insufficient/forbidden/unreachable goods, tier thresholds and unchanged external-goodwill blocking. No claim of live save or mod-stack compatibility verification.


# 0.3.0 alpha API review

Verified supplied Assembly-CSharp.dll rather than guessing signatures: Window/WindowStack GameUI flags, Quest.Accept/End/MakeRaw, QuestManager.Add caches, QuestPart_ExtraFaction, Site/MapParent and SitePartDef fields, TileFinder, PawnGroupMakerUtility, Caravan GetGizmos, GenPlace.TryPlaceThing partial-placement callback, ShipJob_Wait and ShipJob_FlyAway, CompShuttle signals, Thing.TakeDamage, pre-Kill damage snapshot, Recipe_RemoveBodyPart intent, RecruitUtility, Projectile.Launch explicit overload, CompReleaseGas, Explosion.AffectCell source, PollutionGrid.SetPolluted, waste production/decay and abandonment callbacks. Original game code is not included.

The native SentSatisfied signal fires before FlyAway actually spawns the departing skyfaller. A narrowly filtered successful TryStart postfix settles only this mod's tracked shuttle objectives after the launch completes. Exact target identity and life checks precede the signal; objective achieved/settled guards prevent repeated rewards. Physical silver updates unpaid balances through each actual placement callback even when overall placement fails. The generated new XML was checked against actual Site/Building parents, DefNames and SitePartDef fields.

New tests cover all goodwill pool compositions, inactivity bounds, overflow examples, partial physical payout retry accounting, pollution bands, waste remainder and raw crime-window expiry. Existing tests remain. Static content check validates every literal translation reference, DE/EN parity, unique defs, static UN DefDatabase lookups and original assets. Local compile passes with zero warnings. No live Harmony/Unity run, save-load, complete quest playthrough or full mod-stack test is claimed. README explicitly lists unfinished design variants.


## 0.3.1 prisoner crime addition
Publicly available RimWorld decompiled methods checked: Building_GeneExtractor.Finish, Building_SubcoreScanner.EjectContents/KillOccupant, RecipeWorker.ApplyOnPawn, Ability.Activate(LocalTargetInfo, LocalTargetInfo), Pawn.Kill(DamageInfo?, Hediff), Hediff.CauseDeathNow. Compile against pinned 1.6.4871 references; private hook invocation and actual runtime cause attribution still require in-game tests. Optional def names are string-based and DLC-gated. Test: completed extraction −20; repeated lethal extraction only −20; cancelled gene loading 0; completed softscan −20; ripscan completion or lethal cancellation −20; prisoner InstallMindscrew −20 (not additional −15); ordinary surgery 0 under new rules; confirmed lethal Malnutrition −30; other cause while malnourished 0; deathrest/coma 0; no DLC and missing optional defs no error; duplicate events not charged; save/reload and verify ledger and ultimatum.
