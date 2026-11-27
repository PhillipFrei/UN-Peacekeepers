using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace UNPeacekeepers
{
    public sealed class UNCrimeEvent:IExposable
    {
        public int tick,points;
        public bool consumed;
        public void ExposeData(){Scribe_Values.Look(ref tick,"tick");Scribe_Values.Look(ref points,"points");Scribe_Values.Look(ref consumed,"consumed");}
    }
    public sealed class UNCrimeMonitor:GameComponent
    {
        List<UNCrimeEvent> events=new List<UNCrimeEvent>();
        Dictionary<string,int> seen=new Dictionary<string,int>(), defense=new Dictionary<string,int>();
        HashSet<string> equipment=new HashSet<string>();
        bool equipmentInitialized;
        int nextCheck;
        public UNCrimeMonitor(Game game){}
        public static UNCrimeMonitor Instance=>Current.Game?.GetComponent<UNCrimeMonitor>();
        public int UnconsumedPoints=>events.Where(e=>!e.consumed && UNCrimePolicy.InWindow(Find.TickManager.TicksGame,e.tick)).Sum(e=>e.points);
        public void ConsumePoints(){foreach(var e in events)e.consumed=true;}
        public static void ObserveExistingPenalty(int rawPoints,string reason)
        {
            if(rawPoints<=0||Instance==null)return;
            Instance.events.Add(new UNCrimeEvent{tick=Find.TickManager.TicksGame,points=rawPoints});
            PrisonMonitor.Instance?.BlockRecovery();
        }
        public static void RecordCrime(int rawPoints,string reason,string uniqueEventKey=null)
        {
            var un=Authorization.GetUN();var m=Instance;
            if(Current.ProgramState!=ProgramState.Playing||un==null||un.defeated||un.deactivated||m==null||rawPoints<=0)return;
            if(uniqueEventKey!=null && !m.Once(uniqueEventKey,600000))return;
            ObserveExistingPenalty(rawPoints,reason);
            UNContact.ApplyPrisonGoodwill(un,-rawPoints,reason);
        }
        bool Once(string key,int duration)
        {
            int now=Find.TickManager.TicksGame;
            bool fresh=!seen.TryGetValue(key,out int until)||now>=until;
            seen[key]=now+duration;return fresh;
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref events,"unCrimeEvents",LookMode.Deep);
            Scribe_Collections.Look(ref seen,"unCrimeDedupe",LookMode.Value,LookMode.Value);
            Scribe_Collections.Look(ref defense,"unCrimeDefense",LookMode.Value,LookMode.Value);
            Scribe_Collections.Look(ref equipment,"unCrimeEquipment",LookMode.Value);
            Scribe_Values.Look(ref equipmentInitialized,"unCrimeEquipmentInitialized",false);
            if(Scribe.mode==LoadSaveMode.PostLoadInit){events=events??new List<UNCrimeEvent>();seen=seen??new Dictionary<string,int>();defense=defense??new Dictionary<string,int>();equipment=equipment??new HashSet<string>();}
        }
        public override void GameComponentTick()
        {
            int now=Find.TickManager.TicksGame;if(now<nextCheck)return;nextCheck=now+250;
            events.RemoveAll(e=>!UNCrimePolicy.InWindow(now,e.tick));
            foreach(var key in seen.Where(k=>now>=k.Value).Select(k=>k.Key).ToList())seen.Remove(key);
            foreach(var key in defense.Where(k=>now>=k.Value).Select(k=>k.Key).ToList())defense.Remove(key);
            var current=new HashSet<string>();
            if(!Authorization.Granted)
            foreach(var p in Find.Maps.SelectMany(m=>m.mapPawns.AllPawnsSpawned).Where(p=>p.Faction==Faction.OfPlayer))
            {
                if((p.apparel?.WornApparel.Any(a=>IsRestricted(a.def))??false)||(p.equipment?.AllEquipmentListForReading.Any(e=>IsRestricted(e.def))??false))
                {
                    current.Add(p.ThingID);
                    if(equipmentInitialized&&!equipment.Contains(p.ThingID))RecordCrime(10,"UNCrime_Equipment".Translate(p.LabelShortCap),"equipment:"+p.ThingID+":"+now);
                    PrisonMonitor.Instance?.BlockRecovery();
                }
            }
            foreach(var p in Find.Maps.SelectMany(m=>m.mapPawns.AllPawnsSpawned).Where(p=>p.Faction==Faction.OfPlayer))
                if(!current.Contains(p.ThingID))equipment.Remove(p.ThingID);
            equipment.UnionWith(current);equipmentInitialized=true;
        }
        static bool IsRestricted(ThingDef d)=>d?.defName=="UN_PeacekeeperHelmet"||d?.defName=="UN_PeacekeeperArmor"||d?.defName=="UN_ChargeRifle";
        internal bool Defense(Pawn p)=>p!=null&&defense.TryGetValue(p.ThingID,out int until)&&Find.TickManager.TicksGame<until;
        internal void NoteDefense(Pawn attacker)
        {
            if(attacker==null)return;
            var members=attacker.GetLord()?.ownedPawns??new List<Pawn>{attacker};
            foreach(var p in members)defense[p.ThingID]=Find.TickManager.TicksGame+60000;
        }
        internal void Aggression(Pawn victim,Thing source,Faction priorFaction,bool priorHostile,bool priorAlly,Map map)
        {
            if(source?.Faction!=Faction.OfPlayer||priorFaction==null||priorFaction.IsPlayer||priorHostile||Defense(victim)||UNMissionSites.AuthorizedCombat(source,victim)||map==null)return;
            string key="aggression:"+map.uniqueID+":"+priorFaction.loadID;
            if(!Once(key,60000))return;
            bool peaceful=map.Parent is Settlement && map.Parent.Faction==priorFaction;
            int points=UNCrimePolicy.Aggression(priorAlly,peaceful,UNContact.IsUN(priorFaction));
            RecordCrime(points,(UNContact.IsUN(priorFaction)?"UNCrime_UNAttack":peaceful?"UNCrime_Settlement":"UNCrime_Aggression").Translate(priorFaction.Name));
        }
        internal static bool Deliberate(Pawn attacker,Pawn victim)
        {
            if(attacker?.Faction!=Faction.OfPlayer||attacker.CurJob==null||attacker.InMentalState)return false;
            var job=attacker.CurJob;
            return job.playerForced && job.targetA.Thing==victim && (job.def==JobDefOf.AttackMelee||job.def==JobDefOf.AttackStatic);
        }
        internal sealed class DamageSnapshot
        {
            public Pawn victim;public Thing source;public bool downed,prisoner,hostile,ally,deliberate,activeFighter;public Faction faction;public Map map;
        }
        [ThreadStatic] static List<DamageSnapshot> damageStack;
        [HarmonyPatch(typeof(Thing),nameof(Thing.TakeDamage))]
        static class Damage
        {
            static void Prefix(Thing __instance,DamageInfo dinfo,out DamageSnapshot __state)
            {
                __state=null;if(Current.ProgramState!=ProgramState.Playing||!(__instance is Pawn p)||!p.RaceProps.Humanlike||p.Dead||dinfo.Amount<=0||dinfo.Instigator==null)return;
                var src=dinfo.Instigator;var attacker=src as Pawn;
                __state=new DamageSnapshot{victim=p,source=src,downed=p.Downed,prisoner=p.IsPrisonerOfColony,faction=p.Faction,map=p.Map,ally=p.Faction?.PlayerRelationKind==FactionRelationKind.Ally,hostile=p.Faction?.HostileTo(Faction.OfPlayer)??false,deliberate=Deliberate(attacker,p),activeFighter=p.CurJob?.def==JobDefOf.AttackMelee||p.CurJob?.def==JobDefOf.AttackStatic||p.CurJob?.def==JobDefOf.Wait_Combat};
                if(damageStack==null)damageStack=new List<DamageSnapshot>();damageStack.Add(__state);
            }
            static void Postfix(DamageSnapshot __state,DamageWorker.DamageResult __result)
            {
                if(__state==null||__result.totalDamageDealt<=0)return;
                var s=__state;var m=Instance;if(m==null)return;
                if(s.victim.Faction==Faction.OfPlayer && s.source is Pawn attacker && attacker.Faction!=Faction.OfPlayer)m.NoteDefense(attacker);
                if(s.deliberate)m.Aggression(s.victim,s.source,s.faction,s.hostile,s.ally,s.map);
                if(s.deliberate&&s.prisoner&&!s.victim.Dead)RecordCrime(5,"UNCrime_Abuse".Translate(s.victim.LabelShortCap),"abuse:"+s.victim.ThingID+":"+(Find.TickManager.TicksGame/60000));
            }
            static Exception Finalizer(Exception __exception,DamageSnapshot __state){if(__state!=null)damageStack?.Remove(__state);return __exception;}
        }
        [HarmonyPatch(typeof(Pawn),nameof(Pawn.Kill))]
        static class Death
        {
            static void Prefix(Pawn __instance)
            {
                var s=damageStack?.LastOrDefault(x=>x.victim==__instance);
                if(s==null||!s.deliberate||s.source?.Faction!=Faction.OfPlayer)return;
                if(s.prisoner)RecordCrime(25,"UNCrime_Execution".Translate(__instance.LabelShortCap),"death:"+__instance.ThingID);
                else if(s.downed)RecordCrime(25,"UNCrime_Downed".Translate(__instance.LabelShortCap),"death:"+__instance.ThingID);
                else if(!s.activeFighter && __instance.equipment?.Primary==null && !s.hostile && !(Instance?.Defense(__instance)??false))RecordCrime(30,"UNCrime_Civilian".Translate(__instance.LabelShortCap),"death:"+__instance.ThingID);
            }
        }
        [HarmonyPatch(typeof(ExecutionUtility),nameof(ExecutionUtility.DoExecutionByCut))]
        static class Execution
        {
            static void Postfix(Pawn executioner,Pawn victim)
            {if(executioner?.Faction==Faction.OfPlayer && victim?.Dead==true && victim.RaceProps.Humanlike)RecordCrime(25,"UNCrime_Execution".Translate(victim.LabelShortCap),"death:"+victim.ThingID);}
        }
        [HarmonyPatch(typeof(Pawn_GuestTracker),nameof(Pawn_GuestTracker.SetGuestStatus))]
        static class Enslave
        {
            static void Prefix(Pawn_GuestTracker __instance,out bool __state){__state=__instance.IsSlave;}
            static void Postfix(Pawn_GuestTracker __instance,Pawn ___pawn,Faction newHost,GuestStatus guestStatus,bool __state)
            {if(!__state&&__instance.IsSlave&&newHost==Faction.OfPlayer)RecordCrime(15,"UNCrime_Slavery".Translate(___pawn.LabelShortCap),"slave:"+___pawn.ThingID+":"+Find.TickManager.TicksGame);}
        }
        [HarmonyPatch(typeof(Recipe_RemoveBodyPart),nameof(Recipe_RemoveBodyPart.ApplyOnPawn))]
        static class Harvest
        {
            static void Prefix(Recipe_RemoveBodyPart __instance,Pawn pawn,BodyPartRecord part,Pawn billDoer,out bool __state)
            {__state=UNPrisonCrimeRules.FindRule("Recipe",__instance.recipe?.defName)==null&&billDoer?.Faction==Faction.OfPlayer&&pawn.IsPrisonerOfColony&&HealthUtility.PartRemovalIntent(pawn,part)==BodyPartRemovalIntent.Harvest&&!pawn.health.hediffSet.PartIsMissing(part);}
            static void Postfix(Pawn pawn,BodyPartRecord part,bool __state)
            {if(__state&&pawn.health.hediffSet.PartIsMissing(part))RecordCrime(part.depth==BodyPartDepth.Inside?20:15,(part.depth==BodyPartDepth.Inside?"UNCrime_Organ":"UNCrime_Mutilation").Translate(pawn.LabelShortCap),"harvest:"+pawn.ThingID+":"+part.Index+":"+Find.TickManager.TicksGame);}
        }

        [HarmonyPatch(typeof(RecruitUtility),nameof(RecruitUtility.Recruit))]
        static class PrisonerRecruitment
        {
            static void Prefix(Pawn pawn,Faction faction,out bool __state){__state=pawn.IsPrisonerOfColony && faction==Faction.OfPlayer;}
            static void Postfix(Pawn pawn,Faction faction,bool __state)
            {if(__state&&pawn.Faction==Faction.OfPlayer&&!pawn.IsPrisoner)RecordCrime(5,"UNCrime_Recruit".Translate(pawn.LabelShortCap),"recruit:"+pawn.ThingID);}
        }
        [HarmonyPatch(typeof(CompReleaseGas),nameof(CompReleaseGas.StartRelease))]
        static class GasWeapon
        {
            static void Prefix(CompReleaseGas __instance,bool ___started)
            {
                if(___started || !(__instance.props is CompProperties_ReleaseGas props)||props.gasType!=GasType.ToxGas)return;
                var wearer=(__instance.parent.ParentHolder as Pawn_ApparelTracker)?.pawn;
                if(wearer?.Faction==Faction.OfPlayer)RecordCrime(30,"UNCrime_Chemical".Translate(),"gas:"+__instance.parent.ThingID+":"+Find.TickManager.TicksGame);
            }
        }
        [HarmonyPatch(typeof(Projectile),nameof(Projectile.Launch),new[]{typeof(Thing),typeof(UnityEngine.Vector3),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(ProjectileHitFlags),typeof(bool),typeof(Thing),typeof(ThingDef)})]
        static class ChemicalProjectile
        {
            static void Postfix(Projectile __instance,Thing launcher)
            {if(launcher?.Faction==Faction.OfPlayer&&__instance.def.projectile?.postExplosionGasType==GasType.ToxGas)RecordCrime(30,"UNCrime_Chemical".Translate(),"chemical:"+__instance.ThingID);}
        }
    }
}
