using System.Collections.Generic;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    public class HediffCompProperties_IntimidationAura : HediffCompProperties
    {
        public float range = 15f;
        public int tickInterval = 120;

        public HediffCompProperties_IntimidationAura()
        {
            compClass = typeof(HediffComp_IntimidationAura);
        }
    }

    public class HediffComp_IntimidationAura : HediffComp
    {
        private int tickCounter;

        public HediffCompProperties_IntimidationAura Props
        {
            get { return (HediffCompProperties_IntimidationAura)props; }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            tickCounter++;
            if (tickCounter < Props.tickInterval) return;
            tickCounter = 0;

            if (!parent.pawn.Spawned || parent.pawn.Dead) return;

            var map = parent.pawn.Map;
            var pos = parent.pawn.Position;
            var debuffDef = Kurin_DefOf.DG_IntimidatedHediff;
            if (debuffDef == null) return;

            float effectiveRange = Props.range * GameComponent_DivineFavor.GetAuraMultiplier();

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var target = pawns[i];
                if (target == parent.pawn) continue;
                if (target.Dead || target.Downed) continue;
                if (!target.HostileTo(parent.pawn)) continue;
                if (target.Position.DistanceTo(pos) > effectiveRange) continue;

                // Apply or refresh the intimidation debuff (refresh timer to prevent flickering)
                var existingDebuff = target.health.hediffSet.GetFirstHediffOfDef(debuffDef);
                if (existingDebuff != null)
                {
                    var dc = existingDebuff.TryGetComp<HediffComp_Disappears>();
                    if (dc != null) dc.ticksToDisappear = 600;
                }
                else
                {
                    target.health.AddHediff(debuffDef);
                }
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref tickCounter, "DG_IntimidationTickCounter", 0);
        }
    }
}
