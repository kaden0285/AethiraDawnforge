using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Prevents the Demigodess from losing ideology certainty.
    /// Her divine will is absolute. She cannot be swayed.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.CertaintyChangePerDay), MethodType.Getter)]
    public static class CertaintyLock_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_IdeoTracker __instance, Pawn ___pawn, ref float __result)
        {
            if (___pawn != null && Gene_Demigodess.HasDemigodessGene(___pawn))
            {
                if (__result < 0f)
                {
                    __result = 0f;
                }
            }
        }
    }
}
