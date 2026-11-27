using System;
namespace UNPeacekeepers
{
    public static class UNMissionPolicy
    {
        public const int RefillTicks=180000, InactivityTicks=600000, MaxActive=5;
        public static int RemainingAfterPlacement(int pending,int placed)=>Math.Max(0,pending-Math.Max(0,placed));
        public static int Threshold(int tier)=>tier==2?80:tier==1?50:0;
        public static int Goodwill(int tier)=>tier==2?30:tier==1?20:10;
        public static int BaseSilver(int tier)=>tier==2?10000:tier==1?5000:1500;
        public static int SlotTier(int slot,int goodwill)=>goodwill<0?-1:slot<5?0:goodwill<50?0:slot==9&&goodwill>=80?2:1;
        public static int InactivityPenalty(int goodwill)=>goodwill<=50?0:-Math.Min(10,goodwill-50);
        public static int ExcessSilver(int goodwill,int earned)=>100*Math.Max(0,goodwill+earned-100);
        public static int ScaledSilver(int tier,float difficulty)=>(int)Math.Round(BaseSilver(tier)*Math.Max(.25f,Math.Min(5f,difficulty)),MidpointRounding.AwayFromZero);
    }
}
