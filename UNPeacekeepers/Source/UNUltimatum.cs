using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace UNPeacekeepers
{
    public sealed class UNUltimatum:GameComponent
    {
        Pawn selectedLeader,captureTarget;
        bool capturePending;
        int nextCheck,nextCaptureAttempt;
        public UNUltimatum(Game game){}
        public static UNUltimatum Instance=>Current.Game?.GetComponent<UNUltimatum>();
        public Pawn SelectedLeader=>selectedLeader;
        public QuestPart_UNUltimatum Active=>Find.QuestManager.QuestsListForReading.Where(q=>q.State==QuestState.Ongoing).SelectMany(q=>q.PartsListForReading).OfType<QuestPart_UNUltimatum>().FirstOrDefault();
        public bool CaptureOperationActive=>capturePending||Find.Maps.Any(m=>m.lordManager.lords.Any(l=>l.LordJob is LordJob_UNCapture && l.ownedPawns.Count>0));
        public bool HasActive=>Active!=null||CaptureOperationActive;
        public string StatusText
        {
            get
            {
                var part=Active;
                if(part!=null)return part.DescriptionPart;
                if(capturePending)return "UNCrime_ArrestPending".Translate(captureTarget?.LabelShortCap.ToString()??"UNCrime_NoLeader".Translate().ToString());
                return "UNCrime_LeaderStatus".Translate(selectedLeader?.LabelShortCap.ToString()??"UNCrime_NoLeader".Translate().ToString(),UNCrimeMonitor.Instance?.UnconsumedPoints??0);
            }
        }
        public void SelectLeaderMenu()
        {
            var part=Active;
            if(part?.target!=null || captureTarget!=null && capturePending){Messages.Message("UNCrime_LeaderLocked".Translate(),MessageTypeDefOf.RejectInput,false);return;}
            var candidates=Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).Where(p=>!p.Dead&&!p.IsSlave).Distinct().ToList();
            var options=candidates.Select(p=>new FloatMenuOption(p.LabelShortCap,()=>{
                selectedLeader=p;
                if(part!=null)part.target=p;
                if(capturePending)captureTarget=p;
            })).ToList();
            if(options.Count==0)options.Add(new FloatMenuOption("UNCrime_NoCandidate".Translate(),null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        public override void ExposeData()
        {
            Scribe_References.Look(ref selectedLeader,"unSelectedLeader");Scribe_References.Look(ref captureTarget,"unCaptureTarget");
            Scribe_Values.Look(ref capturePending,"unCapturePending",false);Scribe_Values.Look(ref nextCaptureAttempt,"unCaptureNextAttempt",0);
        }
        public override void GameComponentTick()
        {
            int now=Find.TickManager.TicksGame;if(now<nextCheck)return;nextCheck=now+250;
            var un=Authorization.GetUN();if(un==null||un.defeated||un.deactivated)return;
            if(capturePending)
            {
                if(captureTarget?.Dead==true){capturePending=false;captureTarget=null;return;}
                if(captureTarget!=null && captureTarget.Spawned && captureTarget.Map.IsPlayerHome && now>=nextCaptureAttempt)
                {
                    nextCaptureAttempt=now+60000;
                    if(DeployCapture(un,captureTarget))capturePending=false;
                }
                return;
            }
            if(HasActive||UNCrimeMonitor.Instance==null||UNCrimeMonitor.Instance.UnconsumedPoints<100)return;
            var quest=Quest.MakeRaw();quest.root=DefDatabase<QuestScriptDef>.GetNamed("UN_AccountabilityUltimatum");
            quest.name="UNCrime_UltimatumTitle".Translate();quest.description="UNCrime_UltimatumTerms".Translate();
            var part=new QuestPart_UNUltimatum{inSignalEnable=quest.InitiateSignal,deadline=now+180000,target=selectedLeader?.Dead==false&&selectedLeader.Faction==Faction.OfPlayer?selectedLeader:null};
            quest.AddPart(part);Find.QuestManager.Add(quest);quest.Accept(null);
            UNCrimeMonitor.Instance.ConsumePoints();
            UNContact.ApplyPrisonGoodwill(un,0,"UNCrime_UltimatumTitle".Translate());
            Find.LetterStack.ReceiveLetter(quest.name,quest.description,LetterDefOf.ThreatBig);
            if(part.target==null)SelectLeaderMenu();
        }
        internal void Fail(QuestPart_UNUltimatum part,UNMissionFailure reason)
        {
            if(part.resolved)return;part.resolved=true;part.failureReason=(int)reason;
            UNCrimeMonitor.RecordCrime(20,"UNCrime_UltimatumRefused".Translate(),"ultimatum:"+part.quest.id);
            captureTarget=part.target;capturePending=true;nextCaptureAttempt=Find.TickManager.TicksGame;
            part.quest.End(QuestEndOutcome.Fail);
        }
        internal void Surrender(QuestPart_UNUltimatum part)
        {
            if(part.resolved||part.target==null||part.target.Dead)return;
            part.resolved=true;
            UNContact.ApplyPrisonGoodwill(Authorization.GetUN(),20,"UNCrime_UltimatumDelivered".Translate(part.target.LabelShortCap));
            part.quest.End(QuestEndOutcome.Success);
            if(selectedLeader==part.target)selectedLeader=null;
        }
        bool DeployCapture(Faction un,Pawn target)
        {
            Map map=target.Map;
            if(!CellFinder.TryFindRandomEdgeCellWith(c=>c.Standable(map)&&map.reachability.CanReach(c,target,PathEndMode.Touch,TraverseParms.For(TraverseMode.PassDoors)),map,CellFinder.EdgeRoadChance_Neutral,out IntVec3 entry))return false;
            var parms=new PawnGroupMakerParms{groupKind=PawnGroupKindDefOf.Combat,faction=un,tile=map.Tile,points=Math.Max(300f,StorytellerUtility.DefaultThreatPointsNow(map)*UNMod.Strength)};
            var pawns=PawnGroupMakerUtility.GeneratePawns(parms).Where(p=>p.RaceProps.Humanlike).ToList();
            if(pawns.Count==0)return false;
            foreach(var p in pawns)
            {
                // Capture detail carries clubs, not ranged demolition weapons. Equipment defs are untouched.
                p.equipment?.DestroyAllEquipment();
                if(p.equipment!=null)p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Club"),ThingDefOf.WoodLog));
                GenSpawn.Spawn(p,CellFinder.RandomClosewalkCellNear(entry,map,8),map);
            }
            LordMaker.MakeNewLord(un,new LordJob_UNCapture(target),map,pawns);
            UNContact.ApplyPrisonGoodwill(un,0,"UNCrime_ArrestDeployed".Translate(target.LabelShortCap));
            Find.LetterStack.ReceiveLetter("UNCrime_ArrestTitle".Translate(),"UNCrime_ArrestDeployed".Translate(target.LabelShortCap),LetterDefOf.ThreatBig,target);
            return true;
        }
    }
    public sealed class QuestPart_UNUltimatum:QuestPartActivable
    {
        public Pawn target;public int deadline,failureReason;public bool resolved;
        TransportShip shuttle;int nextShuttle;bool satisfied;
        bool launchBoarded,launchBeforeDeadline;
        string PickupStatus()
        {
            if(target==null)return "UNQ_NextLeader".Translate();
            if(target.Dead)return "UNMIL_TargetMissing".Translate();
            if(shuttle!=null)
            {
                if(shuttle.TransporterComp?.innerContainer.Contains(target)==true)return "UNMIL_UltimatumBoarded".Translate();
                return (shuttle.ShipExistsAndIsSpawned?"UNMIL_UltimatumLanded":"UNMIL_UltimatumIncoming").Translate();
            }
            if(target.MapHeld?.IsPlayerHome!=true)return "UNQ_NextUltimatumHome".Translate();
            if(!target.Spawned)return "UNMIL_EliteReleaseTransport".Translate(target.LabelShortCap);
            int now=Find.TickManager.TicksGame;
            return now<nextShuttle?"UNMIL_PickupRetry".Translate(UNQuestDisplay.Time(UNMissionPolicy.TicksUntil(nextShuttle,now))).ToString():
                "UNMIL_UltimatumRequest".Translate().ToString();
        }
        string Tag=>"UNUltimatum"+quest.id;
        public override string DescriptionPart
        {
            get
            {
                if(quest==null)return null;
                string header=UNQuestDisplay.Header(quest,UNMissionPolicy.TicksUntil(deadline,Find.TickManager.TicksGame));
                if(quest.State==QuestState.EndedFailed)return header+"\n"+UNQuestDisplay.Failed((UNMissionFailure)failureReason);
                if(quest.State!=QuestState.Ongoing)return header;
                return header+"\n"+"UNQ_UltimatumTarget".Translate(target?.LabelShortCap.ToString()??"UNCrime_NoLeader".Translate().ToString())+
                    "\n"+UNQuestDisplay.Next(PickupStatus());
            }
        }
        public override void ExposeData(){base.ExposeData();Scribe_References.Look(ref target,"target");Scribe_Values.Look(ref deadline,"deadline");Scribe_Values.Look(ref resolved,"resolved");Scribe_Values.Look(ref failureReason,"unFailureReason",0);Scribe_References.Look(ref shuttle,"shuttle");Scribe_Values.Look(ref nextShuttle,"nextShuttle");Scribe_Values.Look(ref satisfied,"satisfied",false);}
        public override void QuestPartTick()
        {
            base.QuestPartTick();if(resolved)return;int now=Find.TickManager.TicksGame;
            if(now>=deadline||target?.Dead==true){UNUltimatum.Instance?.Fail(this,now>=deadline?UNMissionFailure.Deadline:UNMissionFailure.TargetLost);return;}
            if(shuttle!=null&&shuttle.shipThing.Destroyed)
            {
                // Lost pickup without the leader: request a replacement if the leader is safe.
                shuttle=null;satisfied=false;nextShuttle=now+UNMissionPolicy.GuestPickupRetryTicks;
            }
            if(shuttle==null&&target?.Spawned==true&&target.Map.IsPlayerHome&&now>=nextShuttle)
            {
                nextShuttle=now+UNMissionPolicy.GuestPickupRetryTicks;satisfied=false;
                if(UNMissionSites.TryPickup(target.Map,new List<Pawn>{target},Tag,out shuttle))
                    Find.LetterStack.ReceiveLetter("UNMIL_PickupTitle".Translate(),"UNMIL_PickupBody".Translate(quest.name),LetterDefOf.NeutralEvent,target);
            }
        }
        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if(signal.tag==Tag+".SentSatisfied"&&!resolved&&Find.TickManager.TicksGame<deadline&&target!=null&&!target.Dead&&shuttle?.TransporterComp?.innerContainer.Contains(target)==true)satisfied=true;
        }
        public void PrepareLaunch(TransportShip departing)
        {
            if(resolved||departing!=shuttle)return;
            launchBeforeDeadline=Find.TickManager.TicksGame<deadline;
            launchBoarded=target!=null&&!target.Dead&&shuttle?.TransporterComp?.innerContainer.Contains(target)==true;
        }
        public void NotifyDeparted(TransportShip departed)
        {
            if(resolved||departed!=shuttle)return;
            if(UNMissionPolicy.DirectPickupComplete(launchBeforeDeadline&&Find.TickManager.TicksGame<deadline,
                target!=null&&!target.Dead,launchBoarded,true,satisfied))
                UNUltimatum.Instance?.Surrender(this);
            else if(Find.TickManager.TicksGame>=deadline)UNUltimatum.Instance?.Fail(this,UNMissionFailure.Deadline);
            else if(launchBoarded)UNUltimatum.Instance?.Fail(this,UNMissionFailure.PickupUnverified);
            else {shuttle=null;satisfied=false;}
        }
        public override void Cleanup()
        {
            // Never destroy a shuttle that may hold a colonist; permit safe manual unloading on failure.
            if(shuttle?.ShuttleComp!=null)shuttle.ShuttleComp.requiredPawns.Clear();
            base.Cleanup();
        }
    }
    public sealed class QuestNode_UNUltimatum:QuestNode
    {
        protected override bool TestRunInt(Slate slate)=>false;
        protected override void RunInt(){}
    }
    public sealed class LordJob_UNCapture:LordJob
    {
        public Pawn target;public int started;
        public LordJob_UNCapture(){}
        public LordJob_UNCapture(Pawn target){this.target=target;started=Find.TickManager.TicksGame;}
        public override bool CanAutoAddPawns=>false;
        public override StateGraph CreateGraph(){var graph=new StateGraph();graph.AddToil(new LordToil_UNCapture());return graph;}
        public override void ExposeData(){Scribe_References.Look(ref target,"target");Scribe_Values.Look(ref started,"started");}
    }
    public sealed class LordToil_UNCapture:LordToil
    {
        public override void UpdateAllDuties()
        {
            var job=(LordJob_UNCapture)lord.LordJob;
            foreach(var p in lord.ownedPawns)p.mindState.duty=new PawnDuty(DefDatabase<DutyDef>.GetNamed("UN_TargetedCapture"),job.target);
        }
        public override void LordToilTick(){if(Find.TickManager.TicksGame%250==0)UpdateAllDuties();}
    }
    public sealed class JobGiver_UNCapture:ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var lord=pawn.GetLord()?.LordJob as LordJob_UNCapture;var target=lord?.target;if(lord==null)return null;
            if(target==null||target.Dead||!target.Spawned||target.Map!=pawn.Map||Find.TickManager.TicksGame-lord.started>60000)
            {
                if(RCellFinder.TryFindBestExitSpot(pawn,out IntVec3 exit)){var leave=JobMaker.MakeJob(JobDefOf.Goto,exit);leave.exitMapOnArrival=true;return leave;}return null;
            }
            if(target.Downed)
            {
                if(pawn.CanReserve(target)&&RCellFinder.TryFindBestExitSpot(pawn,out IntVec3 exit))
                {var take=JobMaker.MakeJob(JobDefOf.Kidnap,target,exit);take.count=1;return take;}
                return JobMaker.MakeJob(JobDefOf.Wait,120);
            }
            var attack=JobMaker.MakeJob(JobDefOf.AttackMelee,target);attack.maxNumMeleeAttacks=1;attack.expiryInterval=300;attack.canBashDoors=false;attack.canBashFences=false;return attack;
        }
    }
}
