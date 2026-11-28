using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace UNPeacekeepers
{
    public static class UNMissionSites
    {
        public static bool Supports(int kind)=>kind>=0 && kind<=6 && (kind==0 || ModsConfig.RoyaltyActive);
        public static IEnumerable<UNMissionObjective> Pending => Find.QuestManager.QuestsListForReading.Where(q=>q.State==QuestState.Ongoing||q.State==QuestState.NotYetAccepted).SelectMany(q=>q.PartsListForReading).OfType<UNMissionObjective>();
        public static IEnumerable<UNMissionObjective> Active => Find.QuestManager.QuestsListForReading.Where(q=>q.State==QuestState.Ongoing).SelectMany(q=>q.PartsListForReading).OfType<UNMissionObjective>();
        // Mapless camps are not covered by MapParent.CheckRemoveMapNow.
        // Snapshot because removing a world object mutates the holder.
        public static void CleanupCompletedSites()
        {
            // Migrate old unaccepted follow-ups before closing their original site.
            // Already accepted follow-ups keep their exact destination and progress.
            foreach(var q in Find.QuestManager.QuestsListForReading.Where(q=>q.State==QuestState.NotYetAccepted).ToList())
            {
                try{RelocateLegacySupplement(q);}
                catch(Exception ex){Log.Error("[UN Peacekeepers] Supplement relocation failed for "+q.id+": "+ex);}
            }
            foreach(var site in Find.WorldObjects.AllWorldObjects.OfType<UNMissionSite>().ToList())
            {
                try{site.TryAutomaticClose();}
                catch(Exception ex){Log.Error("[UN Peacekeepers] Site cleanup failed for "+site.ID+": "+ex);}
            }
        }
        public static bool TryPrepare(int kind,int tier,Map home,Faction supported,float difficulty,out UNMissionObjective objective,out string reason)
        {
            objective=null;reason="UNMS_NoSite".Translate();
            if(!Supports(kind) || home==null || supported==null || supported.IsPlayer || UNContact.IsUN(supported) || supported.defeated || supported.deactivated)return false;
            if((kind>=3 && tier<1)||(kind==6 && tier<2))return false;
            PlanetTile tile;
            if(!TileFinder.TryFindNewSiteTile(out tile,home.Tile,tier==0?5:10,tier==0?15:30,allowCaravans:false,validator:t=>!Find.WorldObjects.AnyWorldObjectAt(t)&&!Pending.Any(o=>o.tile==t)))return false;
            var o=new UNMissionObjective{kind=kind,tier=tier,homeMap=home,supportedFaction=supported,tile=tile,difficulty=difficulty,seed=Rand.Int};
            if(kind>=3)
            {
                // Actual existing hostility is required. Never rewrite faction relations for a mission.
                o.enemyFaction=Find.FactionManager.AllFactionsListForReading.Where(f=>!f.IsPlayer&&!UNContact.IsUN(f)&&!f.Hidden&&!f.defeated&&!f.deactivated&&f!=supported&&f.HostileTo(supported)&&f.HostileTo(Faction.OfPlayer)&&f.def.pawnGroupMakers!=null).RandomElementWithFallback();
                if(o.enemyFaction==null){reason="UNMS_NoEnemy".Translate();return false;}
                o.enemyBudget=StorytellerUtility.DefaultThreatPointsNow(home)*(tier==2?3f:1.5f)*difficulty;
                if(kind==6)
                {
                    var leader=o.enemyFaction.leader;
                    if(leader==null||leader.Dead||leader.Spawned||leader.ParentHolder!=null||!Find.WorldPawns.Contains(leader)||Pending.Any(p=>p.targets.Contains(leader)))return false;
                    o.targets.Add(leader);
                }
            }
            if(kind==0)
            {
                string[] goods={"MealSurvivalPack","MedicineHerbal","MedicineIndustrial","RawRice","Cloth","Steel","WoodLog"};
                int[] lo={50,50,20,500,100,200,200}, hi={100,100,50,1000,300,500,500};
                var indices=Enumerable.Range(0,goods.Length).InRandomOrder().Take(tier==0?1:Rand.RangeInclusive(2,3));
                foreach(int i in indices){o.goods.Add(DefDatabase<ThingDef>.GetNamed(goods[i]));int baseAmount=Rand.RangeInclusive(lo[i],hi[i])*(tier==0?1:3);o.baseRequired.Add(baseAmount);o.required.Add(Math.Max(1,Mathf.RoundToInt(baseAmount*difficulty)));o.delivered.Add(0);}
                // Existing friendly settlements are valid endpoints without loading/replacing their map.
                if(Rand.Value<0.4f)
                {
                    var settlement=Find.WorldObjects.Settlements.Where(s=>s.Faction==supported&&s.Tile.Layer==home.Tile.Layer&&Find.WorldGrid.ApproxDistanceInTiles(home.Tile,s.Tile)>=(tier==0?5:10)&&Find.WorldGrid.ApproxDistanceInTiles(home.Tile,s.Tile)<=(tier==0?15:30)).OrderBy(s=>Find.WorldGrid.ApproxDistanceInTiles(home.Tile,s.Tile)).FirstOrDefault();
                    if(settlement!=null && !Pending.Any(p=>p.tile==settlement.Tile)){o.destination=settlement;o.tile=settlement.Tile;}
                }
            }
            o.patientCount=kind==6?1:kind==3?Rand.RangeInclusive(4,12):tier==0?Rand.RangeInclusive(3,6):Rand.RangeInclusive(8,kind==2?20:15);
            // Persist the single-patient scenario with the offer, not a reroll on visit.
            if(kind==1 && Rand.Value<0.15f){o.bionicCase=true;o.patientCount=1;}
            o.durationTicks=(kind==0?(tier==0?20:30):kind==1?(tier==0?20:40):kind==2?(tier==0?15:30):kind==3?25:kind==6?40:30)*60000;
            o.defenceTicks=Rand.RangeInclusive(1,3)*60000;
            objective=o;reason=null;return true;
        }
        public static bool TryPickup(Map map,List<Pawn> required,string signalTag,out TransportShip ship)
        {
            ship=null;
            var shipDef=DefDatabase<TransportShipDef>.GetNamedSilentFail("Ship_Shuttle");
            if(map==null||shipDef==null||required.NullOrEmpty()||required.Any(p=>p==null||p.Dead||!p.Spawned||p.Map!=map))return false;
            IntVec3 landing=DropCellFinder.TryFindSafeLandingSpotCloseToColony(map,ThingDefOf.Shuttle.size,Authorization.GetUN());
            if(!landing.IsValid)return false;
            ship=TransportShipMaker.MakeTransportShip(shipDef,null);
            ship.shipThing.SetFaction(Authorization.GetUN());
            ship.shipThing.questTags=new List<string>{signalTag};
            ship.ShuttleComp.requiredPawns=required.ToList();
            ship.ShuttleComp.acceptColonists=true;ship.ShuttleComp.acceptColonyPrisoners=true;ship.ShuttleComp.acceptChildren=true;
            ship.ShuttleComp.onlyAcceptColonists=false;ship.ShuttleComp.onlyAcceptHealthy=false;
            ship.ArriveAt(landing,map.Parent);
            var wait=(ShipJob_Wait)ShipJobMaker.MakeShipJob(ShipJobDefOf.WaitForever);
            wait.leaveImmediatelyWhenSatisfied=true;
            ship.AddJob(wait);
            return true;
        }
        public static bool TryPrepareSupplement(UNMissionObjective main,int tier,out UNMissionObjective extra)
        {
            extra=null;
            if(main==null || (main.kind!=1&&main.kind!=4) || main.supportedFaction==null || main.supportedFaction.defeated || main.supportedFaction.deactivated)return false;
            if(!TrySupplementTile(main.homeMap,tier,out PlanetTile tile))return false;
            // Reserve a different tile; the actual camp is only created on acceptance.
            var o=new UNMissionObjective{kind=0,tier=tier,homeMap=main.homeMap,supportedFaction=main.supportedFaction,tile=tile,difficulty=main.difficulty,seed=Rand.Int,supplement=true,durationTicks=(tier==0?20:30)*60000};
            o.goods.Add(DefDatabase<ThingDef>.GetNamed("MedicineIndustrial"));o.required.Add(Math.Max(1,Mathf.RoundToInt((tier==0?30:90)*main.difficulty)));o.delivered.Add(0);
            extra=o;return true;
        }
        static bool TrySupplementTile(Map home,int tier,out PlanetTile tile)
        {
            tile=PlanetTile.Invalid;
            return home!=null&&Find.Maps.Contains(home)&&TileFinder.TryFindNewSiteTile(out tile,home.Tile,tier==0?5:10,tier==0?15:30,
                allowCaravans:false,validator:t=>!Find.WorldObjects.AnyWorldObjectAt(t)&&!Pending.Any(o=>o.tile==t));
        }
        static void RelocateLegacySupplement(Quest q)
        {
            var reward=q.GetFirstPartOfType<UNMissionReward>();
            var objective=q.GetFirstPartOfType<UNMissionObjective>();
            if(reward==null||!reward.optional||objective==null||!objective.supplement||
                !(objective.destination is UNMissionSite oldSite)||oldSite.missionQuestId!=reward.parentId)return;
            if(!TrySupplementTile(objective.homeMap,objective.tier,out PlanetTile tile))return;
            objective.destination=null;
            objective.tile=tile;
            q.description=objective.OfferDescription;
        }
        // Permission is bound to registered combatants on this mission map, never an entire faction.
        public static bool AuthorizedCombat(Thing attacker,Pawn victim)=>attacker?.Faction==Faction.OfPlayer && victim?.Map!=null && Active.Any(o=>o.destination?.Map==victim.Map&&o.enemies.Contains(victim)&&!victim.Downed&&!victim.IsPrisoner);
        public static bool IsProtected(Pawn pawn)=>Active.Any(o=>o.targets.Contains(pawn)&&o.kind!=6);
        internal static bool? LocalHostility(Thing a,Thing b)
        {
            if(a==null||b==null||a.Map==null||a.Map!=b.Map)return null;
            foreach(var o in Active.Where(o=>o.destination?.Map==a.Map))
            {
                bool ae=a is Pawn ap&&o.enemies.Contains(ap),be=b is Pawn bp&&o.enemies.Contains(bp);
                bool aa=a.Faction==Faction.OfPlayer||(a is Pawn pa&&o.allies.Contains(pa));
                bool ba=b.Faction==Faction.OfPlayer||(b is Pawn pb&&o.allies.Contains(pb));
                if((ae&&ba)||(be&&aa))return true;
                if((a is Pawn p1&&o.targets.Contains(p1)&&o.kind!=6&&ba)||(b is Pawn p2&&o.targets.Contains(p2)&&o.kind!=6&&aa))return false;
            }
            return null;
        }
        [HarmonyPatch(typeof(GenHostility),nameof(GenHostility.HostileTo),new[]{typeof(Thing),typeof(Thing)})]
        static class MissionHostility
        {static void Postfix(Thing a,Thing b,ref bool __result){var local=LocalHostility(a,b);if(local.HasValue)__result=local.Value;}}
        [HarmonyPatch(typeof(Caravan),nameof(Caravan.GetGizmos))]
        static class CaravanHandover
        {
            static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result,Caravan __instance)
            {
                foreach(var g in __result)yield return g;
                if(!__instance.IsPlayerControlled)yield break;
                foreach(var o in Active.Where(o=>o.kind==0&&o.destination!=null&&o.destination.Tile==__instance.Tile).ToList())
                {var current=o;yield return new Command_Action{defaultLabel="UNMS_Deliver".Translate(),defaultDesc=current.OfferDescription,icon=TexButton.Info,action=()=>current.DeliverCaravan(__instance)};}
            }
        }
    }
    public sealed class UNMissionSite:Site
    {
        public int missionQuestId;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref missionQuestId,"unMissionQuestId",-1);
            // Old item/structure tracking is intentionally no longer used.
        }
        public UNMissionObjective Objective=>Find.QuestManager.QuestsListForReading.FirstOrDefault(q=>q.id==missionQuestId)?.PartsListForReading.OfType<UNMissionObjective>().FirstOrDefault();
        public override void PostMapGenerate()
        {
            base.PostMapGenerate();
            Objective?.Generate(Map);
        }
        bool Reserved
        {
            get
            {
                foreach(var q in Find.QuestManager.QuestsListForReading)
                {
                    var o=q.GetFirstPartOfType<UNMissionObjective>();
                    if(o?.destination!=this && q.id!=missionQuestId)continue;
                    if(q.State==QuestState.Ongoing||q.State==QuestState.NotYetAccepted)return true;
                    // Do not race Package 1's reward processing and optional follow-up generation.
                    var r=q.GetFirstPartOfType<UNMissionReward>();
                    if(q.State==QuestState.EndedSuccess&&r!=null&&!r.settled)return true;
                }
                return false;
            }
        }
        public bool CanSafelyClose
        {
            get
            {
                if(!Spawned||Destroyed||Reserved||Faction==Faction.OfPlayer)return false;
                if(!HasMap)return true;
                if(Map.IsPlayerHome)return false;
                // Native removal protects incoming transports, pocket maps and blocking buildings.
                bool nativeBlocked=!base.ShouldRemoveMapNow(out bool vanillaRemove)||!vanillaRemove;
                // Protect every owned pawn/animal/mech/vehicle, including held units and prisoners.
                // Other active quests' exact targets remain protected; ordinary NPCs and loose items do not.
                var activeTargets=new HashSet<Pawn>(UNMissionSites.Active.SelectMany(o=>o.targets).Where(p=>p!=null));
                bool people=Map.mapPawns.AllPawns.Any(p=>p.Faction==Faction.OfPlayer||p.IsPrisonerOfColony||p.IsSlaveOfColony||activeTargets.Contains(p));
                var held=new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(Map,held,true);
                people|=held.OfType<Pawn>().Any(p=>!p.Dead&&(p.Faction==Faction.OfPlayer||p.IsPrisonerOfColony||p.IsSlaveOfColony||activeTargets.Contains(p)));
                // Building-based transports (such as shuttles) also count as player units.
                bool transports=Map.listerThings.AllThings.Concat(held).Any(t=>t.Faction==Faction.OfPlayer&&t.TryGetComp<CompTransporter>()!=null);
                return UNMissionPolicy.CanCloseSite(false,false,people||transports,nativeBlocked);
            }
        }
        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject=HasMap&&CanSafelyClose;
            return alsoRemoveWorldObject;
        }
        public void TryAutomaticClose()
        {
            if(!Spawned||Destroyed||Reserved||Faction==Faction.OfPlayer)return;
            if(HasMap)CheckRemoveMapNow();
            else if(CanSafelyClose)Destroy();
        }
        public override string GetInspectString()
        {
            var result=base.GetInspectString();
            foreach(var o in UNMissionSites.Active.Where(o=>o.destination==this))
                result+="\n"+"UNQ_Remaining".Translate(UNQuestDisplay.Time(o.RemainingTicks));
            if(!Reserved&&Spawned&&!Destroyed)
                result+="\n"+(CanSafelyClose?"UNMS_CleanupReady":"UNMS_CleanupWaiting").Translate();
            return result;
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach(var g in base.GetGizmos())yield return g;
            foreach(var o in UNMissionSites.Active.Where(o=>o.destination==this).ToList())
            {
                var current=o;
                if(HasMap && (o.kind==0||o.kind==2||o.kind==3))yield return new Command_Action{defaultLabel=(o.kind==0?"UNMS_Deliver":"UNMS_TakeCustody").Translate(),defaultDesc=current.OfferDescription,icon=TexButton.Info,action=()=>current.Review(Map)};
            }
            if(!Reserved)
            {
                var close=new Command_Action{defaultLabel="UNMS_RemoveSite".Translate(),defaultDesc="UNMS_UnsafeCleanup".Translate(),icon=TexButton.Info,action=()=>{if(CanSafelyClose){if(HasMap)CheckRemoveMapNow();else Destroy();}}};
                if(!CanSafelyClose)close.Disable("UNMS_UnsafeCleanup".Translate());
                yield return close;
            }
        }
    }
    // A real NPC guest lord, without vanilla visitor gift/early-departure timers.
    // The objective controls each person's stay and later changes the duty to native exit AI.
    public sealed class LordJob_UNGuestStay:LordJob
    {
        private IntVec3 spot;
        public LordJob_UNGuestStay(){}
        public LordJob_UNGuestStay(IntVec3 spot){this.spot=spot;}
        public override bool CanAutoAddPawns=>false;
        public override StateGraph CreateGraph()
        {
            var graph=new StateGraph();graph.AddToil(new LordToil_DefendPoint(spot));return graph;
        }
        public override void ExposeData(){Scribe_Values.Look(ref spot,"unGuestSpot");}
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.ExitMap))]
    internal static class UNGuestDeparture
    {
        static void Prefix(Pawn __instance,out UNMissionObjective __state)
        {__state=UNMissionSites.Active.FirstOrDefault(o=>o.MayDepartGuest(__instance));}
        static void Postfix(Pawn __instance,UNMissionObjective __state)
        {
            if(__state!=null&&!__instance.Dead&&!__instance.Spawned&&Find.WorldPawns.Contains(__instance)&&
                !__instance.IsCaravanMember()&&__instance.ParentHolder==null)
                __state.ConfirmGuestDeparture(__instance);
        }
    }
    // A vanished pawn reference is not proof of flight. Observe the actual map exit.
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.ExitMap))]
    internal static class UNEnemyDeparture
    {
        static void Prefix(Pawn __instance,out List<UNMissionObjective> __state)
        {__state=UNMissionSites.Active.Where(o=>o.MayRecordEnemyExit(__instance)).ToList();}
        static void Postfix(Pawn __instance,List<UNMissionObjective> __state)
        {
            if(__instance.Dead||__instance.Spawned||!Find.WorldPawns.Contains(__instance)||
                __instance.IsCaravanMember()||__instance.ParentHolder!=null)return;
            foreach(var o in __state)o.RecordEnemyExit(__instance);
        }
    }
    [HarmonyPatch(typeof(RecruitUtility),nameof(RecruitUtility.Recruit))]
    internal static class UNGuestRecruitment
    {
        static bool Prefix(Pawn pawn,Faction faction)
        {
            if(faction!=Faction.OfPlayer||!UNMissionSites.Active.Any(o=>o.IsMissionGuest(pawn)))return true;
            Messages.Message("UNMS_GuestNoRecruit".Translate(pawn.LabelShortCap),MessageTypeDefOf.RejectInput,false);
            return false;
        }
    }
    public sealed class UNMissionObjective:QuestPartActivable
    {
        public int kind,tier,seed,patientCount,durationTicks,defenceTicks;
        public float difficulty,enemyBudget;
        public PlanetTile tile=PlanetTile.Invalid;
        public Faction supportedFaction,enemyFaction;
        public Map homeMap;
        public MapParent destination;
        public List<ThingDef> goods=new List<ThingDef>();
        public List<int> required=new List<int>(),delivered=new List<int>(),baseRequired=new List<int>();
        public List<Pawn> targets=new List<Pawn>(),enemies=new List<Pawn>(),allies=new List<Pawn>();
        public List<Thing> protectedThings=new List<Thing>(),ownedStructures=new List<Thing>();
        public bool supplement,bionicCase;
        private int requiredLimbIndex=-1;
        private string requiredBionic;
        private bool achieved;
        public bool CanRewardSuccess=>achieved;
        public bool CompletionRequested=>finished;
        // Only durable proof, never the mere disappearance of a pawn, ship or world object.
        public bool RestoreRecordedOutcome()
        {
            if(finished)return false;
            int outcome=UNMissionPolicy.RecordedOutcome(finished,achieved,kind==0&&goods.All(g=>g!=null)&&
                UNMissionPolicy.StoredDeliveryComplete(required,delivered,goods.Count),
                kind!=0&&shuttleSent&&shuttleSatisfied,AllGuestsDeparted());
            if(outcome!=1)return false;
            Finish(true);return finished;
        }
        public bool RestoreLoadedState(Quest owningQuest,int parentId)
        {
            quest=owningQuest;
            if(finished)return true;
            if(destination==null||!destination.Spawned)
            {
                // New supplements own their site. Old accepted supplements used the parent site.
                var matching=Find.WorldObjects.AllWorldObjects.OfType<UNMissionSite>().Where(site=>
                    site.Spawned&&site.missionQuestId==owningQuest.id&&site.Tile==tile&&site.Faction==supportedFaction).ToList();
                if(matching.Count==0&&supplement&&parentId>=0)
                    matching=Find.WorldObjects.AllWorldObjects.OfType<UNMissionSite>().Where(site=>
                        site.Spawned&&site.missionQuestId==parentId&&site.Tile==tile&&site.Faction==supportedFaction).ToList();
                if(matching.Count==1)destination=matching[0];
            }
            // Never regenerate an already accepted map or replace a missing quest pawn.
            if(!started)
            {
                if(destination==null||!destination.Spawned)return false;
                started=true;
            }
            if(savedCollectionsMissing)return false;
            if(kind==0&&(goods.Count==0||goods.Any(g=>g==null)||required.Count!=goods.Count||delivered.Count!=goods.Count||
                required.Any(n=>n<=0)||delivered.Where((n,i)=>n<0||n>required[i]).Any()))return false;
            if(generated&&kind!=0)
            {
                int present=targets.Where(p=>p!=null&&(!HasGuestPhase||!GuestDeparted(p))).Select(p=>p.ThingID).Distinct().Count();
                if(patientCount<=0||present+(HasGuestPhase?departedGuests.Count:0)!=patientCount)return false;
            }
            if(guestPickupActive&&(shuttle==null||guestPickup.Count==0||guestPickup.Any(p=>p==null)))return false;
            if(kind==1&&generated&&bionicCase&&(patientCount!=1||requiredLimbIndex<0||string.IsNullOrEmpty(requiredBionic)||
                DefDatabase<HediffDef>.GetNamedSilentFail(requiredBionic)==null))return false;
            RememberMedicalHomePatients();
            UpdateEnemyRoster();
            return true;
        }
        public void RequestCompletion(bool success,UNMissionFailure reason=UNMissionFailure.Unknown)=>Finish(success,reason);
        private QuestPart_ExtraFaction custody;
        private bool started,generated,finished,shuttleSent,shuttleSatisfied,savedCollectionsMissing;
        private int began=-1,nextPoll,failureReason;
        public UNMissionFailure FailureReason=>(UNMissionFailure)failureReason;
        private TransportShip shuttle;
        private HashSet<string> enemyIds=new HashSet<string>(),enemyDeaths=new HashSet<string>(),enemyExits=new HashSet<string>();
        private bool enemyRosterRecorded,enemyRosterUncertain;
        private int nextElitePickupAttempt;
        private bool directLaunchBoarded,directLaunchEligible,directLaunchBeforeDeadline;
        bool TracksEnemies=>kind==3||kind==4||kind==5;
        bool DefenseMission=>kind==4||kind==5;
        public bool MayRecordEnemyExit(Pawn pawn)=>TracksEnemies&&generated&&!finished&&pawn!=null&&pawn.Spawned&&pawn.Map==destination?.Map&&enemies.Contains(pawn);
        public void RecordEnemyExit(Pawn pawn)
        {
            if(!finished&&TracksEnemies&&pawn!=null&&enemies.Contains(pawn))
            {enemyIds.Add(pawn.ThingID);enemyExits.Add(pawn.ThingID);}
        }
        void UpdateEnemyRoster()
        {
            if(!TracksEnemies||!generated)return;
            if(!enemyRosterRecorded)
            {
                enemyRosterUncertain|=enemies.Any(p=>p==null);
                enemyRosterRecorded=true;
            }
            foreach(var p in enemies.Where(p=>p!=null))
            {
                enemyIds.Add(p.ThingID);
                if(p.Dead)enemyDeaths.Add(p.ThingID);
                if(p.MapHeld!=null&&p.MapHeld==destination?.Map)enemyExits.Remove(p.ThingID);
            }
        }
        bool EnemyResolved(string id)
        {
            var p=enemies.FirstOrDefault(e=>e!=null&&e.ThingID==id);
            bool returned=p!=null&&p.MapHeld!=null&&p.MapHeld==destination?.Map;
            return UNMissionPolicy.EnemyNeutralized(true,enemyDeaths.Contains(id)||p?.Dead==true,p?.Downed==true,
                p?.IsPrisonerOfColony==true,enemyExits.Contains(id),returned);
        }
        bool AllEnemiesResolved=>enemyRosterRecorded&&!enemyRosterUncertain&&enemyIds.All(EnemyResolved);
        int DefenseTicksLeft=>UNMissionPolicy.DefenseRemaining(began,defenceTicks,Find.TickManager.TicksGame);
        bool ProtectedPeopleSafe=>patientCount>0&&targets.Count==patientCount&&targets.All(p=>p!=null&&!p.Dead&&p.Spawned&&p.Map==destination?.Map);
        bool ProtectedStructureSafe=>protectedThings.Count>0&&protectedThings.All(t=>t!=null&&!t.Destroyed&&t.Spawned&&t.Map==destination?.Map);
        bool EliteEligible(Pawn p)=>p!=null&&!p.Dead&&p.IsPrisonerOfColony&&GuestHealthy(p);
        Pawn EliteTarget=>targets.Count==1?targets[0]:null;
        string EliteStatus()
        {
            var p=EliteTarget;
            if(p==null||p.Dead)return "UNMIL_TargetMissing".Translate();
            if(shuttle!=null)
            {
                if(!EliteEligible(p))return "UNMIL_EliteStabilize".Translate(p.LabelShortCap);
                if(OnPickupShuttle(p))return "UNMIL_EliteBoarded".Translate(p.LabelShortCap);
                if(!PickupLanded)return "UNMIL_EliteIncoming".Translate(p.LabelShortCap);
                return "UNMIL_EliteLoad".Translate(p.LabelShortCap);
            }
            if(!p.IsPrisonerOfColony)return "UNMIL_EliteCapture".Translate(p.LabelShortCap);
            if(!OnHomeHeld(p))return "UNMIL_EliteHome".Translate(p.LabelShortCap);
            if(!EliteEligible(p))return "UNMIL_EliteStabilize".Translate(p.LabelShortCap);
            if(!p.Spawned)return "UNMIL_EliteReleaseTransport".Translate(p.LabelShortCap);
            if(Find.TickManager.TicksGame<nextElitePickupAttempt)
                return "UNMIL_PickupRetry".Translate(UNQuestDisplay.Time(UNMissionPolicy.TicksUntil(nextElitePickupAttempt,Find.TickManager.TicksGame)));
            return "UNMIL_EliteRequest".Translate(p.LabelShortCap);
        }
        public string MilitarySummary()
        {
            if(kind<3||finished)return null;
            if(kind==6)return generated?EliteStatus():null;
            var lines=new List<string>();
            if(DefenseMission)
            {
                lines.Add("UNMIL_DefenseDuration".Translate(UNQuestDisplay.Time(defenceTicks)));
                if(!generated||began<0)lines.Add("UNMIL_DefenseNotStarted".Translate());
                else lines.Add(DefenseTicksLeft>0?"UNMIL_DefenseRemaining".Translate(UNQuestDisplay.Time(DefenseTicksLeft)).ToString():"UNMIL_DefenseDone".Translate().ToString());
                if(generated)lines.Add("UNMIL_Protected".Translate(targets.Count(p=>p!=null&&!p.Dead&&p.Spawned&&p.Map==destination?.Map),
                    patientCount,ProtectedStructureSafe?"UNMIL_Intact".Translate().ToString():"UNMIL_NotIntact".Translate().ToString()));
            }
            if(generated&&TracksEnemies)
            {
                int resolved=enemyIds.Count(EnemyResolved);
                lines.Add("UNMIL_Enemies".Translate(resolved,enemyIds.Count,enemyIds.Count-resolved));
                if(!enemyRosterRecorded||enemyRosterUncertain)lines.Add("UNMIL_RosterUnknown".Translate());
                if(kind==3)
                {
                    int freed=departedGuests.Count+targets.Count(p=>p!=null&&!GuestDeparted(p)&&!p.Dead&&
                        (p.Faction==Faction.OfPlayer||IsNativeGuest(p)||guestSince.ContainsKey(p.ThingID))&&!p.IsPrisoner);
                    lines.Add("UNMIL_Rescued".Translate(freed,patientCount));
                }
            }
            return string.Join("\n",lines);
        }
        // Stable ThingIDs survive individual exits and save/load, even if a world pawn is discarded.
        private Dictionary<string,int> guestSince=new Dictionary<string,int>();
        private HashSet<string> departedGuests=new HashSet<string>();
        // Medical patients observed at home must finish the guest route.
        private HashSet<string> medicalHomePatients=new HashSet<string>();
        private List<Pawn> guestPickup=new List<Pawn>();
        private bool guestPickupActive,guestPickupAborting,guestUnloadOrdered;
        private int nextGuestPickupAttempt;
        // Transient UI observations populated only by the mission poll, never by drawing the quest.
        private readonly HashSet<string> guestNeedsPickup=new HashSet<string>();
        private readonly HashSet<string> guestShuttleUnreachable=new HashSet<string>();
        private bool guestLaunchAllBoarded,guestLaunchAllEligible,guestLaunchAnyBoarded,guestLaunchBeforeDeadline;
        bool HasGuestPhase=>UNMissionPolicy.UsesGuestStay(kind);
        public bool IsMissionGuest(Pawn pawn)=>HasGuestPhase&&pawn!=null&&guestSince.ContainsKey(pawn.ThingID)&&!departedGuests.Contains(pawn.ThingID);
        bool GuestDeparted(Pawn pawn)=>pawn!=null&&departedGuests.Contains(pawn.ThingID);
        bool OnHomeHeld(Pawn pawn)=>pawn!=null&&homeMap!=null&&pawn.MapHeld==homeMap&&Find.Maps.Contains(homeMap);
        bool OnHome(Pawn pawn)=>OnHomeHeld(pawn)&&pawn.Spawned;
        bool AtField(Pawn pawn)=>pawn!=null&&destination?.Map!=null&&pawn.MapHeld==destination.Map;
        bool MedicalHomeRequired(Pawn pawn)=>kind==1&&pawn!=null&&
            (medicalHomePatients.Contains(pawn.ThingID)||guestSince.ContainsKey(pawn.ThingID)||GuestDeparted(pawn)||OnHomeHeld(pawn));
        bool FieldRecovery(Pawn pawn)=>kind==1&&pawn!=null&&pawn.Spawned&&
            UNMissionPolicy.CanCountFieldRecovery(AtField(pawn),MedicalHomeRequired(pawn),MedicallyRecovered(pawn));
        void RememberMedicalHomePatients()
        {
            if(kind!=1)return;
            foreach(var pawn in targets.Where(p=>p!=null&&MedicalHomeRequired(p)))
                medicalHomePatients.Add(pawn.ThingID);
            foreach(var id in guestSince.Keys.Concat(departedGuests))medicalHomePatients.Add(id);
        }
        List<Pawn> RemainingMedicalOrGuestTargets()=>targets.Where(p=>p!=null&&!GuestDeparted(p)).Distinct().ToList();
        bool MedicalObjectivesComplete()
        {
            if(kind!=1||!generated||!started||(shuttle!=null&&!guestPickupActive))return false;
            var remaining=RemainingMedicalOrGuestTargets();
            if(remaining.Any(p=>p.Dead)||remaining.Select(p=>p.ThingID).Distinct().Count()!=remaining.Count)return false;
            return UNMissionPolicy.MedicalObjectivesComplete(patientCount,remaining.Count,remaining.Count(FieldRecovery),departedGuests.Count);
        }
        bool GuestRouteCandidate(Pawn pawn)=>kind!=1||MedicalHomeRequired(pawn)||!AtField(pawn);
        bool GuestHealthy(Pawn pawn)=>pawn!=null&&!pawn.Dead&&pawn.health!=null&&pawn.health.hediffSet.BleedRateTotal<=0f&&
            !pawn.health.hediffSet.HasHediff(HediffDefOf.Anesthetic)&&(kind!=1||MedicallyRecovered(pawn));
        bool GuestFactionValid=>supportedFaction!=null&&!supportedFaction.defeated&&!supportedFaction.deactivated&&!supportedFaction.HostileTo(Faction.OfPlayer);
        bool IsNativeGuest(Pawn pawn)=>pawn!=null&&pawn.Faction==supportedFaction&&pawn.HostFaction==Faction.OfPlayer&&
            pawn.guest!=null&&pawn.guest.GuestStatus==GuestStatus.Guest;
        public int DurationTicks=>durationTicks;
        public string OfferDescription=>"UNMS_Kind".Translate()+": "+("UNMS_Kind"+kind).Translate()+"\n"+"UNMS_Destination".Translate(destination?.Label??"UNMS_Camp".Translate().ToString(),tile.ToString())+"\n"+"UNMS_Faction".Translate(supportedFaction?.Name??"—")+"\n"+
            (kind==0?string.Join("\n",goods.Select((g,i)=>(g?.LabelCap.ToString()??"—")+": "+(i<delivered.Count?delivered[i]:0)+"/"+(i<required.Count?required[i]:0))):"UNMS_People".Translate(patientCount).ToString())+"\n"+("UNMS_Goal"+kind).Translate()+(kind==1&&bionicCase?"\n"+"UNMS_BionicGoal".Translate().ToString():"")+"\n"+"UNQ_TotalDuration".Translate(UNQuestDisplay.Time(durationTicks))+
            (DefenseMission?"\n"+"UNMIL_DefenseDuration".Translate(UNQuestDisplay.Time(defenceTicks)).ToString():"")+
            (enemyFaction!=null?"\n"+"UNMS_Enemy".Translate(enemyFaction.Name,enemyBudget.ToString("0")):"\n"+"UNMS_Peaceful".Translate());
        public int RemainingTicks=>UNMissionPolicy.RemainingTicks(durationTicks,quest?.TicksSinceAccepted??0);
        // Read-only progress display. The rules remain in PollMission/ProcessGuests.
        public override string DescriptionPart
        {
            get
            {
                if(quest==null)return null;
                string header=UNQuestDisplay.Header(quest,RemainingTicks);
                if(quest.State==QuestState.NotYetAccepted)return header+"\n"+UNQuestDisplay.Next("UNQ_NextAccept".Translate());
                if(quest.State==QuestState.EndedFailed)
                {
                    var r=quest.GetFirstPartOfType<UNMissionReward>();
                    return header+"\n"+UNQuestDisplay.Failed(r?.technicalRepair==true?UNMissionFailure.Technical:FailureReason);
                }
                if(quest.State!=QuestState.Ongoing)return header;
                if(finished)return header+"\n"+(achieved?"UNQ_CompletionPending".Translate().ToString():UNQuestDisplay.Failed(FailureReason));
                var lines=new List<string>{header,UNQuestDisplay.Next(NextStep())};
                if(kind==0)
                {
                    for(int i=0;i<goods.Count&&i<required.Count&&i<delivered.Count;i++)
                        lines.Add((goods[i]?.LabelCap.ToString()??"—")+": "+delivered[i]+"/"+required[i]);
                }
                else
                {
                    var names=targets.Where(p=>p!=null).Select(p=>p.LabelShortCap.ToString()).ToList();
                    if(names.Count>0)lines.Add("UNMS_NameTargets".Translate(string.Join(", ",names)));
                    var military=MilitarySummary();if(!military.NullOrEmpty())lines.Add(military);
                    if(HasGuestPhase&&generated)lines.Add(GuestProgress());
                }
                return string.Join("\n",lines);
            }
        }
        string NextStep()
        {
            if(!started)return "UNQ_NextSetup".Translate();
            if(kind==0)return "UNQ_NextSupply".Translate();
            if(!generated)return "UNQ_NextTravel".Translate();
            if(DefenseMission)
            {
                if(!ProtectedPeopleSafe)return "UNMIL_NextProtectPeople".Translate();
                if(!AllEnemiesResolved)return "UNMIL_NextEnemies".Translate();
                return "UNMIL_NextHold".Translate();
            }
            if(kind==6)return EliteStatus();
            if(shuttle!=null)return guestPickupAborting?"UNG_PickupAbort".Translate().ToString():
                PickupLanded?"UNQ_NextShuttle".Translate().ToString():"UNG_WaitLanding".Translate().ToString();
            if(kind==3&&!AllEnemiesResolved&&targets.Any(p=>p!=null&&!p.Dead&&!GuestDeparted(p)&&p.Faction!=Faction.OfPlayer&&AtField(p)))
                return "UNQ_NextGuards".Translate();
            var remaining=targets.Where(p=>p!=null&&!GuestDeparted(p)).ToList();
            if(kind==1)
            {
                if(remaining.Any(p=>AtField(p)&&!MedicalHomeRequired(p)&&!FieldRecovery(p)))return "UNMED_NextField".Translate();
                if(remaining.Any(p=>GuestRouteCandidate(p)&&!OnHomeHeld(p)))return "UNMED_NextTransport".Translate();
                if(remaining.Any(p=>GuestRouteCandidate(p)&&!MedicallyRecovered(p)))return "UNMED_NextHome".Translate();
                remaining=remaining.Where(GuestRouteCandidate).ToList();
                if(remaining.Count==0)return "UNMED_NextReview".Translate();
            }
            if(remaining.Any(p=>!OnHomeHeld(p)))return "UNQ_NextBringHome".Translate();
            if(remaining.Any(p=>!GuestHealthy(p)))return "UNQ_NextStabilize".Translate();
            if(!GuestFactionValid)return "UNQ_NextGuestFaction".Translate();
            if(remaining.Any(p=>!IsNativeGuest(p)))return "UNG_GuestStatusPending".Translate();
            if(remaining.Any(p=>!ReadyToLeave(p,Find.TickManager.TicksGame)))return "UNQ_NextGuestStay".Translate();
            return "UNQ_NextDeparture".Translate();
        }
        public override IEnumerable<GlobalTargetInfo> QuestLookTargets {get{if(destination!=null)yield return new GlobalTargetInfo(destination);foreach(var p in targets)if(p!=null)yield return new GlobalTargetInfo(p);}}
        public override IEnumerable<Faction> InvolvedFactions{get{if(supportedFaction!=null)yield return supportedFaction;if(enemyFaction!=null)yield return enemyFaction;}}
        public override bool QuestPartReserves(Pawn pawn)=>targets.Contains(pawn);
        public void AddCustodyPart(Quest q)
        {
            if(kind==0||kind==6)return;
            custody=new QuestPart_ExtraFaction{extraFaction=new ExtraFaction(supportedFaction,ExtraFactionType.HomeFaction),affectedPawns=targets,inSignalEnable=q.InitiateSignal};
            q.AddPart(custody);
        }
        public bool RefreshOffer(float newDifficulty)
        {
            if(started)return false;
            if(baseRequired==null)baseRequired=new List<int>();
            if(baseRequired.Count!=required.Count)baseRequired=required.Select(n=>Math.Max(1,Mathf.RoundToInt(n/difficulty))).ToList();
            bool changed=Math.Abs(difficulty-newDifficulty)>0.001f;
            if(enemyBudget>0)enemyBudget=enemyBudget/difficulty*newDifficulty;
            difficulty=newDifficulty;
            for(int i=0;i<required.Count;i++)required[i]=Math.Max(1,Mathf.RoundToInt(baseRequired[i]*difficulty));
            return changed;
        }
        public bool ValidateAcceptance(out string reason)
        {
            reason="UNMS_Stale".Translate();
            if(started)return true;
            if(homeMap==null||!Find.Maps.Contains(homeMap)||supportedFaction==null||supportedFaction.defeated||supportedFaction.deactivated)return false;
            if(destination!=null && (!destination.Spawned||destination.Faction!=supportedFaction))return false;
            if(destination==null && (Find.WorldObjects.AnyWorldObjectAt(tile)||UNMissionSites.Pending.Any(o=>o!=this&&o.tile==tile)))return false;
            if(kind==6&&(targets.Count!=1||targets[0]==null||targets[0].Dead||targets[0].Spawned||targets[0].ParentHolder!=null||!Find.WorldPawns.Contains(targets[0])||UNMissionSites.Pending.Any(o=>o!=this&&o.targets.Contains(targets[0]))))return false;
            reason=null;return true;
        }
        public void Start(Quest owningQuest)
        {
            if(started)return;
            quest=owningQuest;
            string reason;if(!ValidateAcceptance(out reason)){Finish(false,UNMissionFailure.AcceptanceInvalid);return;}
            started=true;
            if(destination==null)
            {
                var site=(UNMissionSite)SiteMaker.MakeSite(DefDatabase<SitePartDef>.GetNamed("UN_MissionCamp"),tile,supportedFaction,false,0f,DefDatabase<WorldObjectDef>.GetNamed("UN_MissionSite"));
                site.missionQuestId=quest.id;site.customLabel=quest.name;destination=site;
                Find.WorldObjects.Add(site);
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref kind,"unKind");Scribe_Values.Look(ref tier,"unTier");Scribe_Values.Look(ref seed,"unSeed");Scribe_Values.Look(ref patientCount,"unPatientCount");Scribe_Values.Look(ref durationTicks,"unDuration");Scribe_Values.Look(ref defenceTicks,"unDefence");
            Scribe_Values.Look(ref difficulty,"unDifficulty",1f);Scribe_Values.Look(ref enemyBudget,"unEnemyBudget");Scribe_Values.Look(ref tile,"unTile");
            Scribe_References.Look(ref supportedFaction,"unSupported");Scribe_References.Look(ref enemyFaction,"unEnemy");Scribe_References.Look(ref homeMap,"unHome");Scribe_References.Look(ref destination,"unDestination");
            Scribe_Collections.Look(ref goods,"unGoods",LookMode.Def);Scribe_Collections.Look(ref required,"unRequired",LookMode.Value);Scribe_Collections.Look(ref delivered,"unDelivered",LookMode.Value);Scribe_Collections.Look(ref baseRequired,"unBaseRequired",LookMode.Value);
            Scribe_Collections.Look(ref targets,"unTargets",LookMode.Reference);Scribe_Collections.Look(ref enemies,"unEnemies",LookMode.Reference);Scribe_Collections.Look(ref allies,"unAllies",LookMode.Reference);Scribe_Collections.Look(ref protectedThings,"unProtectedThings",LookMode.Reference);Scribe_Collections.Look(ref ownedStructures,"unOwnedStructures",LookMode.Reference);Scribe_Values.Look(ref supplement,"unSupplement");Scribe_Values.Look(ref achieved,"unAchieved");
            Scribe_Values.Look(ref bionicCase,"unBionicCase",false);Scribe_Values.Look(ref requiredLimbIndex,"unRequiredLimbIndex",-1);Scribe_Values.Look(ref requiredBionic,"unRequiredBionic");
            Scribe_Values.Look(ref started,"unStarted");Scribe_Values.Look(ref generated,"unGenerated");Scribe_Values.Look(ref finished,"unFinished");Scribe_Values.Look(ref shuttleSent,"unShuttleSent");Scribe_Values.Look(ref shuttleSatisfied,"unShuttleSatisfied");Scribe_Values.Look(ref began,"unBegan",-1);Scribe_References.Look(ref shuttle,"unShuttle");
            Scribe_Collections.Look(ref guestSince,"unGuestSince",LookMode.Value,LookMode.Value);
            Scribe_Collections.Look(ref departedGuests,"unDepartedGuests",LookMode.Value);
            Scribe_Collections.Look(ref medicalHomePatients,"unMedicalHomePatients",LookMode.Value);
            Scribe_Collections.Look(ref enemyIds,"unEnemyIds",LookMode.Value);
            Scribe_Collections.Look(ref enemyDeaths,"unEnemyDeaths",LookMode.Value);
            Scribe_Collections.Look(ref enemyExits,"unEnemyExits",LookMode.Value);
            Scribe_Values.Look(ref enemyRosterRecorded,"unEnemyRosterRecorded",false);
            Scribe_Values.Look(ref enemyRosterUncertain,"unEnemyRosterUncertain",false);
            Scribe_Values.Look(ref nextElitePickupAttempt,"unNextElitePickupAttempt",0);
            Scribe_Collections.Look(ref guestPickup,"unGuestPickup",LookMode.Reference);
            Scribe_Values.Look(ref guestPickupActive,"unGuestPickupActive",false);
            Scribe_Values.Look(ref guestPickupAborting,"unGuestPickupAborting",false);
            Scribe_Values.Look(ref guestUnloadOrdered,"unGuestUnloadOrdered",false);
            Scribe_Values.Look(ref nextGuestPickupAttempt,"unNextGuestPickupAttempt",0);
            Scribe_Values.Look(ref savedCollectionsMissing,"unSavedCollectionsMissing",false);
            Scribe_Values.Look(ref failureReason,"unFailureReason",0);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            {
                // Older saves have no guest collections; defaults are safe. Core missing collections are not proof.
                savedCollectionsMissing|=(kind==0&&(goods==null||required==null||delivered==null))||
                    (kind!=0&&(generated||kind==6)&&targets==null)||
                    (generated&&(kind==4||kind==5)&&(enemies==null||protectedThings==null));
                if(goods==null)goods=new List<ThingDef>();
                if(required==null)required=new List<int>();
                if(delivered==null)delivered=new List<int>();
                if(baseRequired==null)baseRequired=new List<int>();
                if(targets==null)targets=new List<Pawn>();
                if(enemies==null)enemies=new List<Pawn>();
                if(allies==null)allies=new List<Pawn>();
                if(protectedThings==null)protectedThings=new List<Thing>();
                if(ownedStructures==null)ownedStructures=new List<Thing>();
                if(guestSince==null)guestSince=new Dictionary<string,int>();
                if(departedGuests==null)departedGuests=new HashSet<string>();
                if(medicalHomePatients==null)medicalHomePatients=new HashSet<string>();
                if(enemyRosterRecorded&&(enemyIds==null||enemyDeaths==null||enemyExits==null))enemyRosterUncertain=true;
                if(enemyIds==null)enemyIds=new HashSet<string>();
                if(enemyDeaths==null)enemyDeaths=new HashSet<string>();
                if(enemyExits==null)enemyExits=new HashSet<string>();
                if(guestPickup==null)guestPickup=new List<Pawn>();
            }
        }
        public override void QuestPartTick()
        {
            base.QuestPartTick();
            PollMission();
        }
        // The manager also calls this for accepted quests, in case the old activation signal
        // was lost. One shared poll timer prevents double processing.
        public void PollMission()
        {
            if(!started||finished||quest.State!=QuestState.Ongoing)return;
            if(Find.TickManager.TicksGame<nextPoll)return;nextPoll=Find.TickManager.TicksGame+250;
            if(HasGuestPhase&&AllGuestsDeparted()){Finish(true);return;}
            if(quest.TicksSinceAccepted>=durationTicks){Finish(false,UNMissionFailure.Deadline);return;}
            if(MissingOrDeadTarget()){Finish(false,UNMissionFailure.TargetLost);return;}
            if(destination==null||!destination.Spawned){Finish(false,UNMissionFailure.DestinationLost);return;}
            if(custody==null)custody=quest.PartsListForReading.OfType<QuestPart_ExtraFaction>().FirstOrDefault();
            if(custody!=null)custody.affectedPawns=targets;
            UpdateEnemyRoster();
            if(generated&&DefenseMission)
            {
                if(targets.Count!=patientCount||patientCount<=0||protectedThings.Count==0){Finish(false,UNMissionFailure.ProtectionLost);return;}
                if(protectedThings.Any(t=>t==null||t.Destroyed)){Finish(false,UNMissionFailure.ProtectionLost);return;}
                if(UNMissionPolicy.DefenseComplete(RemainingTicks>0,began>=0,DefenseTicksLeft,ProtectedPeopleSafe,ProtectedStructureSafe,AllEnemiesResolved))
                {Finish(true);return;}
            }
            if(generated&&HasGuestPhase)
            {
                // Preserve an already dispatched pre-v0.3.3 shuttle. New civilian journeys
                // use the guest lifecycle; only military arrests retain direct pickup.
                if(shuttle==null||guestPickupActive)ProcessGuests();
            }
            if(generated&&kind==6&&targets.Count==patientCount&&shuttle==null&&homeMap!=null&&Find.Maps.Contains(homeMap)&&
                targets.All(p=>OnHome(p)&&EliteEligible(p))&&Find.TickManager.TicksGame>=nextElitePickupAttempt)
            {
                nextElitePickupAttempt=Find.TickManager.TicksGame+UNMissionPolicy.GuestPickupRetryTicks;
                shuttleSatisfied=false;
                if(UNMissionSites.TryPickup(homeMap,targets,SignalTag,out shuttle))
                    Find.LetterStack.ReceiveLetter("UNMIL_PickupTitle".Translate(),"UNMIL_PickupBody".Translate(quest.name),LetterDefOf.NeutralEvent,targets[0]);
            }
            if(shuttle!=null&&shuttle.shipThing.Destroyed&&!shuttleSent)Finish(false,UNMissionFailure.ShuttleLost);
        }
        bool MissingOrDeadTarget()
        {
            if(!HasGuestPhase)return targets.Any(p=>p==null||p.Dead);
            var remaining=targets.Where(p=>p!=null&&!GuestDeparted(p)).ToList();
            return remaining.Any(p=>p.Dead)||(generated&&remaining.Count+departedGuests.Count!=patientCount);
        }
        bool AllGuestsDeparted()=>HasGuestPhase&&generated&&UNMissionPolicy.AllDeparted(patientCount,departedGuests.Count);
        void StayAtColony(Pawn pawn)
        {
            if(!IsNativeGuest(pawn))
            {
                if(pawn.IsPrisonerOfColony||pawn.IsSlaveOfColony||pawn.guest==null)return;
                pawn.GetLord()?.Notify_PawnLost(pawn,PawnLostCondition.LeftVoluntarily);
                if(pawn.Faction==Faction.OfPlayer)pawn.SetFaction(supportedFaction);
                if(pawn.Faction!=supportedFaction)return;
                // Suppress vanilla rescuee auto-joining, not the normal guest needs.
                pawn.guest.leftAfterRescue=true;
                pawn.guest.SetGuestStatus(Faction.OfPlayer,GuestStatus.Guest);
                if(!IsNativeGuest(pawn))return;
            }
            if(!(pawn.GetLord()?.LordJob is LordJob_UNGuestStay))
            {
                pawn.GetLord()?.Notify_PawnLost(pawn,PawnLostCondition.LeftVoluntarily);
                LordMaker.MakeNewLord(supportedFaction,new LordJob_UNGuestStay(pawn.Position),homeMap,new[]{pawn});
            }
        }
        bool ReadyToLeave(Pawn pawn,int now)=>GuestFactionValid&&IsNativeGuest(pawn)&&GuestHealthy(pawn)&&
            guestSince.TryGetValue(pawn.ThingID,out int since)&&UNMissionPolicy.GuestStayComplete(since,now);
        int GuestSince(Pawn pawn)=>pawn!=null&&guestSince.TryGetValue(pawn.ThingID,out int since)?since:-1;
        bool PickupPassenger(Pawn pawn)=>guestPickupActive&&guestPickup.Contains(pawn);
        bool OnPickupShuttle(Pawn pawn)=>pawn!=null&&shuttle?.TransporterComp?.innerContainer.Contains(pawn)==true;
        int GuestCountdown(Pawn pawn,int now)=>UNMissionPolicy.ValidGuestCountdown(GuestSince(pawn),now,
            GuestFactionValid&&GuestHealthy(pawn)&&IsNativeGuest(pawn)&&(OnHomeHeld(pawn)||(PickupPassenger(pawn)&&OnPickupShuttle(pawn))));
        bool PickupLanded=>shuttle!=null&&shuttle.ShipExistsAndIsSpawned&&shuttle.shipThing.Map==homeMap;
        void ResetPickup()
        {
            guestPickup.Clear();guestPickupActive=false;guestPickupAborting=false;guestUnloadOrdered=false;
            shuttle=null;shuttleSatisfied=false;
        }
        void AbortGuestPickup()
        {
            guestPickupAborting=true;shuttleSatisfied=false;
            // Keep the existing ship reference until it physically leaves; unload safely first.
            if(!PickupLanded||guestUnloadOrdered)return;
            shuttle.ShuttleComp.requiredPawns.Clear();
            shuttle.ForceJob(ShipJobDefOf.Unload);
            shuttle.AddJob(ShipJobDefOf.FlyAway);
            guestUnloadOrdered=true;
        }
        void ProcessGuests()
        {
            guestNeedsPickup.Clear();guestShuttleUnreachable.Clear();
            RememberMedicalHomePatients();
            // Recheck actual recovery rather than permanently crediting a patient who may relapse.
            if(MedicalObjectivesComplete()){Finish(true);return;}
            if(homeMap==null||!Find.Maps.Contains(homeMap))return;
            int now=Find.TickManager.TicksGame;
            if(guestPickupActive)
            {
                foreach(var pawn in guestPickup.Where(p=>p!=null))
                    if(!GuestFactionValid||!GuestHealthy(pawn)||!IsNativeGuest(pawn)||
                        (!OnHomeHeld(pawn)&&!OnPickupShuttle(pawn)))
                        guestSince[pawn.ThingID]=-1;
                if(guestPickupAborting||guestPickup.Any(p=>p==null||!ReadyToLeave(p,now)))AbortGuestPickup();
            }
            if(!GuestFactionValid)
            {
                foreach(var pawn in targets.Where(p=>p!=null&&!GuestDeparted(p)&&guestSince.ContainsKey(p.ThingID)))
                    guestSince[pawn.ThingID]=-1;
                return;
            }
            var pickupCandidates=new List<Pawn>();
            foreach(var pawn in targets.Where(p=>p!=null&&!GuestDeparted(p)).ToList())
            {
                string id=pawn.ThingID;
                if(PickupPassenger(pawn))
                {
                    if(guestPickupAborting&&OnHome(pawn)&&!OnPickupShuttle(pawn))StayAtColony(pawn);
                    continue;
                }
                if(!OnHome(pawn))
                {
                    // Carrying a guest to a bed on the home map must not erase a valid stay.
                    // An actual departure from the map, relapse, or loss of guest status does.
                    if(guestSince.ContainsKey(id)&&(!OnHomeHeld(pawn)||!GuestHealthy(pawn)||!IsNativeGuest(pawn)))guestSince[id]=-1;
                    continue;
                }
                if(guestSince.ContainsKey(id)&&!IsNativeGuest(pawn))guestSince[id]=-1;
                if(!GuestHealthy(pawn))
                {
                    // A relapse restarts the required uninterrupted day; retain the visitor status.
                    if(guestSince.ContainsKey(id)){guestSince[id]=-1;StayAtColony(pawn);}
                    continue;
                }
                if(!guestSince.TryGetValue(id,out int since)||since<0)
                {
                    StayAtColony(pawn);
                    if(!IsNativeGuest(pawn))continue;
                    guestSince[id]=now;
                }
                if(!ReadyToLeave(pawn,now)){StayAtColony(pawn);continue;}
                bool exitAvailable=!pawn.Downed&&RCellFinder.TryFindBestExitSpot(pawn,out IntVec3 exit);
                if(UNMissionPolicy.NeedsGuestPickup(GuestSince(pawn),now,true,pawn.Downed,exitAvailable))
                {
                    guestNeedsPickup.Add(id);StayAtColony(pawn);pickupCandidates.Add(pawn);continue;
                }
                if(!(pawn.GetLord()?.LordJob is LordJob_ExitMapBest))
                {
                    pawn.GetLord()?.Notify_PawnLost(pawn,PawnLostCondition.LeftVoluntarily);
                    LordMaker.MakeNewLord(supportedFaction,new LordJob_ExitMapBest(),homeMap,new[]{pawn});
                }
            }
            if(shuttle==null&&pickupCandidates.Count>0&&now>=nextGuestPickupAttempt)
            {
                nextGuestPickupAttempt=now+UNMissionPolicy.GuestPickupRetryTicks;
                if(UNMissionSites.TryPickup(homeMap,pickupCandidates,SignalTag,out shuttle))
                {
                    guestPickup=pickupCandidates;guestPickupActive=true;guestPickupAborting=false;
                    guestUnloadOrdered=false;shuttleSatisfied=false;
                    Find.LetterStack.ReceiveLetter("UNG_PickupLetterTitle".Translate(),
                        "UNG_PickupLetterBody".Translate(quest.name,guestPickup.Count),
                        LetterDefOf.NeutralEvent,guestPickup[0]);
                }
            }
            if(guestPickupActive&&!guestPickupAborting&&PickupLanded)
            {
                var enter=DefDatabase<JobDef>.GetNamedSilentFail("EnterTransporterAndDefendSelf");
                if(enter!=null)foreach(var pawn in guestPickup.Where(p=>OnHome(p)&&!p.Downed&&ReadyToLeave(p,now)))
                {
                    if(pawn.CurJob?.def==enter)continue;
                    if(pawn.CanReach(shuttle.shipThing,PathEndMode.Touch,Danger.Some))
                        pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(enter,shuttle.shipThing));
                    else guestShuttleUnreachable.Add(pawn.ThingID);
                }
            }
        }
        public bool MayDepartGuest(Pawn pawn)=>!finished&&quest.State==QuestState.Ongoing&&RemainingTicks>0&&IsMissionGuest(pawn)&&
            OnHome(pawn)&&ReadyToLeave(pawn,Find.TickManager.TicksGame)&&!PickupPassenger(pawn);
        public void ConfirmGuestDeparture(Pawn pawn)
        {
            if(finished||quest.State!=QuestState.Ongoing||!IsMissionGuest(pawn)||pawn.Dead)return;
            if(RemainingTicks<=0){Finish(false,UNMissionFailure.Deadline);return;}
            departedGuests.Add(pawn.ThingID);
            if(AllGuestsDeparted()||MedicalObjectivesComplete())Finish(true);
        }
        string SignalTag=>"Quest"+quest.id+".UNPickup";
        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            var passengers=guestPickupActive?guestPickup:targets;
            if(!finished&&signal.tag==SignalTag+".SentSatisfied"&&shuttle!=null&&passengers.Count>0&&
                RemainingTicks>0&&!guestPickupAborting&&
                passengers.All(p=>p!=null&&!p.Dead&&(!guestPickupActive||ReadyToLeave(p,Find.TickManager.TicksGame))&&
                    (kind!=6||EliteEligible(p))&&OnPickupShuttle(p)))
                shuttleSatisfied=true;
        }
        // Snapshot actual passengers before the native launch moves the container.
        public void PrepareGuestLaunch(TransportShip departing)
        {
            if(finished||departing!=shuttle)return;
            if(!guestPickupActive)
            {
                directLaunchBeforeDeadline=RemainingTicks>0;
                directLaunchBoarded=targets.Count==patientCount&&patientCount>0&&targets.All(OnPickupShuttle);
                directLaunchEligible=targets.All(p=>p!=null&&!p.Dead&&(kind!=6||EliteEligible(p)));
                return;
            }
            guestLaunchBeforeDeadline=RemainingTicks>0;
            guestLaunchAnyBoarded=guestPickup.Any(OnPickupShuttle);
            guestLaunchAllBoarded=guestPickup.Count>0&&guestPickup.All(OnPickupShuttle);
            guestLaunchAllEligible=guestPickup.Count>0&&guestPickup.All(p=>p!=null&&!p.Dead&&ReadyToLeave(p,Find.TickManager.TicksGame));
        }
        public void NotifyDeparted(TransportShip departed)
        {
            if(finished||quest.State!=QuestState.Ongoing||departed!=shuttle)return;
            if(RemainingTicks<=0){Finish(false,UNMissionFailure.Deadline);return;}
            if(guestPickupActive)
            {
                bool confirmed=UNMissionPolicy.CanConfirmGuestLaunch(guestPickupActive,guestPickupAborting,
                    guestLaunchBeforeDeadline,guestLaunchAllBoarded,guestLaunchAllEligible,shuttleSatisfied);
                if(confirmed)
                {
                    if(guestPickup.Any(p=>p==null||p.Dead)){Finish(false,UNMissionFailure.TargetLost);return;}
                    foreach(var pawn in guestPickup)departedGuests.Add(pawn.ThingID);
                    ResetPickup();
                    if(AllGuestsDeparted()){shuttleSent=true;Finish(true);}
                    else if(MedicalObjectivesComplete())Finish(true);
                }
                else if(guestLaunchAnyBoarded)Finish(false,UNMissionFailure.GuestDepartureUnverified);
                else ResetPickup(); // confirmed empty departure; another pickup may be attempted
                return;
            }
            // Legacy civilian direct shuttles retain their eligibility rules; elite arrests
            // additionally require the original living, stabilized prisoner at launch.
            if(UNMissionPolicy.DirectPickupComplete(directLaunchBeforeDeadline,
                targets.Count==patientCount&&targets.All(p=>p!=null&&!p.Dead),directLaunchBoarded,directLaunchEligible,shuttleSatisfied))
            {shuttleSent=true;Finish(true);}
            else if(kind==6)
            {
                // A failed native launch cannot be called a success; only retry when the
                // actual target remains at home, otherwise fail with a concrete reason.
                if(EliteTarget!=null&&OnHomeHeld(EliteTarget)&&!OnPickupShuttle(EliteTarget))
                {shuttle=null;shuttleSatisfied=false;}
                else Finish(false,UNMissionFailure.PickupUnverified);
            }
        }
        bool CanAct()
        {
            if(finished||quest.State!=QuestState.Ongoing)return false;
            if(quest.TicksSinceAccepted>=durationTicks){Finish(false,UNMissionFailure.Deadline);return false;}
            if(MissingOrDeadTarget()){Finish(false,UNMissionFailure.TargetLost);return false;}
            return true;
        }
        void Finish(bool success,UNMissionFailure reason=UNMissionFailure.Unknown)
        {
            if(finished||quest.State!=QuestState.Ongoing)return;
            // Record the proven outcome. The game component ends the quest outside quest/signal callbacks.
            achieved=success;failureReason=success?0:(int)reason;finished=true;
        }
        public void DeliverCaravan(Caravan caravan)
        {
            if(!CanAct()||kind!=0||destination==null||caravan.Tile!=destination.Tile||!caravan.IsPlayerControlled)return;
            Deliver(caravan.PawnsListForReading.Where(p=>p.inventory!=null).SelectMany(p=>p.inventory.innerContainer).ToList());
        }
        void Deliver(List<Thing> things)
        {
            for(int i=0;i<goods.Count;i++)foreach(var thing in things.Where(t=>t.def==goods[i]&&!t.Destroyed).ToList())
            {
                int count=Math.Min(required[i]-delivered[i],thing.stackCount);if(count<=0)break;
                var taken=thing.SplitOff(count);taken.Destroy(DestroyMode.Vanish);delivered[i]+=count;
            }
            if(delivered.SequenceEqual(required))Finish(true);
            else Messages.Message("UNMS_Partial".Translate(),MessageTypeDefOf.NeutralEvent,false);
        }
        public void Review(Map map)
        {
            if(!CanAct()||destination?.Map!=map)return;
            var marker=map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("UN_MissionHandover")).FirstOrDefault();
            var colonist=map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p=>!p.Dead&&!p.Downed&&marker!=null&&p.Position.DistanceTo(marker.Position)<=8f);
            if(colonist==null){Messages.Message("UNMS_MoveMarker".Translate(),MessageTypeDefOf.RejectInput,false);return;}
            if(kind==0)
            {
                var list=map.listerThings.AllThings.Where(t=>t.Spawned&&goods.Contains(t.def)&&!t.IsForbidden(colonist)&&t.Position.DistanceTo(marker.Position)<=8f&&colonist.CanReach(t,PathEndMode.Touch,Danger.Some)).ToList();
                list.AddRange(map.mapPawns.FreeColonistsSpawned.Where(p=>p.inventory!=null&&p.Position.DistanceTo(marker.Position)<=8f).SelectMany(p=>p.inventory.innerContainer));Deliver(list);
            }
            else if(kind==2||kind==3)
            {
                UpdateEnemyRoster();
                if(kind==3&&!AllEnemiesResolved)
                {Messages.Message("UNMS_EnemiesRemain".Translate(),MessageTypeDefOf.RejectInput,false);return;}
                // Consent/release applies to these exact mission people, making them controllable for travel.
                foreach(var p in targets.Where(p=>p!=null&&!p.Dead&&p.Map==map&&p.Position.DistanceTo(marker.Position)<=8f)){p.GetLord()?.Notify_PawnLost(p,PawnLostCondition.LeftVoluntarily);p.guest?.SetGuestStatus(null);p.SetFaction(Faction.OfPlayer);}
                Messages.Message("UNMS_ReturnHome".Translate(),MessageTypeDefOf.NeutralEvent,false);
            }
            else Messages.Message(DescriptionPart,MessageTypeDefOf.NeutralEvent,false);
        }
        // The treatment display and actual success use the same predicates.
        bool AcuteInjury(Hediff h)=>h is Hediff_Injury&&!h.IsPermanent();
        bool ActiveMedicalIllness(Hediff h)=>h.def.isBad&&!h.IsPermanent()&&h.TryGetComp<HediffComp_Immunizable>()!=null;
        BodyPartRecord BionicLimb(Pawn p)=>p?.RaceProps?.body?.AllParts.FirstOrDefault(x=>x.Index==requiredLimbIndex);
        bool RequiredBionicPresent(Pawn p)
        {
            if(!bionicCase)return true;
            if(requiredLimbIndex<0||string.IsNullOrEmpty(requiredBionic)||p?.health==null)return false;
            var part=BionicLimb(p);
            return part!=null&&!p.health.hediffSet.PartIsMissing(part)&&
                p.health.hediffSet.hediffs.Any(h=>h.Part==part&&h is Hediff_AddedPart&&h.def.defName==requiredBionic);
        }
        bool MedicallyRecovered(Pawn p)
        {
            if(p==null||p.Dead||p.health==null)return false;
            var hs=p.health.hediffSet;
            return UNMissionPolicy.MedicalRecoveryComplete(true,hs.BleedRateTotal>0f,hs.HasHediff(HediffDefOf.Anesthetic),
                hs.hediffs.Count(AcuteInjury),hs.hediffs.Count(ActiveMedicalIllness),RequiredBionicPresent(p));
        }
        string MedicalTreatment(Pawn pawn)
        {
            if(pawn==null||pawn.Dead||pawn.health==null)return "UNG_Dead".Translate();
            var hs=pawn.health.hediffSet;
            var lines=new List<string>();
            int injuries=hs.hediffs.Count(AcuteInjury);
            var illnesses=hs.hediffs.Where(ActiveMedicalIllness).Select(h=>h.def.label??h.def.defName).ToList();
            if(hs.BleedRateTotal>0f)lines.Add("UNG_Bleeding".Translate());
            if(injuries>0)lines.Add("UNMED_Injuries".Translate(injuries));
            if(illnesses.Count>0)lines.Add("UNMED_Illnesses".Translate(string.Join(", ",illnesses)));
            if(bionicCase)
            {
                var limb=BionicLimb(pawn);
                var implant=DefDatabase<ThingDef>.GetNamedSilentFail(requiredBionic??"");
                string part=limb?.Label??"—",item=implant?.LabelCap.ToString()??requiredBionic??"—";
                lines.Add((RequiredBionicPresent(pawn)?"UNMED_ImplantDone":"UNMED_ImplantPending").Translate(part,item));
            }
            if(hs.HasHediff(HediffDefOf.Anesthetic))lines.Add("UNMS_Anesthetized".Translate());
            if(MedicallyRecovered(pawn))lines.Add("UNMED_Recovered".Translate());
            return string.Join(" · ",lines);
        }
        public string MedicalSummary()
        {
            if(kind!=1||!generated||finished)return null;
            var remaining=RemainingMedicalOrGuestTargets();
            int field=remaining.Count(p=>AtField(p)&&!MedicalHomeRequired(p));
            int home=remaining.Count(MedicalHomeRequired)+departedGuests.Count;
            int transit=remaining.Count-field-(home-departedGuests.Count);
            return "UNMED_Summary".Translate(remaining.Count(FieldRecovery),field,home,patientCount,departedGuests.Count,transit);
        }
        public string GuestSummary()
        {
            if(!HasGuestPhase||!generated||finished)return null;
            var allRemaining=RemainingMedicalOrGuestTargets();
            var remaining=allRemaining.Where(GuestRouteCandidate).ToList();
            if(kind==1&&remaining.Count==0&&departedGuests.Count==0)return null;
            if(shuttle!=null&&!guestPickupActive)return "UNG_LegacyPickup".Translate();
            int now=Find.TickManager.TicksGame;
            int expected=patientCount-departedGuests.Count-(allRemaining.Count-remaining.Count);
            int groupTotal=expected+departedGuests.Count;
            int eta=UNMissionPolicy.GroupGuestCountdown(expected,remaining.Select(p=>GuestCountdown(p,now)));
            int waiting=remaining.Count(p=>GuestCountdown(p,now)<0)+Math.Max(0,expected-remaining.Count);
            var lines=new List<string>();
            if(expected==0&&eta==0)lines.Add("UNG_AllDeparted".Translate());
            else if(eta<0)lines.Add("UNG_GroupPending".Translate(waiting,groupTotal));
            else if(eta>0)lines.Add("UNG_GroupTime".Translate(UNQuestDisplay.Time(eta)));
            else lines.Add("UNG_GroupReady".Translate());
            if(guestPickupActive)
            {
                int boarded=guestPickup.Count(OnPickupShuttle);
                if(guestPickupAborting)lines.Add("UNG_PickupAbort".Translate());
                else if(!PickupLanded)lines.Add("UNG_PickupEnRoute".Translate(guestPickup.Count));
                else if(boarded==guestPickup.Count&&boarded>0)lines.Add("UNG_PickupLoaded".Translate(boarded));
                else lines.Add("UNG_PickupLanded".Translate(boarded,guestPickup.Count));
            }
            else if(remaining.Any(p=>OnHome(p)&&ReadyToLeave(p,now)))
            {
                if(now<nextGuestPickupAttempt)lines.Add("UNG_PickupRetry".Translate(UNQuestDisplay.Time(UNMissionPolicy.TicksUntil(nextGuestPickupAttempt,now))));
                else lines.Add("UNG_NaturalExit".Translate());
            }
            return string.Join("\n",lines);
        }
        string GuestProgress()
        {
            int now=Find.TickManager.TicksGame;
            var remaining=RemainingMedicalOrGuestTargets();
            var guests=remaining.Where(GuestRouteCandidate).ToList();
            var lines=new List<string>();
            if(kind==1)
            {
                lines.Add("UNMS_Recovered".Translate(departedGuests.Count+remaining.Count(MedicallyRecovered),patientCount));
                var medical=MedicalSummary();if(!medical.NullOrEmpty())lines.Add(medical);
            }
            if(kind!=1||guests.Count>0||departedGuests.Count>0)
            {
                int guestTotal=patientCount-(remaining.Count-guests.Count);
                lines.Add("UNMS_GuestProgress".Translate(departedGuests.Count+guests.Count(p=>OnHomeHeld(p)||PickupPassenger(p)&&OnPickupShuttle(p)),
                    departedGuests.Count+guests.Count(p=>(OnHomeHeld(p)||PickupPassenger(p)&&OnPickupShuttle(p))&&GuestHealthy(p)),
                    departedGuests.Count+guests.Count(p=>GuestCountdown(p,now)==0),departedGuests.Count,guestTotal).ToString());
                var summary=GuestSummary();if(!summary.NullOrEmpty())lines.Add(summary);
            }
            foreach(var pawn in targets.Where(p=>p!=null))
            {
                string state;
                int countdown=GuestCountdown(pawn,now);
                if(GuestDeparted(pawn))state="UNMS_GuestDeparted".Translate();
                else if(pawn.Dead)state="UNG_Dead".Translate();
                else if(kind==1&&AtField(pawn)&&!MedicalHomeRequired(pawn))
                    state=FieldRecovery(pawn)?"UNMED_FieldReady".Translate().ToString():
                        pawn.Spawned?"UNMED_FieldTreatment".Translate().ToString():"UNMED_FieldHeld".Translate().ToString();
                else if(shuttle!=null&&!guestPickupActive)state=OnPickupShuttle(pawn)?"UNG_Boarded".Translate().ToString():"UNG_LegacyPickup".Translate().ToString();
                else if(!OnHomeHeld(pawn)&&!(PickupPassenger(pawn)&&OnPickupShuttle(pawn)))
                    state=kind==1?"UNMED_Transport".Translate().ToString():"UNMS_GuestNeedsHome".Translate().ToString();
                else if(pawn.health?.hediffSet.HasHediff(HediffDefOf.Anesthetic)==true)state="UNMS_Anesthetized".Translate();
                else if(pawn.health?.hediffSet.BleedRateTotal>0f)state="UNG_Bleeding".Translate();
                else if(!GuestHealthy(pawn))state="UNMS_Unwell".Translate();
                else if(!GuestFactionValid)state="UNG_FactionBlocked".Translate();
                else if(!IsNativeGuest(pawn))state="UNG_GuestStatusPending".Translate();
                else if(PickupPassenger(pawn)&&guestPickupAborting)state="UNG_PickupAbortingPerson".Translate();
                else if(countdown<0)state="UNG_TimerPending".Translate();
                else if(countdown>0)state="UNQ_GuestTime".Translate(UNQuestDisplay.Time(countdown));
                else if(PickupPassenger(pawn))
                {
                    if(OnPickupShuttle(pawn))state="UNG_Boarded".Translate();
                    else if(!PickupLanded)state="UNG_PickupRequestedPerson".Translate();
                    else if(pawn.Downed)state="UNG_CarryToShuttle".Translate();
                    else if(pawn.Spawned&&guestShuttleUnreachable.Contains(pawn.ThingID))state="UNG_ShuttleUnreachable".Translate();
                    else state="UNG_BoardShuttle".Translate();
                }
                else if(!pawn.Spawned)state="UNG_HeldReady".Translate();
                else if(UNMissionPolicy.NeedsGuestPickup(GuestSince(pawn),now,true,pawn.Downed,
                    !guestNeedsPickup.Contains(pawn.ThingID)))
                    state=shuttle!=null?"UNG_NextPickupBatch".Translate().ToString():"UNG_WaitPickup".Translate().ToString();
                else state="UNG_ExitGrace".Translate(UNQuestDisplay.Time(UNMissionPolicy.ExitGraceRemaining(GuestSince(pawn),now)));
                lines.Add(pawn.LabelShortCap+": "+state);
                if(kind==1&&!GuestDeparted(pawn)&&!pawn.Dead)lines.Add("  "+MedicalTreatment(pawn));
            }
            return string.Join("\n",lines);
        }
        void AddInjury(Pawn p,BodyPartRecord part,float severity)
        {
            if(p.Dead||part==null||p.health.hediffSet.PartIsMissing(part))return;
            // Keep a margin against destroying a limb or the torso during generation.
            float safe=Math.Min(severity,p.health.hediffSet.GetPartHealth(part)*0.45f);
            if(safe<1f)return;
            var injury=HediffMaker.MakeHediff(HediffDefOf.Cut,p,part);
            injury.Severity=safe;p.health.AddHediff(injury);
        }
        void GenerateInjuries(Pawn p)
        {
            // Different severities for each person. Advanced missions and camps lean more severe.
            var limbs=p.RaceProps.body.AllParts.Where(x=>(x.def.defName=="Shoulder"||x.def.defName=="Leg")&&!p.health.hediffSet.PartIsMissing(x)).ToList();
            float roll=Rand.Value;
            if(roll<(tier==0?0.25f:0.40f))
            {
                AddInjury(p,p.RaceProps.body.corePart,Rand.Range(12f,19f));
                if(limbs.Count>0)AddInjury(p,limbs.RandomElement(),Rand.Range(5f,9f));
            }
            else if(roll<0.75f)
            {
                foreach(var limb in limbs.InRandomOrder().Take(Rand.RangeInclusive(2,4)))AddInjury(p,limb,Rand.Range(3f,7f));
                AddInjury(p,p.RaceProps.body.corePart,Rand.Range(3f,6f));
            }
            else AddInjury(p,p.RaceProps.body.corePart,Rand.Range(4f,8f));
        }
        bool GenerateBionicCase(Pawn p,Map map,IntVec3 center)
        {
            var candidates=p.RaceProps.body.AllParts.Where(x=>(x.def.defName=="Shoulder"||x.def.defName=="Leg")&&
                !p.health.hediffSet.PartIsMissing(x)&&!p.health.hediffSet.hediffs.Any(h=>h.Part==x&&h is Hediff_AddedPart)).ToList();
            if(candidates.Count==0)return false;
            var part=candidates.RandomElement();
            string name=part.def.defName=="Shoulder"?"BionicArm":"BionicLeg";
            var item=DefDatabase<ThingDef>.GetNamedSilentFail(name);
            if(item==null||DefDatabase<HediffDef>.GetNamedSilentFail(name)==null)return false;
            // Place the correct implant before creating the missing limb.
            var implant=ThingMaker.MakeThing(item);
            if(!GenPlace.TryPlaceThing(implant,center,map,ThingPlaceMode.Near))
            {
                if(!implant.Destroyed&&!implant.Spawned)implant.Destroy();
                return false;
            }
            requiredLimbIndex=part.Index;requiredBionic=name;
            p.health.AddHediff(HediffDefOf.MissingBodyPart,part);
            var medicine=ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);medicine.stackCount=6;
            if(!GenPlace.TryPlaceThing(medicine,center,map,ThingPlaceMode.Near))
            {
                if(!medicine.Destroyed&&!medicine.Spawned)medicine.Destroy();
                Log.Warning("[UN Peacekeepers] Medical quest "+quest.id+": supplied medicine could not be placed.");
            }
            return true;
        }
        public void Generate(Map map)
        {
            if(generated||finished||quest.State!=QuestState.Ongoing)return;generated=true;began=Find.TickManager.TicksGame;
            Rand.PushState(seed);
            try
            {
                IntVec3 center=CellFinder.RandomClosewalkCellNear(map.Center,map,12);
                var board=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("UN_MissionHandover"));board.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(board,center,map);protectedThings.Add(board);ownedStructures.Add(board);
                if(kind==0)return;
                if(kind!=6)
                {
                    for(int i=0;i<patientCount;i++)
                    {
                        var p=PawnGenerator.GeneratePawn(PawnKindDefOf.SpaceRefugee,supportedFaction);targets.Add(p);
                        GenSpawn.Spawn(p,CellFinder.RandomClosewalkCellNear(center,map,7),map);
                        if(kind==3)p.guest.SetGuestStatus(enemyFaction,GuestStatus.Prisoner);
                        if(kind==1&&bionicCase)
                        {
                            if(!GenerateBionicCase(p,map,center))
                            {
                                bionicCase=false;requiredLimbIndex=-1;requiredBionic=null;GenerateInjuries(p);
                                quest.description=OfferDescription;
                                Messages.Message("UNMED_BionicFallback".Translate(),MessageTypeDefOf.NeutralEvent,false);
                            }
                        }
                        else if(kind==1||kind==3)GenerateInjuries(p);
                    }
                    if(kind==1)
                    {
                        foreach(var p in targets){p.SetFaction(Faction.OfPlayer);}
                        // Optional convenience ward; never evaluated as a quest requirement.
                        CreateHospital(map,center,tier==0?6:12);
                    }
                    else LordMaker.MakeNewLord(supportedFaction,new LordJob_DefendPoint(center,6f,10f,true),map,targets);
                }
                else
                {
                    var leader=targets[0];
                    if(leader.Dead||leader.Spawned||leader.ParentHolder!=null){Finish(false,UNMissionFailure.TargetLost);return;}
                    Find.WorldPawns.RemovePawn(leader);GenSpawn.Spawn(leader,CellFinder.RandomClosewalkCellNear(center,map,8),map);enemies.Add(leader);
                }
                if(enemyFaction!=null)
                {
                    float existing=enemies.Where(p=>p!=null).Sum(p=>p.kindDef.combatPower);
                    var attackers=Group(map,enemyFaction,Math.Max(0,enemyBudget-existing),center+new IntVec3(30,0,0));enemies.AddRange(attackers);
                    var assaultTargets=targets.Where(p=>!enemies.Contains(p)).Cast<Thing>().Concat(protectedThings).ToList();
                    LordMaker.MakeNewLord(enemyFaction,kind==3||kind==6?(LordJob)new LordJob_DefendPoint(center,15f,45f):(LordJob)new LordJob_AssaultThings(enemyFaction,assaultTargets),map,enemies);
                    var un=Authorization.GetUN();
                    if(un!=null){var force=Group(map,un,enemyBudget*0.1f,center+new IntVec3(-12,0,0));allies.AddRange(force);LordMaker.MakeNewLord(un,new LordJob_DefendPoint(center,12f,45f),map,force);}
                    if(kind==5){var defenders=Group(map,supportedFaction,enemyBudget*0.25f,center);allies.AddRange(defenders);LordMaker.MakeNewLord(supportedFaction,new LordJob_DefendPoint(center,15f,45f),map,defenders);}
                    foreach(var p in enemies.Concat(allies))map.attackTargetsCache.UpdateTarget(p);
                    UpdateEnemyRoster();
                }
            }
            finally{Rand.PopState();}
        }
        public override void Cleanup()
        {
            base.Cleanup();
            // Cancelled/failed stays must not strand non-player guests in a permanent stay lord.
            if(HasGuestPhase)foreach(var pawn in targets.Where(p=>p!=null&&!p.Dead&&!GuestDeparted(p)&&p.Spawned&&
                p.Faction==supportedFaction&&p.GetLord()?.LordJob is LordJob_UNGuestStay).ToList())
            {
                pawn.GetLord()?.Notify_PawnLost(pawn,PawnLostCondition.LeftVoluntarily);
                if(!pawn.IsPrisonerOfColony&&!pawn.IsSlaveOfColony)
                    LordMaker.MakeNewLord(pawn.Faction,new LordJob_ExitMapBest(),pawn.Map,new[]{pawn});
            }
            // Exact mission civilians return to their original faction, even after cancellation/reload.
            if(kind!=6)foreach(var pawn in targets.Where(p=>p!=null&&!p.Dead&&p.Faction==Faction.OfPlayer))
            {
                pawn.guest?.SetGuestStatus(null);
                pawn.SetFaction(supportedFaction);
                if(pawn.Spawned&&!shuttleSent)
                {
                    pawn.GetLord()?.Notify_PawnLost(pawn,PawnLostCondition.LeftVoluntarily);
                    LordMaker.MakeNewLord(supportedFaction,new LordJob_ExitMapBest(),pawn.Map,new[]{pawn});
                }
            }
            if(shuttle!=null&&!shuttleSent&&shuttle.ShipExistsAndIsSpawned)
            {
                shuttle.ShuttleComp.requiredPawns.Clear();
                shuttle.ForceJob(ShipJobDefOf.Unload);shuttle.AddJob(ShipJobDefOf.FlyAway);
            }
        }
        static List<Pawn> Group(Map map,Faction faction,float points,IntVec3 center)
        {
            if(points<=0||faction?.def.pawnGroupMakers==null)return new List<Pawn>();
            var pawns=PawnGroupMakerUtility.GeneratePawns(new PawnGroupMakerParms{groupKind=PawnGroupKindDefOf.Combat,faction=faction,tile=map.Tile,points=points,generateFightersOnly=true,dontUseSingleUseRocketLaunchers=true},false).ToList();
            foreach(var pawn in pawns)GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(center.InBounds(map)?center:map.Center,map,10),map);
            return pawns;
        }
        void CreateHospital(Map map,IntVec3 center,int beds)
        {
            // New temporary map only. Functional basic shelter; modded medical beds can substitute.
            var area=CellRect.CenteredOn(center+new IntVec3(0,0,10),8,6).ClipInsideMap(map);
            foreach(var c in area)
            {
                foreach(var t in c.GetThingList(map).Where(t=>t.def.category==ThingCategory.Building||t.def.category==ThingCategory.Plant).ToList())t.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);map.roofGrid.SetRoof(c,RoofDefOf.RoofConstructed);
                if(c.x==area.minX||c.x==area.maxX||c.z==area.minZ||c.z==area.maxZ)
                {
                    var wall=ThingMaker.MakeThing(c==new IntVec3(area.CenterCell.x,0,area.minZ)?ThingDefOf.Door:ThingDefOf.Wall,ThingDefOf.WoodLog);wall.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(wall,c,map);ownedStructures.Add(wall);
                }
            }
            for(int i=0;i<beds;i++)
            {
                var bed=(Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed,ThingDefOf.WoodLog);bed.SetFaction(Faction.OfPlayer);bed.Medical=true;GenSpawn.Spawn(bed,new IntVec3(area.minX+2+(i%6)*2,0,area.minZ+2+(i/6)*4),map);ownedStructures.Add(bed);
            }
        }
    }
    public sealed class CompProperties_UNHandover:CompProperties{public CompProperties_UNHandover(){compClass=typeof(CompUNHandover);}}
    public sealed class CompUNHandover:ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if(!(parent.Map?.Parent is UNMissionSite site))yield break;
            foreach(var o in UNMissionSites.Active.Where(o=>o.destination==site&&(o.kind==0||o.kind==2||o.kind==3)).ToList())
            {var current=o;yield return new Command_Action{defaultLabel=(o.kind==0?"UNMS_Deliver":"UNMS_TakeCustody").Translate(),defaultDesc=current.OfferDescription,icon=TexButton.Info,action=()=>current.Review(parent.Map)};}
        }
    }
}
