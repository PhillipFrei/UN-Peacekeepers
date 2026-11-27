using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace UNPeacekeepers
{
    public sealed class UNOwnedPollution:MapComponent
    {
        public HashSet<int> cells=new HashSet<int>();
        public UNOwnedPollution(Map map):base(map){}
        public override void ExposeData(){Scribe_Collections.Look(ref cells,"unOwnedPollutedCells",LookMode.Value);if(Scribe.mode==LoadSaveMode.PostLoadInit)cells=cells??new HashSet<int>();}
    }
    public sealed class UNPollutionMonitor:GameComponent
    {
        int nextAssessment=-1,nextCheck,wasteRemainder;
        public UNPollutionMonitor(Game game){}
        public static UNPollutionMonitor Instance=>Current.Game?.GetComponent<UNPollutionMonitor>();
        public float NextAssessmentDays=>nextAssessment<0?3f:Math.Max(0,nextAssessment-Find.TickManager.TicksGame)/60000f;
        public int OwnedCells=>ModsConfig.BiotechActive?Find.Maps.Where(m=>m.IsPlayerHome).Sum(m=>m.GetComponent<UNOwnedPollution>().cells.Count):0;
        public int EligibleCells=>ModsConfig.BiotechActive?Find.Maps.Where(m=>m.IsPlayerHome).Sum(m=>m.pollutionGrid.AllPollutableCells.Count):0;
        public float OwnedFraction=>EligibleCells==0?0f:OwnedCells/(float)EligibleCells;
        public int AssessmentPenalty=>UNCrimePolicy.PollutionPenalty(OwnedCells,EligibleCells);
        public string StatusText=>"UNCrime_PollutionStatus".Translate(OwnedCells,OwnedFraction.ToStringPercent(),AssessmentPenalty,wasteRemainder);
        public override void ExposeData(){Scribe_Values.Look(ref nextAssessment,"unPollutionNextAssessment",-1);Scribe_Values.Look(ref wasteRemainder,"unWasteRemainder",0);}
        public void WasteDumped(int amount)
        {
            int points=UNCrimePolicy.WastePenalty(ref wasteRemainder,amount);
            if(amount>0)PrisonMonitor.Instance?.BlockRecovery();
            if(points>0)UNCrimeMonitor.RecordCrime(points,"UNCrime_WasteDump".Translate(amount));
        }
        public override void GameComponentTick()
        {
            if(!ModsConfig.BiotechActive)return;
            int now=Find.TickManager.TicksGame;if(now<nextCheck)return;nextCheck=now+250;
            if(nextAssessment<0)nextAssessment=now+180000;
            foreach(var map in Find.Maps.Where(m=>m.IsPlayerHome))
                map.GetComponent<UNOwnedPollution>().cells.RemoveWhere(i=>i<0||i>=map.cellIndices.NumGridCells||!map.pollutionGrid.IsPolluted(map.cellIndices.IndexToCell(i)));
            if(OwnedCells>0)PrisonMonitor.Instance?.BlockRecovery();
            if(now<nextAssessment)return;
            nextAssessment=now+180000;
            if(AssessmentPenalty>0)UNCrimeMonitor.RecordCrime(AssessmentPenalty,"UNCrime_PollutionAssessment".Translate(OwnedFraction.ToStringPercent()));
        }
        // Context lifetime is the exact synchronous emission operation, never a global 'last player action'.
        [ThreadStatic] internal static bool ownEmission;
        [ThreadStatic] internal static bool weaponEmission;
        [ThreadStatic] internal static bool creatingOwnWaste;
        public static IDisposable OwnWeaponEmission(Thing source)=>new Emission(source?.Faction==Faction.OfPlayer,true);
        internal sealed class Emission:IDisposable
        {
            readonly bool oldOwn,oldWeapon;
            public Emission(bool own,bool weapon){oldOwn=ownEmission;oldWeapon=weaponEmission;ownEmission=own;weaponEmission=own&&weapon;}
            public void Dispose(){ownEmission=oldOwn;weaponEmission=oldWeapon;}
        }
        // Explosion.instigator is a serialized source reference; delayed radial cells stay attributable.
        [HarmonyPatch(typeof(Explosion),"AffectCell")]
        static class ExplosionPollution
        {
            static void Prefix(Explosion __instance,out Emission __state)
            {__state=new Emission(__instance.instigator?.Faction==Faction.OfPlayer,true);}
            static Exception Finalizer(Exception __exception,Emission __state)
            {__state?.Dispose();return __exception;}
        }
        [HarmonyPatch(typeof(PollutionGrid),nameof(PollutionGrid.SetPolluted))]
        static class GridChange
        {
            static void Prefix(PollutionGrid __instance,IntVec3 cell,out bool __state){__state=__instance.IsPolluted(cell);}
            static void Postfix(PollutionGrid __instance,Map ___map,IntVec3 cell,bool isPolluted,bool __state)
            {
                if(Current.ProgramState!=ProgramState.Playing||!cell.InBounds(___map))return;
                var owned=___map.GetComponent<UNOwnedPollution>();int i=___map.cellIndices.CellToIndex(cell);
                if(!isPolluted){owned.cells.Remove(i);return;}
                if(__state||!__instance.IsPolluted(cell)||!ownEmission)return;
                owned.cells.Add(i);PrisonMonitor.Instance?.BlockRecovery();
                if(weaponEmission)UNCrimeMonitor.RecordCrime(1,"UNCrime_WeaponPollution".Translate());
            }
        }
        [HarmonyPatch(typeof(CompPolluteOverTime),"Pollute")]
        static class MachinePollution
        {
            static void Prefix(CompPolluteOverTime __instance,out Emission __state){__state=new Emission(__instance.parent.Faction==Faction.OfPlayer,false);}
            static Exception Finalizer(Exception __exception,Emission __state){__state?.Dispose();return __exception;}
        }
        [HarmonyPatch(typeof(CompDissolutionEffect_Pollution),nameof(CompDissolutionEffect_Pollution.DoDissolutionEffectMap))]
        static class WasteDecay
        {
            static void Prefix(CompDissolutionEffect_Pollution __instance,out Emission __state){__state=new Emission(__instance.parent.TryGetComp<CompUNWasteOrigin>()?.owned==true,false);}
            static Exception Finalizer(Exception __exception,Emission __state){__state?.Dispose();return __exception;}
        }
        [HarmonyPatch(typeof(CompWasteProducer),nameof(CompWasteProducer.ProduceWaste))]
        static class WasteProduction
        {
            static void Prefix(CompWasteProducer __instance,out bool __state){__state=creatingOwnWaste;creatingOwnWaste=__instance.parent.Faction==Faction.OfPlayer;}
            static Exception Finalizer(Exception __exception,bool __state){creatingOwnWaste=__state;return __exception;}
        }
        [HarmonyPatch(typeof(CompPollutionPump),"Pump")]
        static class PumpWaste
        {
            static void Prefix(CompPollutionPump __instance,out bool __state){__state=creatingOwnWaste;creatingOwnWaste=__instance.parent.Faction==Faction.OfPlayer;}
            static Exception Finalizer(Exception __exception,bool __state){creatingOwnWaste=__state;return __exception;}
        }
    }
    public sealed class CompProperties_UNWasteOrigin:CompProperties
    {public CompProperties_UNWasteOrigin(){compClass=typeof(CompUNWasteOrigin);}}
    public sealed class CompUNWasteOrigin:ThingComp
    {
        public bool owned;
        bool abandoned;
        public override void PostPostMake(){owned=UNPollutionMonitor.creatingOwnWaste;}
        public override void PostExposeData(){Scribe_Values.Look(ref owned,"unWasteOwned",false);Scribe_Values.Look(ref abandoned,"unWasteAbandoned",false);}
        public override bool AllowStackWith(Thing other){var comp=other.TryGetComp<CompUNWasteOrigin>();return comp!=null&&comp.owned==owned&&comp.abandoned==abandoned;}
        public override void PostSplitOff(Thing piece){var comp=piece.TryGetComp<CompUNWasteOrigin>();if(comp!=null){comp.owned=owned;comp.abandoned=abandoned;}}
        public override void Notify_AbandonedAtTile(PlanetTile tile)
        {
            // This callback is the player's explicit caravan discard action. It is valid even for purchased waste.
            if(!abandoned){abandoned=true;owned=true;UNPollutionMonitor.Instance?.WasteDumped(parent.stackCount);}
        }
    }
}
