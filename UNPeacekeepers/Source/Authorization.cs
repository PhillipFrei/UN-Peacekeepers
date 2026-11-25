using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace UNPeacekeepers
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap() { new Harmony("local.geneva.unpeacekeepers.authorization").PatchAll(); }
    }
    public sealed class Authorization : GameComponent
    {
        private const int RequiredTicks = 15 * 60000;
        private int startTick = -1;
        private int reportedDay = -1;
        private bool granted;
        public Authorization(Game game) { }
        public static Authorization Instance => Current.Game?.GetComponent<Authorization>();
        public static bool Granted => Instance?.granted == true;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref startTick, "unProbationStart", -1);
            Scribe_Values.Look(ref reportedDay, "unProbationReportedDay", -1);
            Scribe_Values.Look(ref granted, "unManufacturingGranted", false);
        }
        public override void GameComponentTick() { Observe(); }
        public void Observe()
        {
            if (granted || Current.ProgramState != ProgramState.Playing || Find.FactionManager == null || Faction.OfPlayerSilentFail == null) return;
            var def = DefDatabase<FactionDef>.GetNamedSilentFail("UN_Peacekeepers");
            var un = def == null ? null : Find.FactionManager.FirstFactionOfDef(def);
            int now = Find.TickManager.TicksGame;
            if (un == null || un.defeated || un.PlayerGoodwill != 100)
            {
                if (startTick >= 0) Messages.Message("UN_ProbationReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
                startTick = -1; reportedDay = -1; return;
            }
            if (startTick < 0 || startTick > now)
            {
                startTick = now; reportedDay = 0;
                Messages.Message("UN_ProbationStart".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            int elapsed = now - startTick;
            if (elapsed >= RequiredTicks)
            {
                var permit = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("UN_ManufacturingAuthorization");
                if (permit == null) return;
                granted = true;
                Find.ResearchManager.FinishProject(permit, false, null, false);
                Messages.Message("UN_ProbationGranted".Translate(), MessageTypeDefOf.PositiveEvent, false);
                return;
            }
            int day = elapsed / 60000;
            if (day > reportedDay)
            {
                reportedDay = day;
                Messages.Message("UN_ProbationProgress".Translate(day), MessageTypeDefOf.NeutralEvent, false);
            }
        }
        public static bool IsRestricted(RecipeDef recipe)
        {
            return recipe?.products != null && recipe.products.Any(p => p.thingDef != null &&
                (p.thingDef.defName == "UN_PeacekeeperArmor" || p.thingDef.defName == "UN_PeacekeeperHelmet" || p.thingDef.defName == "UN_ChargeRifle"));
        }
    }
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    internal static class GoodwillChanged
    {
        static void Postfix(Faction __instance, Faction other)
        {
            if (other == null || __instance == null) return;
            if ((__instance.IsPlayer && other.def.defName == "UN_Peacekeepers") || (other.IsPlayer && __instance.def.defName == "UN_Peacekeepers"))
                Authorization.Instance?.Observe();
        }
    }
    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AvailableNow), MethodType.Getter)]
    internal static class RecipeGate
    {
        static bool Prefix(RecipeDef __instance, ref bool __result)
        {
            if (!Authorization.IsRestricted(__instance) || Authorization.Granted) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    internal static class ExistingBillGate
    {
        static bool Prefix(Bill_Production __instance, ref bool __result)
        {
            if (!Authorization.IsRestricted(__instance.recipe) || Authorization.Granted) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.CanStartNow), MethodType.Getter)]
    internal static class PreventManualPermitResearch
    {
        static bool Prefix(ResearchProjectDef __instance, ref bool __result)
        {
            if (__instance.defName != "UN_ManufacturingAuthorization") return true;
            __result = false; return false;
        }
    }
}