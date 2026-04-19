using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Prevents the Demigodess from gaining psylink levels or becoming a psycaster.
    /// Her divine nature is incompatible with psychic powers.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.Psylink), MethodType.Getter)]
    public static class AntiPsylink_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_PsychicEntropyTracker __instance, ref Hediff_Psylink __result, Pawn ___pawn)
        {
            // If she somehow got a psylink, remove it
            if (___pawn != null && Gene_Demigodess.HasDemigodessGene(___pawn) && __result != null)
            {
                ___pawn.health.RemoveHediff(__result);
                __result = null;
            }
        }
    }

    /// <summary>
    /// Block psylink hediff from being added via AddHediff.
    /// This is checked in the disease immunity patch's AddHediff prefix.
    /// </summary>
    public static class AntiPsycast_Helper
    {
        public static bool IsPsylinkHediff(HediffDef def)
        {
            return def == HediffDefOf.PsychicAmplifier;
        }
    }
}
