using System;
namespace UNPeacekeepers
{
    public static class UNRules
    {
        public static int Recovery(int goodwill,bool negative)=>negative||goodwill>=0?0:Math.Min(10,-goodwill);
        public static int MixedHousingPenalty(int eligibleMixedPrisoners)=>eligibleMixedPrisoners>0?-10:0;
        public const int InterventionThreshold=-50;
        public static bool InterventionActive(int goodwill)=>goodwill<=InterventionThreshold;
        private static readonly int[] severity={50,65,80,90,100};
        private static readonly float[] multipliers={1.5f,2f,3f,4f,5f};
        private static readonly float[] days={12f,9f,6f,4f,3f};
        private static float Interpolate(int goodwill,float[] values)
        {
            int g=Math.Max(50,Math.Min(100,-goodwill));
            for(int i=0;i<severity.Length-1;i++)
            {
                if(g<=severity[i+1])
                {
                    float t=(g-severity[i])/(float)(severity[i+1]-severity[i]);
                    return values[i]+(values[i+1]-values[i])*t;
                }
            }
            return values[values.Length-1];
        }
        public static float InterventionMultiplier(int goodwill)=>Interpolate(goodwill,multipliers);
        public static float InterventionDays(int goodwill)=>Interpolate(goodwill,days);
        public static int AssignmentThreshold(bool advanced)=>advanced?50:0;
    }
}
