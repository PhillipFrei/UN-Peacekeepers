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
        foreach(int required in new[]{30,40})
        {
            var delivery=new UNSupplyProgress();
            Check(!delivery.TryClaim(required),"No reward without goods");
            foreach(int stack in new[]{7,0,12})delivery.Delivered+=delivery.TakeCount(required,stack);
            Check(delivery.Delivered==19 && !delivery.TryClaim(required),"Partial delivery does not pay");
            // Simulate a save/reload using the two persisted values.
            delivery=new UNSupplyProgress{Delivered=delivery.Delivered,Rewarded=delivery.Rewarded};
            int take=delivery.TakeCount(required,75);
            Check(take==required-19,"Only the missing goods are removed after reload");
            delivery.Delivered+=take;
            Check(delivery.TryClaim(required),"Completed objective pays");
            Check(!delivery.TryClaim(required) && delivery.TakeCount(required,75)==0,"Repeated callback cannot pay or consume again");
            delivery=new UNSupplyProgress{Delivered=delivery.Delivered,Rewarded=delivery.Rewarded};
            Check(!delivery.TryClaim(required),"Reward guard survives reload");
        }
        Check(!UNSupplyProgress.Expired(599999) && UNSupplyProgress.Expired(600000),"Deadline boundary");
        for(int goodwill=-100;goodwill<=100;goodwill++)
        {
            int[] counts=new int[3];
            for(int slot=0;slot<10;slot++){int tier=UNMissionPolicy.SlotTier(slot,goodwill);if(tier>=0)counts[tier]++;}
            Check(counts[0]==(goodwill<0?0:goodwill<50?10:5),"Ordinary pool count at "+goodwill);
            Check(counts[1]==(goodwill<50?0:goodwill<80?5:4),"Advanced pool count at "+goodwill);
            Check(counts[2]==(goodwill<80?0:1),"Elite pool count at "+goodwill);
            int penalty=UNMissionPolicy.InactivityPenalty(goodwill);
            Check(penalty<=0&&penalty>=-10,"Inactivity magnitude");
            Check(goodwill<=50?penalty==0:goodwill+penalty>=50,"Inactivity floor");
        }
        Check(UNMissionPolicy.ExcessSilver(95,20)==1500,"+95 plus 20 pays 1500 overflow");
        Check(UNMissionPolicy.ExcessSilver(100,10)+UNMissionPolicy.ScaledSilver(0,1)==2500,"Ordinary at cap");
        Check(UNMissionPolicy.ExcessSilver(100,20)+UNMissionPolicy.ScaledSilver(1,1)==7000,"Advanced at cap");
        Check(UNMissionPolicy.ExcessSilver(100,30)+UNMissionPolicy.ScaledSilver(2,1)==13000,"Elite at cap");
        Check(UNMissionPolicy.ScaledSilver(1,5)==25000&&UNMissionPolicy.ExcessSilver(95,20)==1500,"Difficulty scales regular silver only");
        // Native End must be confirmed before rewards; duplicate callbacks and partial objectives never pay.
        foreach(bool accepted in new[]{false,true})
        foreach(bool settled in new[]{false,true})
        foreach(bool achieved in new[]{false,true})
        {
            Check(!UNMissionPolicy.CanSettle(accepted,settled,false,false,achieved),"Ongoing quest never settles");
            Check(UNMissionPolicy.CanSettle(accepted,settled,true,false,achieved)==(accepted&&!settled&&achieved),"Success requires accepted proven objective");
            Check(UNMissionPolicy.CanSettle(accepted,settled,false,true,achieved)==(accepted&&!settled),"Failure settles once");
        }
        Check(UNMissionPolicy.RemainingAfterAttempt(1500,1000,500,true)==1000,"Placement callback not counted twice");
        Check(UNMissionPolicy.RemainingAfterAttempt(1500,1500,500,true)==1000,"Successful placement without callback");
        Check(UNMissionPolicy.RemainingAfterAttempt(1500,1350,500,false)==1350,"Partial merge on failure");
        Check(UNMissionPolicy.RemainingAfterAttempt(1500,1500,500,false)==1500,"Blocked placement preserves balance");
        // All independent cleanup blockers must be absent, including pending follow-up offers.
        for(int mask=0;mask<16;mask++)
            Check(UNMissionPolicy.CanCloseSite((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0)==(mask==0),"Cleanup blockers "+mask);
        Check(UNMissionPolicy.IsUnverifiedSiteItem(-1,1),"Unknown or newly imported cargo blocks removal");
        Check(UNMissionPolicy.IsUnverifiedSiteItem(10,11),"Merged imported cargo blocks removal");
        Check(!UNMissionPolicy.IsUnverifiedSiteItem(10,10)&&!UNMissionPolicy.IsUnverifiedSiteItem(10,5),"Original unchanged or consumed cargo does not block");
        Check(!UNMissionPolicy.IsUnverifiedSiteItem(-1,0),"No remaining item");
        // Failure after merging some silver must leave only the undelivered remainder.
        int pending=1500, physical=0;
        foreach(int actual in new[]{120,380}){pending=UNMissionPolicy.RemainingAfterPlacement(pending,actual);physical+=actual;}
        Check(pending==1000&&physical==500,"Partly failed placement ledger");
        int restoredPending=pending;
        foreach(int actual in new[]{500,500}){restoredPending=UNMissionPolicy.RemainingAfterPlacement(restoredPending,actual);physical+=actual;}
        Check(restoredPending==0&&physical==1500,"Reload retries only unpaid silver");
        Check(UNCrimePolicy.PollutionPenalty(0,100)==0,"Unattributed and clean cells cause no periodic penalty");
        Check(UNCrimePolicy.PollutionPenalty(1,100)==10&&UNCrimePolicy.PollutionPenalty(24,100)==10,"Low pollution tier");
        Check(UNCrimePolicy.PollutionPenalty(25,100)==20&&UNCrimePolicy.PollutionPenalty(74,100)==20,"Middle pollution tier");
        Check(UNCrimePolicy.PollutionPenalty(75,100)==50&&UNCrimePolicy.PollutionPenalty(100,100)==50,"High pollution tier");
        int wasteRemainder=0;
        Check(UNCrimePolicy.WastePenalty(ref wasteRemainder,4)==0&&wasteRemainder==4,"First waste remainder");
        Check(UNCrimePolicy.WastePenalty(ref wasteRemainder,4)==1&&wasteRemainder==3,"4 plus 4 waste packs");
        Check(UNCrimePolicy.InWindow(600000,1)&&!UNCrimePolicy.InWindow(600000,0),"Rolling ten-day crime boundary");
        Check(UNCrimePolicy.Aggression(true,true,false)==75,"Aggression uses highest tier once");
        Console.WriteLine("All prison, intervention, supply, mission pool, silver, pollution and crime-window assertions passed.");
    }
}
