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
        public int amount,questId=-1,total,retryAt,blockReason;
        public bool uncertain;
        public Map home;
        public string reason;
        public string Status
        {
            get
            {
                if(uncertain)return "UNPAY_Uncertain".Translate(amount);
                if(blockReason==1)return "UNPAY_NoHome".Translate(amount);
                if(blockReason==2)return "UNPAY_NoLanding".Translate(amount);
                if(blockReason==3)return "UNPAY_Retry".Translate(amount);
                return "UNPAY_Queued".Translate(amount);
            }
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref amount,"amount",0);Scribe_References.Look(ref home,"home");Scribe_Values.Look(ref reason,"reason");
            Scribe_Values.Look(ref questId,"unQuestId",-1);Scribe_Values.Look(ref total,"unTotal",0);
            Scribe_Values.Look(ref retryAt,"unRetryAt",0);Scribe_Values.Look(ref blockReason,"unBlockReason",0);
            Scribe_Values.Look(ref uncertain,"unDispatchUncertain",false);
        }
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
        public int paymentTotal,paymentDispatched,offerClosedReason;
        public override bool RequiresAccepter=>true;
        public override string DescriptionPart=>"UNM_Reward".Translate(silver,UNMissionPolicy.Goodwill(tier),supported?.Name??"—",(difficulty*100).ToString("0")).ToString()+
            (legacyRepairReported?"\n"+"UNM_RepairPaymentUnknown".Translate(quest?.name??"—").ToString():"")+
            (technicalRepair?"\n"+"UNM_RepairTechnical".Translate(quest?.name??"—").ToString():"")+
            "\n"+PaymentStatus+
            (offerClosedReason==1?"\n"+"UNPAY_Declined".Translate().ToString():offerClosedReason==2?"\n"+"UNPAY_Expired".Translate().ToString():"");
        public string PaymentStatus
        {
            get
            {
                if(quest==null||!quest.EverAccepted)return "UNPAY_NotEarned".Translate();
                if(quest.State==QuestState.EndedFailed)return "UNPAY_NoReward".Translate();
                if(quest.State!=QuestState.EndedSuccess)return "UNPAY_NotEarned".Translate();
                var parcel=UNMissionManager.Instance?.ParcelFor(quest.id);
                if(parcel!=null)return parcel.Status;
                if(paymentTotal>0&&paymentDispatched==paymentTotal)return "UNPAY_Dispatched".Translate(paymentDispatched);
                if(paymentTotal>0)return "UNPAY_Uncertain".Translate(Math.Max(0,paymentTotal-paymentDispatched));
                if(silverQueued||settled)return "UNPAY_LegacyUnknown".Translate();
                return "UNPAY_Settling".Translate();
            }
        }
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
            Scribe_Values.Look(ref paymentTotal,"unPaymentTotal",0);Scribe_Values.Look(ref paymentDispatched,"unPaymentDispatched",0);
            Scribe_Values.Look(ref offerClosedReason,"unOfferClosedReason",0);
        }
    }
    public sealed class UNMissionManager : GameComponent
    {
        private List<UNMissionSlot> slots=new List<UNMissionSlot>();
        private List<Quest> supplementOffers=new List<Quest>();
        private List<UNSilverParcel> parcels=new List<UNSilverParcel>();
        private int nextInactivity=-1, nextCheck, nextCompletionCheck;
        private bool repairOnLoad,deliveringParcels;
        private readonly HashSet<int> restoredThisLoad=new HashSet<int>();
        internal static Faction exactRewardFaction;
        public UNMissionManager(Game game){}
        public static UNMissionManager Instance=>Current.Game?.GetComponent<UNMissionManager>();
        public static bool OfferOpen(Quest q)=>q!=null&&q.State==QuestState.NotYetAccepted&&
            !UNMissionPolicy.OfferExpired(q.acceptanceExpireTick,Find.TickManager.TicksGame);
        public IEnumerable<Quest> Offers=>slots.Select(s=>s.offer).Concat(supplementOffers).Where(OfferOpen).Distinct();
        public UNSilverParcel ParcelFor(int id)=>id<0?null:parcels.FirstOrDefault(p=>p.questId==id&&p.amount>0);
        public IEnumerable<string> PendingPaymentLines=>parcels.Where(p=>p.amount>0).Select(p=>
            (p.questId>=0?"UNPAY_QuestId".Translate(p.questId,p.reason??"—").ToString():"UNPAY_OldParcel".Translate(p.reason??"—").ToString())+"\n"+p.Status);
        public int OptionalActiveCount=>Find.QuestManager.QuestsListForReading.Count(q=>q.State==QuestState.Ongoing&&q.GetFirstPartOfType<UNMissionReward>()?.optional==true);
        public static Pawn OptionalAccepter(Quest q)
        {
            var home=q?.GetFirstPartOfType<UNMissionReward>()?.home;
            return Find.Maps.Where(map=>map.IsPlayerHome).OrderByDescending(map=>map==home)
                .SelectMany(map=>map.mapPawns.FreeColonistsSpawned)
                .FirstOrDefault(p=>p.Faction==Faction.OfPlayer&&!p.Dead&&!p.Downed);
        }
        public bool RefreshTerms(Quest q)
        {
            if(!OfferOpen(q))return false;
            var reward=q.GetFirstPartOfType<UNMissionReward>();
            var objective=q.GetFirstPartOfType<UNMissionObjective>();
            if(reward==null||objective==null||!objective.RefreshOffer(UNMod.MissionDifficulty))return false;
            reward.difficulty=UNMod.MissionDifficulty;
            reward.silver=UNMissionPolicy.ScaledSilver(reward.tier,reward.difficulty);
            q.description=objective.OfferDescription;return true;
        }
        public void CloseSupplement(Quest q,bool expired)
        {
            var r=q?.GetFirstPartOfType<UNMissionReward>();
            if(r==null||!r.optional||q.EverAccepted||q.State!=QuestState.NotYetAccepted)return;
            r.offerClosedReason=expired||UNMissionPolicy.OfferExpired(q.acceptanceExpireTick,Find.TickManager.TicksGame)?2:1;
            q.End(QuestEndOutcome.InvalidPreAcceptance,false,false);
            if(q.State==QuestState.NotYetAccepted){r.offerClosedReason=0;return;}
            supplementOffers.Remove(q);
            TryRewardStep(q,"optional close notice",()=>UNContact.Instance?.LogEvent(q.name+" — "+(r.offerClosedReason==2?"UNPAY_Expired":"UNPAY_Declined").Translate()));
        }
        void MaintainSupplements(int now)
        {
            foreach(var q in supplementOffers.Where(q=>q!=null).ToList())
                if(q.State==QuestState.NotYetAccepted&&UNMissionPolicy.OfferExpired(q.acceptanceExpireTick,now))
                    CloseSupplement(q,true);
            supplementOffers.RemoveAll(q=>q==null||q.State!=QuestState.NotYetAccepted);
            foreach(var letter in Find.LetterStack.LettersListForReading.OfType<UNSupplementLetter>().Where(l=>!OfferOpen(l.quest)).ToList())
                Find.LetterStack.RemoveLetter(letter);
        }
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
            MaintainSupplements(now);
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
        public string AcceptBlock(Quest q,Pawn pawn,bool refreshTerms=true)
        {
            var reward=q?.PartsListForReading.OfType<UNMissionReward>().FirstOrDefault();
            var un=Authorization.GetUN();
            if(reward==null||q.State!=QuestState.NotYetAccepted||un==null||un.defeated||un.deactivated)return "UN_C_Unavailable".Translate();
            if(!OfferOpen(q))return "UNPAY_Expired".Translate();
            if(un.PlayerGoodwill<UNMissionPolicy.Threshold(reward.tier))return "UN_AssignmentLow".Translate(UNMissionPolicy.Threshold(reward.tier));
            if(!reward.optional&&ActiveCount>=UNMissionPolicy.MaxActive)return "UNM_Full".Translate();
            if(!ValidSupported(reward.supported)||reward.supported.HostileTo(Faction.OfPlayer))return "UNM_RecipientGone".Translate();
            if(pawn==null||pawn.Dead||pawn.Faction!=Faction.OfPlayer||pawn.Map==null||!pawn.Map.IsPlayerHome)return "UNM_NeedPawn".Translate();
            var objective=q.GetFirstPartOfType<UNMissionObjective>();
            if(refreshTerms&&RefreshTerms(q))return "UNM_TermsChanged".Translate();
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
            // A corrupted save may contain duplicate keyed parcels. Neither is safe to replay.
            foreach(var group in parcels.Where(p=>p.questId>=0&&p.amount>0).GroupBy(p=>p.questId).Where(g=>g.Count()>1))
                foreach(var parcel in group)parcel.uncertain=true;
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
                var existing=ParcelFor(q.id);
                if(UNMissionPolicy.CanQueueSilver(r.silverQueued,r.paymentTotal,existing!=null))
                {
                    int overflow=unAvailable?UNMissionPolicy.ExcessSilver(un.PlayerGoodwill,g):0;
                    int total=r.silver+overflow;
                    parcels.Add(new UNSilverParcel{questId=q.id,total=total,amount=total,home=r.home,reason=q.name});
                    r.paymentTotal=total;
                    r.silverQueued=true;
                    TryRewardStep(q,"payment journal",()=>UNContact.Instance?.LogEvent("UNM_Payment".Translate(q.name,r.silver,overflow)));
                }
                else
                {
                    // Recover a saved keyed parcel or respect a recorded award. Never infer
                    // identity from repeated quest names and never create a second parcel.
                    if(existing!=null&&r.paymentTotal==0)r.paymentTotal=existing.total>0?existing.total:existing.amount;
                    r.silverQueued=true;
                }
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
            if(r==null||!r.optional||r.supplementLetterSent||!OfferOpen(q))return;
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
            if(deliveringParcels)return;
            deliveringParcels=true;
            try
            {
                int now=Find.TickManager.TicksGame;
                foreach(var parcel in parcels.ToList())
                {
                    if(parcel.amount<=0){parcels.Remove(parcel);continue;}
                    if(!UNMissionPolicy.CanDispatchSilver(parcel.amount,parcel.uncertain,now,parcel.retryAt))continue;
                    // Independent parcels cannot block each other. Retry ordinary failures hourly.
                    parcel.retryAt=now+2500;
                    try{DeliverParcel(parcel);}
                    catch(Exception ex)
                    {
                        if(!parcel.uncertain)parcel.blockReason=3;
                        Log.Error("[UN Peacekeepers] Silver dispatch check failed for "+parcel.reason+": "+ex);
                    }
                }
            }
            finally{deliveringParcels=false;}
        }
        void DeliverParcel(UNSilverParcel parcel)
        {
            var reward=parcel.questId<0?null:Find.QuestManager.QuestsListForReading.FirstOrDefault(q=>q.id==parcel.questId)?.GetFirstPartOfType<UNMissionReward>();
            int ledger=reward==null?0:UNMissionPolicy.ParcelLedgerState(reward.paymentTotal,reward.paymentDispatched,parcel.amount);
            if(ledger==1){parcels.Remove(parcel);return;} // a recorded paid balance cannot be dispatched again
            if(ledger==2){parcel.uncertain=true;return;} // inconsistent ledger: do not infer a new payment
            var map=parcel.home!=null&&Find.Maps.Contains(parcel.home)&&parcel.home.IsPlayerHome?parcel.home:UNContact.RaidTarget();
            if(map==null||!map.IsPlayerHome){parcel.blockReason=1;return;}
            // Prefer a safe outdoor spot near the orbital trade beacon.
            var center=DropCellFinder.TradeDropSpot(map);
            IntVec3 spot=IntVec3.Invalid;
            if(center.IsValid&&center.InBounds(map)&&
                !DropCellFinder.TryFindDropSpotNear(center,map,out spot,allowFogged:false,canRoofPunch:false))spot=IntVec3.Invalid;
            if(!SafeSilverSpot(spot,map))
                spot=DropCellFinder.TryFindSafeLandingSpotCloseToColony(map,ThingDefOf.DropPodIncoming.size,Faction.OfPlayer);
            if(!SafeSilverSpot(spot,map)){parcel.blockReason=2;return;}
            int amount=parcel.amount;
            Skyfaller incoming=null;
            var contents=new List<Thing>();
            bool spawnAttempted=false,committed=false;
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
                // Persist the uncertainty guard BEFORE any engine/mod spawn callbacks.
                // A reentrant save during dispatch must never authorize a second payment.
                spawnAttempted=true;parcel.uncertain=true;
                GenSpawn.Spawn(incoming,spot,map);
                committed=incoming.Spawned&&incoming.Map==map;
            }
            catch(Exception ex)
            {
                committed=incoming!=null&&incoming.Spawned&&incoming.Map==map;
                Log.Error("[UN Peacekeepers] Silver pod dispatch for "+parcel.reason+": "+ex);
            }
            int result=UNMissionPolicy.DispatchOutcome(committed,spawnAttempted,contents.Any(t=>t.MapHeld!=null));
            if(result==2)
            {
                // A third-party hook may have spawned/moved/destroyed the cargo before an exception.
                // Do not destroy potentially delivered silver, and do not replay an ambiguous payment.
                parcel.uncertain=true;
                UNContact.Instance?.LogEvent(parcel.reason+" — "+"UNPAY_Uncertain".Translate(amount));
                return;
            }
            if(result==0)
            {
                foreach(var silver in contents)if(!silver.Destroyed&&silver.MapHeld==null)silver.Destroy();
                parcel.uncertain=false;parcel.blockReason=3;
                return;
            }
            // Positive spawn evidence: commit debit before notification. No landing/collection
            // claim is made; normal in-game loss after dispatch does not trigger a replacement.
            parcel.amount=UNMissionPolicy.RemainingAfterDropDispatch(amount,true);
            if(reward!=null)
            {
                if(reward.paymentTotal<=0)reward.paymentTotal=parcel.total>0?parcel.total:amount;
                reward.paymentDispatched=Math.Min(reward.paymentTotal,reward.paymentDispatched+amount);
            }
            parcels.Remove(parcel);
            try
            {
                UNContact.Instance?.LogEvent("UNM_PaymentPodJournal".Translate(parcel.reason,amount));
                Find.LetterStack.ReceiveLetter("UNM_PaymentPodTitle".Translate(),"UNM_PaymentPodText".Translate(parcel.reason,amount),
                    LetterDefOf.PositiveEvent,new LookTargets(spot,map));
            }
            catch(Exception ex){Log.Error("[UN Peacekeepers] Silver pod notification failed: "+ex);}
        }
        static bool SafeSilverSpot(IntVec3 spot,Map map)=>spot.IsValid&&spot.InBounds(map)&&!spot.Roofed(map)&&
            !spot.Fogged(map)&&spot.Standable(map)&&DropCellFinder.SkyfallerCanLandAt(spot,map,ThingDefOf.DropPodIncoming.size,Faction.OfPlayer);
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
                if(!IsManaged(__instance))return true;
                if(__instance.State==QuestState.NotYetAccepted)
                {
                    var r=__instance.GetFirstPartOfType<UNMissionReward>();
                    if(r?.optional==true&&r.offerClosedReason==0&&__0==QuestEndOutcome.InvalidPreAcceptance&&
                        UNMissionPolicy.OfferExpired(__instance.acceptanceExpireTick,Find.TickManager.TicksGame))r.offerClosedReason=2;
                    return true;
                }
                if(__instance.State!=QuestState.Ongoing)return true;
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
        private float shownDifficulty=-1f;
        bool Available=>UNMissionManager.OfferOpen(quest)&&
            quest.GetFirstPartOfType<UNMissionReward>()?.optional==true;
        public override bool CanShowInLetterStack=>base.CanShowInLetterStack&&Available;
        public override bool CanDismissWithRightClick=>false;
        static Pawn Accepter(Quest q)=>UNMissionManager.OptionalAccepter(q);
        public static string Brief(Quest q)
        {
            var r=q?.GetFirstPartOfType<UNMissionReward>();
            var o=q?.GetFirstPartOfType<UNMissionObjective>();
            if(r==null||o==null||!UNMissionManager.OfferOpen(q))return "UNM_SupplementUnavailable".Translate();
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
            if(Available)UNMissionManager.Instance?.RefreshTerms(quest);
            shownDifficulty=quest?.GetFirstPartOfType<UNMissionReward>()?.difficulty??-1f;
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
                    var reward=quest.GetFirstPartOfType<UNMissionReward>();
                    if(reward==null||Math.Abs(shownDifficulty-reward.difficulty)>0.001f||
                        Math.Abs(shownDifficulty-UNMod.MissionDifficulty)>0.001f)
                    {
                        UNMissionManager.Instance?.RefreshTerms(quest);
                        Messages.Message("UNM_TermsChanged".Translate(),MessageTypeDefOf.RejectInput,false);return;
                    }
                    var block=UNMissionManager.Instance?.AcceptBlock(quest,current)??(UNMissionManager.Instance==null?"UN_C_Unavailable".Translate().ToString():null);
                    if(block!=null){Messages.Message(block,MessageTypeDefOf.RejectInput,false);return;}
                    quest.Accept(current);
                    if(quest.State!=QuestState.NotYetAccepted)Find.LetterStack.RemoveLetter(this);
                }};
                string reason=manager==null?"UN_C_Unavailable".Translate().ToString():pawn==null?"UNM_SupplementNoPawn".Translate().ToString():manager.AcceptBlock(quest,pawn,false);
                if(reason!=null)accept.Disable(reason);
                yield return accept;
                yield return new DiaOption("UNM_SupplementDecline".Translate()){resolveTree=true,action=()=>{
                    if(Available)UNMissionManager.Instance?.CloseSupplement(quest,false);
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
            Widgets.Label(new Rect(0,0,rect.width,30),"UNPAY_Pool".Translate(manager?.ActiveCount??0,manager?.OptionalActiveCount??0));
            if(manager==null)return;
            // Live active missions first, including surviving legacy orders and the ultimatum.
            var quests=Find.QuestManager.QuestsListForReading.Where(q=>q.State==QuestState.Ongoing&&
                (UNMissionManager.IsManaged(q)||q.GetFirstPartOfType<QuestPart_UNSupply>()!=null||q.GetFirstPartOfType<QuestPart_UNUltimatum>()!=null))
                .Concat(manager?.Offers??Enumerable.Empty<Quest>()).Distinct().ToList();
            // Update terms before measuring or drawing text, never in the layout acceptance predicate.
            foreach(var offer in quests.Where(UNMissionManager.OfferOpen))manager.RefreshTerms(offer);
            float w=rect.width-24;
            var labels=quests.Select(q=>q.name+"\n"+(q.GetFirstPartOfType<UNMissionReward>()?.optional==true?"UNPAY_Optional".Translate().ToString()+"\n":"")+q.description+"\n"+string.Join("\n",q.PartsListForReading.Select(p=>p.DescriptionPart).Where(d=>!d.NullOrEmpty()))).ToList();
            var blocks=quests.Select(q=>q.State==QuestState.NotYetAccepted?
                manager.AcceptBlock(q,q.GetFirstPartOfType<UNMissionReward>()?.optional==true?UNMissionManager.OptionalAccepter(q):pawn,false):null).ToList();
            var heights=labels.Select((l,i)=>Text.CalcHeight(l,w-20)+100+(blocks[i]==null?0:Text.CalcHeight(blocks[i],w-20)+8)).ToList();
            Widgets.BeginScrollView(new Rect(0,35,rect.width,rect.height-85),ref scroll,new Rect(0,0,w,Math.Max(80,heights.Sum())));
            if(quests.Count==0)Widgets.Label(new Rect(5,5,w-10,70),"UNM_Empty".Translate());
            float y=0;
            for(int i=0;i<quests.Count;i++)
            {
                var q=quests[i];Widgets.DrawHighlight(new Rect(0,y,w,heights[i]-5));
                float labelHeight=Text.CalcHeight(labels[i],w-20);
                Widgets.Label(new Rect(10,y+5,w-20,labelHeight+4),labels[i]);
                if(q.State==QuestState.NotYetAccepted)
                {
                    bool optional=q.GetFirstPartOfType<UNMissionReward>()?.optional==true;
                    var accepter=optional?UNMissionManager.OptionalAccepter(q):pawn;
                    string block=blocks[i];
                    if(block!=null)Widgets.Label(new Rect(10,y+labelHeight+12,w-20,Text.CalcHeight(block,w-20)+4),block);
                    if(block==null&&Widgets.ButtonText(new Rect(10,y+heights[i]-48,210,30),"UN_C_Accept".Translate()))q.Accept(accepter);
                    if(optional&&Widgets.ButtonText(new Rect(230,y+heights[i]-48,210,30),"UNM_SupplementDecline".Translate()))
                        manager.CloseSupplement(q,false);
                }
                else if(UNMissionManager.IsManaged(q)&&Widgets.ButtonText(new Rect(10,y+heights[i]-48,310,30),"UNM_Cancel".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("UNM_CancelConfirm".Translate(),()=>manager.Complete(q.id,false),true));
                y+=heights[i];
            }
            Widgets.EndScrollView();
        }
    }
}
