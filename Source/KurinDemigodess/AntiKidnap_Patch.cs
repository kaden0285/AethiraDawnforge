using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace KurinDemigodess
{
    /// <summary>
    /// Prevents the Demigodess from being kidnapped by raiders.
    /// Patches the kidnap job giver to return null when targeting a Demigodess.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Kidnap), "TryGiveJob")]
    public static class AntiKidnap_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            if (__result == null) return;

            // Check if the kidnap target is a demigodess
            var target = __result.targetA.Thing as Pawn;
            if (target != null && Gene_Demigodess.HasDemigodessGene(target))
            {
                __result = null; // Cancel the kidnap job
            }
        }
    }
}
