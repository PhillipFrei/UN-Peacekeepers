using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace UNPeacekeepers
{
    // Only the player's humanlike prisoners on loaded player-home maps are assessed.
    public sealed class PrisonMonitor : GameComponent
    {
        public const int Interval = 180000;
        private Dictionary<string,int> due = new Dictionary<string,int>();
        private List<string> lastEvents = new List<string>();
        private int nextScan;
        private int nextAssessment = -1;
        private bool negativeSinceAssessment;
        public PrisonMonitor(Game game) { }
        public static PrisonMonitor Instance => Current.Game?.GetComponent<PrisonMonitor>();
        public static bool Eligible(Pawn p) => p != null && !p.Dead && p.Spawned && p.IsPrisonerOfColony && p.RaceProps.Humanlike && p.Map != null && p.Map.IsPlayerHome;
        public static List<Pawn> Prisoners() => Find.Maps.Where(m=>m.IsPlayerHome).SelectMany(m=>m.mapPawns.AllPawnsSpawned).Where(Eligible).ToList();
        public override void ExposeData()
        {
            Scribe_Values.Look(ref nextAssessment, "unNextAssessment", -1);
            Scribe_Values.Look(ref negativeSinceAssessment, "unNegativeSinceAssessment", false);
            Scribe_Collections.Look(ref due, "unPrisonDue", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastEvents, "unPrisonEvents", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { if(due==null) due=new Dictionary<string,int>(); if(lastEvents==null) lastEvents=new List<string>(); }
        }
        public override void GameComponentTick()
        {
            if(Current.ProgramState != ProgramState.Playing || Find.FactionManager == null) return;
            int now=Find.TickManager.TicksGame;
            if(now < nextScan) return;
            nextScan=now+250;
            var prisoners=Prisoners();
            var ids=new HashSet<string>(prisoners.Select(p=>p.ThingID));
            foreach(var key in due.Keys.Where(k=>!ids.Contains(k)).ToList()) due.Remove(key);
            // Keep old per-prisoner deadlines as grace periods when migrating existing saves.
            foreach(var p in prisoners)
                if(!due.ContainsKey(p.ThingID)) due[p.ThingID]=now+Interval;
            if(nextAssessment<0) nextAssessment=now+Interval;
            var un=Authorization.GetUN();
            if(un==null || un.defeated || un.deactivated) return;
            var inspections=prisoners.ToDictionary(p=>p,p=>Inspect(p,prisoners));
            bool mixed=HasMixedHousing(prisoners);
            // Any observed negative condition blocks recovery for the entire assessment period.
            if(mixed || inspections.Values.Any(v=>v.Any(x=>x.Value<0))) negativeSinceAssessment=true;
            if(now<nextAssessment) return;
            nextAssessment=now+Interval;
            int eligibleMixed=0;
            foreach(var p in prisoners.Where(p=>now>=due[p.ThingID]))
            {
                var result=inspections[p];
                int score=result.Sum(x=>x.Value);
                if(score<0) Record(p,score,string.Join("; ",result.Select(x=>x.Key)));
                if(IsMixed(p,prisoners))eligibleMixed++;
            }
            int mixedPenalty=UNRules.MixedHousingPenalty(eligibleMixed);
            if(mixedPenalty<0)Record(prisoners.First(p=>IsMixed(p,prisoners)),mixedPenalty,"UN_MixedSummary".Translate().ToString());
            int recovery=UNRules.Recovery(un.PlayerGoodwill,negativeSinceAssessment);
            if(recovery>0)UNContact.ApplyPrisonGoodwill(un,recovery,"UN_Recovery".Translate().ToString());
            negativeSinceAssessment=false;
        }
        public void BlockRecovery(){negativeSinceAssessment=true;}
        public float NextAssessmentDays=>nextAssessment<0?3f:Math.Max(0,nextAssessment-Find.TickManager.TicksGame)/60000f;
        public int ExpectedAssessmentDelta
        {
            get
            {
                var prisoners=Prisoners();int tick=nextAssessment<0?Find.TickManager.TicksGame+Interval:nextAssessment;
                var eligible=prisoners.Where(p=>due.TryGetValue(p.ThingID,out var grace)&&grace<=tick).ToList();
                int penalty=eligible.Sum(p=>Inspect(p,prisoners).Sum(x=>x.Value))+UNRules.MixedHousingPenalty(eligible.Count(p=>IsMixed(p,prisoners)));
                bool negative=negativeSinceAssessment||HasMixedHousing(prisoners)||prisoners.Any(p=>Inspect(p,prisoners).Any(x=>x.Value<0));
                int g=Authorization.GetUN()?.PlayerGoodwill??0;
                return Math.Max(-100,Math.Min(100,g+penalty+UNRules.Recovery(g,negative)))-g;
            }
        }
        public string AssessmentStatus()
        {
            var prisoners=Prisoners();
            bool negative=negativeSinceAssessment || HasMixedHousing(prisoners) || prisoners.Any(p=>Inspect(p,prisoners).Any(x=>x.Value<0));
            return "UN029_Assessment".Translate(nextAssessment<0?3f:Math.Max(0,nextAssessment-Find.TickManager.TicksGame)/60000f,
                (negative?"UN029_RecoveryBlocked":"UN029_RecoveryPossible").Translate()).ToString();
        }
        public float DaysUntil(Pawn p)
        {
            int grace;
            due.TryGetValue(p.ThingID,out grace);
            return Math.Max(0,Math.Max(grace,nextAssessment)-Find.TickManager.TicksGame)/60000f;
        }
        public static bool IsMixed(Pawn p,List<Pawn> prisoners)
        {
            var room=RestUtility.FindBedFor(p)?.GetRoom();
            return room!=null && p.gender!=Gender.None && prisoners.Any(q=>q!=p && q.Map==p.Map && q.gender!=Gender.None && q.gender!=p.gender && RestUtility.FindBedFor(q)?.GetRoom()==room);
        }
        public static bool HasMixedHousing(List<Pawn> prisoners)=>prisoners.Any(p=>IsMixed(p,prisoners));
        public IEnumerable<string> Events => lastEvents;
        public void Record(Pawn p,int delta,string reason)
        {
            var un=Authorization.GetUN();
            if(un==null || un.defeated || delta==0) return;
            if(delta<0) negativeSinceAssessment=true;
            int before=un.PlayerGoodwill;
            UNContact.ApplyPrisonGoodwill(un,delta,p.LabelShortCap.ToString()+": "+reason);
            if(delta<0)UNCrimeMonitor.ObserveExistingPenalty(-delta,reason);
            string msg="UN_PrisonResult".Translate(p.LabelShortCap,delta.ToString("+0;-0;0"),(un.PlayerGoodwill-before).ToString("+0;-0;0"),reason).ToString();
            lastEvents.Insert(0,msg);
            if(lastEvents.Count>60) lastEvents.RemoveAt(lastEvents.Count-1);
            Messages.Message(msg,delta<0?MessageTypeDefOf.NegativeEvent:MessageTypeDefOf.PositiveEvent,false);
        }
        public static List<KeyValuePair<string,int>> Inspect(Pawn p,List<Pawn> prisoners)
        {
            var issues=new List<KeyValuePair<string,int>>();
            Action<string,int> add=(key,points)=>issues.Add(new KeyValuePair<string,int>(key.Translate().ToString(),points));
            var bed=RestUtility.FindBedFor(p);
            var room=bed?.GetRoom();
            if(bed==null || !bed.ForPrisoners || bed.def==ThingDefOf.SleepingSpot || room==null || room.PsychologicallyOutdoors) add("UN_NoBed", -2);

            if(p.needs?.food!=null && p.needs.food.CurLevelPercentage<0.25f) add("UN_Hungry",-5);
            var h=p.health.hediffSet;
            int missing=h.GetMissingPartsCommonAncestors().Count(m=>m.Part!=null && new[]{"Arm","Leg","Hand","Foot","Eye","Ear","Kidney","Lung","Heart","Liver","Stomach"}.Contains(m.Part.def.defName) && !h.hediffs.Any(a=>a is Hediff_AddedPart && a.Part==m.Part));
            for(int i=0;i<Math.Min(4,missing);i++) add("UN_MissingPart",-5);
            if(h.hediffs.Any(a=>!(a is Hediff_Injury) && a.TendableNow())) add("UN_Illness",-5);
            if(h.hediffs.Any(a=>a is Hediff_Injury && a.TendableNow())) add("UN_UntendedInjury",-3);
            if(issues.Count==0) add("UN_GoodCare",0);
            return issues;
        }
    }
    // Vanilla explicitly reports unlawful surgery. Legitimate surgery is not charged as an acute crime.
    [HarmonyPatch(typeof(RecipeWorker), "ReportViolation")]
    internal static class SurgeryViolation
    {
        static void Postfix(Pawn __0,Pawn __1)
        {
            if(PrisonMonitor.Eligible(__0) && __1?.Faction==Faction.OfPlayer)
                PrisonMonitor.Instance?.Record(__0,-15,"UN_SurgeryViolation".Translate().ToString());
        }
    }
    public sealed class MainTabWindow_UN : Window
    {
        public MainTabWindow_UN(){doCloseX=true;doCloseButton=true;absorbInputAroundWindow=true;}
        private Vector2 scroll;
        private int refreshAt=-1;
        private List<Pawn> prisoners=new List<Pawn>();
        private Dictionary<Pawn,List<KeyValuePair<string,int>>> statuses=new Dictionary<Pawn,List<KeyValuePair<string,int>>>();
        public override Vector2 InitialSize => new Vector2(830f,570f);
        public override void DoWindowContents(Rect rect)
        {
            var un=Authorization.GetUN();
            var auth=Authorization.Instance;
            var mon=PrisonMonitor.Instance;
            float y=0;
            Widgets.Label(new Rect(0,y,rect.width,26),"UN_Overview".Translate());y+=28;
            Widgets.Label(new Rect(0,y,rect.width,26),"UN_StatusLine".Translate(un==null?"—":un.PlayerGoodwill.ToString(),un?.leader?.LabelShortCap.ToString()??"—",Authorization.Granted?"UN_Yes".Translate():"UN_No".Translate()));y+=28;
            Widgets.Label(new Rect(0,y,rect.width,26),"UN_DaysLine".Translate((auth?.ProgressDays??0f).ToString("0.0")));y+=32;
            int now=Find.TickManager.TicksGame;
            if(refreshAt<0 || now>=refreshAt || Widgets.ButtonText(new Rect(rect.width-115,0,110,25),"UN_Refresh".Translate()))
            {
                refreshAt=now+250;prisoners=PrisonMonitor.Prisoners();statuses=prisoners.ToDictionary(p=>p,p=>PrisonMonitor.Inspect(p,prisoners));
            }
            Widgets.Label(new Rect(0,y,rect.width,25),"UN_Count".Translate(prisoners.Count));y+=28;
            Widgets.Label(new Rect(0,y,rect.width,28),(PrisonMonitor.HasMixedHousing(prisoners)?"UN029_MixedBad":"UN029_MixedOK").Translate());y+=32;
            var history=mon?.Events.Take(15).ToList()??new List<string>();
            float w=rect.width-24; float height=prisoners.Count*92+history.Count*48+70;
            Widgets.BeginScrollView(new Rect(0,y,rect.width,rect.height-y-45),ref scroll,new Rect(0,0,w,height));
            float sy=0;
            foreach(var p in prisoners)
            {
                if(!PrisonMonitor.Eligible(p)) continue;
                Widgets.Label(new Rect(0,sy,w,24),p.LabelShortCap+"  |  "+p.gender.GetLabel()+"  |  "+"UN_Next".Translate((mon?.DaysUntil(p)??3f).ToString("0.00")));
                if(p.needs?.food!=null) Widgets.Label(new Rect(0,sy+24,w,23),"UN_Condition".Translate(p.needs.food.CurLevelPercentage.ToStringPercent(),p.health.summaryHealth.SummaryHealthPercent.ToStringPercent()));
                var result=statuses[p];
                Widgets.Label(new Rect(0,sy+47,w,43),string.Join("; ",result.Select(r=>r.Key+" ("+r.Value.ToString("+0;-0;0")+")")));
                sy+=92;
            }
            if(prisoners.Count==0) { Widgets.Label(new Rect(0,sy,w,30),"UN_Empty".Translate());sy+=32; }
            Widgets.Label(new Rect(0,sy,w,28),"UN_History".Translate());sy+=30;
            foreach(var msg in history){Widgets.Label(new Rect(0,sy,w,46),msg);sy+=48;}
            Widgets.EndScrollView();
        }
    }
}