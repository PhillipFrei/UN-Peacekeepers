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
        private HashSet<string> initialItems=new HashSet<string>();
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref missionQuestId,"unMissionQuestId",-1);Scribe_Collections.Look(ref initialItems,"unInitialSiteItems",LookMode.Value);if(Scribe.mode==LoadSaveMode.PostLoadInit&&initialItems==null)initialItems=new HashSet<string>();}
        public UNMissionObjective Objective=>Find.QuestManager.QuestsListForReading.FirstOrDefault(q=>q.id==missionQuestId)?.PartsListForReading.OfType<UNMissionObjective>().FirstOrDefault();
        public override void PostMapGenerate(){base.PostMapGenerate();initialItems=new HashSet<string>(Map.listerThings.AllThings.Where(t=>t.def.category==ThingCategory.Item).Select(t=>t.ThingID));Objective?.Generate(Map);}
        public bool CanSafelyClose
        {
            get
            {
                if(UNMissionSites.Pending.Any(o=>o.destination==this))return false;
                if(!HasMap)return true;
                var owned=Objective?.ownedStructures??new List<Thing>();
                return !Map.mapPawns.AllPawnsSpawned.Any(p=>p.Faction==Faction.OfPlayer||p.IsPrisonerOfColony||p.IsSlaveOfColony||Objective?.targets.Contains(p)==true) &&
                    !Map.listerThings.AllThings.Any(t=>(t.def.category==ThingCategory.Item&&!initialItems.Contains(t.ThingID))||(t.Faction==Faction.OfPlayer&&!owned.Contains(t)));
            }
        }
        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            // Quest targets, player gear and player pawns must not be silently discarded on departure.
            alsoRemoveWorldObject=false;
            return false;
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach(var g in base.GetGizmos())yield return g;
            foreach(var o in UNMissionSites.Active.Where(o=>o.destination==this).ToList())
            {
                var current=o;
                if(HasMap)yield return new Command_Action{defaultLabel="UNMS_Review".Translate(),defaultDesc=current.OfferDescription,icon=TexButton.Info,action=()=>current.Review(Map)};
            }
            if(!UNMissionSites.Pending.Any(o=>o.destination==this))
            {
                var close=new Command_Action{defaultLabel="UNMS_RemoveSite".Translate(),defaultDesc="UNMS_UnsafeCleanup".Translate(),icon=TexButton.Info,action=()=>{if(CanSafelyClose){if(HasMap)Current.Game.DeinitAndRemoveMap(Map,false);Destroy();}}};
                if(!CanSafelyClose)close.Disable("UNMS_UnsafeCleanup".Translate());
                yield return close;
            }
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
        public bool supplement;
        private bool achieved;
        public bool CanRewardSuccess=>achieved;
        private QuestPart_ExtraFaction custody;
        private bool started,generated,finished,shuttleSent,shuttleSatisfied;
        private int began=-1,nextPoll;
        private TransportShip shuttle;
        public int DurationTicks=>durationTicks;
        public string OfferDescription=>"UNMS_Kind".Translate()+": "+("UNMS_Kind"+kind).Translate()+"\n"+"UNMS_Destination".Translate(destination?.Label??"UNMS_Camp".Translate().ToString(),tile.ToString())+"\n"+"UNMS_Faction".Translate(supportedFaction?.Name??"—")+"\n"+
            (kind==0?string.Join("\n",goods.Select((g,i)=>g.LabelCap+": "+delivered[i]+"/"+required[i])):"UNMS_People".Translate(patientCount).ToString())+"\n"+("UNMS_Goal"+kind).Translate()+"\n"+"UNMS_Time".Translate(durationTicks/60000)+
            (enemyFaction!=null?"\n"+"UNMS_Enemy".Translate(enemyFaction.Name,enemyBudget.ToString("0")):"\n"+"UNMS_Peaceful".Translate());
        public override string DescriptionPart=>started?("UNMS_Remaining".Translate(Math.Max(0,durationTicks-quest.TicksSinceAccepted)/60000f)+"\n"+
            (kind==0?string.Join("\n",goods.Select((g,i)=>g.LabelCap+": "+delivered[i]+"/"+required[i])):"UNMS_NameTargets".Translate(string.Join(", ",targets.Where(p=>p!=null).Select(p=>p.LabelShortCap.ToString()))).ToString())):null;
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
            Scribe_Values.Look(ref started,"unStarted");Scribe_Values.Look(ref generated,"unGenerated");Scribe_Values.Look(ref finished,"unFinished");Scribe_Values.Look(ref shuttleSent,"unShuttleSent");Scribe_Values.Look(ref shuttleSatisfied,"unShuttleSatisfied");Scribe_Values.Look(ref began,"unBegan",-1);Scribe_References.Look(ref shuttle,"unShuttle");
        }
        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if(!started||finished||quest.State!=QuestState.Ongoing)return;
            if(Find.TickManager.TicksGame<nextPoll)return;nextPoll=Find.TickManager.TicksGame+250;
            if(quest.TicksSinceAccepted>=durationTicks||targets.Any(p=>p==null||p.Dead)||destination==null||!destination.Spawned){Finish(false);return;}
            if(custody==null)custody=quest.PartsListForReading.OfType<QuestPart_ExtraFaction>().FirstOrDefault();
            if(custody!=null)custody.affectedPawns=targets;
            if(generated&&(kind==4||kind==5))
            {
                if(protectedThings.Any(t=>t==null||t.Destroyed)){Finish(false);return;}
                if(Find.TickManager.TicksGame>=began+defenceTicks&&targets.All(p=>p.Spawned&&p.Map==destination.Map)&&enemies.All(p=>p!=null&&(p.Dead||p.Downed))){Finish(true);return;}
            }
            if(generated&&(kind==2||kind==3||kind==6)&&shuttle==null&&homeMap!=null&&Find.Maps.Contains(homeMap)&&targets.All(p=>p.Spawned&&p.Map==homeMap&&p.health.hediffSet.BleedRateTotal<=0f)&&(kind!=6||targets.All(p=>p.IsPrisonerOfColony)))
                UNMissionSites.TryPickup(homeMap,targets,SignalTag,out shuttle);
            if(shuttle!=null&&shuttle.shipThing.Destroyed&&!shuttleSent)Finish(false);
        }
        string SignalTag=>"Quest"+quest.id+".UNPickup";
        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if(!finished&&signal.tag==SignalTag+".SentSatisfied"&&shuttle!=null&&targets.All(p=>p!=null&&!p.Dead&&shuttle.TransporterComp.innerContainer.Contains(p)))
            {shuttleSatisfied=true;}
        }
        public void NotifyDeparted(TransportShip departed)
        {
            if(!finished&&quest.State==QuestState.Ongoing&&departed==shuttle&&shuttleSatisfied&&targets.Count==patientCount&&targets.All(p=>p!=null&&!p.Dead))
            {shuttleSent=true;Finish(true);}
        }
        bool CanAct()
        {
            if(finished||quest.State!=QuestState.Ongoing)return false;
            if(quest.TicksSinceAccepted>=durationTicks||targets.Any(p=>p==null||p.Dead)){Finish(false);return false;}
            return true;
        }
        void Finish(bool success)
        {
            if(finished||quest.State!=QuestState.Ongoing)return;
            achieved=success;finished=true;UNMissionManager.Instance?.Complete(quest.id,success);
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
            else if(kind==1)
            {
                if(targets.All(p=>p!=null&&!p.Dead&&p.Spawned&&p.Map==map&&!p.health.hediffSet.hediffs.Any(h=>h is Hediff_Injury&&!h.IsPermanent())) && HospitalReady(map))Finish(true);
                else Messages.Message("UNMS_TreatmentIncomplete".Translate(),MessageTypeDefOf.RejectInput,false);
            }
            else if(kind==2||kind==3)
            {
                // Consent/release applies to these exact mission people, making them controllable for travel.
                foreach(var p in targets.Where(p=>p!=null&&!p.Dead&&p.Map==map&&p.Position.DistanceTo(marker.Position)<=8f)){p.GetLord()?.Notify_PawnLost(p,PawnLostCondition.LeftVoluntarily);p.guest?.SetGuestStatus(null);p.SetFaction(Faction.OfPlayer);}
                Messages.Message("UNMS_ReturnHome".Translate(),MessageTypeDefOf.NeutralEvent,false);
            }
            else Messages.Message(DescriptionPart,MessageTypeDefOf.NeutralEvent,false);
        }
        bool HospitalReady(Map map)
        {
            int needed=tier==0?Math.Min(3,patientCount):6;
            return map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>().Count(b=>b.Medical&&b.Position.Roofed(map)&&b.Position.GetTemperature(map)>=16f&&b.Position.GetTemperature(map)<=30f)>=needed;
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
                        if(kind==1){var injury=HediffMaker.MakeHediff(HediffDefOf.Cut,p,p.RaceProps.body.corePart);injury.Severity=tier==0?5f:10f;p.health.AddHediff(injury);}
                    }
                    if(kind==1)
                    {
                        foreach(var p in targets){p.SetFaction(Faction.OfPlayer);}
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
            foreach(var o in UNMissionSites.Active.Where(o=>o.destination==site).ToList())
            {var current=o;yield return new Command_Action{defaultLabel="UNMS_Review".Translate(),defaultDesc=current.OfferDescription,icon=TexButton.Info,action=()=>current.Review(parent.Map)};}
        }
    }
}
