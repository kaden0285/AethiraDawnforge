using System.Linq;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    public class HediffCompProperties_BlessedBuff : HediffCompProperties
    {
        public HediffCompProperties_BlessedBuff()
        {
            compClass = typeof(HediffComp_BlessedBuff);
        }
    }

    /// <summary>
    /// Active-over-time effects attached to DG_AethirasBlessing. Makes the
    /// blessing a portable aura: all the tick-based benefits of standing next
    /// to Aethira, applied to the blessed pawn wherever they are - on map,
    /// in a caravan, across the world. Even works during Aethira's ascension.
    ///
    /// Effects mirror HediffComp_HealingPresence but operate on a single pawn
    /// (the one wearing the blessing) instead of scanning an area.
    /// Stat factors (work speed, combat, learning) are on the hediff itself,
    /// not here - they apply passively via RimWorld's stat system.
    /// </summary>
    public class HediffComp_BlessedBuff : HediffComp
    {
        private int tickCounter;
        private int tendCounter;
        private int scarCounter;
        private int regenCounter;

        public override void CompPostTick(ref float severityAdjustment)
        {
            var pawn = parent.pawn;
            if (pawn == null || pawn.Dead) return;

            // If the blessed pawn is within Aethira's aura range, skip all active
            // effects. Her aura handles them directly and we don't want double healing,
            // double hunger reduction, etc. The blessing comp only activates when the
            // pawn is OUT of range, on a different map, or in a caravan.
            if (IsWithinAethiraAuraRange(pawn)) return;

            tickCounter++;
            tendCounter++;
            scarCounter++;
            regenCounter++;

            // Every 60 ticks (1 in-game second): heal / hunger / disease / blood / reapply combat buff
            if (tickCounter >= 60)
            {
                tickCounter = 0;
                DoHealPulse(pawn);
            }

            // Every 300 ticks (5 seconds): auto-tend one untended injury
            if (tendCounter >= 300)
            {
                tendCounter = 0;
                AutoTend(pawn);
            }

            // Every 600 ticks (10 seconds): remove one permanent scar
            if (scarCounter >= 600)
            {
                scarCounter = 0;
                RemoveScar(pawn);
            }

            // Every 2500 ticks (~1 in-game hour): regrow one missing body part
            if (regenCounter >= 2500)
            {
                regenCounter = 0;
                RegrowPart(pawn);
            }
        }

        private static void DoHealPulse(Pawn pawn)
        {
            // Apply or refresh combat / learning buff (refresh timer to prevent flickering)
            if (Kurin_DefOf.DG_DivineInspirationHediff != null)
            {
                var existingInspire = pawn.health.hediffSet.GetFirstHediffOfDef(Kurin_DefOf.DG_DivineInspirationHediff);
                if (existingInspire != null)
                {
                    var dc = existingInspire.TryGetComp<HediffComp_Disappears>();
                    if (dc != null) dc.ticksToDisappear = 600;
                }
                else
                {
                    pawn.health.AddHediff(Kurin_DefOf.DG_DivineInspirationHediff);
                }
            }

            // Reduce hunger rate (half the fall rate by adding back what they just lost)
            if (pawn.needs != null && pawn.needs.food != null)
            {
                pawn.needs.food.CurLevel += pawn.needs.food.FoodFallPerTick * 0.5f;
            }

            // Heal injuries, scaled by current Divine Favor
            float mult = GameComponent_DivineFavor.GetAuraMultiplier();
            var injuries = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => h.Severity > 0 && !h.IsPermanent())
                .ToList();
            foreach (var injury in injuries)
            {
                injury.Heal(0.05f * mult);
            }

            // Reduce disease severity
            foreach (var hediff in pawn.health.hediffSet.hediffs.ToList())
            {
                if (hediff.def.CompProps<HediffCompProperties_Immunizable>() != null && hediff.Severity > 0)
                {
                    hediff.Severity -= 0.01f;
                    if (hediff.Severity < 0f) hediff.Severity = 0f;
                }
            }

            // Reduce blood loss
            var bloodLoss = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bloodLoss != null)
            {
                bloodLoss.Severity -= 0.02f;
                if (bloodLoss.Severity <= 0f)
                {
                    pawn.health.RemoveHediff(bloodLoss);
                }
            }
        }

        private static void AutoTend(Pawn pawn)
        {
            var untended = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => h.Severity > 0 && !h.IsTended())
                .FirstOrDefault();
            if (untended != null)
            {
                untended.Tended(0.5f, 0.5f);
            }
        }

        private static void RemoveScar(Pawn pawn)
        {
            var scar = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => h.IsPermanent())
                .FirstOrDefault();
            if (scar != null)
            {
                pawn.health.RemoveHediff(scar);
            }
        }

        private static void RegrowPart(Pawn pawn)
        {
            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            if (!missingParts.Any()) return;

            var regen = Hediff_DivineRegenerating.GetOrCreate(pawn);
            if (regen == null) return;

            foreach (var missing in missingParts)
            {
                if (missing.Part != null)
                {
                    regen.StartRegeneration(missing.Part);
                }
            }
        }

        /// <summary>
        /// Returns true if the pawn is on the same map as Aethira and within
        /// her current aura range (50 base tiles x favor multiplier). Used to
        /// suppress the blessing's active effects when the aura already covers them.
        /// </summary>
        private static bool IsWithinAethiraAuraRange(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return false;

            float effectiveRange = 50f * GameComponent_DivineFavor.GetAuraMultiplier();

            foreach (var other in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (other == null || other == pawn || other.Dead) continue;
                if (!Gene_Demigodess.HasDemigodessGene(other)) continue;
                if (pawn.Position.DistanceTo(other.Position) <= effectiveRange)
                    return true;
            }
            return false;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref tickCounter, "blessedTickCounter", 0);
            Scribe_Values.Look(ref tendCounter, "blessedTendCounter", 0);
            Scribe_Values.Look(ref scarCounter, "blessedScarCounter", 0);
            Scribe_Values.Look(ref regenCounter, "blessedRegenCounter", 0);
        }
    }
}
