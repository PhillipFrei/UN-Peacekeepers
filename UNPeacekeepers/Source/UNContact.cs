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
        internal static bool MutationAllowed => prisonContext != null;
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
            try { un.TryAffectGoodwillWith(Faction.OfPlayer,delta,false,false); SyncInterventionRelation(un); }
            finally { prisonContext=old; }
        }
        public void LogChange(int before,int after,string reason)
        {
            knownGoodwill=after;
            if(before==after && prisonContext==null)return;
            Add("UN_C_Entry".Translate((after-before).ToString("+0;-0;0"),before,after,reason).ToString());
        }
        public void LogEvent(string text)=>Add(text);
        private void Add(string text)
        {
            journal.Insert(0,"UN_C_Dated".Translate((Find.TickManager.TicksGame/(float)Day).ToString("0.0"),text).ToString());
            if(journal.Count>300)journal.RemoveAt(journal.Count-1);
        }
        public IEnumerable<string> Entries => journal;
        public static int Interval(int g) => Mathf.RoundToInt(UNRules.InterventionDays(g)*Day);
        public static float GoodwillMultiplier(int g) => UNRules.InterventionMultiplier(g);
        // Vanilla becomes hostile at -75. Synchronize only the UN/player relationship
        // to the agreed -50 boundary without changing goodwill or involving NPC diplomacy.
        private static void SyncInterventionRelation(Faction un)
        {
            if(Authorization.Instance?.IsolationReady!=true || Faction.OfPlayerSilentFail==null) return;
            var player=Faction.OfPlayer;
            var a=un.RelationWith(player);
            var b=player.RelationWith(un);
            if(UNRules.InterventionActive(un.PlayerGoodwill))
            {
                a.kind=FactionRelationKind.Hostile;
                b.kind=FactionRelationKind.Hostile;
            }
            else if(a.kind==FactionRelationKind.Hostile || b.kind==FactionRelationKind.Hostile)
            {
                var kind=un.PlayerGoodwill>=75 ? FactionRelationKind.Ally : FactionRelationKind.Neutral;
                a.kind=kind;
                b.kind=kind;
            }
        }
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
            if(un==null || un.defeated || un.deactivated || !UNRules.InterventionActive(un.PlayerGoodwill))
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
            if(!UNRules.InterventionActive(un.PlayerGoodwill))return "UN_C_NoRaid".Translate().ToString();
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
            SyncInterventionRelation(un);
            if(knownGoodwill==1000){knownGoodwill=g;Add("UN_C_Baseline".Translate(g).ToString());}
            else if(g!=knownGoodwill)LogChange(knownGoodwill,g,"UN_C_Unknown".Translate().ToString());
            if(!UNRules.InterventionActive(g))
            {nextRaid=-1;lastRaid=-1;escalationStart=-1;return;}
            if(escalationStart<0)
            {
                escalationStart=now;
                nextRaid=now+Day;
                Find.LetterStack.ReceiveLetter("UN_C_WarningTitle".Translate(),"UN_C_WarningText".Translate(g),LetterDefOf.ThreatBig);
            }
            if(lastRaid>=0)nextRaid=lastRaid+Interval(g);
            if(now<nextRaid)return;
            if(UNUltimatum.Instance?.CaptureOperationActive==true)return;
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
            static DiaOption AssignmentOption(Pawn pawn,Faction f,bool advanced)
            {
                return UNSupplyOrders.Option(pawn,f,advanced);
            }
            static void Postfix(Pawn __0,Faction __1,DiaNode __result)
            {
                var pawn=__0;var f=__1;var comp=Instance;
                if(!IsUN(f)||__result==null||comp==null)return;
                // Replace vanilla gift/trader/quest options for this faction only.
                __result.options.Clear();
                __result.options.Add(new DiaOption("UN_Central_Title".Translate()){resolveTree=true,action=()=>UNCentralWindow.Open()});
                var aid=new DiaOption("UN_C_Aid".Translate()){resolveTree=true,action=()=>comp.RequestAid(f,pawn.Map)};
                string block=comp.AidBlock(f,pawn.Map);
                if(block!=null)aid.Disable(block);
                __result.options.Add(aid);
                __result.options.Add(new DiaOption("UNM_Offers".Translate()){resolveTree=true,action=()=>UNMissionManager.Open(pawn)});
                // Retain only already accepted 0.2.9 orders; never generate legacy offers.
                var legacy=UNSupplyOrders.Active;
                if(legacy!=null)__result.options.Add(UNSupplyOrders.Option(pawn,f,legacy.advanced));
                __result.options.Add(new DiaOption("Disconnect".Translate()){resolveTree=true});
            }
        }
    }
    public sealed class Dialog_UNJournal:Window
    {
        private Vector2 scroll;
        private int page;
        private static float ColumnWidth(float width,int count,int index)
        {return count==3?width*(index==0?0.48f:index==1?0.22f:0.30f):count==2?width*(index==0?0.42f:0.58f):width;}
        public override Vector2 InitialSize=>new Vector2(900,700);
        public Dialog_UNJournal(){doCloseX=true;doCloseButton=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect rect)
        {
            var un=Authorization.GetUN();var comp=UNContact.Instance;
            Text.Font=GameFont.Medium;
            Widgets.Label(new Rect(0,0,rect.width,36),"UN_C_Report".Translate());
            Text.Font=GameFont.Small;
            for(int i=0;i<3;i++)
                if(Widgets.ButtonText(new Rect(i*rect.width/3,42,rect.width/3-5,30),(i==0?"UN029_Status":i==1?"UN029_Rules":"UN_C_HistoryTitle").Translate())){page=i;scroll=Vector2.zero;}
            var lines=new List<string>();
            if(page==0)
            {
                lines.Add("UN029_Goodwill".Translate(un==null?"—":un.PlayerGoodwill.ToString("+0;-0;0")));
                lines.Add(comp?.RaidStatus(un)??"—");
                lines.Add(comp?.RaidStrengthStatus(un)??"—");
                lines.Add(PrisonMonitor.Instance?.AssessmentStatus()??"—");
                lines.Add("UN029_Access".Translate());
                int g=un?.PlayerGoodwill??-100;
                lines.Add("UN_OrdinaryAssignments".Translate()+": "+(g>=0?"UN_Yes":"UN_No").Translate()+" (0)");
                lines.Add("UN_AdvancedAssignments".Translate()+": "+(g>=50?"UN_Yes":"UN_No").Translate()+" (+50)");
                lines.Add("UN_C_Aid".Translate()+": "+(comp?.AidBlock(un,Find.CurrentMap)??"UN_Yes".Translate().ToString()));
                lines.Add("UN_StatusLine".Translate(un==null?"—":g.ToString(),un?.leader?.LabelShortCap.ToString()??"—",Authorization.Granted?"UN_Yes".Translate():"UN_No".Translate()));
                lines.Add("UN_DaysLine".Translate((Authorization.Instance?.ProgressDays??0f).ToString("0.0")));
                lines.AddRange("UN029_AccessRules".Translate().ToString().Split('\n'));
                if(UNSupplyOrders.Active!=null)lines.Add(UNSupplyOrders.Active.DescriptionPart);
            }
            else if(page==1)
            {
                lines.AddRange("UN029_PrisonRules".Translate().ToString().Split('\n'));
                lines.AddRange("UN029_RecoveryRules".Translate().ToString().Split('\n'));
                lines.AddRange("UN029_AccessRules".Translate().ToString().Split('\n'));
            }
            else lines.AddRange(comp?.Entries??new[]{"UN_C_Empty".Translate().ToString()});
            float width=rect.width-24;
            var heights=lines.Select(e=>e.Split('|').Select((cell,j)=>Text.CalcHeight(cell,ColumnWidth(width,e.Split('|').Length,j)-16)+18).Max()).ToList();
            Widgets.BeginScrollView(new Rect(0,84,rect.width,rect.height-134),ref scroll,new Rect(0,0,width,Math.Max(35,heights.Sum())));
            float y=0;
            for(int i=0;i<lines.Count;i++)
            {
                if(i%2==0)Widgets.DrawHighlight(new Rect(0,y,width,heights[i]));
                var cells=lines[i].Split('|');float x=0;
                for(int j=0;j<cells.Length;j++)
                {
                    float cw=ColumnWidth(width,cells.Length,j);
                    Widgets.Label(new Rect(x+8,y+6,cw-16,heights[i]-6),cells[j]);x+=cw;
                }
                y+=heights[i];
            }
            Widgets.EndScrollView();
        }
    }
}
