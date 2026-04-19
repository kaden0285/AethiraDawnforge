using System.Linq;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Centralized hediff purging logic (Tier 3 #10).
    /// Previously duplicated across Gene_Demigodess.PurgeHarmfulHediffs,
    /// WorldComponent.EmergencyRespawn, WorldComponent.FullyHealPawn, and
    /// WorldComponent.RepairLivingDemigodess with slightly different lists.
    /// Now a single source of truth.
    /// </summary>
    public static class DemigodessHealing
    {
        /// <summary>
        /// Removes transient bad states: temperature, toxic, chemical, food poisoning,
        /// addictions, tolerances, malnutrition, catatonic breakdown. Does NOT touch
        /// injuries, missing parts, or resurrection-lifecycle hediffs.
        /// Safe to call on alive pawns every tick.
        /// </summary>
        public static void PurgeHarmfulStatusEffects(Pawn pawn)
        {
            if (pawn == null || pawn.health == null) return;

            foreach (var hediff in pawn.health.hediffSet.hediffs.ToList())
            {
                if (hediff == null) continue;
                string defName = hediff.def.defName;

                // Temperature hediffs
                if (defName == "Hypothermia" || defName == "Heatstroke" || defName == "Frostbite")
                {
                    pawn.health.RemoveHediff(hediff);
                    continue;
                }

                // Toxic / chemical hediffs
                if (defName == "ToxicBuildup" || defName == "ToxGasExposure" ||
                    defName == "ChemicalDamageSevere" || defName == "ChemicalDamageModerate")
                {
                    pawn.health.RemoveHediff(hediff);
                    continue;
                }

                // Food poisoning
                if (defName == "FoodPoisoning")
                {
                    pawn.health.RemoveHediff(hediff);
                    continue;
                }

                // Addictions and tolerances
                if (hediff is Hediff_Addiction || defName.EndsWith("Addiction") || defName.EndsWith("Tolerance"))
                {
                    pawn.health.RemoveHediff(hediff);
                    continue;
                }

                // Malnutrition
                if (hediff.def == HediffDefOf.Malnutrition)
                {
                    pawn.health.RemoveHediff(hediff);
                    continue;
                }

                // Catatonic breakdown
                if (defName == "CatatonicBreakdown")
                {
                    pawn.health.RemoveHediff(hediff);
                    continue;
                }
            }
        }

        /// <summary>
        /// Removes all injuries, missing parts, and blood loss.
        /// Use during emergency respawn or full heal - not during normal tick.
        /// </summary>
        public static void PurgeInjuriesAndBloodLoss(Pawn pawn)
        {
            if (pawn == null || pawn.health == null) return;

            var toRemove = pawn.health.hediffSet.hediffs
                .Where(h => h is Hediff_Injury || h is Hediff_MissingPart ||
                            h.def == HediffDefOf.BloodLoss)
                .ToList();

            foreach (var hediff in toRemove)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        /// <summary>
        /// Removes resurrection-lifecycle hediffs left over on a living pawn
        /// (DG_DivineResurrecting, DG_CorpsePreservation, DG_DivineRegenerating,
        /// ResurrectionSickness, ResurrectionPsychosis).
        /// Safe to call on living pawns during normal tick - these should never
        /// persist on an alive pawn.
        /// </summary>
        public static void PurgeResurrectionLeftovers(Pawn pawn)
        {
            if (pawn == null || pawn.health == null) return;

            foreach (var hediff in pawn.health.hediffSet.hediffs.ToList())
            {
                if (hediff.def == Kurin_DefOf.DG_DivineResurrecting ||
                    hediff.def == Kurin_DefOf.DG_CorpsePreservation ||
                    hediff.def.defName == "ResurrectionSickness" ||
                    hediff.def.defName == "ResurrectionPsychosis")
                {
                    pawn.health.RemoveHediff(hediff);
                }
                // NOTE: DG_DivineRegenerating is deliberately NOT removed here.
                // It is valid on living pawns (active limb regrowth). It self-manages
                // via Hediff_DivineRegenerating.Advance() and removes itself when done.
                // FullPurge() handles stripping it for complete-rebuild scenarios.
            }
        }

        /// <summary>
        /// Removes all disease hediffs (anything with an Immunizable comp).
        /// Catches flu, plague, malaria, sleeping sickness, wound infection, gut worms,
        /// muscle parasites, organ decay, lung rot, and any modded disease following
        /// the same pattern. Safe to call on any pawn.
        /// </summary>
        public static void PurgeDiseases(Pawn pawn)
        {
            if (pawn == null || pawn.health == null) return;

            foreach (var hediff in pawn.health.hediffSet.hediffs.ToList())
            {
                if (hediff == null || hediff.def == null) continue;
                if (hediff.def.CompProps<HediffCompProperties_Immunizable>() != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }

        /// <summary>
        /// Removes the vanilla psychic amplifier (psylink) - the Demigodess is not a psycaster.
        /// </summary>
        public static void RemovePsylink(Pawn pawn)
        {
            if (pawn == null || pawn.health == null) return;
            var psylink = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.PsychicAmplifier);
            if (psylink != null)
            {
                pawn.health.RemoveHediff(psylink);
            }
        }

        /// <summary>
        /// Emergency full-heal: status effects, injuries, blood loss, and resurrection leftovers.
        /// Use only for emergency respawn / ascension return - wipes everything.
        /// </summary>
        public static void FullPurge(Pawn pawn)
        {
            PurgeHarmfulStatusEffects(pawn);
            PurgeInjuriesAndBloodLoss(pawn);
            PurgeResurrectionLeftovers(pawn);
            // DG_DivineRegenerating is NOT in PurgeResurrectionLeftovers (valid on living pawns).
            // Strip it here explicitly for complete-rebuild scenarios (ascension return, emergency respawn).
            if (pawn != null && pawn.health != null && Kurin_DefOf.DG_DivineRegenerating != null)
            {
                var regen = pawn.health.hediffSet.GetFirstHediffOfDef(Kurin_DefOf.DG_DivineRegenerating);
                if (regen != null) pawn.health.RemoveHediff(regen);
            }
        }
    }
}
