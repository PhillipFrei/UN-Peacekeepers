using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace UNPeacekeepers
{
    // A live, local dashboard. It neither pauses nor consumes input outside its rectangle.
    public sealed class UNCentralWindow : Window
    {
        private int tab;
        private Vector2 scroll;
        private float contentHeight=1000f;
        private int nextRefresh=-1;
        private List<Pawn> prisoners=new List<Pawn>();
        private Dictionary<Pawn,List<KeyValuePair<string,int>>> inspections=new Dictionary<Pawn,List<KeyValuePair<string,int>>>();
        private bool mixed;
        private static readonly Color Blue=new Color(0.28f,0.61f,0.83f);
        private static readonly Color Green=new Color(0.32f,0.70f,0.47f);
        private static readonly Color Amber=new Color(0.85f,0.65f,0.28f);
        private static readonly Color Red=new Color(0.82f,0.30f,0.30f);
        private static readonly Color Muted=new Color(0.68f,0.73f,0.78f);
        public override Vector2 InitialSize=>new Vector2(Mathf.Min(900f,UI.screenWidth-80f),Mathf.Min(800f,UI.screenHeight-120f));
        public UNCentralWindow()
        {
            layer=WindowLayer.GameUI;
            doCloseX=true;
            draggable=true;
            forcePause=false;
            preventCameraMotion=false;
            absorbInputAroundWindow=false;
            closeOnClickedOutside=false;
            closeOnAccept=false;
            closeOnCancel=true;
            focusWhenOpened=false;
        }
        public static void Open()
        {
            if(Find.WindowStack.WindowOfType<UNCentralWindow>()==null)Find.WindowStack.Add(new UNCentralWindow());
        }
        private void Refresh()
        {
            int now=Find.TickManager.TicksGame;
            if(nextRefresh>=0 && now<nextRefresh)return;
            nextRefresh=now+250;
            prisoners=PrisonMonitor.Prisoners();
            inspections=prisoners.ToDictionary(p=>p,p=>PrisonMonitor.Inspect(p,prisoners));
            mixed=PrisonMonitor.HasMixedHousing(prisoners);
        }
        public override void DoWindowContents(Rect rect)
        {
            Refresh();
            Text.Font=GameFont.Medium;
            Widgets.Label(new Rect(0,0,rect.width-30,36),"UN_Central_Title".Translate());
            Text.Font=GameFont.Small;
            GUI.color=Muted;
            Widgets.Label(new Rect(0,38,rect.width,24),"UN_Central_Subtitle".Translate());
            GUI.color=Color.white;
            string[] tabs={"UN_Central_Overview","UN_Central_Prison","UN_Central_History","UN_Central_Rules"};
            float tabWidth=rect.width/tabs.Length;
            for(int i=0;i<tabs.Length;i++)
            {
                var area=new Rect(i*tabWidth,72,tabWidth-5,32);
                if(tab==i)Widgets.DrawBoxSolid(area,Blue*0.55f);
                if(Widgets.ButtonText(area,tabs[i].Translate())){tab=i;scroll=Vector2.zero;contentHeight=1000f;}
            }
            var viewport=new Rect(0,118,rect.width,rect.height-118);
            float width=viewport.width-22;
            Widgets.BeginScrollView(viewport,ref scroll,new Rect(0,0,width,Math.Max(viewport.height,contentHeight)));
            float y=0;
            if(tab==0)Overview(width,ref y);
            else if(tab==1)Prison(width,ref y);
            else if(tab==2)History(width,ref y);
            else Rules(width,ref y);
            Widgets.EndScrollView();
            contentHeight=y+10;
            Text.Font=GameFont.Small;
            Text.Anchor=TextAnchor.UpperLeft;
            GUI.color=Color.white;
        }
        private static void Label(float width,ref float y,string text,Color? color=null,GameFont font=GameFont.Small)
        {
            Text.Font=font;
            float height=Math.Max(25,Text.CalcHeight(text,width));
            GUI.color=color??Color.white;
            Widgets.Label(new Rect(0,y,width,height),text);
            GUI.color=Color.white;
            Text.Font=GameFont.Small;
            y+=height+8;
        }
        private static void Section(float width,ref float y,string text)
        {
            y+=10;
            Widgets.DrawLineHorizontal(0,y,width);y+=12;
            Label(width,ref y,text,null,GameFont.Medium);
        }
        private static void Pair(float width,ref float y,string label,string value,Color? color=null)
        {
            float right=width*0.34f,left=width-right-16;
            float height=Math.Max(28,Math.Max(Text.CalcHeight(label,left),Text.CalcHeight(value,right)));
            Widgets.Label(new Rect(0,y,left,height),label);
            Text.Anchor=TextAnchor.UpperRight;
            GUI.color=color??Color.white;
            Widgets.Label(new Rect(width-right,y,right,height),value);
            GUI.color=Color.white;Text.Anchor=TextAnchor.UpperLeft;
            y+=height+8;
        }
        private void Overview(float width,ref float y)
        {
            var un=Authorization.GetUN();
            if(un==null || un.defeated || un.deactivated){Label(width,ref y,"UN_C_Unavailable".Translate(),Amber);return;}
            int goodwill=un.PlayerGoodwill;
            var color=goodwill<=-50?Red:goodwill<0?Amber:goodwill<50?Blue:Green;
            Text.Anchor=TextAnchor.UpperCenter;
            Label(width,ref y,"UN_Central_Goodwill".Translate(),Muted);
            Text.Font=GameFont.Medium;
            GUI.color=color;
            Widgets.Label(new Rect(0,y,width,52),"<size=40>"+goodwill.ToString("+0;-0;0")+"</size>");y+=56;
            GUI.color=Color.white;Text.Font=GameFont.Small;
            Label(width,ref y,Status(goodwill),color,GameFont.Medium);
            if(!UNRules.InterventionActive(goodwill))Label(width,ref y,"UN_C_NoRaid".Translate(),Muted);
            Text.Anchor=TextAnchor.UpperLeft;
            Scale(width,ref y,goodwill);
            Section(width,ref y,"UN_Central_Action".Translate());
            int count=inspections.Count(p=>p.Value.Any(v=>v.Value<0));
            if(mixed)Pair(width,ref y,"UN_Central_MixedAction".Translate(),"−10",Red);
            if(count>0)Pair(width,ref y,"UN_Central_PrisonAction".Translate(count),inspections.Values.Sum(items=>items.Sum(v=>v.Value)).ToString("+0;-0;0"),Red);
            if(!mixed && count==0)Label(width,ref y,"UN_Central_NoPrisonAction".Translate(),Green);
            var pollution=UNPollutionMonitor.Instance;
            if(ModsConfig.BiotechActive && pollution!=null && pollution.AssessmentPenalty>0)
                Pair(width,ref y,"UN_Central_EnvironmentAction".Translate(),(-pollution.AssessmentPenalty).ToString(),Red);
            var monitor=PrisonMonitor.Instance;
            if(monitor!=null)
            {
                Pair(width,ref y,"UN_Central_Next".Translate(),"UN_Central_Days".Translate(monitor.NextAssessmentDays.ToString("0.00")));
                int forecast=monitor.ExpectedAssessmentDelta;
                Pair(width,ref y,"UN_Central_Forecast".Translate(),forecast.ToString("+0;-0;0"),forecast<0?Red:forecast>0?Green:Muted);
                Label(width,ref y,"UN_Central_ForecastNote".Translate(),Muted);
            }
            if(goodwill>50 && UNMissionManager.Instance!=null)
                Pair(width,ref y,"UN_Central_InactivityDue".Translate(),"UN_Central_Days".Translate(UNMissionManager.Instance.InactivityDays.ToString("0.00")),Amber);
            if(UNRules.InterventionActive(goodwill))
            {
                Section(width,ref y,"UN_Central_Intervention".Translate());
                Label(width,ref y,UNContact.Instance?.RaidStatus(un)??"—",Red);
                Label(width,ref y,UNContact.Instance?.RaidStrengthStatus(un)??"—");
            }
            Section(width,ref y,"UN_Central_Access".Translate());
            Permission(width,ref y,"UN_OrdinaryAssignments".Translate(),goodwill>=0,"0");
            Permission(width,ref y,"UN_AdvancedAssignments".Translate(),goodwill>=50,"+50");
            Permission(width,ref y,"UN_C_Aid".Translate(),goodwill>=75 && un.PlayerRelationKind==FactionRelationKind.Ally,"+75 · "+"UN_Central_Alliance".Translate());
            Permission(width,ref y,"UN_Central_Elite".Translate(),goodwill>=80,"+80");
            Permission(width,ref y,"UN_Central_Manufacturing".Translate(),Authorization.Granted,"+100 · "+"UN_Central_15Days".Translate());
            if(goodwill==100 && !Authorization.Granted)
                Label(width,ref y,"UN_DaysLine".Translate((Authorization.Instance?.ProgressDays??0f).ToString("0.0")),Amber);
            if(Authorization.Granted)Label(width,ref y,"UN_Central_Permanent".Translate(),Green);
            Section(width,ref y,"UN_Central_Leadership".Translate());
            var ultimatum=UNUltimatum.Instance;
            if(ultimatum!=null)
            {
                Label(width,ref y,ultimatum.StatusText,ultimatum.HasActive?Amber:Muted);
                if(Widgets.ButtonText(new Rect(0,y,Math.Min(320,width),30),"UN_Central_SelectLeader".Translate()))ultimatum.SelectLeaderMenu();
                y+=38;
            }
            Section(width,ref y,"UN_Central_ActiveMissions".Translate());
            var active=Find.QuestManager.QuestsListForReading.Where(q=>q.State==QuestState.Ongoing && q.root!=null && q.root.defName.StartsWith("UN_",StringComparison.Ordinal)).ToList();
            if(active.Count==0)Label(width,ref y,"UN_Central_NoMissions".Translate(),Muted);
            foreach(var quest in active)
            {
                Label(width,ref y,"• "+quest.name,null,GameFont.Medium);
                var mission=quest.GetFirstPartOfType<UNMissionObjective>();
                var legacy=quest.GetFirstPartOfType<QuestPart_UNSupply>();
                var ultimatumPart=quest.GetFirstPartOfType<QuestPart_UNUltimatum>();
                // The detailed mission pool renders all objective conditions; the overview shows the live status.
                if(mission!=null)
                {
                    Label(width,ref y,UNQuestDisplay.Header(quest,mission.RemainingTicks),Muted);
                    var guestSummary=mission.GuestSummary();
                    if(!guestSummary.NullOrEmpty())Label(width,ref y,guestSummary,Muted);
                }
                else if(legacy!=null)Label(width,ref y,legacy.DescriptionPart,Muted);
                else if(ultimatumPart!=null)Label(width,ref y,ultimatumPart.DescriptionPart,Muted);
            }
            if(Widgets.ButtonText(new Rect(0,y,Math.Min(320,width),30),"UN_Central_MissionPool".Translate()))UNMissionManager.Open();
            y+=38;
        }
        private static string Status(int goodwill)
        {
            return (goodwill<=-50?"UN_Central_StatusIntervention":goodwill<0?"UN_Central_StatusProbation":goodwill<50?"UN_Central_StatusCooperation":goodwill<80?"UN_Central_StatusTrusted":"UN_Central_StatusPartner").Translate();
        }
        private static void Permission(float width,ref float y,string name,bool unlocked,string threshold)
        {
            Pair(width,ref y,(unlocked?"✓ ":"— ")+name,"UN_Central_From".Translate(threshold),unlocked?Green:Muted);
        }
        private static void Scale(float width,ref float y,int goodwill)
        {
            y+=10;float x=18,w=width-36;
            Color[] colors={Red,Amber,Blue,Green};
            for(int i=0;i<4;i++)Widgets.DrawBoxSolid(new Rect(x+i*w/4,y,w/4,12),colors[i]);
            float position=x+(Mathf.Clamp(goodwill,-100,100)+100)/200f*w;
            Widgets.DrawBoxSolid(new Rect(position-1.5f,y-7,3,26),Color.white);
            y+=21;
            Text.Font=GameFont.Tiny;
            Text.Anchor=TextAnchor.UpperCenter;
            for(int i=0;i<5;i++)Widgets.Label(new Rect(x+i*w/4-25,y,50,22),(i*50-100).ToString("+0;-0;0"));
            Text.Anchor=TextAnchor.UpperLeft;Text.Font=GameFont.Small;y+=25;
        }
        private void Prison(float width,ref float y)
        {
            Label(width,ref y,"UN_Count".Translate(prisoners.Count),null,GameFont.Medium);
            Label(width,ref y,(mixed?"UN029_MixedBad":"UN029_MixedOK").Translate(),mixed?Red:Green);
            var monitor=PrisonMonitor.Instance;
            if(monitor!=null)Pair(width,ref y,"UN_Central_Next".Translate(),"UN_Central_Days".Translate(monitor.NextAssessmentDays.ToString("0.00")));
            if(ModsConfig.BiotechActive && UNPollutionMonitor.Instance!=null)
            {
                Section(width,ref y,"UN_Central_Environment".Translate());
                var pollution=UNPollutionMonitor.Instance;
                Label(width,ref y,pollution.StatusText,pollution.AssessmentPenalty>0?Amber:Green);
                Pair(width,ref y,"UN_Central_NextEnvironment".Translate(),"UN_Central_Days".Translate(pollution.NextAssessmentDays.ToString("0.00")));
                Label(width,ref y,"UN_Central_EnvironmentNote".Translate(),Muted);
            }
            if(prisoners.Count==0)Label(width,ref y,"UN_Empty".Translate(),Muted);
            foreach(var pawn in prisoners)
            {
                if(!PrisonMonitor.Eligible(pawn))continue;
                Section(width,ref y,pawn.LabelShortCap+" · "+pawn.gender.GetLabel());
                Pair(width,ref y,"UN_Central_NextIndividual".Translate(),"UN_Central_Days".Translate((monitor?.DaysUntil(pawn)??3f).ToString("0.00")));
                Bar(width,ref y,"UN_Central_Health".Translate(),pawn.health.summaryHealth.SummaryHealthPercent,Green);
                if(pawn.needs?.food!=null)Bar(width,ref y,"UN_Central_Food".Translate(),pawn.needs.food.CurLevelPercentage,pawn.needs.food.CurLevelPercentage<0.25f?Red:Blue);
                var bed=RestUtility.FindBedFor(pawn);
                Label(width,ref y,"UN_Central_Bed".Translate(bed?.LabelCap.ToString()??"UN_Central_NoBed".Translate().ToString()),Muted);
                foreach(var issue in inspections[pawn])Pair(width,ref y,issue.Key,issue.Value.ToString("+0;-0;0"),issue.Value<0?Red:Green);
            }
        }
        private static void Bar(float width,ref float y,string label,float fraction,Color color)
        {
            fraction=Mathf.Clamp01(fraction);
            float labelWidth=width*0.28f;
            Widgets.Label(new Rect(0,y,labelWidth,25),label);
            float x=labelWidth+8,w=width-x-55;
            Widgets.DrawBoxSolid(new Rect(x,y+4,w,16),new Color(0.18f,0.20f,0.23f));
            Widgets.DrawBoxSolid(new Rect(x,y+4,w*fraction,16),color);
            Text.Anchor=TextAnchor.UpperRight;
            Widgets.Label(new Rect(width-50,y,50,25),fraction.ToStringPercent());
            Text.Anchor=TextAnchor.UpperLeft;y+=32;
        }
        private static void History(float width,ref float y)
        {
            var entries=UNContact.Instance?.Entries.ToList()??new List<string>();
            if(entries.Count==0)Label(width,ref y,"UN_C_Empty".Translate(),Muted);
            for(int i=0;i<entries.Count;i++)
            {
                float height=Math.Max(28,Text.CalcHeight(entries[i],width-16))+14;
                if(i%2==0)Widgets.DrawHighlight(new Rect(0,y,width,height));
                Widgets.Label(new Rect(8,y+7,width-16,height-7),entries[i]);y+=height;
            }
        }
        private static void Rules(float width,ref float y)
        {
            foreach(string key in new[]{"UN029_PrisonRules","UN029_RecoveryRules","UN_Central_ActivityRules","UNCrime_Rules","UN_Central_AccessRules","UN_Central_InterventionRules"})
            {
                foreach(var line in key.Translate().ToString().Split('\n'))
                {
                    if(string.IsNullOrWhiteSpace(line)){y+=7;continue;}
                    if(!line.Contains("|")){Label(width,ref y,line);continue;}
                    var cells=line.Split('|');float x=0;
                    float[] fractions=cells.Length==3?new[]{0.48f,0.22f,0.30f}:new[]{0.42f,0.58f};
                    float height=0;
                    for(int i=0;i<cells.Length;i++)height=Math.Max(height,Text.CalcHeight(cells[i],width*fractions[i]-16));
                    height+=18;
                    Widgets.DrawHighlight(new Rect(0,y,width,height));
                    for(int i=0;i<cells.Length;i++)
                    {
                        float cw=width*fractions[i];
                        Widgets.Label(new Rect(x+8,y+7,cw-16,height-7),cells[i]);x+=cw;
                    }
                    y+=height+2;
                }
                y+=16;
            }
        }
    }
}
