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
        public bool started, settled, optional, supplementOffered;
        public bool silverQueued, unGoodwillAttempted, supportedGoodwillAttempted;
        public int parentId=-1;
        public override bool RequiresAccepter=>true;
        public override string DescriptionPart=>"UNM_Reward".Translate(silver,UNMissionPolicy.Goodwill(tier),supported?.Name??"—",(difficulty*100).ToString("0")).ToString();
        public override void ExposeData()
        {
            base.ExposeData();Scribe_Values.Look(ref tier,"tier",0);Scribe_Values.Look(ref slot,"slot",0);
            Scribe_Values.Look(ref silver,"silver",0);Scribe_Values.Look(ref difficulty,"difficulty",1f);
            Scribe_References.Look(ref supported,"supported");Scribe_References.Look(ref home,"home");
            Scribe_Values.Look(ref optional,"optional",false);Scribe_Values.Look(ref supplementOffered,"supplementOffered",false);Scribe_Values.Look(ref parentId,"parentId",-1);
            Scribe_Values.Look(ref started,"started",false);Scribe_Values.Look(ref settled,"settled",false);
            Scribe_Values.Look(ref silverQueued,"silverQueued",false);Scribe_Values.Look(ref unGoodwillAttempted,"unGoodwillAttempted",false);Scribe_Values.Look(ref supportedGoodwillAttempted,"supportedGoodwillAttempted",false);
        }
    }
    public sealed class UNMissionManager : GameComponent
    {
        private List<UNMissionSlot> slots=new List<UNMissionSlot>();
        private List<Quest> supplementOffers=new List<Quest>();
        private List<UNSilverParcel> parcels=new List<UNSilverParcel>();
        private int nextInactivity=-1, nextCheck, nextCompletionCheck;
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
            if(Scribe.mode==LoadSaveMode.PostLoadInit){if(slots==null)slots=new List<UNMissionSlot>();if(parcels==null)parcels=new List<UNSilverParcel>();if(supplementOffers==null)supplementOffers=new List<Quest>();}
        }
        public override void GameComponentTick()
        {
            if(Current.ProgramState!=ProgramState.Playing||Find.FactionManager==null)return;
            int now=Find.TickManager.TicksGame;
            if(now>=nextCompletionCheck){nextCompletionCheck=now+250;ProcessCompletions();}
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
                reward.settled=true;q.End(QuestEndOutcome.Fail);
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
            if(objective==null || (success&&!objective.CanRewardSuccess))return;
            objective.RequestCompletion(success);
        }
        void ProcessCompletions()
        {
            // Snapshot: native End and supplemental offers can mutate QuestManager's collection.
            foreach(var q in Find.QuestManager.QuestsListForReading.Where(IsManaged).ToList())
            {
                try
                {
                    var r=q.GetFirstPartOfType<UNMissionReward>();
                    var o=q.GetFirstPartOfType<UNMissionObjective>();
                    if(q.State==QuestState.Ongoing && o?.CompletionRequested==true)
                    {
                        q.End(o.CanRewardSuccess?QuestEndOutcome.Success:QuestEndOutcome.Fail);
                    }
                    if(q.State==QuestState.EndedSuccess||q.State==QuestState.EndedFailed)
                        Settle(q,r,o);
                }
                catch(Exception ex){Log.Error("[UN Peacekeepers] Quest completion retry for "+q.id+": "+ex);}
            }
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
                    if(unAvailable)TryRewardStep(q,"failure goodwill",()=>UNContact.ApplyPrisonGoodwill(un,-5,"UNM_Failed".Translate(q.name)));
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
                    Find.LetterStack.ReceiveLetter(supplemental.name,"UNM_Optional".Translate(),LetterDefOf.NeutralEvent);
                }
            }
        }
        void DeliverParcels()
        {
            foreach(var parcel in parcels.ToList())
            {
                var map=parcel.home!=null&&Find.Maps.Contains(parcel.home)&&parcel.home.IsPlayerHome?parcel.home:UNContact.RaidTarget();
                if(map==null)continue;
                // Physical silver near a living colonist. Keep the unpaid remainder across saves.
                var pawn=map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                if(pawn==null)continue;
                while(parcel.amount>0)
                {
                    int before=parcel.amount;
                    var silver=ThingMaker.MakeThing(ThingDefOf.Silver);silver.stackCount=Math.Min(before,ThingDefOf.Silver.stackLimit);
                    int requested=silver.stackCount;
                    // Native placement may merge part of a stack before reporting failure.
                    bool placed=GenPlace.TryPlaceThing(silver,pawn.Position,map,ThingPlaceMode.Near,
                        placedAction:(thing,count)=>parcel.amount=UNMissionPolicy.RemainingAfterPlacement(parcel.amount,count));
                    parcel.amount=UNMissionPolicy.RemainingAfterAttempt(before,parcel.amount,requested,placed);
                    if(!placed||parcel.amount>=before){if(!silver.Destroyed&&!silver.Spawned)silver.Destroy();break;}
                }
                if(parcel.amount==0){parcels.Remove(parcel);UNContact.Instance?.LogEvent("UNM_PaymentDelivered".Translate(parcel.reason));}
            }
        }
        public static void Open(Pawn pawn=null)=>Find.WindowStack.Add(new Dialog_UNMissions(pawn));
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
            var quests=(manager?.Offers??Enumerable.Empty<Quest>()).Concat(Find.QuestManager.QuestsListForReading.Where(q=>q.State==QuestState.Ongoing&&UNMissionManager.IsManaged(q))).ToList();
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
                else if(Widgets.ButtonText(new Rect(10,y+heights[i]-48,310,30),"UNM_Cancel".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("UNM_CancelConfirm".Translate(),()=>manager.Complete(q.id,false),true));
                y+=heights[i];
            }
            Widgets.EndScrollView();
        }
    }
}
