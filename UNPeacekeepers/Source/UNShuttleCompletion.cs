using HarmonyLib;
using RimWorld;
using System.Linq;
namespace UNPeacekeepers
{
    // Vanilla's SentSatisfied is emitted before the physical launch is created.
    // Settle only after FlyAway has successfully finished that operation.
    [HarmonyPatch(typeof(ShipJob_FlyAway),nameof(ShipJob_FlyAway.TryStart))]
    internal static class UNShuttleCompletion
    {
        static void Postfix(ShipJob_FlyAway __instance,bool __result)
        {
            if(!__result)return;
            foreach(var mission in UNMissionSites.Active.ToList())mission.NotifyDeparted(__instance.transportShip);
            UNUltimatum.Instance?.Active?.NotifyDeparted(__instance.transportShip);
        }
    }
}
