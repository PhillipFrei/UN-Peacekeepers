using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace UNPeacekeepers
{
    public sealed class UNMissionSlot : IExposable
    {
        public Quest offer;
        public int refillAt;
        public void ExposeData(){Scribe_References.Look(ref offer,"offer");Scribe_Values.Look(ref refillAt,"refillAt",0);}
    }
    public sealed class UNSilverParcel : IExposable
    {
        public int amount;
        public Map home;
        public string reason;
        public void ExposeData(){Scribe_Values.Look(ref amount,"amount",0);Scribe_References.Look(ref home,"home");Scribe_Values.Look(ref reason,"reason");}
    }
    public sealed class UNMissionReward : QuestPart
    {
        public int tier, slot, silver;
        public float difficulty=1f;
        public Faction supported;
        public Map home;
        public bool started, settled, optional, supplementOffered, supplementLetterSent;
        public bool silverQueued, unGoodwillAttempted, supportedGoodwillAttempted, legacyRepairReported, technicalRepair;
        public int parentId=-1;
        public override bool RequiresAccepter=>true;
        public override string DescriptionPart=>"UNM_Reward".Translate(silver,UNMissionPolicy.Goodwill(tier),supported?.Name??"—",(difficulty*100).ToString("0")).ToString()+
            (legacyRepairReported?"\n"+"UNM_RepairPaymentUnknown".Translate(quest?.name??"—").ToString():"")+
            (technicalRepair?"\n"+"UNM_RepairTechnical".Translate(quest?.name??"—").ToString():"");
        public override void ExposeData()
        {
            base.ExposeData();Scribe_Values.Look(ref tier,"tier",0);Scribe_Values.Look(ref slot,"slot",0);
            Scribe_Values.Look(ref silver,"silver",0);Scribe_Values.Look(ref difficulty,"difficulty",1f);
            Scribe_References.Look(ref supported,"supported");Scribe_References.Look(ref home,"home");
            Scribe_Values.Look(ref optional,"optional",false);Scribe_Values.Look(ref supplementOffered,"supplementOffered",false);Scribe_Values.Look(ref parentId,"parentId",-1);
            Scribe_Values.Look(ref supplementLetterSent,"supplementLetterSent",false);
            Scribe_Values.Look(ref started,"started",false);Scribe_Values.Look(ref settled,"settled",false);
            Scribe_Values.Look(ref silverQueued,"silverQueued",false);Scribe_Values.Look(ref unGoodwillAttempted,"unGoodwillAttempted",false);Scribe_Values.Look(ref supportedGoodwillAttempted,"supportedGoodwillAttempted",false);
            Scribe_Values.Look(ref legacyRepairReported,"legacyRepairReported",false);Scribe_Values.Look(ref technicalRepair,"technicalRepair",false);
        }
    }
    public sealed class UNMissionManager : GameComponent
    {
        private List<UNMissionSlot> slots=new List<UNMissionSlot>();
        private List<Quest> supplementOffers=new List<Quest>();
        private List<UNSilverParcel> parcels=new List<UNSilverParcel>();
        private int nextInactivity=-1, nextCheck, nextCompletionCheck;
        private bool repairOnLoad;
        private readonly HashSet<int> restoredThisLoad=new HashSet<int>();
        internal static Faction exactRewardFaction;
        public UNMissionManager(Game game){}
        public static UNMissionManager Instance=>Current.Game?.GetComponent<UNMissionManager>();
        public IEnumerable<Quest> Offers=>slots.Select(s=>s.offer).Concat(supplementOffers).Where(q=>q!=null&&q.State==QuestState.NotYetAccepted);
        public static bool IsManaged(Quest q)=>q?.PartsListForReading.OfType<UNMissionReward>().Any()==true;
        public int ActiveCount=>Find.QuestManager.QuestsListForReading.Count(q=>q.State==QuestState.Ongoing && (q.PartsListForReading.OfType<UNMissionReward>().Any(r=>!r.optional)||q.PartsListForReading.OfType<QuestPart_UNSupply>().Any()));
        public float InactivityDays=>nextInactivity<0?10f:Math.Max(0,nextInactivity-Find.TickManager.TicksGame)/60000f;
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref slots,"unMissionSlots",LookMode.Deep);
            Scribe_Collections.Look(ref supplementOffers,"unSupplementOffers",LookMode.Reference);
            Scribe_Collections.Look(ref parcels,"unMissionParcels",LookMode.Deep);
            Scribe_Values.Look(ref nextInactivity,"unMissionNextInactivity",-1);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            {
                if(slots==null)slots=new List<UNMissionSlot>();
                for(int i=0;i<slots.Count;i++)if(slots[i]==null)slots[i]=new UNMissionSlot();
                if(parcels==null)parcels=new List<UNSilverParcel>();
                parcels.RemoveAll(p=>p==null);
                if(supplementOffers==null)supplementOffers=new List<Quest>();
                repairOnLoad=true;restoredThisLoad.Clear();
            }
        }
        public override void GameComponentTick()
        {
            if(Current.ProgramState!=ProgramState.Playing||Find.FactionManager==null||Find.QuestManager==null||Find.WorldObjects==null)return;
            int now=Find.TickManager.TicksGame;
            if(now>=nextCompletionCheck)
            {
                nextCompletionCheck=now+250;
                if(repairOnLoad)
                {
                    try{RestoreOfferReferences();ProcessCompletions(true);repairOnLoad=false;}
                    catch(Exception ex){Log.Error("[UN Peacekeepers] Save repair will retry: "+ex);return;}
                }
                else ProcessCompletions(false);
            }
            if(now<nextCheck)return;nextCheck=now+2500;
            if(nextInactivity<0)nextInactivity=now+UNMissionPolicy.InactivityTicks;
            var un=Authorization.GetUN();
            if(un!=null&&!un.defeated&&!un.deactivated)
            {
                if(now>=nextInactivity)
                {
                    int delta=UNMissionPolicy.InactivityPenalty(un.PlayerGoodwill);
                    if(delta!=0)UNContact.ApplyPrisonGoodwill(un,delta,"UNM_Inactive".Translate());
                    nextInactivity=now+UNMissionPolicy.InactivityTicks;
                }
                MaintainPool(un.PlayerGoodwill,now);
            }
            supplementOffers.RemoveAll(q=>q==null||q.State!=QuestState.NotYetAccepted);
            UNMissionSites.CleanupCompletedSites();
            foreach(var q in supplementOffers.ToList())TryRewardStep(q,"supplemental letter",()=>EnsureSupplementLetter(q));
            DeliverParcels();
        }
        void MaintainPool(int goodwill,int now)
        {
            while(slots.Count<10)slots.Add(new UNMissionSlot());
            for(int i=0;i<10;i++)
            {
                var slot=slots[i];int tier=UNMissionPolicy.SlotTier(i,goodwill);
                var old=slot.offer;
                if(old!=null && old.State!=QuestState.NotYetAccepted){slot.offer=null;old=null;slot.refillAt=Math.Max(slot.refillAt,now+UNMissionPolicy.RefillTicks);}
                if(old!=null && (tier<0 || old.PartsListForReading.OfType<UNMissionReward>().First().tier!=tier))
                {old.End(QuestEndOutcome.InvalidPreAcceptance,false,false);slot.offer=null;}
                if(slot.offer==null && tier>=0 && now>=slot.refillAt)
                    slot.offer=CreateOffer(i,tier);
            }
        }
        Quest CreateOffer(int slot,int tier)
        {
            var home=Find.CurrentMap?.IsPlayerHome==true?Find.CurrentMap:UNContact.RaidTarget();
            if(home==null)return null;
            var factions=Find.FactionManager.AllFactionsListForReading.Where(f=>ValidSupported(f)&&!f.HostileTo(Faction.OfPlayer)).ToList();
            if(factions.Count==0)return null;
            var supported=factions.RandomElement();
            int[] kinds=tier==0?new[]{0,1,2}:tier==1?new[]{0,1,2,3,4,5}:new[]{6};
            var available=kinds.Where(UNMissionSites.Supports).InRandomOrder().ToList();
            foreach(int kind in available)
            {
                UNMissionObjective objective;string reason;
                if(!UNMissionSites.TryPrepare(kind,tier,home,supported,UNMod.MissionDifficulty,out objective,out reason))continue;
                var q=Quest.MakeRaw();q.root=DefDatabase<QuestScriptDef>.GetNamed("UN_Mission_Contract");
                q.name="UNM_Title".Translate(("UNM_Kind"+kind).Translate(),("UNM_Tier"+tier).Translate());
                q.description=objective.OfferDescription;
                objective.inSignalEnable=q.InitiateSignal;q.AddPart(objective);objective.AddCustodyPart(q);
                q.AddPart(new UNMissionReward{tier=tier,slot=slot,supported=supported,home=home,difficulty=UNMod.MissionDifficulty,silver=UNMissionPolicy.ScaledSilver(tier,UNMod.MissionDifficulty)});
                Find.QuestManager.Add(q);return q;
            }
            return null;
        }
        public static bool ValidSupported(Faction f)=>f!=null&&!f.IsPlayer&&!UNContact.IsUN(f)&&!f.defeated&&!f.deactivated&&!f.temporary&&!f.Hidden;
        public string AcceptBlock(Quest q,Pawn pawn)
        {
            var reward=q?.PartsListForReading.OfType<UNMissionReward>().FirstOrDefault();
            var un=Authorization.GetUN();
            if(reward==null||q.State!=QuestState.NotYetAccepted||un==null||un.defeated||un.deactivated)return "UN_C_Unavailable".Translate();
            if(un.PlayerGoodwill<UNMissionPolicy.Threshold(reward.tier))return "UN_AssignmentLow".Translate(UNMissionPolicy.Threshold(reward.tier));
            if(!reward.optional&&ActiveCount>=UNMissionPolicy.MaxActive)return "UNM_Full".Translate();
            if(!ValidSupported(reward.supported)||reward.supported.HostileTo(Faction.OfPlayer))return "UNM_RecipientGone".Translate();
            if(pawn==null||pawn.Dead||pawn.Faction!=Faction.OfPlayer||pawn.Map==null||!pawn.Map.IsPlayerHome)return "UNM_NeedPawn".Translate();
            var objective=q.GetFirstPartOfType<UNMissionObjective>();
            if(objective!=null&&objective.RefreshOffer(UNMod.MissionDifficulty))
            {
                reward.difficulty=UNMod.MissionDifficulty;reward.silver=UNMissionPolicy.ScaledSilver(reward.tier,reward.difficulty);
                q.description=objective.OfferDescription;
                return "UNM_TermsChanged".Translate();
            }
            string problem;
            if(objective==null)return "UN_C_Unavailable".Translate();
            if(!objective.ValidateAcceptance(out problem))return problem;
            return null;
        }
        public void Accepted(Quest q)
        {
            var reward=q.PartsListForReading.OfType<UNMissionReward>().FirstOrDefault();
            if(reward==null||reward.started||q.State!=QuestState.Ongoing)return;
            reward.started=true;
            int now=Find.TickManager.TicksGame;nextInactivity=now+UNMissionPolicy.InactivityTicks;
            var slot=slots.FirstOrDefault(s=>s.offer==q);
            if(slot!=null){slot.offer=null;slot.refillAt=now+UNMissionPolicy.RefillTicks;}
            try{q.GetFirstPartOfType<UNMissionObjective>().Start(q);}
            catch(Exception ex)
            {
                // Technical setup failure is not a player crime or failed objective.
                reward.technicalRepair=true;reward.settled=true;
                q.GetFirstPartOfType<UNMissionObjective>()?.RequestCompletion(false,UNMissionFailure.Technical);
                q.End(QuestEndOutcome.Fail);
                if(slot!=null)slot.refillAt=now;
                Log.Error("[UN Peacekeepers] Mission setup failed: "+ex);
                return;
            }
            UNContact.Instance?.LogEvent("UNM_Accepted".Translate(q.name));
        }
        public void Complete(int questId,bool success)
        {
            var q=Find.QuestManager.QuestsListForReading.FirstOrDefault(x=>x.id==questId);
            if(q==null||q.State!=QuestState.Ongoing)return;
            var objective=q.GetFirstPartOfType<UNMissionObjective>();
            if(objective==null)
            {
                if(!success){var r=q.GetFirstPartOfType<UNMissionReward>();if(r!=null){r.technicalRepair=true;r.settled=true;}q.End(QuestEndOutcome.Fail);}
                return;
            }
            if(success&&!objective.CanRewardSuccess)return;
            objective.RequestCompletion(success,success?UNMissionFailure.Unknown:UNMissionFailure.Cancelled);
        }
        void RestoreOfferReferences()
        {
            while(slots.Count<10)slots.Add(new UNMissionSlot());
            var quests=Find.QuestManager.QuestsListForReading.Where(IsManaged).ToList();
            // Refresh the static terms of still-live quests; preserve all saved quantities and deadlines.
            foreach(var q in quests.Where(q=>q.State==QuestState.NotYetAccepted||q.State==QuestState.Ongoing))
            {
                var objective=q.GetFirstPartOfType<UNMissionObjective>();
                if(objective!=null)q.description=objective.OfferDescription;
            }
            // The quest itself owns the original slot, difficulty and recipient. Never reroll these.
            for(int i=0;i<slots.Count;i++)
            {
                var slot=slots[i];
                if(slot.offer!=null&&slot.offer.EverAccepted)
                    slot.refillAt=UNMissionPolicy.RestoredRefillAt(slot.refillAt,slot.offer.acceptanceTick);
                if(slot.offer==null||slot.offer.State!=QuestState.NotYetAccepted)slot.offer=null;
            }
            foreach(var q in quests.Where(q=>q.EverAccepted))
            {
                var r=q.GetFirstPartOfType<UNMissionReward>();
                if(!r.optional&&r.slot>=0&&r.slot<10)
                    slots[r.slot].refillAt=UNMissionPolicy.RestoredRefillAt(slots[r.slot].refillAt,q.acceptanceTick);
            }
            supplementOffers=supplementOffers.Where(q=>q!=null&&q.State==QuestState.NotYetAccepted&&IsManaged(q)).Distinct().ToList();
            foreach(var q in quests.Where(q=>q.State==QuestState.NotYetAccepted).OrderBy(q=>q.id))
            {
                var r=q.GetFirstPartOfType<UNMissionReward>();
                if(r.optional){if(!supplementOffers.Contains(q))supplementOffers.Add(q);continue;}
                if(r.slot<0||r.slot>=10)continue;
                if(slots[r.slot].offer==null)slots[r.slot].offer=q;
                else if(slots[r.slot].offer!=q)
                    q.End(QuestEndOutcome.InvalidPreAcceptance,false,false);
            }
        }
        static void RepairJournal(Quest q,string key)
        {
            string message=key.Translate(q.name).ToString();
            UNContact.Instance?.LogEvent(message);
            Log.Message("[UN Peacekeepers] Quest "+q.id+": "+message);
        }
        void ReportLegacyPayment(Quest q,UNMissionReward r,UNMissionObjective o)
        {
            if(r==null||r.legacyRepairReported||o?.CanRewardSuccess!=true||
                !UNMissionPolicy.LegacyPaymentUncertain(r.settled,r.silverQueued,r.unGoodwillAttempted,r.supportedGoodwillAttempted))return;
            r.legacyRepairReported=true;
            TryRewardStep(q,"legacy payment notice",()=>RepairJournal(q,"UNM_RepairPaymentUnknown"));
        }
        void ProcessCompletions(bool loading)
        {
            bool retryLoad=false;
            foreach(var q in Find.QuestManager.QuestsListForReading.ToList())
            {
                try
                {
                    if(q.State==QuestState.Ongoing)
                    {
                        var supply=q.GetFirstPartOfType<QuestPart_UNSupply>();
                        if(supply!=null)supply.RepairCompletion();
                    }
                    if(!IsManaged(q))continue;
                    var r=q.GetFirstPartOfType<UNMissionReward>();
                    var o=q.GetFirstPartOfType<UNMissionObjective>();
                    if(q.State==QuestState.Ongoing)
                    {
                        bool restored=o?.RestoreRecordedOutcome()==true;
                        if(loading&&!restoredThisLoad.Contains(q.id))
                        {
                            if(o==null||(!o.CompletionRequested&&!o.RestoreLoadedState(q,r.parentId)))
                            {
                                // Corrupt data cannot earn a success; do not charge a gameplay
                                // failure penalty for an unrecoverable technical setup.
                                r.technicalRepair=true;r.settled=true;
                                TryRewardStep(q,"technical repair notice",()=>RepairJournal(q,"UNM_RepairTechnical"));
                                if(o!=null)o.RequestCompletion(false,UNMissionFailure.Technical);
                                else q.End(QuestEndOutcome.Fail);
                            }
                            else
                            {
                                r.started=true;
                                if(restored||o.CompletionRequested)
                                    TryRewardStep(q,"repair notice",()=>RepairJournal(q,"UNM_RepairOutcome"));
                            }
                            restoredThisLoad.Add(q.id);
                        }
                        if(q.State==QuestState.Ongoing)
                        {
                            if(o==null)
                            {
                                if(r.technicalRepair)q.End(QuestEndOutcome.Fail);
                            }
                            else
                            {
                                if(!o.CompletionRequested)o.PollMission();
                                if(o.CompletionRequested)
                                {
                                    ReportLegacyPayment(q,r,o);
                                    q.End(o.CanRewardSuccess?QuestEndOutcome.Success:QuestEndOutcome.Fail);
                                }
                            }
                        }
                    }
                    if(q.State==QuestState.EndedSuccess||q.State==QuestState.EndedFailed)
                        Settle(q,r,o);
                }
                catch(Exception ex)
                {
                    if(loading&&!restoredThisLoad.Contains(q.id))retryLoad=true;
                    Log.Error("[UN Peacekeepers] Quest completion retry for "+q.id+": "+ex);
                }
            }
            if(retryLoad)throw new InvalidOperationException("Some saved missions could not be inspected.");
        }
        void Settle(Quest q,UNMissionReward r,UNMissionObjective objective)
        {
            bool success=q.State==QuestState.EndedSuccess;
            if(r==null||!UNMissionPolicy.CanSettle(q.EverAccepted,r.settled,success,q.State==QuestState.EndedFailed,objective?.CanRewardSuccess==true))return;
            var un=Authorization.GetUN();
            bool unAvailable=un!=null&&!un.defeated&&!un.deactivated;
            if(!success)
            {
                if(!r.unGoodwillAttempted)
                {
                    r.unGoodwillAttempted=true;
                    if(unAvailable)TryRewardStep(q,"failure goodwill",()=>UNContact.ApplyPrisonGoodwill(un,-5,"UNM_Failed".Translate(q.name).ToString()+" — "+UNQuestDisplay.Failure(objective?.FailureReason??UNMissionFailure.Unknown)));
                }
                r.settled=true;
                return;
            }
            int g=UNMissionPolicy.Goodwill(r.tier);
            // Queue silver before goodwill changes, so overflow is computed from the pre-award balance.
            if(!r.silverQueued)
            {
                int overflow=unAvailable?UNMissionPolicy.ExcessSilver(un.PlayerGoodwill,g):0;
                parcels.Add(new UNSilverParcel{amount=r.silver+overflow,home=r.home,reason=q.name});
                r.silverQueued=true;
                TryRewardStep(q,"payment journal",()=>UNContact.Instance?.LogEvent("UNM_Payment".Translate(q.name,r.silver,overflow)));
            }
            // An exception from another mod may occur AFTER a faction mutation. Never replay an
            // ambiguous goodwill call; log it, and continue the independent silver/other faction steps.
            if(!r.unGoodwillAttempted)
            {
                r.unGoodwillAttempted=true;
                if(unAvailable)TryRewardStep(q,"UN goodwill",()=>UNContact.ApplyPrisonGoodwill(un,g,q.name));
            }
            if(!r.supportedGoodwillAttempted)
            {
                r.supportedGoodwillAttempted=true;
                if(ValidSupported(r.supported))TryRewardStep(q,"supported faction goodwill",()=>
                {
                    var previous=exactRewardFaction;exactRewardFaction=r.supported;
                    try{r.supported.TryAffectGoodwillWith(Faction.OfPlayer,g,true,true);}
                    finally{exactRewardFaction=previous;}
                });
            }
            r.settled=true;
            TryRewardStep(q,"silver delivery",DeliverParcels);
            TryRewardStep(q,"supplemental offer",()=>OfferSupplement(q,r,objective));
        }
        static void TryRewardStep(Quest q,string step,Action action)
        {
            try{action();}
            catch(Exception ex){Log.Error("[UN Peacekeepers] Quest "+q.id+" "+step+" failed: "+ex);}
        }
        void OfferSupplement(Quest q,UNMissionReward r,UNMissionObjective objective)
        {
            if(!r.optional&&!r.supplementOffered)
            {
                r.supplementOffered=true;
                UNMissionObjective extra;
                if(UNMissionSites.TryPrepareSupplement(objective,Math.Min(1,r.tier),out extra))
                {
                    var supplemental=Quest.MakeRaw();supplemental.root=q.root;
                    supplemental.name="UNM_Supplement".Translate(q.name);
                    supplemental.description=extra.OfferDescription;
                    supplemental.acceptanceExpireTick=Find.TickManager.TicksGame+180000;
                    extra.inSignalEnable=supplemental.InitiateSignal;supplemental.AddPart(extra);extra.AddCustodyPart(supplemental);
                    int tier=Math.Min(1,r.tier);
                    supplemental.AddPart(new UNMissionReward{tier=tier,slot=-1,optional=true,parentId=q.id,home=r.home,supported=r.supported,difficulty=r.difficulty,silver=UNMissionPolicy.ScaledSilver(tier,r.difficulty)});
                    Find.QuestManager.Add(supplemental);supplementOffers.Add(supplemental);
                    EnsureSupplementLetter(supplemental);
                }
            }
        }
        void EnsureSupplementLetter(Quest q)
        {
            var r=q?.GetFirstPartOfType<UNMissionReward>();
            if(r==null||!r.optional||r.supplementLetterSent||q.State!=QuestState.NotYetAccepted)return;
            var stack=Find.LetterStack;
            if(stack.LettersListForReading.OfType<UNSupplementLetter>().Any(l=>l.quest==q))
            {r.supplementLetterSent=true;return;}
            var letter=(UNSupplementLetter)LetterMaker.MakeLetter("UNM_SupplementLetterTitle".Translate(),
                UNSupplementLetter.Brief(q),DefDatabase<LetterDef>.GetNamed("UN_SupplementOfferLetter"),quest:q);
            letter.title="UNM_SupplementLetterTitle".Translate();
            stack.ReceiveLetter(letter);
            if(!stack.LettersListForReading.Contains(letter))return;
            r.supplementLetterSent=true;
            // Replace old informational letters from pre-0.3.5, never another quest's live decision letter.
            foreach(var old in stack.LettersListForReading.Where(l=>l!=letter&&l.def==LetterDefOf.NeutralEvent&&
                l.Label.ToString()==q.name).ToList())stack.RemoveLetter(old);
        }
        void DeliverParcels()
        {
            foreach(var parcel in parcels.ToList())
            {
                if(parcel.amount<=0){parcels.Remove(parcel);continue;}
                var map=parcel.home!=null&&Find.Maps.Contains(parcel.home)&&parcel.home.IsPlayerHome?parcel.home:UNContact.RaidTarget();
                if(map==null||!map.IsPlayerHome)continue;
                // Prefer the normal orbital trade delivery area. Never punch a roof or
                // force an unsafe landing. A pending reward remains saved if no spot exists.
                var center=DropCellFinder.TradeDropSpot(map);
                IntVec3 spot=IntVec3.Invalid;
                if(center.IsValid&&center.InBounds(map))
                    DropCellFinder.TryFindDropSpotNear(center,map,out spot,allowFogged:false,canRoofPunch:false);
                if(!spot.IsValid||!spot.InBounds(map)||spot.Roofed(map))
                    spot=DropCellFinder.TryFindSafeLandingSpotCloseToColony(map,ThingDefOf.DropPodIncoming.size,Faction.OfPlayer);
                if(!spot.IsValid||!spot.InBounds(map)||spot.Roofed(map)||spot.Fogged(map)||
                    !spot.Standable(map)||!DropCellFinder.SkyfallerCanLandAt(spot,map,ThingDefOf.DropPodIncoming.size,Faction.OfPlayer))continue;
                int amount=parcel.amount;
                // One native incoming pod per payment, with ordinary silver stacks inside.
                // Keep a reference to the actual skyfaller so an exception after spawn
                // cannot cause a second payment. After dispatch, normal game risks apply.
                Skyfaller incoming=null;
                var contents=new List<Thing>();
                bool committed=false;
                try
                {
                    var info=new ActiveTransporterInfo{openDelay=110,leaveSlag=false};
                    int remaining=amount;
                    while(remaining>0)
                    {
                        var silver=ThingMaker.MakeThing(ThingDefOf.Silver);
                        silver.stackCount=Math.Min(remaining,ThingDefOf.Silver.stackLimit);
                        remaining-=silver.stackCount;contents.Add(silver);
                        if(!info.innerContainer.TryAdd(silver,false))throw new InvalidOperationException("Could not fill silver drop pod.");
                    }
                    var pod=(ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
                    pod.Contents=info;
                    incoming=SkyfallerMaker.MakeSkyfaller(ThingDefOf.DropPodIncoming,pod);
                    if(pod.Destroyed)throw new InvalidOperationException("Could not load silver drop pod.");
                    GenSpawn.Spawn(incoming,spot,map);
                    committed=incoming.Spawned&&incoming.Map==map;
                }
                catch(Exception ex)
                {
                    committed=incoming!=null&&incoming.Spawned&&incoming.Map==map;
                    Log.Error("[UN Peacekeepers] Silver pod dispatch "+(committed?"spawned before exception":"will retry")+" for "+parcel.reason+": "+ex);
                }
                if(!committed)
                {
                    // Nothing reached a map: discard only the unspawned temporary cargo.
                    // If a third-party hook moved some stacks onto a map, account for those
                    // individually instead of ever paying those same stacks again.
                    foreach(var silver in contents)
                    {
                        if(silver.MapHeld!=null)parcel.amount=UNMissionPolicy.RemainingAfterPlacement(parcel.amount,silver.stackCount);
                        else if(!silver.Destroyed)silver.Destroy();
                    }
                    continue;
                }
                parcel.amount=UNMissionPolicy.RemainingAfterDropDispatch(amount,true);
                parcels.Remove(parcel);
                // The debit precedes the notice; a UI failure cannot replay the payment.
                try
                {
                    UNContact.Instance?.LogEvent("UNM_PaymentPodJournal".Translate(parcel.reason,amount));
                    Find.LetterStack.ReceiveLetter("UNM_PaymentPodTitle".Translate(),"UNM_PaymentPodText".Translate(parcel.reason,amount),
                        LetterDefOf.PositiveEvent,new LookTargets(spot,map));
                }
                catch(Exception ex){Log.Error("[UN Peacekeepers] Silver pod notification failed: "+ex);}
            }
        }
        public static void Open(Pawn pawn=null)
        {
            Find.WindowStack.WindowOfType<Dialog_UNMissions>()?.Close();
            Find.WindowStack.Add(new Dialog_UNMissions(pawn));
        }
        [HarmonyPatch(typeof(Quest),nameof(Quest.Accept))]
        static class AcceptGate
        {
            static bool Prefix(Quest __instance,Pawn __0)
            {
                if(!IsManaged(__instance))return true;
                var block=Instance==null?"UN_C_Unavailable".Translate().ToString():Instance.AcceptBlock(__instance,__0);
                if(block==null)return true;
                Messages.Message(block,MessageTypeDefOf.RejectInput,false);return false;
            }
            static void Postfix(Quest __instance){if(IsManaged(__instance))Instance?.Accepted(__instance);}
        }
        [HarmonyPatch(typeof(Quest),nameof(Quest.End))]
        static class EndGuard
        {
            sealed class EndContext
            {
                public UNMissionReward reward;
                public UNMissionObjective objective;
            }
            static bool Prefix(Quest __instance,QuestEndOutcome __0,out EndContext __state)
            {
                __state=null;
                if(!IsManaged(__instance)||__instance.State!=QuestState.Ongoing)return true;
                var objective=__instance.GetFirstPartOfType<UNMissionObjective>();
                if(__0==QuestEndOutcome.Success&&objective?.CanRewardSuccess!=true)return false;
                __state=new EndContext{reward=__instance.GetFirstPartOfType<UNMissionReward>(),objective=objective};
                return true;
            }
            static void Postfix(Quest __instance,EndContext __state)
            {
                // A skipped End or failed state transition must never award anything.
                if(__state==null)return;
                try{Instance?.Settle(__instance,__state.reward,__state.objective);}
                catch(Exception ex){Log.Error("[UN Peacekeepers] Settlement failed for quest "+__instance.id+": "+ex);}
            }
        }
        [HarmonyPatch(typeof(Faction),nameof(Faction.CalculateAdjustedGoodwillChange))]
        static class ExactMissionBonus
        {
            static void Postfix(Faction __instance,Faction __0,int __1,ref int __result)
            {if(exactRewardFaction!=null&&__instance==exactRewardFaction&&__0==Faction.OfPlayer)__result=__1;}
        }
    }
    // A native, save-persistent choice letter. ChoiceLetter serializes its Quest reference.
    public sealed class UNSupplementLetter:ChoiceLetter
    {
        bool Available=>quest!=null&&quest.State==QuestState.NotYetAccepted&&
            quest.GetFirstPartOfType<UNMissionReward>()?.optional==true;
        public override bool CanShowInLetterStack=>base.CanShowInLetterStack&&Available;
        public override bool CanDismissWithRightClick=>false;
        static Pawn Accepter(Quest q)
        {
            var home=q?.GetFirstPartOfType<UNMissionReward>()?.home;
            return Find.Maps.Where(m=>m.IsPlayerHome).OrderByDescending(m=>m==home)
                .SelectMany(m=>m.mapPawns.FreeColonistsSpawned)
                .FirstOrDefault(p=>p.Faction==Faction.OfPlayer&&!p.Dead&&!p.Downed);
        }
        public static string Brief(Quest q)
        {
            var r=q?.GetFirstPartOfType<UNMissionReward>();
            var o=q?.GetFirstPartOfType<UNMissionObjective>();
            if(r==null||o==null||q.State!=QuestState.NotYetAccepted)return "UNM_SupplementUnavailable".Translate();
            var lines=new List<string>{
                q.name,
                "",
                "UNM_SupplementRecipient".Translate(r.supported?.Name??"—"),
                "UNM_SupplementSite".Translate(o.tile.ToString())
            };
            for(int i=0;i<o.goods.Count&&i<o.required.Count;i++)
                lines.Add("UNM_SupplementGoods".Translate(o.goods[i]?.LabelCap.ToString()??"—",o.required[i]));
            lines.Add("");
            lines.Add("UNM_SupplementSilver".Translate(r.silver));
            lines.Add("UNM_SupplementGoodwill".Translate(UNMissionPolicy.Goodwill(r.tier),r.supported?.Name??"—"));
            lines.Add(UNQuestDisplay.OfferTime(q));
            lines.Add("UNQ_TotalDuration".Translate(UNQuestDisplay.Time(o.DurationTicks)));
            lines.Add("");
            lines.Add("UNM_SupplementRules".Translate());
            return string.Join("\n",lines);
        }
        public override void OpenLetter()
        {
            if(Available)
            {
                var r=quest.GetFirstPartOfType<UNMissionReward>();
                var o=quest.GetFirstPartOfType<UNMissionObjective>();
                if(o!=null&&o.RefreshOffer(UNMod.MissionDifficulty))
                {
                    r.difficulty=UNMod.MissionDifficulty;r.silver=UNMissionPolicy.ScaledSilver(r.tier,r.difficulty);
                    quest.description=o.OfferDescription;
                }
            }
            Text=Brief(quest);
            base.OpenLetter();
        }
        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if(!Available){yield return Option_Close;yield break;}
                var manager=UNMissionManager.Instance;
                var pawn=Accepter(quest);
                var accept=new DiaOption("UN_C_Accept".Translate()){resolveTree=true,action=()=>{
                    if(!Available)return;
                    var current=Accepter(quest);
                    var block=UNMissionManager.Instance?.AcceptBlock(quest,current)??(UNMissionManager.Instance==null?"UN_C_Unavailable".Translate().ToString():null);
                    if(block!=null){Messages.Message(block,MessageTypeDefOf.RejectInput,false);return;}
                    quest.Accept(current);
                    if(quest.State!=QuestState.NotYetAccepted)Find.LetterStack.RemoveLetter(this);
                }};
                string reason=manager==null?"UN_C_Unavailable".Translate().ToString():pawn==null?"UNM_SupplementNoPawn".Translate().ToString():manager.AcceptBlock(quest,pawn);
                if(reason!=null)accept.Disable(reason);
                yield return accept;
                yield return new DiaOption("UNM_SupplementDecline".Translate()){resolveTree=true,action=()=>{
                    if(Available)quest.End(QuestEndOutcome.InvalidPreAcceptance,false,false);
                    if(!Available)Find.LetterStack.RemoveLetter(this);
                }};
                yield return Option_Postpone;
            }
        }
    }
    public sealed class Dialog_UNMissions : Window
    {
        readonly Pawn pawn;
        Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(860,700);
        public Dialog_UNMissions(Pawn pawn){this.pawn=pawn;doCloseX=true;doCloseButton=true;draggable=true;layer=WindowLayer.GameUI;forcePause=false;absorbInputAroundWindow=false;preventCameraMotion=false;closeOnClickedOutside=false;}
        public override void DoWindowContents(Rect rect)
        {
            var manager=UNMissionManager.Instance;
            Widgets.Label(new Rect(0,0,rect.width,30),"UNM_Pool".Translate(manager?.ActiveCount??0));
            // Live active missions first, including surviving legacy orders and the ultimatum.
            var quests=Find.QuestManager.QuestsListForReading.Where(q=>q.State==QuestState.Ongoing&&
                (UNMissionManager.IsManaged(q)||q.GetFirstPartOfType<QuestPart_UNSupply>()!=null||q.GetFirstPartOfType<QuestPart_UNUltimatum>()!=null))
                .Concat(manager?.Offers??Enumerable.Empty<Quest>()).Distinct().ToList();
            float w=rect.width-24;
            var labels=quests.Select(q=>q.name+"\n"+q.description+"\n"+string.Join("\n",q.PartsListForReading.Select(p=>p.DescriptionPart).Where(d=>!d.NullOrEmpty()))).ToList();
            var heights=labels.Select(l=>Text.CalcHeight(l,w-20)+90).ToList();
            Widgets.BeginScrollView(new Rect(0,35,rect.width,rect.height-85),ref scroll,new Rect(0,0,w,Math.Max(80,heights.Sum())));
            if(quests.Count==0)Widgets.Label(new Rect(5,5,w-10,70),"UNM_Empty".Translate());
            float y=0;
            for(int i=0;i<quests.Count;i++)
            {
                var q=quests[i];Widgets.DrawHighlight(new Rect(0,y,w,heights[i]-5));
                Widgets.Label(new Rect(10,y+5,w-20,heights[i]-60),labels[i]);
                if(q.State==QuestState.NotYetAccepted)
                {
                    string block=manager.AcceptBlock(q,pawn);
                    if(block==null){if(Widgets.ButtonText(new Rect(10,y+heights[i]-48,210,30),"UN_C_Accept".Translate()))q.Accept(pawn);}
                    else Widgets.Label(new Rect(10,y+heights[i]-48,w-20,40),block);
                }
                else if(UNMissionManager.IsManaged(q)&&Widgets.ButtonText(new Rect(10,y+heights[i]-48,310,30),"UNM_Cancel".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("UNM_CancelConfirm".Translate(),()=>manager.Complete(q.id,false),true));
                y+=heights[i];
            }
            Widgets.EndScrollView();
        }
    }
}
