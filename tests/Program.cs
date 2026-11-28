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
        Check(UNMissionPolicy.RemainingAfterDropDispatch(1500,false)==1500,"No pod: keep pending silver");
        Check(UNMissionPolicy.RemainingAfterDropDispatch(1500,true)==0,"Dispatched pod clears full payment");
        Check(UNMissionPolicy.RemainingAfterDropDispatch(0,true)==0,"Dispatched payment cannot be replayed");
        // Display arithmetic: one game day is 60,000 ticks, round up until deadline.
        Check(UNMissionPolicy.DisplayMinutes(60000)==1440,"24 hours per game day");
        Check(UNMissionPolicy.DisplayMinutes(2500)==60,"Game hour");
        Check(UNMissionPolicy.DisplayMinutes(1)==1&&UNMissionPolicy.DisplayMinutes(0)==0&&UNMissionPolicy.DisplayMinutes(-1)==0,"No premature zero");
        Check(UNMissionPolicy.DisplayMinutes(62500)==1500,"Day and hour");
        Check(UNMissionPolicy.DisplayMinutes(int.MaxValue)>0,"Long display avoids overflow");
        Check(UNMissionPolicy.RemainingTicks(60000,60000)==0&&UNMissionPolicy.RemainingTicks(60000,60001)==0&&UNMissionPolicy.RemainingTicks(60000,59999)==1,"Exact mission deadline display");
        Check(UNMissionPolicy.TicksUntil(100,101)==0&&UNMissionPolicy.TicksUntil(100,99)==1,"Absolute deadline display");
        Check(!UNMissionPolicy.HasOfferDeadline(-1)&&!UNMissionPolicy.HasOfferDeadline(0)&&!UNMissionPolicy.HasOfferDeadline(int.MaxValue)&&UNMissionPolicy.HasOfferDeadline(180000),"Offer deadline sentinels");
        Check(UNMissionPolicy.DefenseRemaining(-1,60000,100)==60000,"Defense timer starts on visit");
        Check(UNMissionPolicy.DefenseRemaining(100,60000,60099)==1&&UNMissionPolicy.DefenseRemaining(100,60000,60100)==0,"Exact defense timer");
        for(int mask=0;mask<64;mask++)
        {
            bool known=(mask&1)!=0,dead=(mask&2)!=0,downed=(mask&4)!=0,detained=(mask&8)!=0,exit=(mask&16)!=0,returned=(mask&32)!=0;
            Check(UNMissionPolicy.EnemyNeutralized(known,dead,downed,detained,exit,returned)==(known&&(dead||downed||detained||exit&&!returned)),"Enemy proof "+mask);
            Check(UNMissionPolicy.DefenseComplete(known,dead,downed?0:1,detained,exit,returned)==(mask==63),"Defense proof "+mask);
        }
        for(int mask=0;mask<32;mask++)
            Check(UNMissionPolicy.DirectPickupComplete((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0,(mask&16)!=0)==(mask==31),"Direct pickup proof "+mask);
        Check(!UNMissionPolicy.OfferExpired(180000,179999)&&UNMissionPolicy.OfferExpired(180000,180000)&&UNMissionPolicy.OfferExpired(180000,180001),"Offer exact expiry");
        Check(!UNMissionPolicy.OfferExpired(-1,int.MaxValue)&&!UNMissionPolicy.OfferExpired(int.MaxValue,int.MaxValue),"No expiry sentinel");
        for(int required=1;required<=40;required++)for(int delivered=0;delivered<=required;delivered++)for(int available=0;available<=45;available++)
            Check(UNMissionPolicy.SupplyTake(required,delivered,available)==Math.Min(required-delivered,available),"Supply exact remainder");
        for(int mask=0;mask<8;mask++)
        {
            bool a=(mask&1)!=0,b=(mask&2)!=0,c=(mask&4)!=0;
            Check(UNMissionPolicy.CanQueueSilver(a,b?1500:0,c)==(mask==0),"Queue once "+mask);
            Check(UNMissionPolicy.DispatchOutcome(a,b,c)==(a?1:b||c?2:0),"Spawn proof "+mask);
        }
        Check(UNMissionPolicy.CanDispatchSilver(1500,false,100,100)&&!UNMissionPolicy.CanDispatchSilver(1500,false,99,100),"Retry timing");
        Check(!UNMissionPolicy.CanDispatchSilver(1500,true,int.MaxValue,0)&&!UNMissionPolicy.CanDispatchSilver(0,false,100,0),"Never replay uncertain or paid");
        // Only active site reservations, home maps, protected units and native safety block removal.
        for(int mask=0;mask<16;mask++)
            Check(UNMissionPolicy.CanCloseSite((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0)==(mask==0),"Cleanup blockers "+mask);
        Check(UNMissionPolicy.CanCloseSite(false,false,false,false),"Finished site closes regardless of abandoned cargo or ordinary NPCs");
        for(int kind=0;kind<=6;kind++)
            Check(UNMissionPolicy.UsesGuestStay(kind)==(kind==1||kind==2||kind==3),"Civilian guest types only "+kind);
        Check(!UNMissionPolicy.GuestStayComplete(-1,60000),"No stay without recovery");
        Check(!UNMissionPolicy.GuestStayComplete(1234,61233)&&UNMissionPolicy.GuestStayComplete(1234,61234),"Exactly 24 game hours");
        Check(UNMissionPolicy.GuestTicksRemaining(1234,31234)==30000,"Individual remaining stay");
        Check(UNMissionPolicy.GuestTicksRemaining(-1,70000)==60000,"Relapse restarts stay");
        Check(UNMissionPolicy.GuestTicksRemaining(1234,61235)==0,"No negative countdown");
        Check(UNMissionPolicy.ValidGuestCountdown(0,30000,true)==30000,"Live valid timer");
        Check(UNMissionPolicy.ValidGuestCountdown(-1,30000,true)==-1&&UNMissionPolicy.ValidGuestCountdown(0,30000,false)==-1,"No ETA for unstarted or invalid stay");
        Check(UNMissionPolicy.GroupGuestCountdown(3,new[]{0,1200,6000})==6000,"Latest individual defines group ETA");
        Check(UNMissionPolicy.GroupGuestCountdown(3,new[]{0,-1,6000})==-1&&UNMissionPolicy.GroupGuestCountdown(3,new[]{0,6000})==-1,"Pending or missing guest invalidates ETA");
        Check(UNMissionPolicy.GroupGuestCountdown(0,new int[0])==0,"All departed");
        Check(!UNMissionPolicy.NeedsGuestPickup(0,59999,true,true,false),"No pickup before full day");
        Check(UNMissionPolicy.NeedsGuestPickup(0,60000,true,true,true)&&UNMissionPolicy.NeedsGuestPickup(0,60000,true,false,false),"Immediate fallback for downed or no route");
        Check(!UNMissionPolicy.NeedsGuestPickup(0,74999,true,false,true)&&UNMissionPolicy.NeedsGuestPickup(0,75000,true,false,true),"Six game hours for natural exit");
        Check(!UNMissionPolicy.NeedsGuestPickup(0,100000,false,true,false)&&!UNMissionPolicy.NeedsGuestPickup(-1,100000,true,true,false),"Relapse invalidates fallback");
        Check(UNMissionPolicy.ExitGraceRemaining(0,60000)==15000&&UNMissionPolicy.ExitGraceRemaining(0,75000)==0,"Fallback countdown");
        for(int mask=0;mask<64;mask++)
            Check(UNMissionPolicy.CanConfirmGuestLaunch((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0,(mask&16)!=0,(mask&32)!=0)==(mask==61),"Guest launch proof "+mask);
        Check(!UNMissionPolicy.AllDeparted(5,4)&&UNMissionPolicy.AllDeparted(5,5)&&!UNMissionPolicy.AllDeparted(0,0),"No partial or empty group completion");
        for(int required=1;required<=20;required++)
        for(int departed=0;departed<=required;departed++)
        for(int remaining=0;remaining<=required;remaining++)
        for(int field=0;field<=remaining;field++)
            Check(UNMissionPolicy.MedicalObjectivesComplete(required,remaining,field,departed)==
                (remaining+departed==required&&field==remaining),"Medical group accounting");
        Check(!UNMissionPolicy.MedicalObjectivesComplete(0,0,0,0),"No empty medical success");
        Check(!UNMissionPolicy.MedicalObjectivesComplete(4,3,3,2),"No duplicated credit");
        for(int mask=0;mask<8;mask++)
            Check(UNMissionPolicy.CanCountFieldRecovery((mask&1)!=0,(mask&2)!=0,(mask&4)!=0)==(mask==5),"Field route proof "+mask);
        for(int mask=0;mask<64;mask++)
            Check(UNMissionPolicy.MedicalRecoveryComplete((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,
                (mask&8)!=0?1:0,(mask&16)!=0?1:0,(mask&32)!=0)==(mask==33),"Medical recovery conditions "+mask);
        // Migration only trusts persisted outcomes or complete positive evidence.
        for(int mask=0;mask<32;mask++)
        {
            bool finished=(mask&1)!=0,achieved=(mask&2)!=0,deliveryProof=(mask&4)!=0,shuttleProof=(mask&8)!=0,guestProof=(mask&16)!=0;
            int expected=finished?(achieved?1:-1):(achieved||deliveryProof||shuttleProof||guestProof?1:0);
            Check(UNMissionPolicy.RecordedOutcome(finished,achieved,deliveryProof,shuttleProof,guestProof)==expected,"Saved outcome "+mask);
        }
        Check(UNMissionPolicy.StoredDeliveryComplete(new[]{10,20},new[]{10,20},2),"Complete saved delivery");
        Check(!UNMissionPolicy.StoredDeliveryComplete(new[]{10,20},new[]{10,19},2),"Partial saved delivery");
        Check(!UNMissionPolicy.StoredDeliveryComplete(new int[0],new int[0],0),"Empty saved delivery is not proof");
        Check(!UNMissionPolicy.StoredDeliveryComplete(new[]{0},new[]{0},1),"Zero demand is not proof");
        Check(!UNMissionPolicy.StoredDeliveryComplete(new[]{10},new[]{11},1),"Corrupt overdelivery is not proof");
        Check(!UNMissionPolicy.StoredDeliveryComplete(new[]{10},null,1),"Missing saved data is not proof");
        for(int mask=0;mask<16;mask++)
            Check(UNMissionPolicy.LegacyPaymentUncertain((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0)==(mask==1),"Legacy payment guard "+mask);
        Check(UNMissionPolicy.RestoredRefillAt(300000,100000)==300000,"Preserve later refill");
        Check(UNMissionPolicy.RestoredRefillAt(0,100000)==280000,"Restore cooldown from original acceptance");
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
