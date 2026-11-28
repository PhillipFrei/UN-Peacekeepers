using System;
using System.Collections.Generic;
namespace UNPeacekeepers
{
    public static class UNMissionPolicy
    {
        public const int RefillTicks=180000, InactivityTicks=600000, MaxActive=5;
        public static int RemainingAfterPlacement(int pending,int placed)=>Math.Max(0,pending-Math.Max(0,placed));
        // A native terminal state, acceptance and a proven objective are required for success.
        public static bool CanSettle(bool accepted,bool settled,bool endedSuccess,bool endedFailed,bool achieved)
            =>accepted&&!settled&&((endedSuccess&&achieved)||(!endedSuccess&&endedFailed));
        // A successful placement means the full requested amount was placed, even if a callback
        // was omitted. A failed placement can still have merged part of the stack.
        public static int RemainingAfterAttempt(int before,int callbackRemaining,int requested,bool placed)
            =>placed?Math.Min(callbackRemaining,RemainingAfterPlacement(before,requested)):callbackRemaining;
        public static bool CanCloseSite(bool reserved,bool playerHome,bool people,bool assets)
            =>!reserved&&!playerHome&&!people&&!assets;
        public static bool IsUnverifiedSiteItem(int originalCount,int currentCount)
            =>currentCount>0&&(originalCount<0||currentCount>originalCount);
        public const int GuestStayTicks=60000;
        public static bool UsesGuestStay(int kind)=>kind==1||kind==2||kind==3;
        public static bool GuestStayComplete(int since,int now)=>since>=0&&(long)now-since>=GuestStayTicks;
        public static int GuestTicksRemaining(int since,int now)=>since<0?GuestStayTicks:(int)Math.Max(0,Math.Min(GuestStayTicks,(long)since+GuestStayTicks-now));
        public static bool AllDeparted(int required,int departed)=>required>0&&departed==required;
        // A saved explicit outcome takes precedence over reconstruction; never turn a recorded failure into success.
        public static int RecordedOutcome(bool finished,bool achieved,bool delivered,bool shuttleConfirmed,bool guestsDeparted)
            =>finished?(achieved?1:-1):achieved||delivered||shuttleConfirmed||guestsDeparted?1:0;
        public static bool StoredDeliveryComplete(IList<int> required,IList<int> delivered,int goodsCount)
        {
            if(required==null||delivered==null||goodsCount<=0||required.Count!=goodsCount||delivered.Count!=goodsCount)return false;
            for(int i=0;i<goodsCount;i++)if(required[i]<=0||delivered[i]!=required[i])return false;
            return true;
        }
        public static bool LegacyPaymentUncertain(bool settled,bool silverQueued,bool unAttempted,bool supportedAttempted)
            =>settled&&!silverQueued&&!unAttempted&&!supportedAttempted;
        public static int RestoredRefillAt(int existing,int acceptedAt)
            =>Math.Max(existing,acceptedAt+RefillTicks);
        public static int Threshold(int tier)=>tier==2?80:tier==1?50:0;
        public static int Goodwill(int tier)=>tier==2?30:tier==1?20:10;
        public static int BaseSilver(int tier)=>tier==2?10000:tier==1?5000:1500;
        public static int SlotTier(int slot,int goodwill)=>goodwill<0?-1:slot<5?0:goodwill<50?0:slot==9&&goodwill>=80?2:1;
        public static int InactivityPenalty(int goodwill)=>goodwill<=50?0:-Math.Min(10,goodwill-50);
        public static int ExcessSilver(int goodwill,int earned)=>100*Math.Max(0,goodwill+earned-100);
        public static int ScaledSilver(int tier,float difficulty)=>(int)Math.Round(BaseSilver(tier)*Math.Max(.25f,Math.Min(5f,difficulty)),MidpointRounding.AwayFromZero);
    }
}
