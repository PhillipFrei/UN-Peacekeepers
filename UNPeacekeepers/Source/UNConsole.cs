using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace UNPeacekeepers
{
    public sealed class CompProperties_UNConsole : CompProperties
    { public CompProperties_UNConsole(){compClass=typeof(CompUNConsole);} }
    public sealed class CompUNConsole : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if(!(parent is Building_CommsConsole) || parent.Faction!=Faction.OfPlayer)yield break;
            // Local reports/settings need neither a negotiator nor electricity; contact remains a pawn job.
            yield return new Command_Action{groupable=false,defaultLabel="UN_PrisonReport".Translate(),defaultDesc="UN_ConsoleLocal".Translate(),icon=TexButton.Info,action=()=>Find.WindowStack.Add(new MainTabWindow_UN())};
            yield return new Command_Action{groupable=false,defaultLabel="UN_C_Report".Translate(),defaultDesc="UN_ConsoleLocal".Translate(),icon=TexButton.Info,action=()=>Find.WindowStack.Add(new Dialog_UNJournal())};
            yield return new Command_Action{groupable=false,defaultLabel="UN_DirectSettings".Translate(),defaultDesc="UN_ConsoleLocal".Translate(),icon=TexButton.Info,action=()=>Find.WindowStack.Add(new Dialog_UNSettings())};
        }
    }
    public sealed class Dialog_UNSettings:Window
    {
        public override Vector2 InitialSize=>new Vector2(650,430);
        public Dialog_UNSettings(){doCloseX=true;doCloseButton=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0,0,rect.width,30),"UN_DirectSettings".Translate());
            UNMod.Instance?.DoSettingsWindowContents(new Rect(0,40,rect.width,rect.height-90));
        }
        public override void PostClose(){UNMod.Instance?.WriteSettings();base.PostClose();}
    }
}
