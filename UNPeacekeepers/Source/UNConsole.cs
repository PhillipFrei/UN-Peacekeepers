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
            // This local dashboard is intentionally independent of power and negotiator jobs.
            yield return new Command_Action
            {
                groupable=true,
                defaultLabel="UN_Central_Title".Translate(),
                defaultDesc="UN_Central_ConsoleDescription".Translate(),
                icon=TexButton.Info,
                action=UNCentralWindow.Open
            };
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
