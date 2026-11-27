using System;
namespace UNPeacekeepers
{
    // Engine-independent delivery accounting; persisted by QuestPart_UNSupply.
    public sealed class UNSupplyProgress
    {
        public int Delivered;
        public bool Rewarded;
        public int TakeCount(int required,int available)=>Rewarded?0:Math.Max(0,Math.Min(required-Delivered,available));
        public bool TryClaim(int required)
        {
            if(Rewarded || Delivered<required)return false;
            Rewarded=true;
            return true;
        }
        public static bool Expired(int elapsed)=>elapsed>=600000;
    }
}
