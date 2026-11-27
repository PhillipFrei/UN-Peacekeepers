using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace UNPeacekeepers
{
    // Hidden excludes UN from vanilla visible/random diplomacy pools. Never temporary:
    // temporary factions lose goodwill and are eligible for automatic faction deletion.
    public static class UNIsolation
    {
        internal static bool Pair(Faction a,Faction b)=>UNContact.IsUN(a)||UNContact.IsUN(b);
        internal static bool PlayerPair(Faction a,Faction b)=>
            (UNContact.IsUN(a)&&b?.IsPlayer==true)||(a?.IsPlayer==true&&UNContact.IsUN(b));
        internal static bool Protected(WorldObject obj)=>Current.ProgramState==ProgramState.Playing &&
            obj is Settlement && UNContact.IsUN(obj.Faction) && !obj.Faction.defeated && obj.Spawned;

        [HarmonyPatch(typeof(Faction),nameof(Faction.HasGoodwill),MethodType.Getter)]
        static class KeepGoodwill
        { static void Postfix(Faction __instance,ref bool __result){if(UNContact.IsUN(__instance))__result=true;} }

        [HarmonyPatch(typeof(Faction),"ShouldHaveLeader",MethodType.Getter)]
        static class KeepLeader
        { static void Postfix(Faction __instance,ref bool __result){if(UNContact.IsUN(__instance))__result=true;} }

        [HarmonyPatch(typeof(Faction),nameof(Faction.CanChangeGoodwillFor))]
        static class GoodwillGate
        {
            static bool Prefix(Faction __instance,Faction __0,ref bool __result)
            {
                if(!Pair(__instance,__0))return true;
                __result=UNContact.MutationAllowed && PlayerPair(__instance,__0) && !__instance.defeated && !__0.defeated;
                return false;
            }
        }
        [HarmonyPatch(typeof(Faction),nameof(Faction.TryAffectGoodwillWith))]
        static class MutationGate
        {
            static bool Prefix(Faction __instance,Faction __0,ref bool __result)
            {
                if(!Pair(__instance,__0)|| (UNContact.MutationAllowed && PlayerPair(__instance,__0)))return true;
                __result=false;return false;
            }
        }
        // Vanilla amplifies changes away from natural goodwill by 25%; the agreed scores are exact.
        [HarmonyPatch(typeof(Faction),nameof(Faction.CalculateAdjustedGoodwillChange))]
        static class ExactScores
        {
            static bool Prefix(Faction __instance,Faction __0,int __1,ref int __result)
            {if(!Pair(__instance,__0))return true;__result=__1;return false;}
        }
        [HarmonyPatch(typeof(Faction),"CheckReachNaturalGoodwill")]
        static class NoDrift
        { static bool Prefix(Faction __instance)=>!UNContact.IsUN(__instance); }

        [HarmonyPatch(typeof(Faction),nameof(Faction.GoodwillWith))]
        static class NoSituationalCaps
        {
            static bool Prefix(Faction __instance,Faction __0,ref int __result)
            {
                if(!PlayerPair(__instance,__0)||Authorization.Instance?.IsolationReady!=true)return true;
                __result=__instance.BaseGoodwillWith(__0);return false;
            }
        }
        [HarmonyPatch(typeof(Faction),nameof(Faction.SetRelationDirect))]
        static class NoExternalWar
        { static bool Prefix(Faction __instance,Faction __0)=>!Pair(__instance,__0); }

        [HarmonyPatch(typeof(Building_CommsConsole),nameof(Building_CommsConsole.GetCommTargets))]
        static class RestoreContact
        {
            static IEnumerable<ICommunicable> Postfix(IEnumerable<ICommunicable> __result)
            {
                var un=Authorization.GetUN();bool found=false;
                foreach(var target in __result){if(ReferenceEquals(target,un))found=true;yield return target;}
                if(!found&&un!=null&&!un.defeated&&!un.deactivated)yield return un;
            }
        }
        [HarmonyPatch(typeof(SettlementDefeatUtility),nameof(SettlementDefeatUtility.CheckDefeated))]
        static class NoConquest
        { static bool Prefix(Settlement __0)=>!Protected(__0); }
        [HarmonyPatch(typeof(WorldObject),nameof(WorldObject.SetFaction))]
        static class NoTransfer
        { static bool Prefix(WorldObject __instance,Faction __0)=>!Protected(__instance)||__instance.Faction==__0; }
        [HarmonyPatch(typeof(WorldObject),nameof(WorldObject.Destroy))]
        static class NoDestroy
        { static bool Prefix(WorldObject __instance)=>!Protected(__instance); }
        [HarmonyPatch(typeof(WorldObjectsHolder),nameof(WorldObjectsHolder.Remove))]
        static class NoRemoval
        { static bool Prefix(WorldObject __0)=>!Protected(__0); }
    }
}
