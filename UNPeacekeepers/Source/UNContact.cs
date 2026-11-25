using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace UNPeacekeepers
{
    public sealed class UNContact : GameComponent
    {
        const int Day=60000;
        private int nextRaid=-1, lastRaid=-1, escalationStart=-1, nextCheck=-1, nextAid=-1;
        private int knownGoodwill=1000;
        private List<string> journal=new List<string>();
        private static string prisonContext;
        public UNContact(Game game) {}
        public static UNContact Instance => Current.Game?.GetComponent<UNContact>();
        public static bool IsUN(Faction f) => f?.def?.defName=="UN_Peacekeepers";
        public override void ExposeData()
        {
            Scribe_Values.Look(ref nextRaid,"unNextRaid",-1);
            Scribe_Values.Look(ref lastRaid,"unLastRaid",-1);
            Scribe_Values.Look(ref escalationStart,"unEscalationStart",-1);
            Scribe_Values.Look(ref nextAid,"unNextAid",-1);
            Scribe_Values.Look(ref knownGoodwill,"unKnownGoodwill",1000);
            Scribe_Collections.Look(ref journal,"unGoodwillJournal",LookMode.Value);
            if(Scribe.mode==LoadSaveMode.PostLoadInit && journal==null)journal=new List<string>();
        }
        public static void ApplyPrisonGoodwill(Faction un,int delta,string reason)
        {
            var old=prisonContext; prisonContext=reason;
            try { un.TryAffectGoodwillWith(Faction.OfPlayer,delta,false,false); }
            finally { prisonContext=old; }
        }
        public void LogChange(int before,int after,string reason)
        {
            knownGoodwill=after;
            if(before==after && prisonContext==null)return;
            Add("UN_C_Entry".Translate((after-before).ToString("+0;-0;0"),before,after,reason).ToString());
        }
        private void Add(string text)
        {
            journal.Insert(0,"UN_C_Dated".Translate((Find.TickManager.TicksGame/(float)Day).ToString("0.0"),text).ToString());
            if(journal.Count>300)journal.RemoveAt(journal.Count-1);
        }
        public IEnumerable<string> Entries => journal;
        // Piecewise linear interpolation between the agreed goodwill thresholds.
        private static readonly float[] GoodwillStrength = { 1.5f, 2f, 3f, 4f, 5f };
        private static readonly float[] GoodwillDays = { 12f, 9f, 6f, 4f, 3f };
        private static float EscalationValue(int g, float[] values)
        {
            float step = Mathf.Clamp((-g - 80) / 5f, 0f, 4f);
            int lo = Mathf.Min(3, Mathf.FloorToInt(step));
            return Mathf.Lerp(values[lo], values[lo+1], step-lo);
        }
        public static int Interval(int g) => Mathf.RoundToInt(EscalationValue(g, GoodwillDays)*Day);
        public static float GoodwillMultiplier(int g) => EscalationValue(g, GoodwillStrength);
        public static float RaidPoints(Map map,int g)
        {
            // Use the actual storyteller result, including other mods' threat-scaling patches.
            // Wealth is already included there; no duplicate wealth factor, minimum, or cap.
            return StorytellerUtility.DefaultThreatPointsNow(map)*GoodwillMultiplier(g)*UNMod.Strength;
        }
        public static Map RaidTarget() => Find.Maps.Where(m=>m.IsPlayerHome)
            .OrderByDescending(m=>StorytellerUtility.DefaultThreatPointsNow(m)).FirstOrDefault();
        public string RaidStrengthStatus(Faction un)
        {
            if(un==null || un.defeated || un.deactivated || un.PlayerGoodwill>-80)
                return "UN_C_StrengthInactive".Translate().ToString();
            var map=RaidTarget();
            if(map==null) return "UN_C_NoMap".Translate().ToString();
            float baseline=StorytellerUtility.DefaultThreatPointsNow(map);
            float factor=GoodwillMultiplier(un.PlayerGoodwill);
            float strength=UNMod.Strength;
            return "UN_C_StrengthPreview".Translate(baseline.ToString("0"),factor.ToString("0.00"),(strength*100f).ToString("0"),(baseline*factor*strength).ToString("0")).ToString();
        }
        public string RaidStatus(Faction un)
        {
            if(un==null || un.defeated || un.deactivated)return "UN_C_Unavailable".Translate().ToString();
            if(un.PlayerGoodwill>-80)return "UN_C_NoRaid".Translate().ToString();
            if(!un.HostileTo(Faction.OfPlayer))return "UN_C_NotHostile".Translate().ToString();
            if(!Find.Maps.Any(m=>m.IsPlayerHome))return "UN_C_NoMap".Translate().ToString();
            return "UN_C_RaidDue".Translate((Math.Max(0,nextRaid-Find.TickManager.TicksGame)/(float)Day).ToString("0.00"),(Interval(un.PlayerGoodwill)/(float)Day).ToString("0.0")).ToString();
        }
        public override void GameComponentTick()
        {
            if(Current.ProgramState!=ProgramState.Playing || Find.FactionManager==null)return;
            int now=Find.TickManager.TicksGame;
            if(now<nextCheck)return;
            nextCheck=now+250;
            var un=Authorization.GetUN();
            if(un==null || un.defeated || un.deactivated)return;
            int g=un.PlayerGoodwill;
            if(knownGoodwill==1000){knownGoodwill=g;Add("UN_C_Baseline".Translate(g).ToString());}
            else if(g!=knownGoodwill)LogChange(knownGoodwill,g,"UN_C_Unknown".Translate().ToString());
            if(g>-80)
            {nextRaid=-1;lastRaid=-1;escalationStart=-1;return;}
            if(escalationStart<0)
            {
                escalationStart=now;
                nextRaid=now+Day;
                Find.LetterStack.ReceiveLetter("UN_C_WarningTitle".Translate(),"UN_C_WarningText".Translate(g),LetterDefOf.ThreatBig);
            }
            if(lastRaid>=0)nextRaid=lastRaid+Interval(g);
            if(now<nextRaid)return;
            if(!un.HostileTo(Faction.OfPlayer))return;
            var map=RaidTarget();
            if(map==null)return;
            float points=RaidPoints(map,g);
            var parms=new IncidentParms{target=map,faction=un,points=points,forced=true,raidStrategy=RaidStrategyDefOf.ImmediateAttack,raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn};
            try
            {
                if(IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
                {
                    lastRaid=now;nextRaid=now+Interval(g);
                    Add("UN_C_RaidLog".Translate(g,points.ToString("0")).ToString());
                }
                else {nextCheck=now+2500;}
            }
            catch(Exception ex){nextCheck=now+2500;Log.WarningOnce("[UN Peacekeepers] Raid failed: "+ex,82294123);}
        }
        public string AidBlock(Faction un,Map map)
        {
            if(un==null || un.defeated || un.deactivated)return "UN_C_Unavailable".Translate().ToString();
            if(un.PlayerGoodwill<75 || un.PlayerRelationKind!=FactionRelationKind.Ally)return "UN_C_AidLow".Translate().ToString();
            if(map==null || !map.IsPlayerHome)return "UN_C_NoMap".Translate().ToString();
            if(Find.TickManager.TicksGame<nextAid)return "UN_C_AidWait".Translate(((nextAid-Find.TickManager.TicksGame)/(float)Day).ToString("0.00")).ToString();
            if(!un.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.SeasonalTemp))return "BadTemperature".Translate().ToString();
            var targets=map.attackTargetsCache.TargetsHostileToColony.Where(x=>GenHostility.IsActiveThreatToPlayer(x)).Select(x=>(Thing)x).ToList();
            if(targets.Count==0)return "UN_C_NoThreat".Translate().ToString();
            if(!targets.Any(t=>t.Faction!=null && t.Faction.HostileTo(un)))return "UN_C_NoCommonEnemy".Translate().ToString();
            return null;
        }
        public void RequestAid(Faction un,Map map)
        {
            string reason=AidBlock(un,map);
            if(reason!=null){Messages.Message(reason,MessageTypeDefOf.RejectInput,false);return;}
            var parms=new IncidentParms{target=map,faction=un,points=Mathf.Max(500f,StorytellerUtility.DefaultThreatPointsNow(map)),forced=true,raidStrategy=RaidStrategyDefOf.ImmediateAttackFriendly,raidArrivalModeForQuickMilitaryAid=true};
            try
            {
                if(IncidentDefOf.RaidFriendly.Worker.TryExecute(parms))
                {
                    nextAid=Find.TickManager.TicksGame+3*Day;
                    Add("UN_C_AidSent".Translate().ToString());
                    Messages.Message("UN_C_AidSent".Translate(),MessageTypeDefOf.PositiveEvent,false);
                    return;
                }
            }
            catch(Exception ex){Log.WarningOnce("[UN Peacekeepers] Aid failed: "+ex,82294124);}
            Messages.Message("UN_C_AidFailed".Translate(),MessageTypeDefOf.RejectInput,false);
        }
        [HarmonyPatch(typeof(Faction),nameof(Faction.TryAffectGoodwillWith))]
        static class JournalPatch
        {
            static void Prefix(Faction __instance,Faction __0,out int? __state)
            {
                __state=null;
                if(Current.ProgramState!=ProgramState.Playing || __0==null)return;
                if(IsUN(__instance)&&__0.IsPlayer)__state=__instance.PlayerGoodwill;
                else if(__instance.IsPlayer&&IsUN(__0))__state=__0.PlayerGoodwill;
            }
            static void Postfix(Faction __instance,Faction __0,HistoryEventDef __4,int? __state)
            {
                if(!__state.HasValue)return;
                var un=IsUN(__instance)?__instance:__0;
                var reason=prisonContext??(__4!=null?__4.LabelCap.ToString():"UN_C_Unknown".Translate().ToString());
                Instance?.LogChange(__state.Value,un.PlayerGoodwill,reason);
            }
        }
        // Only scheduled UN raids use the normal enemy raid worker with an explicit faction.
        // Do not remove other factions from the storyteller's normal random selection.
        [HarmonyPatch(typeof(IncidentWorker_RaidEnemy),nameof(IncidentWorker_RaidEnemy.FactionCanBeGroupSource))]
        static class RandomRaidFilter
        {
            static void Postfix(Faction __0,ref bool __result){if(IsUN(__0))__result=false;}
        }
        [HarmonyPatch(typeof(FactionDialogMaker),nameof(FactionDialogMaker.FactionDialogFor))]
        static class ContactMenu
        {
            static DiaOption Back(Pawn pawn,Faction f)=>new DiaOption("GoBack".Translate()){linkLateBind=()=>FactionDialogMaker.FactionDialogFor(pawn,f)};
            static void Postfix(Pawn __0,Faction __1,DiaNode __result)
            {
                var pawn=__0;var f=__1;var comp=Instance;
                if(!IsUN(f)||__result==null||comp==null)return;
                var info=new DiaOption("UN_C_Report".Translate()){resolveTree=true,action=()=>Find.WindowStack.Add(new Dialog_UNJournal())};
                __result.options.Insert(0,info);
                var aid=new DiaOption("UN_C_Aid".Translate()){resolveTree=true,action=()=>comp.RequestAid(f,pawn.Map)};
                string block=comp.AidBlock(f,pawn.Map);
                if(block!=null)aid.Disable(block);
                __result.options.Insert(1,aid);
                var quest=new DiaOption("UN_C_Quests".Translate());
                var offers=Find.QuestManager.QuestsListForReading.Where(q=>!q.hidden&&!q.hiddenInUI&&q.State==QuestState.NotYetAccepted&&q.root!=null&&q.root.defName.StartsWith("UN_",StringComparison.Ordinal)).ToList();
                if(f.PlayerGoodwill<50)quest.Disable("UN_C_QuestLow".Translate());
                else if(offers.Count==0)quest.Disable("UN_C_NoQuests".Translate());
                else
                {
                    var node=new DiaNode("UN_C_QuestIntro".Translate());
                    foreach(var q in offers)
                    {
                        var current=q;
                        var detail=new DiaNode(current.description);
                        var accept=new DiaOption("UN_C_Accept".Translate()){resolveTree=true,action=()=>{
                            if(f.PlayerGoodwill>=50&&current.State==QuestState.NotYetAccepted&&!current.RequiresAccepter)current.Accept(null);
                            else Messages.Message("UN_C_QuestChanged".Translate(),MessageTypeDefOf.RejectInput,false);
                        }};
                        if(current.RequiresAccepter)accept.Disable("UN_C_QuestNeedsPawn".Translate());
                        detail.options.Add(accept);detail.options.Add(Back(pawn,f));
                        node.options.Add(new DiaOption(current.name){link=detail});
                    }
                    node.options.Add(Back(pawn,f));quest.link=node;
                }
                __result.options.Insert(2,quest);
            }
        }
    }
    public sealed class Dialog_UNJournal:Window
    {
        private Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(850,650);
        public Dialog_UNJournal(){doCloseX=true;doCloseButton=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect rect)
        {
            var un=Authorization.GetUN();var comp=UNContact.Instance;
            Text.Font=GameFont.Medium;
            Widgets.Label(new Rect(0,0,rect.width,30),"UN_C_Report".Translate());
            Text.Font=GameFont.Small;
            Widgets.Label(new Rect(0,34,rect.width,25),"UN_StatusLine".Translate(un==null?"—":un.PlayerGoodwill.ToString(),un?.leader?.LabelShortCap.ToString()??"—",Authorization.Granted?"UN_Yes".Translate():"UN_No".Translate()));
            Widgets.Label(new Rect(0,62,rect.width,25),comp?.RaidStatus(un)??"—");
            Widgets.Label(new Rect(0,90,rect.width,42),comp?.RaidStrengthStatus(un)??"—");
            Widgets.Label(new Rect(0,135,rect.width,25),"UN_C_HistoryTitle".Translate());
            var entries=comp?.Entries.ToList()??new List<string>();
            float width=rect.width-22;
            var heights=entries.Select(e=>Math.Max(28,Text.CalcHeight(e,width-8)+8)).ToList();
            Widgets.BeginScrollView(new Rect(0,165,rect.width,rect.height-215),ref scroll,new Rect(0,0,width,Math.Max(35,heights.Sum())));
            float y=0;
            if(entries.Count==0)Widgets.Label(new Rect(0,0,width,30),"UN_C_Empty".Translate());
            for(int i=0;i<entries.Count;i++){Widgets.Label(new Rect(0,y,width-8,heights[i]),entries[i]);y+=heights[i];}
            Widgets.EndScrollView();
        }
    }
}