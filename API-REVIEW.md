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
