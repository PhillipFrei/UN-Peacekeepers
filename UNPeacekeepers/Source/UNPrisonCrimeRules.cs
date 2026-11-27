using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace UNPeacekeepers
{
    // String def names intentionally avoid cross-reference errors when optional mods are absent.
    public sealed class UNCrimeRuleDef : Def
    {
        public string eventKind;
        public string targetDefName;
        public int points=20;
    }

    internal static class UNPrisonCrimeRules
    {
        internal static UNCrimeRuleDef FindRule(string kind,string name)
        {
            if(string.IsNullOrEmpty(name))return null;
            return DefDatabase<UNCrimeRuleDef>.AllDefsListForReading
                .Where(r=>r.eventKind==kind && r.targetDefName==name && r.points>0)
                .OrderByDescending(r=>r.points).FirstOrDefault();
        }
        internal static bool Protected(Pawn pawn)=>Current.ProgramState==ProgramState.Playing
            && pawn!=null && !pawn.Dead && pawn.RaceProps.Humanlike && pawn.IsPrisonerOfColony;
        internal sealed class Event
        {
            public Pawn pawn;
            public UNCrimeRuleDef rule;
            public string targetLabel;
            public int tick;
        }
        internal static Event Capture(string kind,Def target,Pawn pawn)
        {
            if(!Protected(pawn))return null;
            var rule=FindRule(kind,target?.defName);
            return rule==null?null:new Event{pawn=pawn,rule=rule,targetLabel=target.label,tick=Find.TickManager.TicksGame};
        }
        internal static void RecordExperiment(Event e)
        {
            if(e==null)return;
            UNCrimeMonitor.RecordCrime(e.rule.points,"UNCrime_Experiment".Translate(e.pawn.LabelShortCap,e.targetLabel),
                "experiment:"+e.pawn.ThingID+":"+e.tick);
        }

        // Patch actual completed recipe worker invocations, including configured mod workers.
        // The base target ensures the patch class has a valid target even without optional DLC.
        [HarmonyPatch]
        static class RecipeExperiment
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var sig=new[]{typeof(Pawn),typeof(BodyPartRecord),typeof(Pawn),typeof(List<Thing>),typeof(Bill)};
                var methods=new HashSet<MethodBase>();
                var baseMethod=AccessTools.Method(typeof(RecipeWorker),nameof(RecipeWorker.ApplyOnPawn),sig);
                if(baseMethod!=null)methods.Add(baseMethod);
                foreach(var r in DefDatabase<UNCrimeRuleDef>.AllDefsListForReading.Where(x=>x.eventKind=="Recipe"))
                {
                    if(string.IsNullOrEmpty(r.targetDefName))continue;
                    var recipe=DefDatabase<RecipeDef>.GetNamedSilentFail(r.targetDefName);
                    if(recipe?.workerClass==null)continue;
                    var method=AccessTools.Method(recipe.workerClass,nameof(RecipeWorker.ApplyOnPawn),sig);
                    if(method!=null&&!method.IsAbstract)methods.Add(method);
                }
                return methods;
            }
            static void Prefix(RecipeWorker __instance,Pawn __0,Pawn __2,out Event __state)
            {
                __state=__2?.Faction==Faction.OfPlayer?Capture("Recipe",__instance.recipe,__0):null;
            }
            static void Postfix(Event __state)=>RecordExperiment(__state);
        }

        // A gene extraction is charged on Finish, not on insertion or cancellation.
        [HarmonyPatch(typeof(Building_GeneExtractor),"Finish")]
        static class Extraction
        {
            static bool Prepare()=>ModsConfig.BiotechActive;
            static void Prefix(Building_GeneExtractor __instance,out Event __state)
            {
                __state=__instance.Faction==Faction.OfPlayer
                    ?Capture("GeneExtraction",__instance.def,__instance.innerContainer.OfType<Pawn>().FirstOrDefault()):null;
            }
            static void Postfix(Event __state)=>RecordExperiment(__state);
        }

        // A completed softscan only. A cancelled loading operation is not an experiment.
        [HarmonyPatch(typeof(Building_SubcoreScanner),nameof(Building_SubcoreScanner.EjectContents))]
        static class Softscan
        {
            static bool Prepare()=>ModsConfig.BiotechActive;
            static void Prefix(Building_SubcoreScanner __instance,int ___fabricationTicksLeft,out Event __state)
            {
                __state=__instance.Faction==Faction.OfPlayer&&!__instance.DestroyOccupantBrain&&___fabricationTicksLeft<=0
                    ?Capture("SoftScan",__instance.def,__instance.Occupant):null;
            }
            static void Postfix(Event __state)=>RecordExperiment(__state);
        }

        // This method is also called on destructive cancellation/de-spawn; do not
        // count the separate execution-cut damage or count a completed scan twice.
        [HarmonyPatch(typeof(Building_SubcoreScanner),"KillOccupant")]
        static class Ripscan
        {
            static bool Prepare()=>ModsConfig.BiotechActive;
            static void Prefix(Building_SubcoreScanner __instance,out Event __state)
            {
                __state=__instance.Faction==Faction.OfPlayer&&__instance.DestroyOccupantBrain
                    ?Capture("RipScan",__instance.def,__instance.Occupant):null;
            }
            static void Postfix(Event __state)=>RecordExperiment(__state);
        }

        // Optional explicit, direct-target ability rules (not a blanket ban on psycasts).
        [HarmonyPatch(typeof(Ability),nameof(Ability.Activate),new[]{typeof(LocalTargetInfo),typeof(LocalTargetInfo)})]
        static class AbilityExperiment
        {
            static void Prefix(Ability __instance,LocalTargetInfo __0,out Event __state)
            {
                __state=__instance.pawn?.Faction==Faction.OfPlayer
                    ?Capture("Ability",__instance.def,__0.Thing as Pawn):null;
            }
            static void Postfix(bool __result,Event __state){if(__result)RecordExperiment(__state);}
        }

        // Only an explicitly identified lethal condition is accepted as the cause.
        // A concurrent hunger hediff, an unknown cause, violence or a coma is not proof.
        [HarmonyPatch(typeof(Pawn),nameof(Pawn.Kill),new[]{typeof(DamageInfo?),typeof(Hediff)})]
        static class NegligentDeath
        {
            static void Prefix(Pawn __instance,DamageInfo? __0,Hediff __1,out Event __state)
            {
                __state=null;
                if(!Protected(__instance)||__0.HasValue||__1==null||__1.pawn!=__instance
                    ||!__instance.health.hediffSet.hediffs.Contains(__1)||!__1.CauseDeathNow())return;
                __state=Capture("FatalHediff",__1.def,__instance);
            }
            static void Postfix(Pawn __instance,Event __state)
            {
                if(__state==null||!__instance.Dead)return;
                UNCrimeMonitor.RecordCrime(__state.rule.points,"UNCrime_NegligentDeath".Translate(__instance.LabelShortCap,__state.targetLabel),
                    "death:"+__instance.ThingID);
            }
        }
    }
}
