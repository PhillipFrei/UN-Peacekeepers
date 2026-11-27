using System;
namespace UNPeacekeepers
{
    public static class UNRules
    {
        public static int Recovery(int goodwill,bool negative)=>negative||goodwill>=0?0:Math.Min(10,-goodwill);
        public static int MixedHousingPenalty(int eligibleMixedPrisoners)=>eligibleMixedPrisoners>0?-10:0;
        public static int AssignmentThreshold(bool advanced)=>advanced?50:0;
    }
}
