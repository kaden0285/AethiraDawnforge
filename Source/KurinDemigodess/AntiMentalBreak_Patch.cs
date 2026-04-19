using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace KurinDemigodess
{
    /// <summary>
    /// Prevents the Demigodess from having ANY mental breaks.
    /// Patches both TryStartMentalState AND the mental break checker.
    /// </summary>
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class AntiMentalBreak_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MentalStateHandler __instance, MentalStateDef stateDef, Pawn ___pawn)
        {
            if (___pawn == null || stateDef == null) return true;
            if (!Gene_Demigodess.HasDemigodessGene(___pawn)) return true;
            return false;
        }
    }

    /// <summary>
    /// Also block mental breaks from being triggered by the MentalBreaker.
    /// This catches catatonic breakdowns and other edge cases.
    /// </summary>
    [HarmonyPatch(typeof(MentalBreaker), nameof(MentalBreaker.TryDoRandomMoodCausedMentalBreak))]
    public static class AntiMentalBreaker_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MentalBreaker __instance, Pawn ___pawn, ref bool __result)
        {
            if (___pawn != null && Gene_Demigodess.HasDemigodessGene(___pawn))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Block the catatonic breakdown hediff from being applied directly.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "AddHediff")]
    public static class AntiCatatonic_Patch
    {
        // This is manually patched in Mod.cs to avoid ambiguous match
        public static void ApplyPatch(Harmony harmony)
        {
            // Already handled by DiseaseImmunity_Patch's manual patch
            // But we add our own check there
        }
    }
}
