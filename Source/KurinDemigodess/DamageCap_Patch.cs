using HarmonyLib;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Caps any single hit to 25% of the Demigodess's max HP.
    /// Nothing can one-shot her - even a 4-hit combo can't finish her off from full health.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class DamageCap_Patch
    {
        private const float MaxDamagePercent = 0.25f;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.VeryHigh)]
        public static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (__instance is Pawn pawn && Gene_Demigodess.HasDemigodessGene(pawn))
            {
                float maxHP = pawn.RaceProps.IsFlesh ? pawn.HealthScale * 40f : pawn.MaxHitPoints;
                float maxAllowed = maxHP * MaxDamagePercent;
                if (dinfo.Amount > maxAllowed)
                {
                    dinfo.SetAmount(maxAllowed);
                }
            }
        }
    }
}
