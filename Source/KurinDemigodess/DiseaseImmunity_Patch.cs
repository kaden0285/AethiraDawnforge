using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Blocks ALL diseases (including modded ones) from being applied to the Demigodess.
    /// Catches any hediff that has HediffCompProperties_Immunizable, which is
    /// the standard component for all disease-type hediffs.
    /// Uses manual patching to target the correct AddHediff overload.
    /// </summary>
    public static class DiseaseImmunity_Patch
    {
        public static void ApplyPatch(Harmony harmony)
        {
            // Find the AddHediff overload that takes (Hediff, BodyPartRecord, DamageInfo?, DamageResult)
            var methods = typeof(Pawn_HealthTracker).GetMethods(BindingFlags.Public | BindingFlags.Instance);
            MethodInfo target = null;
            foreach (var m in methods)
            {
                if (m.Name != "AddHediff") continue;
                var parms = m.GetParameters();
                if (parms.Length >= 1 && parms[0].ParameterType == typeof(Hediff))
                {
                    target = m;
                    break;
                }
            }

            if (target != null)
            {
                var prefix = typeof(DiseaseImmunity_Patch).GetMethod("Prefix",
                    BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            }
            else
            {
                Log.Warning("[KurinDemigodess] Could not find AddHediff(Hediff, ...) to patch for disease immunity.");
            }
        }

        public static bool Prefix(Pawn_HealthTracker __instance, Hediff hediff)
        {
            var pawn = __instance.hediffSet.pawn;
            if (!Gene_Demigodess.HasDemigodessGene(pawn))
                return true;

            // Check if this hediff is a disease (has Immunizable comp)
            if (hediff.def.CompProps<HediffCompProperties_Immunizable>() != null)
            {
                return false; // Block the disease
            }

            // Block catatonic breakdown
            if (hediff.def.defName == "CatatonicBreakdown")
            {
                return false;
            }

            // Block malnutrition (she cannot starve to death)
            if (hediff.def == HediffDefOf.Malnutrition)
            {
                return false;
            }

            // Block hypothermia
            if (hediff.def == HediffDefOf.Hypothermia)
            {
                return false;
            }

            // Block heatstroke
            if (hediff.def == HediffDefOf.Heatstroke)
            {
                return false;
            }

            // Block toxic buildup
            if (hediff.def.defName == "ToxicBuildup" || hediff.def.defName == "ToxGasExposure")
            {
                return false;
            }

            // Block chemical addictions
            if (hediff.def.IsAddiction)
            {
                return false;
            }

            // Block food poisoning
            if (hediff.def == HediffDefOf.FoodPoisoning)
            {
                return false;
            }

            // Block psylink (she cannot become a psycaster)
            if (hediff.def == HediffDefOf.PsychicAmplifier)
            {
                Messages.Message(
                    "The Demigodess's divine nature rejects psychic amplification.",
                    pawn, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return true; // Allow non-disease hediffs
        }
    }
}
