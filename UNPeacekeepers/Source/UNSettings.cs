using UnityEngine;
using Verse;

namespace UNPeacekeepers
{
    public sealed class UNSettings : ModSettings
    {
        public float raidStrength=1f;
        public float missionDifficulty=1f;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref raidStrength,"unRaidStrength",1f);
            if(float.IsNaN(raidStrength)||float.IsInfinity(raidStrength))raidStrength=1f;
            raidStrength=Mathf.Clamp(raidStrength,0.25f,5f);
            Scribe_Values.Look(ref missionDifficulty,"unMissionDifficulty",1f);
            if(float.IsNaN(missionDifficulty)||float.IsInfinity(missionDifficulty))missionDifficulty=1f;
            missionDifficulty=Mathf.Clamp(missionDifficulty,0.25f,5f);
            base.ExposeData();
        }
    }
    public sealed class UNMod : Mod
    {
        private static UNSettings settings;
        public static UNMod Instance { get; private set; }
        public UNMod(ModContentPack content):base(content) { Instance=this; settings=GetSettings<UNSettings>(); }
        public static float Strength => settings==null ? 1f : Mathf.Clamp(settings.raidStrength,0.25f,5f);
        public static float MissionDifficulty => settings==null?1f:Mathf.Clamp(settings.missionDifficulty,0.25f,5f);
        public override string SettingsCategory() => "UN Peacekeepers";
        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing=new Listing_Standard();
            listing.Begin(rect);
            listing.Label("UN_Settings_Strength".Translate((Strength*100f).ToString("0")));
            float value=listing.Slider(Strength,0.25f,5f);
            settings.raidStrength=Mathf.Clamp(Mathf.Round(value*20f)/20f,0.25f,5f);
            listing.Gap();
            listing.Label("UN_Settings_Desc".Translate());
            listing.Gap();
            listing.Label("UNM_Difficulty".Translate((MissionDifficulty*100f).ToString("0")));
            settings.missionDifficulty=Mathf.Round(listing.Slider(MissionDifficulty,0.25f,5f)*20f)/20f;
            listing.Label("UNM_DifficultyInfo".Translate());
            listing.Gap();
            if(listing.ButtonText("UN_Settings_Reset".Translate())){settings.raidStrength=1f;settings.missionDifficulty=1f;}
            listing.End();
        }
    }
}