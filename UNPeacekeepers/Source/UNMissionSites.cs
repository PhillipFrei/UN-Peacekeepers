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
            if(main==null || main.destination==null || !main.destination.Spawned || (main.kind!=1&&main.kind!=4))return false;
            var o=new UNMissionObjective{kind=0,tier=tier,homeMap=main.homeMap,supportedFaction=main.supportedFaction,destination=main.destination,tile=main.tile,difficulty=main.difficulty,seed=Rand.Int,supplement=true,durationTicks=(tier==0?20:30)*60000};
            o.goods.Add(DefDatabase<ThingDef>.GetNamed("MedicineIndustrial"));o.required.Add(Math.Max(1,Mathf.RoundToInt((tier==0?30:90)*main.difficulty)));o.delivered.Add(0);
            extra=o;return true;
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
        // Retain the old Scribe key. Old maps lack reliable original stack counts,
        // so their remaining items are conservatively treated as unverified cargo.
        private HashSet<string> initialItems=new HashSet<string>();
        private Dictionary<string,int> initialItemCounts=new Dictionary<string,int>();
        private HashSet<string> missionStructures=new HashSet<string>(),missionPeople=new HashSet<string>();
        private bool recordedMission;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref missionQuestId,"unMissionQuestId",-1);
            Scribe_Collections.Look(ref initialItems,"unInitialSiteItems",LookMode.Value);
            Scribe_Collections.Look(ref initialItemCounts,"unInitialSiteItemCounts",LookMode.Value,LookMode.Value);
            Scribe_Collections.Look(ref missionStructures,"unSiteStructures",LookMode.Value);
            Scribe_Collections.Look(ref missionPeople,"unSitePeople",LookMode.Value);
            Scribe_Values.Look(ref recordedMission,"unSiteRecordedMission",false);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            {
                if(initialItems==null)initialItems=new HashSet<string>();
                if(initialItemCounts==null)initialItemCounts=new Dictionary<string,int>();
                if(missionStructures==null)missionStructures=new HashSet<string>();
                if(missionPeople==null)missionPeople=new HashSet<string>();
            }
        }
        public UNMissionObjective Objective=>Find.QuestManager.QuestsListForReading.FirstOrDefault(q=>q.id==missionQuestId)?.PartsListForReading.OfType<UNMissionObjective>().FirstOrDefault();
        IEnumerable<Thing> SiteThings()
        {
            var held=new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(Map,held,true);
            // Pawn equipment travels with its owner; loose cargo and container contents do not.
            return Map.listerThings.AllThings.Concat(held.Where(t=>ThingOwnerUtility.GetAnyParent<Pawn>(t)==null)).Distinct();
        }
        public override void PostMapGenerate()
        {
            base.PostMapGenerate();
            initialItemCounts=SiteThings().Where(t=>t.def.category==ThingCategory.Item).ToDictionary(t=>t.ThingID,t=>t.stackCount);
            initialItems=new HashSet<string>(initialItemCounts.Keys);
            var objective=Objective;
            if(objective!=null)
            {
                try{objective.Generate(Map);}
                finally{RememberObjective(objective);}
            }
        }
        void RememberObjective(UNMissionObjective objective)
        {
            if(objective==null)return;
            recordedMission=true;
            foreach(var t in objective.ownedStructures.Where(t=>t!=null))missionStructures.Add(t.ThingID);
            foreach(var p in objective.targets.Where(p=>p!=null))missionPeople.Add(p.ThingID);
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
                RememberObjective(Objective);
                // Keep RimWorld's own checks for incoming transport, pocket maps and blocking buildings.
                if(!base.ShouldRemoveMapNow(out bool vanillaRemove)||!vanillaRemove)return false;
                var pendingPeople=new HashSet<Pawn>(UNMissionSites.Pending.SelectMany(o=>o.targets).Where(p=>p!=null));
                bool people=Map.mapPawns.AllPawns.Any(p=>p.Faction==Faction.OfPlayer||p.IsPrisonerOfColony||p.IsSlaveOfColony||
                    missionPeople.Contains(p.ThingID)||pendingPeople.Contains(p)||!recordedMission);
                bool assets=SiteThings().Any(t=>
                {
                    if(t is Pawn)return false;
                    if(t.Faction==Faction.OfPlayer&&!missionStructures.Contains(t.ThingID))return true;
                    if(t is Corpse corpse&&(corpse.InnerPawn.Faction==Faction.OfPlayer||missionPeople.Contains(corpse.InnerPawn.ThingID)))return true;
                    if(t.def.category!=ThingCategory.Item)return false;
                    int original;
                    return UNMissionPolicy.IsUnverifiedSiteItem(initialItemCounts.TryGetValue(t.ThingID,out original)?original:-1,t.stackCount);
                });
                return UNMissionPolicy.CanCloseSite(false,false,people,assets);
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
            if(HasMap)
            {
                if(Map.IsPlayerHome)return;
                // Defence civilians normally remain in their defend lord. After success, let
                // those exact civilians leave by normal AI rather than silently deleting them.
                var q=Find.QuestManager.QuestsListForReading.FirstOrDefault(x=>x.id==missionQuestId);
                var o=Objective;
                RememberObjective(o);
                if(q?.State==QuestState.EndedSuccess&&o!=null&&(o.kind==1||o.kind==4||o.kind==5))
                {
                    foreach(var pawn in o.targets.Where(p=>p!=null&&!p.Dead&&!p.Downed&&p.Spawned&&p.Map==Map&&
                        p.Faction==o.supportedFaction&&p.Faction!=Faction.OfPlayer&&!p.IsPrisonerOfColony&&!p.IsSlaveOfColony&&
                        !p.Faction.HostileTo(Faction.OfPlayer)).ToList())
                    {
                        var lord=pawn.GetLord();
                        if(lord?.LordJob is LordJob_ExitMapBest)continue;
                        lord?.Notify_PawnLost(pawn,PawnLostCondition.LeftVoluntarily);
                        LordMaker.MakeNewLord(pawn.Faction,new LordJob_ExitMapBest(),Map,new[]{pawn});
                    }
                }
                CheckRemoveMapNow();
            }
            else if(CanSafelyClose)Destroy();
        }
        public override string GetInspectString()
        {
            var result=base.GetInspectString();
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
                int id=supplement?parentId:owningQuest.id;
                var matching=Find.WorldObjects.AllWorldObjects.OfType<UNMissionSite>().Where(site=>
                    site.Spawned&&site.missionQuestId==id&&site.Tile==tile&&site.Faction==supportedFaction).ToList();
                if(id>=0&&matching.Count==1)destination=matching[0];
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
            return true;
        }
        public void RequestCompletion(bool success)=>Finish(success);
        private QuestPart_ExtraFaction custody;
        private bool started,generated,finished,shuttleSent,shuttleSatisfied,savedCollectionsMissing;
        private int began=-1,nextPoll;
        private TransportShip shuttle;
        // Stable ThingIDs survive individual exits and save/load, even if a world pawn is discarded.
        private Dictionary<string,int> guestSince=new Dictionary<string,int>();
        private HashSet<string> departedGuests=new HashSet<string>();
        private List<Pawn> guestPickup=new List<Pawn>();
        private bool guestPickupActive;
        bool HasGuestPhase=>UNMissionPolicy.UsesGuestStay(kind);
        public bool IsMissionGuest(Pawn pawn)=>HasGuestPhase&&pawn!=null&&guestSince.ContainsKey(pawn.ThingID)&&!departedGuests.Contains(pawn.ThingID);
        bool GuestDeparted(Pawn pawn)=>pawn!=null&&departedGuests.Contains(pawn.ThingID);
        bool OnHomeHeld(Pawn pawn)=>pawn!=null&&homeMap!=null&&pawn.MapHeld==homeMap&&Find.Maps.Contains(homeMap);
        bool OnHome(Pawn pawn)=>OnHomeHeld(pawn)&&pawn.Spawned;
        bool GuestHealthy(Pawn pawn)=>pawn!=null&&!pawn.Dead&&pawn.health!=null&&pawn.health.hediffSet.BleedRateTotal<=0f&&
            !pawn.health.hediffSet.HasHediff(HediffDefOf.Anesthetic)&&(kind!=1||MedicallyRecovered(pawn));
        bool GuestFactionValid=>supportedFaction!=null&&!supportedFaction.defeated&&!supportedFaction.deactivated&&!supportedFaction.HostileTo(Faction.OfPlayer);
        bool IsNativeGuest(Pawn pawn)=>pawn!=null&&pawn.Faction==supportedFaction&&pawn.HostFaction==Faction.OfPlayer&&
            pawn.guest!=null&&pawn.guest.GuestStatus==GuestStatus.Guest;
        public int DurationTicks=>durationTicks;
        public string OfferDescription=>"UNMS_Kind".Translate()+": "+("UNMS_Kind"+kind).Translate()+"\n"+"UNMS_Destination".Translate(destination?.Label??"UNMS_Camp".Translate().ToString(),tile.ToString())+"\n"+"UNMS_Faction".Translate(supportedFaction?.Name??"—")+"\n"+
            (kind==0?string.Join("\n",goods.Select((g,i)=>(g?.LabelCap.ToString()??"—")+": "+(i<delivered.Count?delivered[i]:0)+"/"+(i<required.Count?required[i]:0))):"UNMS_People".Translate(patientCount).ToString())+"\n"+("UNMS_Goal"+kind).Translate()+(kind==1&&bionicCase?"\n"+"UNMS_BionicGoal".Translate().ToString():"")+"\n"+"UNMS_Time".Translate(durationTicks/60000)+
            (enemyFaction!=null?"\n"+"UNMS_Enemy".Translate(enemyFaction.Name,enemyBudget.ToString("0")):"\n"+"UNMS_Peaceful".Translate());
        public override string DescriptionPart=>started?("UNMS_Remaining".Translate(Math.Max(0,durationTicks-quest.TicksSinceAccepted)/60000f)+"\n"+
            (kind==0?string.Join("\n",goods.Select((g,i)=>g.LabelCap+": "+delivered[i]+"/"+required[i])):"UNMS_NameTargets".Translate(string.Join(", ",targets.Where(p=>p!=null).Select(p=>p.LabelShortCap.ToString()))).ToString()+
            (HasGuestPhase&&generated?"\n"+GuestProgress():""))):null;
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
            string reason;if(!ValidateAcceptance(out reason)){Finish(false);return;}
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
            Scribe_Collections.Look(ref guestPickup,"unGuestPickup",LookMode.Reference);
            Scribe_Values.Look(ref guestPickupActive,"unGuestPickupActive",false);
            Scribe_Values.Look(ref savedCollectionsMissing,"unSavedCollectionsMissing",false);
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
            if(quest.TicksSinceAccepted>=durationTicks||MissingOrDeadTarget()||destination==null||!destination.Spawned){Finish(false);return;}
            if(custody==null)custody=quest.PartsListForReading.OfType<QuestPart_ExtraFaction>().FirstOrDefault();
            if(custody!=null)custody.affectedPawns=targets;
            if(generated&&(kind==4||kind==5))
            {
                if(targets.Count!=patientCount||patientCount<=0||protectedThings.Count==0){Finish(false);return;}
                if(protectedThings.Any(t=>t==null||t.Destroyed)){Finish(false);return;}
                if(Find.TickManager.TicksGame>=began+defenceTicks&&targets.All(p=>p.Spawned&&p.Map==destination.Map)&&enemies.All(p=>p!=null&&(p.Dead||p.Downed))){Finish(true);return;}
            }
            if(generated&&HasGuestPhase)
            {
                // Preserve an already dispatched pre-v0.3.3 shuttle. New civilian journeys
                // use the guest lifecycle; only military arrests retain direct pickup.
                if(shuttle==null||guestPickupActive)ProcessGuests();
            }
            if(generated&&kind==6&&targets.Count==patientCount&&shuttle==null&&homeMap!=null&&Find.Maps.Contains(homeMap)&&
                targets.All(p=>OnHome(p)&&GuestHealthy(p)&&p.IsPrisonerOfColony))
                UNMissionSites.TryPickup(homeMap,targets,SignalTag,out shuttle);
            if(shuttle!=null&&shuttle.shipThing.Destroyed&&!shuttleSent)Finish(false);
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
        bool ReadyToLeave(Pawn pawn,int now)=>IsNativeGuest(pawn)&&GuestHealthy(pawn)&&
            guestSince.TryGetValue(pawn.ThingID,out int since)&&UNMissionPolicy.GuestStayComplete(since,now);
        void ProcessGuests()
        {
            // The original field-hospital path does not require transporting people home.
            if(kind==1&&guestSince.Count==0&&departedGuests.Count==0&&targets.Count==patientCount&&
                targets.All(p=>p!=null&&p.Spawned&&p.Map==destination.Map&&MedicallyRecovered(p)))
            {Finish(true);return;}
            if(homeMap==null||!Find.Maps.Contains(homeMap)||!GuestFactionValid)return;
            int now=Find.TickManager.TicksGame;
            var pickupCandidates=new List<Pawn>();
            foreach(var pawn in targets.Where(p=>p!=null&&!GuestDeparted(p)).ToList())
            {
                string id=pawn.ThingID;
                if(guestPickupActive&&guestPickup.Contains(pawn))continue;
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
                if(pawn.Downed||!RCellFinder.TryFindBestExitSpot(pawn,out IntVec3 exit))
                {
                    StayAtColony(pawn);pickupCandidates.Add(pawn);continue;
                }
                if(!(pawn.GetLord()?.LordJob is LordJob_ExitMapBest))
                {
                    pawn.GetLord()?.Notify_PawnLost(pawn,PawnLostCondition.LeftVoluntarily);
                    LordMaker.MakeNewLord(supportedFaction,new LordJob_ExitMapBest(),homeMap,new[]{pawn});
                }
            }
            if(shuttle==null&&pickupCandidates.Count>0&&UNMissionSites.TryPickup(homeMap,pickupCandidates,SignalTag,out shuttle))
            {
                guestPickup=pickupCandidates;guestPickupActive=true;shuttleSatisfied=false;
            }
            if(guestPickupActive&&shuttle!=null&&shuttle.ShipExistsAndIsSpawned&&shuttle.shipThing.Map==homeMap)
            {
                var enter=DefDatabase<JobDef>.GetNamedSilentFail("EnterTransporterAndDefendSelf");
                if(enter!=null)foreach(var pawn in guestPickup.Where(p=>OnHome(p)&&!p.Downed&&ReadyToLeave(p,now)))
                {
                    if(pawn.CurJob?.def==enter)continue;
                    if(pawn.CanReach(shuttle.shipThing,PathEndMode.Touch,Danger.Some))
                        pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(enter,shuttle.shipThing));
                }
            }
        }
        public bool MayDepartGuest(Pawn pawn)=>!finished&&quest.State==QuestState.Ongoing&&IsMissionGuest(pawn)&&
            OnHome(pawn)&&ReadyToLeave(pawn,Find.TickManager.TicksGame)&&!(guestPickupActive&&guestPickup.Contains(pawn));
        public void ConfirmGuestDeparture(Pawn pawn)
        {
            if(finished||!IsMissionGuest(pawn)||pawn.Dead)return;
            departedGuests.Add(pawn.ThingID);
            if(AllGuestsDeparted())Finish(true);
        }
        string SignalTag=>"Quest"+quest.id+".UNPickup";
        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            var passengers=guestPickupActive?guestPickup:targets;
            if(!finished&&signal.tag==SignalTag+".SentSatisfied"&&shuttle!=null&&passengers.Count>0&&
                passengers.All(p=>p!=null&&!p.Dead&&(!guestPickupActive||ReadyToLeave(p,Find.TickManager.TicksGame))&&shuttle.TransporterComp.innerContainer.Contains(p)))
                shuttleSatisfied=true;
        }
        public void NotifyDeparted(TransportShip departed)
        {
            if(finished||quest.State!=QuestState.Ongoing||departed!=shuttle||!shuttleSatisfied)return;
            if(guestPickupActive)
            {
                if(guestPickup.Count==0||guestPickup.Any(p=>p==null||p.Dead))return;
                foreach(var pawn in guestPickup)departedGuests.Add(pawn.ThingID);
                guestPickup.Clear();guestPickupActive=false;shuttle=null;shuttleSatisfied=false;
                if(AllGuestsDeparted()){shuttleSent=true;Finish(true);}
                return;
            }
            if(targets.Count==patientCount&&targets.All(p=>p!=null&&!p.Dead))
            {shuttleSent=true;Finish(true);}
        }
        bool CanAct()
        {
            if(finished||quest.State!=QuestState.Ongoing)return false;
            if(quest.TicksSinceAccepted>=durationTicks||MissingOrDeadTarget()){Finish(false);return false;}
            return true;
        }
        void Finish(bool success)
        {
            if(finished||quest.State!=QuestState.Ongoing)return;
            // Record the proven outcome. The game component ends the quest outside quest/signal callbacks.
            achieved=success;finished=true;
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
                if(kind==3&&enemies.Any(p=>p!=null&&!p.Dead&&!p.Downed&&p.Spawned&&p.Map==map))
                {Messages.Message("UNMS_EnemiesRemain".Translate(),MessageTypeDefOf.RejectInput,false);return;}
                // Consent/release applies to these exact mission people, making them controllable for travel.
                foreach(var p in targets.Where(p=>p!=null&&!p.Dead&&p.Map==map&&p.Position.DistanceTo(marker.Position)<=8f)){p.GetLord()?.Notify_PawnLost(p,PawnLostCondition.LeftVoluntarily);p.guest?.SetGuestStatus(null);p.SetFaction(Faction.OfPlayer);}
                Messages.Message("UNMS_ReturnHome".Translate(),MessageTypeDefOf.NeutralEvent,false);
            }
            else Messages.Message(DescriptionPart,MessageTypeDefOf.NeutralEvent,false);
        }
        // Only actual medical state, not buildings, temperature or a completion gizmo.
        bool MedicallyRecovered(Pawn p)
        {
            if(p==null||p.Dead||p.health==null||p.health.hediffSet.BleedRateTotal>0f)return false;
            if(p.health.hediffSet.hediffs.Any(h=>h.def==HediffDefOf.Anesthetic ||
                (h is Hediff_Injury&&!h.IsPermanent()) ||
                (h.def.isBad&&!h.IsPermanent()&&h.TryGetComp<HediffComp_Immunizable>()!=null)))return false;
            if(bionicCase && requiredLimbIndex>=0)
            {
                var part=p.RaceProps.body.AllParts.FirstOrDefault(x=>x.Index==requiredLimbIndex);
                if(part==null||p.health.hediffSet.PartIsMissing(part)||
                    !p.health.hediffSet.hediffs.Any(h=>h.Part==part&&h is Hediff_AddedPart&&h.def.defName==requiredBionic))return false;
            }
            return true;
        }
        string GuestProgress()
        {
            int now=Find.TickManager.TicksGame;
            var remaining=targets.Where(p=>p!=null&&!GuestDeparted(p)).ToList();
            var lines=new List<string>();
            // Keep the original medical count for field-hospital treatment and mixed groups.
            if(kind==1)lines.Add("UNMS_Recovered".Translate(departedGuests.Count+remaining.Count(MedicallyRecovered),patientCount).ToString());
            if(kind!=1||guestSince.Count>0||departedGuests.Count>0||remaining.Any(OnHomeHeld))
                lines.Add("UNMS_GuestProgress".Translate(departedGuests.Count+remaining.Count(OnHomeHeld),
                    departedGuests.Count+remaining.Count(p=>OnHomeHeld(p)&&GuestHealthy(p)),
                    departedGuests.Count+remaining.Count(p=>OnHomeHeld(p)&&ReadyToLeave(p,now)),departedGuests.Count,patientCount).ToString());
            foreach(var pawn in targets.Where(p=>p!=null))
            {
                string state;
                if(GuestDeparted(pawn))state="UNMS_GuestDeparted".Translate();
                else if(!GuestHealthy(pawn))state=(pawn.health?.hediffSet.HasHediff(HediffDefOf.Anesthetic)==true?"UNMS_Anesthetized":"UNMS_Unwell").Translate();
                else if(!OnHomeHeld(pawn))state=kind==1?"UNMS_Well".Translate().ToString():"UNMS_GuestNeedsHome".Translate().ToString();
                else if(guestSince.TryGetValue(pawn.ThingID,out int since)&&since>=0)
                    state=UNMissionPolicy.GuestStayComplete(since,now)?"UNMS_GuestLeaving".Translate().ToString():
                        "UNMS_GuestHours".Translate(UNMissionPolicy.GuestTicksRemaining(since,now)/2500f).ToString();
                else state="UNMS_Well".Translate();
                lines.Add(pawn.LabelShortCap+": "+state);
            }
            if(bionicCase && requiredLimbIndex>=0)
            {
                var limb=targets.FirstOrDefault()?.RaceProps.body.AllParts.FirstOrDefault(x=>x.Index==requiredLimbIndex);
                if(limb!=null)lines.Add("UNMS_BionicRequired".Translate(limb.Label,DefDatabase<ThingDef>.GetNamedSilentFail(requiredBionic)?.LabelCap.ToString()??requiredBionic));
            }
            if(shuttle!=null)lines.Add("UNMS_ShuttleWaiting".Translate());
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
            requiredLimbIndex=part.Index;requiredBionic=name;
            p.health.AddHediff(HediffDefOf.MissingBodyPart,part);
            GenPlace.TryPlaceThing(ThingMaker.MakeThing(item),center,map,ThingPlaceMode.Near);
            var medicine=ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);medicine.stackCount=6;
            GenPlace.TryPlaceThing(medicine,center,map,ThingPlaceMode.Near);
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
                        {if(!GenerateBionicCase(p,map,center)){bionicCase=false;GenerateInjuries(p);}}
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
                    if(leader.Dead||leader.Spawned||leader.ParentHolder!=null){Finish(false);return;}
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
