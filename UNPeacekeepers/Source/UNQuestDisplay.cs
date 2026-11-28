using System;
using RimWorld;
using Verse;

namespace UNPeacekeepers
{
    // Stable values for persisted diagnostic codes. Do not renumber.
    public enum UNMissionFailure
    {
        Unknown=0, Deadline=1, TargetLost=2, DestinationLost=3, ProtectionLost=4,
        ShuttleLost=5, Cancelled=6, Technical=7, AcceptanceInvalid=8, GuestDepartureUnverified=9
    }
    public static class UNQuestDisplay
    {
        public static string Time(int ticks)
        {
            int minutes=UNMissionPolicy.DisplayMinutes(ticks);
            return "UNQ_Time".Translate(minutes/1440,(minutes/60)%24,minutes%60);
        }
        public static string OfferTime(Quest q)
        {
            if(q==null||q.State!=QuestState.NotYetAccepted)return "";
            return UNMissionPolicy.HasOfferDeadline(q.acceptanceExpireTick)?
                "UNQ_AcceptUntil".Translate(Time(UNMissionPolicy.TicksUntil(q.acceptanceExpireTick,Find.TickManager.TicksGame))).ToString():
                "UNQ_NoAcceptanceDeadline".Translate().ToString();
        }
        public static string State(Quest q)
        {
            if(q==null)return "UNQ_Closed".Translate();
            if(q.State==QuestState.NotYetAccepted)return "UNQ_Offered".Translate();
            if(q.State==QuestState.Ongoing)return "UNQ_Active".Translate();
            if(q.State==QuestState.EndedSuccess)return "UNQ_Success".Translate();
            if(q.State==QuestState.EndedFailed)return "UNQ_Failed".Translate();
            return "UNQ_Closed".Translate();
        }
        public static string Header(Quest q,int remaining)
        {
            string text="UNQ_Status".Translate(State(q));
            if(q?.State==QuestState.Ongoing)text+="\n"+"UNQ_Remaining".Translate(Time(remaining));
            else if(q?.State==QuestState.NotYetAccepted)text+="\n"+OfferTime(q);
            return text;
        }
        public static string Next(string action)=>"UNQ_Next".Translate(action);
        public static string Failure(UNMissionFailure reason)
        {
            switch(reason)
            {
                case UNMissionFailure.Deadline:return "UNQ_ReasonDeadline".Translate();
                case UNMissionFailure.TargetLost:return "UNQ_ReasonTargetLost".Translate();
                case UNMissionFailure.DestinationLost:return "UNQ_ReasonDestinationLost".Translate();
                case UNMissionFailure.ProtectionLost:return "UNQ_ReasonProtectionLost".Translate();
                case UNMissionFailure.ShuttleLost:return "UNQ_ReasonShuttleLost".Translate();
                case UNMissionFailure.Cancelled:return "UNQ_ReasonCancelled".Translate();
                case UNMissionFailure.Technical:return "UNQ_ReasonTechnical".Translate();
                case UNMissionFailure.AcceptanceInvalid:return "UNQ_ReasonAcceptance".Translate();
                case UNMissionFailure.GuestDepartureUnverified:return "UNG_ReasonUnverified".Translate();
                default:return "UNQ_ReasonUnknown".Translate();
            }
        }
        public static string Failed(UNMissionFailure reason)=>"UNQ_Failure".Translate(Failure(reason));
    }
}
