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
        Console.WriteLine("504 rule assertions passed.");
    }
}
