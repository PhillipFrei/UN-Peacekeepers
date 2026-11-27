using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace UNPeacekeepers
{
    [HarmonyPatch(typeof(Building),nameof(Building.GetGizmos))]
    internal static class UNConsole
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result,Building __instance)
        {
            foreach(var gizmo in __result)yield return gizmo;
            if(!(__instance is Building_CommsConsole) || __instance.Faction!=Faction.OfPlayer)yield break;
            // Local reports/settings need neither a negotiator nor electricity; contact remains a pawn job.
            yield return new Command_Action{defaultLabel="UN_PrisonReport".Translate(),defaultDesc="UN_ConsoleLocal".Translate(),icon=TexButton.Info,action=()=>Find.WindowStack.Add(new MainTabWindow_UN())};
            yield return new Command_Action{defaultLabel="UN_C_Report".Translate(),defaultDesc="UN_ConsoleLocal".Translate(),icon=TexButton.Info,action=()=>Find.WindowStack.Add(new Dialog_UNJournal())};
            yield return new Command_Action{defaultLabel="UN_DirectSettings".Translate(),defaultDesc="UN_ConsoleLocal".Translate(),icon=TexButton.Info,action=()=>Find.WindowStack.Add(new Dialog_UNSettings())};
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
