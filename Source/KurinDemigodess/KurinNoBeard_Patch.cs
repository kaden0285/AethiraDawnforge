using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Kurins are canonically an all-female xeno-hybrid race - no facial hair, ever.
    /// This patch force-clears any beard on Kurin pawns at generation time.
    ///
    /// Hook: postfix on PawnGenerator.GeneratePawn. Any pawn of defName "Kurin_Race"
    /// gets its beardDef reset to BeardDefOf.NoBeard. Silent on every other race.
    ///
    /// If a mod later hands a Kurin a beard through some non-generation path,
    /// EnforceIdentity still catches Aethira (she is explicitly reset in Gene_Demigodess).
    /// This patch handles the generic "every Kurin" case at gen time.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class KurinNoBeard_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __result)
        {
            if (__result == null) return;
            if (__result.def == null || __result.def.defName != "Kurin_Race") return;
            if (__result.style == null) return;

            if (BeardDefOf.NoBeard != null && __result.style.beardDef != BeardDefOf.NoBeard)
            {
                __result.style.beardDef = BeardDefOf.NoBeard;
            }
        }
    }
}
