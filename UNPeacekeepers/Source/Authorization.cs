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
        static Bootstrap() { new Harmony("local.geneva.unpeacekeepers.authorization").PatchAll(); Log.Message("[UN Peacekeepers] Authorization 0.2.7 initialized."); }
    }
    public sealed class Authorization : GameComponent
    {
        private const int RequiredTicks = 15 * 60000;
        private int startTick = -1;
        private int reportedDay = -1;
        private bool granted;
        private bool isolationReady;
        public bool IsolationReady => isolationReady;
        private bool pendingNewGameGoodwill;
        private int nextMaintenanceTick = -1;
        public Authorization(Game game) { }
        public static Authorization Instance => Current.Game?.GetComponent<Authorization>();
        public static bool Granted => Instance?.granted == true;
        public float ProgressDays => granted ? 15f : startTick < 0 ? 0f : Math.Min(15f, Math.Max(0, Find.TickManager.TicksGame - startTick) / 60000f);
        public override void ExposeData()
        {
            Scribe_Values.Look(ref startTick, "unProbationStart", -1);
            Scribe_Values.Look(ref reportedDay, "unProbationReportedDay", -1);
            Scribe_Values.Look(ref isolationReady, "unIsolationReady", false);
            Scribe_Values.Look(ref granted, "unManufacturingGranted", false);
            Scribe_Values.Look(ref pendingNewGameGoodwill, "unPendingNewGameGoodwill", false);
        }
        public override void StartedNewGame() { pendingNewGameGoodwill = true; nextMaintenanceTick = -1; }
        public override void LoadedGame() { nextMaintenanceTick = -1; }
        public override void GameComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.FactionManager == null || Faction.OfPlayerSilentFail == null) return;
            int now = Find.TickManager.TicksGame;
            if (nextMaintenanceTick < 0 || now >= nextMaintenanceTick)
            {
                nextMaintenanceTick = now + 2500;
                MaintainFaction();
            }
            Observe();
        }
        public static Faction GetUN()
        {
            var def = DefDatabase<FactionDef>.GetNamedSilentFail("UN_Peacekeepers");
            return def == null ? null : Find.FactionManager.FirstFactionOfDef(def);
        }
        private void MaintainFaction()
        {
            var un = GetUN();
            if (un == null || un.defeated) return;
            if(!isolationReady)
            {
                // Snapshot the displayed old goodwill before dropping vanilla situational caps.
                int goodwill=un.PlayerGoodwill;
                un.RelationWith(Faction.OfPlayer).baseGoodwill=goodwill;
                Faction.OfPlayer.RelationWith(un).baseGoodwill=goodwill;
                isolationReady=true;
            }
            un.hidden=true;
            un.temporary=false;
            if (pendingNewGameGoodwill)
            {
                pendingNewGameGoodwill = false;
                // Only a new game's default neutral relation; never reset an existing save.
                if (un.PlayerGoodwill == 0 && !un.HostileTo(Faction.OfPlayer))
                    UNContact.ApplyPrisonGoodwill(un, Rand.RangeInclusive(0, 10), "UN_InitialGoodwill".Translate().ToString());
            }
            if (un.leader == null)
            {
                try
                {
                    if (un.TryGenerateNewLeader()) Log.Message("[UN Peacekeepers] Missing UN leader generated.");
                    else Log.WarningOnce("[UN Peacekeepers] Could not generate UN leader; will retry.", 82731431);
                }
                catch (Exception ex)
                {
                    Log.WarningOnce("[UN Peacekeepers] UN leader generation failed: " + ex, 82731432);
                }
            }
            if (granted)
            {
                var permit = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("UN_ManufacturingAuthorization");
                if (permit != null && !permit.IsFinished) Find.ResearchManager.FinishProject(permit, false, null, false);
            }
        }
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
        static void Postfix(Faction __instance, Faction __0)
        {
            var other = __0;
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
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    internal static class PreventPermitCompletion
    {
        static bool Prefix(ResearchProjectDef __0)
        {
            return __0 == null || __0.defName != "UN_ManufacturingAuthorization" || Authorization.Granted;
        }
    }
}
