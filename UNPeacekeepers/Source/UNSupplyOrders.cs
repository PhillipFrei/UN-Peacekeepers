using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;

namespace UNPeacekeepers
{
    public static class UNSupplyOrders
    {
        public static QuestPart_UNSupply Active => Find.QuestManager.QuestsListForReading
            .Where(q=>q.State==QuestState.Ongoing).SelectMany(q=>q.PartsListForReading).OfType<QuestPart_UNSupply>().FirstOrDefault();
        public static string Block(Pawn pawn,Faction un,bool advanced)
        {
            if(un==null || un.defeated || un.deactivated || !UNContact.IsUN(un))return "UN_C_Unavailable".Translate();
            if(pawn?.Map==null || !pawn.Map.IsPlayerHome)return "UN_C_NoMap".Translate();
            if(un.PlayerGoodwill<UNRules.AssignmentThreshold(advanced))return "UN_AssignmentLow".Translate(UNRules.AssignmentThreshold(advanced));
            if(Active!=null)return "UN029_Active".Translate();
            int now=Find.TickManager.TicksGame;
            if(Find.QuestManager.QuestsListForReading.Any(q=>q.EverAccepted && q.PartsListForReading.OfType<QuestPart_UNSupply>().Any() && now-q.acceptanceTick<180000))return "UN029_Cooldown".Translate();
            return null;
        }
        public static DiaOption Option(Pawn pawn,Faction un,bool advanced)
        {
            var option=new DiaOption((advanced?"UN_AdvancedAssignments":"UN_OrdinaryAssignments").Translate());
            var active=Active;
            if(active!=null && active.advanced==advanced)
            {
                option.link=new DiaNode(active.DescriptionPart);
                option.link.options.Add(new DiaOption("UN029_Deliver".Translate()){resolveTree=true,action=()=>active.Deliver(pawn)});
                option.link.options.Add(new DiaOption("UN029_Cancel".Translate()){resolveTree=true,action=()=>{if(active.quest.State==QuestState.Ongoing)active.quest.End(QuestEndOutcome.Fail);}});
                option.link.options.Add(new DiaOption("GoBack".Translate()){linkLateBind=()=>FactionDialogMaker.FactionDialogFor(pawn,un)});
                return option;
            }
            string block=Block(pawn,un,advanced);
            if(block!=null){option.Disable(block);return option;}
            option.link=new DiaNode(QuestPart_UNSupply.Terms(advanced));
            option.link.options.Add(new DiaOption("UN_C_Accept".Translate()){resolveTree=true,action=()=>Start(pawn,un,advanced)});
            option.link.options.Add(new DiaOption("GoBack".Translate()){linkLateBind=()=>FactionDialogMaker.FactionDialogFor(pawn,un)});
            return option;
        }
        static void Start(Pawn pawn,Faction un,bool advanced)
        {
            var block=Block(pawn,un,advanced);
            if(block!=null){Messages.Message(block,MessageTypeDefOf.RejectInput,false);return;}
            var quest=Quest.MakeRaw();
            quest.root=DefDatabase<QuestScriptDef>.GetNamed(advanced?"UN_Advanced_Supplies":"UN_Supplies");
            Populate(quest,advanced);
            Find.QuestManager.Add(quest);
            quest.Accept(pawn);
            Find.LetterStack.ReceiveLetter(quest.name,quest.description,LetterDefOf.NeutralEvent);
        }
        internal static void Populate(Quest quest,bool advanced)
        {
            quest.name=(advanced?"UN029_MedicalTitle":"UN029_FoodTitle").Translate();
            quest.description=QuestPart_UNSupply.Terms(advanced);
            quest.AddPart(new QuestPart_UNSupply{advanced=advanced,inSignalEnable=quest.InitiateSignal});
        }
    }
    // The def has a real root for developer generation too; normal access is gated at comms.
    public sealed class QuestNode_UNSupply : QuestNode
    {
        public bool advanced;
        protected override bool TestRunInt(Slate slate)=>Authorization.GetUN()?.PlayerGoodwill>=UNRules.AssignmentThreshold(advanced);
        protected override void RunInt()=>UNSupplyOrders.Populate(QuestGen.quest,advanced);
    }
    public sealed class QuestPart_UNSupply : QuestPartActivable
    {
        public bool advanced;
        private readonly UNSupplyProgress progress=new UNSupplyProgress();
        public int Required=>advanced?30:40;
        public int Reward=>advanced?20:10;
        public ThingDef Supply=>DefDatabase<ThingDef>.GetNamed(advanced?"MedicineIndustrial":"MealSurvivalPack");
        public static string Terms(bool advanced)=>"UN029_Terms".Translate(advanced?30:40,
            DefDatabase<ThingDef>.GetNamed(advanced?"MedicineIndustrial":"MealSurvivalPack").label,advanced?20:10).ToString();
        public override string DescriptionPart=>Terms(advanced)+"\n\n"+"UN029_Progress".Translate(progress.Delivered,Required,
            Math.Max(0,600000-quest.TicksSinceAccepted)/60000f);
        public override IEnumerable<Faction> InvolvedFactions { get {var un=Authorization.GetUN();if(un!=null)yield return un;} }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref advanced,"unSupplyAdvanced",false);
            Scribe_Values.Look(ref progress.Delivered,"unSupplyDelivered",0);
            Scribe_Values.Look(ref progress.Rewarded,"unSupplyRewarded",false);
        }
        public override void QuestPartTick()
        {
            base.QuestPartTick();
            // Completion and expiry are handled by the manager outside quest iteration.
        }
        public void RepairCompletion()
        {
            if(quest.State!=QuestState.Ongoing)return;
            if(progress.Delivered<Required)
            {
                if(UNSupplyProgress.Expired(quest.TicksSinceAccepted))quest.End(QuestEndOutcome.Fail);
                return;
            }
            if(!progress.Rewarded)
            {
                var un=Authorization.GetUN();
                if(un==null||un.defeated||un.deactivated)return;
                if(progress.TryClaim(Required))
                    try{UNContact.ApplyPrisonGoodwill(un,Reward,quest.name);}
                    catch(Exception ex){Log.Error("[UN Peacekeepers] Supply goodwill uncertain for "+quest.id+": "+ex);}
            }
            // An old Rewarded flag is not permission to pay again.
            quest.End(QuestEndOutcome.Success);
        }
        public void Deliver(Pawn pawn)
        {
            var un=Authorization.GetUN();
            if(progress.Rewarded || quest.State!=QuestState.Ongoing)return;
            if(UNSupplyProgress.Expired(quest.TicksSinceAccepted)){quest.End(QuestEndOutcome.Fail);return;}
            if(un==null || un.defeated || un.deactivated || un.PlayerGoodwill<0 || pawn?.Map==null || !pawn.Map.IsPlayerHome)
            {Messages.Message("UN_C_Unavailable".Translate(),MessageTypeDefOf.RejectInput,false);return;}
            // Only real, spawned, reachable and non-forbidden goods on the negotiator's home map.
            var stacks=pawn.Map.listerThings.ThingsOfDef(Supply).Where(t=>t.Spawned && !t.IsForbidden(pawn) && pawn.CanReach(t,PathEndMode.Touch,Danger.Some)).ToList();
            foreach(var stack in stacks)
            {
                int count=progress.TakeCount(Required,stack.stackCount);
                if(count<=0)break;
                var taken=stack.SplitOff(count);
                taken.Destroy(DestroyMode.Vanish);
                progress.Delivered+=count;
            }
            if(progress.Delivered<Required)
            {Messages.Message("UN029_Progress".Translate(progress.Delivered,Required,Math.Max(0,600000-quest.TicksSinceAccepted)/60000f),MessageTypeDefOf.NeutralEvent,false);return;}
            // The manager completes this accepted delivery on its next poll.
        }
    }
}
