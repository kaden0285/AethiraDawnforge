using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Boosts the Demigodess's caravan carry capacity by 500 kg.
    /// Caravan capacity is normally BodySize * 35, which is only ~31 kg for Kurin.
    /// This adds 500 kg on top for divine pocket storage.
    /// </summary>
    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.Capacity))]
    public static class CaravanCapacity_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn p, ref float __result)
        {
            if (p != null && Gene_Demigodess.HasDemigodessGene(p))
            {
                __result += 500f;
            }
        }
    }
}
