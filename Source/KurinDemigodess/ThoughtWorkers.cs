using System.Linq;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Leadership Aura: +12 mood to colonists within 25 tiles of a Demigodess.
    /// </summary>
    public class ThoughtWorker_DivineMorale : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p.Dead || !p.Spawned) return ThoughtState.Inactive;
            if (Gene_Demigodess.HasDemigodessGene(p)) return ThoughtState.Inactive; // Don't buff yourself

            var map = p.Map;
            if (map == null) return ThoughtState.Inactive;

            // Use the same favor-scaled aura range as all other auras (50 base, up to 100)
            float effectiveRange = 50f * GameComponent_DivineFavor.GetAuraMultiplier();

            foreach (var other in map.mapPawns.FreeColonistsSpawned)
            {
                if (other == p) continue;
                if (!Gene_Demigodess.HasDemigodessGene(other)) continue;
                if (other.Dead) continue;
                if (other.Position.DistanceTo(p.Position) <= effectiveRange)
                {
                    return ThoughtState.ActiveAtStage(0);
                }
            }

            return ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// Situational thought: -8 mood when the Demigodess is in a divine coma on the same map.
    /// </summary>
    public class ThoughtWorker_DemigodessRests : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p.Dead || !p.Spawned) return ThoughtState.Inactive;
            if (Gene_Demigodess.HasDemigodessGene(p)) return ThoughtState.Inactive;

            var map = p.Map;
            if (map == null) return ThoughtState.Inactive;

            var comaDef = DefDatabase<HediffDef>.GetNamedSilentFail("DG_DivineComa");
            if (comaDef == null) return ThoughtState.Inactive;

            foreach (var other in map.mapPawns.FreeColonistsSpawned)
            {
                if (Gene_Demigodess.HasDemigodessGene(other) &&
                    other.health.hediffSet.HasHediff(comaDef))
                {
                    return ThoughtState.ActiveAtStage(0);
                }
            }

            return ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// Ideology precept thought: +8 mood when Demigodess is on the same map.
    /// Only applies to believers of the Demigodess Worship meme.
    /// </summary>
    public class ThoughtWorker_DemigodessNearbyIdeo : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (p.Dead || !p.Spawned) return ThoughtState.Inactive;
            if (Gene_Demigodess.HasDemigodessGene(p)) return ThoughtState.Inactive;

            var map = p.Map;
            if (map == null) return ThoughtState.Inactive;

            foreach (var other in map.mapPawns.FreeColonistsSpawned)
            {
                if (other == p) continue;
                if (Gene_Demigodess.HasDemigodessGene(other))
                {
                    return ThoughtState.ActiveAtStage(0);
                }
            }

            return ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// Divine Will: +20 mood permanently for the Demigodess herself.
    /// </summary>
    public class ThoughtWorker_DivineWill : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p.Dead) return ThoughtState.Inactive;
            if (Gene_Demigodess.HasDemigodessGene(p))
            {
                return ThoughtState.ActiveAtStage(0);
            }
            return ThoughtState.Inactive;
        }
    }
}
