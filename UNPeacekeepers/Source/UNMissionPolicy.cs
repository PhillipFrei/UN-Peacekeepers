using System;
using System.Collections.Generic;
namespace UNPeacekeepers
{
    public static class UNMissionPolicy
    {
        public const int RefillTicks=180000, InactivityTicks=600000, MaxActive=5;
        // 60,000 ticks = 24 game hours. Round UP so a live deadline never displays zero early.
        public static int RemainingTicks(int limit,int elapsed)
            =>(int)Math.Min(int.MaxValue,Math.Max(0L,(long)limit-Math.Max(0,elapsed)));
        public static int TicksUntil(int deadline,int now)
            =>(int)Math.Min(int.MaxValue,Math.Max(0L,(long)deadline-now));
        public static int DisplayMinutes(int ticks)
            =>(int)(((long)Math.Max(0,ticks)*1440+59999)/60000);
        public static bool HasOfferDeadline(int tick)=>tick>0&&tick<int.MaxValue;
        public static int RemainingAfterDropDispatch(int pending,bool dispatched)=>dispatched?0:Math.Max(0,pending);
        public static int RemainingAfterPlacement(int pending,int placed)=>Math.Max(0,pending-Math.Max(0,placed));
        // A native terminal state, acceptance and a proven objective are required for success.
        public static bool CanSettle(bool accepted,bool settled,bool endedSuccess,bool endedFailed,bool achieved)
            =>accepted&&!settled&&((endedSuccess&&achieved)||(!endedSuccess&&endedFailed));
        // A successful placement means the full requested amount was placed, even if a callback
        // was omitted. A failed placement can still have merged part of the stack.
        public static int RemainingAfterAttempt(int before,int callbackRemaining,int requested,bool placed)
            =>placed?Math.Min(callbackRemaining,RemainingAfterPlacement(before,requested)):callbackRemaining;
        // Loose cargo, loot and ordinary NPCs are intentionally not cleanup blockers.
        public static bool CanCloseSite(bool reserved,bool playerHome,bool protectedUnits,bool nativeBlocked)
            =>!reserved&&!playerHome&&!protectedUnits&&!nativeBlocked;
        public const int GuestStayTicks=60000, GuestExitGraceTicks=15000, GuestPickupRetryTicks=2500;
        public static int ValidGuestCountdown(int since,int now,bool eligible)
            =>eligible&&since>=0?GuestTicksRemaining(since,now):-1;
        public static int GroupGuestCountdown(int expectedRemaining,IEnumerable<int> countdowns)
        {
            if(expectedRemaining<0||countdowns==null)return -1;
            int count=0,max=0;
            foreach(int ticks in countdowns){if(ticks<0)return -1;count++;max=Math.Max(max,ticks);}
            return count==expectedRemaining?max:-1;
        }
        public static int ExitGraceRemaining(int since,int now)
            =>since<0?GuestExitGraceTicks:(int)Math.Max(0L,Math.Min(GuestExitGraceTicks,(long)since+GuestStayTicks+GuestExitGraceTicks-now));
        public static bool NeedsGuestPickup(int since,int now,bool eligible,bool downed,bool exitAvailable)
            =>eligible&&GuestStayComplete(since,now)&&(downed||!exitAvailable||ExitGraceRemaining(since,now)==0);
        public static bool CanConfirmGuestLaunch(bool active,bool aborting,bool beforeDeadline,bool allBoarded,bool allEligible,bool satisfied)
            =>active&&!aborting&&beforeDeadline&&allBoarded&&allEligible&&satisfied;
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
