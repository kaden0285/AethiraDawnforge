using HarmonyLib;
using RimWorld;
using Verse;

namespace KurinDemigodess
{
    /// <summary>
    /// Favor source #5: when a non-hostile, non-player pawn leaves the map alive
    /// (via Pawn.ExitMap), grant +2 Divine Favor. Represents the charity and kindness
    /// Aethira extends to strangers flowing back to her.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class GuestFavor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance == null || __instance.Dead) return;
                if (!KurinDemigodessMod.Settings.divineFavorEnabled) return;
                if (__instance.Faction == null) return;
                if (__instance.Faction.IsPlayer) return;
                if (__instance.HostileTo(Faction.OfPlayer)) return;
                // Only count humanlike visitors, not wild animals or mechs passing through
                if (__instance.RaceProps == null || !__instance.RaceProps.Humanlike) return;

                Current.Game?.GetComponent<GameComponent_DivineFavor>()?.Add(2);
            }
            catch (System.Exception ex)
            {
                Log.Warning("[KurinDemigodess] Guest favor grant failed: " + ex.Message);
            }
        }
    }
}
