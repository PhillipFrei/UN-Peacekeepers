using System;
namespace UNPeacekeepers
{
    public static class UNCrimePolicy
    {
        public static int PollutionPenalty(int owned,int eligible)=>owned<=0||eligible<=0?0:(owned*100L>=eligible*75L?50:owned*100L>=eligible*25L?20:10);
        public static int WastePenalty(ref int remainder,int packs){int total=remainder+Math.Max(0,packs);remainder=total%5;return total/5;}
        public static bool InWindow(int now,int tick)=>tick<=now && now-tick<600000;
        public static int Aggression(bool ally,bool peacefulSettlement,bool un)=>ally||peacefulSettlement?75:un?50:50;
    }
}
