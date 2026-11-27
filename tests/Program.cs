using System;
using UNPeacekeepers;
class Program
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Main()
    {
        // Exhaustive goodwill boundaries: empty and compliant prisons share recovery policy.
        for(int g=-100;g<=100;g++)
        {
            int next=g+UNRules.Recovery(g,false);
            Check(next==(g<0?Math.Min(0,g+10):g),"Recovery at "+g);
            Check(UNRules.Recovery(g,true)==0,"Violation must block recovery at "+g);
        }
        for(int count=0;count<100;count++)Check(UNRules.MixedHousingPenalty(count)==(count==0?0:-10),"Mixed penalty must not scale with prisoner count");
        Check(UNRules.AssignmentThreshold(false)==0,"Ordinary threshold");
        Check(UNRules.AssignmentThreshold(true)==50,"Advanced threshold");
        int[] tiers={-50,-65,-80,-90,-100};
        float[] multipliers={1.5f,2f,3f,4f,5f}, days={12f,9f,6f,4f,3f};
        for(int i=0;i<tiers.Length;i++)
        {
            Check(Math.Abs(UNRules.InterventionMultiplier(tiers[i])-multipliers[i])<0.0001f,"Multiplier at "+tiers[i]);
            Check(Math.Abs(UNRules.InterventionDays(tiers[i])-days[i])<0.0001f,"Days at "+tiers[i]);
        }
        for(int g=-100;g<=100;g++) Check(UNRules.InterventionActive(g)==(g<=-50),"Intervention boundary "+g);
        for(int g=-99;g<=-50;g++)
        {
            Check(UNRules.InterventionMultiplier(g)<UNRules.InterventionMultiplier(g-1),"Increasing strength at "+g);
            Check(UNRules.InterventionDays(g)>UNRules.InterventionDays(g-1),"Shorter interval at "+g);
        }
        Check(Math.Abs(UNRules.InterventionMultiplier(-57)-(1.5f+7f/15f*0.5f))<0.0001f,"Linear interpolation");
        Check(UNRules.InterventionDays(-95)==3.5f,"Linear days interpolation");
        Console.WriteLine("All recovery, housing, assignments and intervention rule assertions passed.");
    }
}
